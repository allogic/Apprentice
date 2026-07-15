using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;

namespace Apprentice
{
    /// <summary>
    /// Owns all temporary, non-level-up XP progress rows.
    ///
    /// One row is active per skill/class. Different skills are shown
    /// simultaneously instead of replacing each other.
    ///
    /// All methods must be called on the Vintage Story client main thread.
    /// ApprenticeModSystem enforces that rule before forwarding packets.
    /// </summary>
    internal sealed class ExperienceProgressOverlay : IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly BaseConfig config;
        private readonly Dictionary<string, ProgressEntry> entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<int> freeSlots = new();

        private long tickListenerId;
        private bool disposed;
        private bool failed;

        public ExperienceProgressOverlay(
            ICoreClientAPI api,
            BaseConfig config,
            int maximumRows)
        {
            this.api = api
                ?? throw new ArgumentNullException(nameof(api));

            this.config = config
                ?? throw new ArgumentNullException(nameof(config));

            int rowCount = Math.Max(1, maximumRows);

            for (int slot = 0; slot < rowCount; slot++)
            {
                freeSlots.Add(slot);
            }
        }

        public bool HasFailed => failed;

        public void Show(
            ExperienceNotificationPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);

            if (disposed || failed)
            {
                return;
            }

            try
            {
                long now = api.ElapsedMilliseconds;

                if (entries.TryGetValue(
                    packet.ClassId,
                    out ProgressEntry? existing))
                {
                    existing.AddPacket(
                        packet,
                        now,
                        config.ExperienceNotificationFillDurationMs
                    );

                    existing.UpdateVisual(
                        now,
                        config.ExperienceNotificationFillDurationMs
                    );

                    return;
                }

                if (freeSlots.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No free XP progress-row slot is available."
                    );
                }

                int slot = freeSlots.Min;
                freeSlots.Remove(slot);

                var row =
                    new ExperienceProgressRowHud(
                        api,
                        slot
                    );

                var entry =
                    new ProgressEntry(
                        packet,
                        row,
                        slot,
                        now
                    );

                entries.Add(packet.ClassId, entry);

                entry.UpdateVisual(
                    now,
                    config.ExperienceNotificationFillDurationMs
                );

