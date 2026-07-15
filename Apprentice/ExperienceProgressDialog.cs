using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;

namespace Apprentice
{
    /// <summary>
    /// Temporary multi-skill XP display implemented as one ordinary
    /// non-focusable GuiDialog.
    ///
    /// It deliberately uses:
    ///
    /// - no HudElement;
    /// - no GuiElementStatbar;
    /// - no custom renderer;
    /// - no mouse or keyboard input;
    /// - one dynamic-text element for all active skills.
    ///
    /// All methods are called on the client main thread.
    /// </summary>
    internal sealed class ExperienceProgressDialog
        : GuiDialog
    {
        private const string ProgressTextKey =
            "apprentice-progress-dialog-text";

        private const int BarSegments = 24;

        private readonly BaseConfig config;
        private readonly Dictionary<string, ProgressEntry> entries =
            new(StringComparer.OrdinalIgnoreCase);

        private long tickListenerId;
        private bool disposed;
        private bool failed;

        public override string? ToggleKeyCombinationCode =>
            null;

        /// <summary>
        /// This is the critical pass-through setting.
        ///
        /// GuiDialog defaults to EnumDialogType.Dialog, which makes the
        /// game treat it like an open menu. Marking it as HUD keeps normal
        /// movement, camera, mining, and interaction controls active.
        /// </summary>
        public override EnumDialogType DialogType =>
            EnumDialogType.HUD;

        public override bool Focusable =>
            false;

        public override bool PrefersUngrabbedMouse =>
            false;

        public override bool DisableMouseGrab =>
            false;

        /// <summary>
        /// Receive input after normal gameplay systems, although this HUD
        /// explicitly declines both keyboard and mouse events.
        /// </summary>
        public override double InputOrder =>
            9999;

        public override double DrawOrder =>
            0.08;

        public bool HasFailed =>
            failed;

        public ExperienceProgressDialog(
            ICoreClientAPI api,
            BaseConfig config)
            : base(api)
        {
            this.config = config
                ?? throw new ArgumentNullException(
                    nameof(config)
                );

            ComposeDialog();
        }

        public override bool ShouldReceiveMouseEvents()
        {
            return false;
        }

        public override bool ShouldReceiveKeyboardEvents()
        {
            return false;
        }

        public override bool CaptureAllInputs()
        {
            return false;
        }

        public override bool CaptureRawMouse()
        {
            return false;
        }

        public void ShowGain(
            ExperienceNotificationPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);

            if (disposed || failed)
            {
                return;
            }

            try
            {
                long now = capi.ElapsedMilliseconds;

                if (entries.TryGetValue(
                    packet.ClassId,
                    out ProgressEntry? existing))
                {
                    existing.AddPacket(
                        packet,
                        now,
                        config
                            .ExperienceNotificationFillDurationMs
                    );
                }
                else
                {
                    entries.Add(
                        packet.ClassId,
                        new ProgressEntry(
                            packet,
                            now
                        )
                    );
                }

                RefreshText(now);

                if (!IsOpened())
                {
                    if (!TryOpen(withFocus: false))
                    {
                        throw new InvalidOperationException(
                            "Vintage Story refused to open " +
                            "the XP progress HUD."
                        );
                    }

                    // DialogType.HUD should already prevent focus, but keep
                    // this explicit so later changes cannot accidentally
                    // turn the progress display into a blocking menu.
                    UnFocus();
                }

                EnsureTickListener();
            }
            catch (Exception exception)
            {
                DisableAfterFailure(exception);
                throw;
            }
        }

        public void RemoveSkill(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                return;
            }

            entries.Remove(classId);

            if (entries.Count == 0)
            {
                CloseAndStop();
                return;
            }