                EnsureTickListener();
            }
            catch (Exception exception)
            {
                DisableAfterFailure(exception);
                throw;
            }
        }

        /// <summary>
        /// A level-up uses the discovery message instead of a progress bar,
        /// so remove any still-visible normal-XP row for that class.
        /// </summary>
        public void Remove(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                return;
            }

            RemoveEntry(classId);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopTickListener();
            ClearRows();
        }

        private void EnsureTickListener()
        {
            if (tickListenerId != 0)
            {
                return;
            }

            tickListenerId =
                api.Event.RegisterGameTickListener(
                    OnClientTick,
                    OnTickException,
                    50
                );
        }

        private void StopTickListener()
        {
            if (tickListenerId == 0)
            {
                return;
            }

            api.Event.UnregisterGameTickListener(
                tickListenerId
            );

            tickListenerId = 0;
        }

        private void OnClientTick(float deltaTime)
        {
            if (disposed || failed)
            {
                return;
            }

            long now = api.ElapsedMilliseconds;

            string[] expiredClassIds =
                entries
                    .Where(
                        pair => pair.Value.IsExpired(
                            now,
                            config
                                .ExperienceNotificationFillDurationMs,
                            config
                                .ExperienceNotificationHoldDurationMs
                        )
                    )
                    .Select(pair => pair.Key)
                    .ToArray();

            foreach (string classId in expiredClassIds)
            {
                RemoveEntry(classId);
            }

            foreach (ProgressEntry entry in entries.Values)
            {
                entry.UpdateVisual(
                    now,
                    config.ExperienceNotificationFillDurationMs
                );
            }

            if (entries.Count == 0)
            {
                StopTickListener();
            }
        }

        private void OnTickException(Exception exception)
        {
            DisableAfterFailure(exception);
        }

        private void DisableAfterFailure(Exception exception)
        {
            if (failed)
            {
                return;
            }

            failed = true;

            api.Logger.Error(
                "[Apprentice] XP progress-bar overlay failed. " +
                "The custom bars are disabled for this session."
            );

            api.Logger.Error(exception);

            StopTickListener();
            ClearRows();
        }

        private void RemoveEntry(string classId)
        {
            if (!entries.Remove(
                classId,
                out ProgressEntry? entry))
            {
                return;
            }

            freeSlots.Add(entry.Slot);

            try
            {
                entry.Row.TryClose();
                entry.Row.Dispose();
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    "[Apprentice] Could not dispose an XP progress row."
                );

                api.Logger.Error(exception);
            }

            if (entries.Count == 0)
            {
                StopTickListener();
            }
        }

        private void ClearRows()
        {
            foreach (ProgressEntry entry in entries.Values)
            {
                try
                {
                    entry.Row.TryClose();
                    entry.Row.Dispose();
                }
                catch (Exception exception)
                {
                    api.Logger.Error(
                        "[Apprentice] Could not dispose an XP progress row."
                    );

                    api.Logger.Error(exception);
                }

                freeSlots.Add(entry.Slot);
            }

            entries.Clear();
        }

        private sealed class ProgressEntry
        {
            private double fromTotalExperience;
            private double toTotalExperience;
            private double accumulatedGainedExperience;
            private long animationStartedMs;
            private string displayName;

            public ProgressEntry(
                ExperienceNotificationPacket packet,
                ExperienceProgressRowHud row,
                int slot,
                long now)
            {
                Row = row;
                Slot = slot;

                displayName =
                    ResolveDisplayName(packet);

                fromTotalExperience =
                    packet.PreviousTotalExperience;

                toTotalExperience =
                    packet.NewTotalExperience;

                accumulatedGainedExperience =
                    packet.GainedExperience;

                animationStartedMs = now;
            }

            public ExperienceProgressRowHud Row { get; }

            public int Slot { get; }

            public void AddPacket(
                ExperienceNotificationPacket packet,
                long now,
                int fillDurationMs)
            {
                double displayedTotal =
                    GetDisplayedTotal(
                        now,
                        fillDurationMs
                    );

                fromTotalExperience = displayedTotal;
                toTotalExperience =
                    packet.NewTotalExperience;

                accumulatedGainedExperience +=
                    packet.GainedExperience;

                animationStartedMs = now;
                displayName =
                    ResolveDisplayName(packet);
            }

            public bool IsExpired(
                long now,
                int fillDurationMs,
                int holdDurationMs)
            {
                return now - animationStartedMs >=
                    fillDurationMs +
                    holdDurationMs;
            }

            public void UpdateVisual(
                long now,
                int fillDurationMs)
            {
                double displayedTotal =
                    GetDisplayedTotal(
                        now,
                        fillDurationMs
                    );

                int level =
                    ExpMath.GetLevel(displayedTotal);

                double progress =
                    ExpMath.GetProgress(displayedTotal);

                double intoLevel =
                    ExpMath.GetExperienceIntoLevel(
                        displayedTotal
                    );

                double levelCost =
                    ExpMath.GetExperienceSpanForLevel(
                        level
                    );

                Row.Update(
                    displayName,
                    level,
                    accumulatedGainedExperience,
                    intoLevel,
                    levelCost,
                    progress
                );
            }

            private double GetDisplayedTotal(
                long now,
                int fillDurationMs)
            {
                if (fillDurationMs <= 0)
                {
                    return toTotalExperience;
                }

                double linear =
                    Math.Clamp(
                        (
                            now -
                            animationStartedMs
                        ) /
                        (double)fillDurationMs,
                        0,
                        1
                    );

                double eased =
                    linear *
                    linear *
                    (3 - 2 * linear);

                return fromTotalExperience +
                    (
                        toTotalExperience -
                        fromTotalExperience
                    ) * eased;
            }

            private static string ResolveDisplayName(
                ExperienceNotificationPacket packet)
            {
                return string.IsNullOrWhiteSpace(
                    packet.ClassDisplayName)
                    ? packet.ClassId
                    : packet.ClassDisplayName;
            }
        }
    }

    /// <summary>
    /// One compact top-left HUD row for one skill.
    ///
    /// The row owns no timer and performs no packet handling. It only
    /// displays values supplied by ExperienceProgressOverlay.
    /// </summary>
    internal sealed class ExperienceProgressRowHud : HudElement
    {
        private const string TitleKey =
            "apprentice-xp-row-title";

        private const string DetailKey =
            "apprentice-xp-row-detail";

        private const string ProgressKey =
            "apprentice-xp-row-progress";

        public override bool Focusable => false;

        public override string? ToggleKeyCombinationCode =>
            null;

        public ExperienceProgressRowHud(
            ICoreClientAPI api,
            int slot)
            : base(api)
        {
            ComposeRow(slot);

            if (!TryOpen(withFocus: false))
            {
                throw new InvalidOperationException(
                    "Vintage Story refused to open an XP progress row."
                );
            }
        }

        public override bool ShouldReceiveMouseEvents()
        {
            return false;
        }

        public override bool ShouldReceiveKeyboardEvents()
        {
            return false;
        }

        public void Update(
            string displayName,
            int level,
            double gainedExperience,
            double experienceIntoLevel,
            double levelCost,
            double progress)
        {
            string title =
                $"{displayName} — Level {level}";

            string detail =
                $"+{gainedExperience:0.###} XP   •   " +
                $"{experienceIntoLevel:0.##} / " +
                $"{levelCost:0.##} XP";

            SingleComposer
                .GetDynamicText(TitleKey)
                .SetNewText(
                    title,
                    forceRedraw: true
                );

            SingleComposer
                .GetDynamicText(DetailKey)
                .SetNewText(
                    detail,
                    forceRedraw: true
                );

            SingleComposer
                .GetStatbar(ProgressKey)
                .SetValues(
                    (float)(
                        Math.Clamp(progress, 0, 1) *
                        100
                    ),
                    0,
                    100
                );
        }

        private void ComposeRow(int slot)
        {
            const double rowWidth = 360;
            const double rowHeight = 54;
            const double rowSpacing = 58;

            ElementBounds dialogBounds =
                ElementBounds.FixedOffseted(
                    EnumDialogArea.LeftTop,
                    18,
                    72 + slot * rowSpacing,
                    rowWidth,
                    rowHeight
                );

            ElementBounds backgroundBounds =
                ElementBounds.Fill.WithFixedPadding(6);

            ElementBounds titleBounds =
                ElementBounds.Fixed(
                    9,
                    4,
                    330,
                    18
                );

            ElementBounds detailBounds =
                ElementBounds.Fixed(
                    9,
                    21,
                    330,
                    16
                );

            ElementBounds progressBounds =
                ElementBounds.Fixed(
                    9,
                    40,
                    330,
                    8
                );

            SingleComposer =
                capi.Gui.CreateCompo(
                    "apprentice-xp-progress-row-" +
                    slot,
                    dialogBounds
                )
                .AddShadedDialogBG(
                    backgroundBounds,
                    withTitleBar: false,
                    strokeWidth: 2,
                    alpha: 0.78f
                )
                .AddDynamicText(
                    string.Empty,
                    CairoFont.WhiteSmallText(),
                    titleBounds,
                    TitleKey
                )
                .AddDynamicText(
                    string.Empty,
                    CairoFont.WhiteDetailText(),
                    detailBounds,
                    DetailKey
                )
                .AddStatbar(
                    progressBounds,
                    GuiStyle.FoodBarColor,
                    hideable: false,
                    key: ProgressKey
                )
                .Compose();

            SingleComposer
                .GetStatbar(ProgressKey)
                .ShowValueOnHover = false;
        }
    }
}