            RefreshText(capi.ElapsedMilliseconds);
        }

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopTickListener();
            entries.Clear();

            base.Dispose();
        }

        private void ComposeDialog()
        {
            ElementBounds dialogBounds =
                ElementBounds.FixedOffseted(
                    EnumDialogArea.LeftTop,
                    18,
                    72,
                    520,
                    700
                );

            ElementBounds textBounds =
                ElementBounds.Fixed(
                    0,
                    0,
                    500,
                    680
                );

            SingleComposer =
                capi.Gui.CreateCompo(
                    "apprentice-xp-progress-dialog",
                    dialogBounds
                )
                .AddDynamicText(
                    string.Empty,
                    CairoFont.WhiteSmallText(),
                    textBounds,
                    ProgressTextKey
                )
                .Compose();
        }

        private void EnsureTickListener()
        {
            if (tickListenerId != 0)
            {
                return;
            }

            tickListenerId =
                capi.Event.RegisterGameTickListener(
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

            capi.Event.UnregisterGameTickListener(
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

            long now = capi.ElapsedMilliseconds;

            string[] expired =
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

            foreach (string classId in expired)
            {
                entries.Remove(classId);
            }

            if (entries.Count == 0)
            {
                CloseAndStop();
                return;
            }

            RefreshText(now);
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

            capi.Logger.Error(
                "[Apprentice] Dialog text-bar display failed. " +
                "It is disabled for this session."
            );

            capi.Logger.Error(exception);

            entries.Clear();
            CloseAndStop();
        }

        private void CloseAndStop()
        {
            StopTickListener();

            if (IsOpened())
            {
                TryClose();
            }
        }

        private void RefreshText(long now)
        {
            var text = new StringBuilder();

            foreach (ProgressEntry entry in
                     entries.Values
                         .OrderBy(entry => entry.DisplayName))
            {
                double displayedTotal =
                    entry.GetDisplayedTotal(
                        now,
                        config
                            .ExperienceNotificationFillDurationMs
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

                text.Append(entry.DisplayName);
                text.Append(" — Level ");
                text.Append(level);
                text.Append("   +");
                text.Append(
                    entry.AccumulatedGainedExperience
                        .ToString("0.###")
                );
                text.AppendLine(" XP");

                text.Append(
                    BuildTextBar(progress)
                );
                text.Append(' ');
                text.Append(
                    intoLevel.ToString("0.##")
                );
                text.Append(" / ");
                text.Append(
                    levelCost.ToString("0.##")
                );
                text.AppendLine(" XP");
                text.AppendLine();
            }

            SingleComposer
                .GetDynamicText(ProgressTextKey)
                .SetNewText(
                    text.ToString().TrimEnd(),
                    autoHeight: true,
                    forceRedraw: true
                );
        }

        private static string BuildTextBar(
            double progress)
        {
            int filled =
                (int)Math.Round(
                    Math.Clamp(progress, 0, 1) *
                    BarSegments
                );

            return "[" +
                new string('#', filled) +
                new string(
                    '-',
                    BarSegments - filled
                ) +
                "]";
        }

        private sealed class ProgressEntry
        {
            private double fromTotalExperience;
            private double toTotalExperience;
            private long animationStartedMs;

            public ProgressEntry(
                ExperienceNotificationPacket packet,
                long now)
            {
                DisplayName =
                    ResolveDisplayName(packet);

                fromTotalExperience =
                    packet.PreviousTotalExperience;

                toTotalExperience =
                    packet.NewTotalExperience;

                AccumulatedGainedExperience =
                    packet.GainedExperience;

                animationStartedMs = now;
            }

            public string DisplayName { get; private set; }

            public double AccumulatedGainedExperience
            {
                get;
                private set;
            }

            public void AddPacket(
                ExperienceNotificationPacket packet,
                long now,
                int fillDurationMs)
            {
                fromTotalExperience =
                    GetDisplayedTotal(
                        now,
                        fillDurationMs
                    );

                toTotalExperience =
                    packet.NewTotalExperience;

                AccumulatedGainedExperience +=
                    packet.GainedExperience;

                DisplayName =
                    ResolveDisplayName(packet);

                animationStartedMs = now;
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

            public double GetDisplayedTotal(
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
}
