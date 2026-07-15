using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Apprentice
{
    /// <summary>
    /// Routes notifications on the client main thread:
    ///
    /// - level-up packets use the vanilla discovery message;
    /// - normal packets use one top-left GuiDialog with text bars;
    /// - all simultaneously rewarded skills are shown together.
    /// </summary>
    internal sealed class OverlayManager : IDisposable
    {
        private const int LevelUpDiscoveryDurationMs =
            3500;

        private readonly ICoreClientAPI api;
        private readonly BaseConfig config;
        private readonly Queue<ExperienceNotificationPacket>
            levelUpQueue = new();

        private ExperienceProgressDialog? progressDialog;
        private bool progressDialogFailed;
        private bool levelUpDiscoveryActive;
        private bool disposed;

        public OverlayManager(
            ICoreClientAPI api,
            BaseConfig config,
            ClassConfig classConfig)
        {
            this.api = api
                ?? throw new ArgumentNullException(nameof(api));

            this.config = config
                ?? throw new ArgumentNullException(nameof(config));

            ArgumentNullException.ThrowIfNull(classConfig);
        }

        public void Enqueue(
            ExperienceNotificationPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);

            if (disposed ||
                !config.EnableExperienceNotifications)
            {
                return;
            }

            if (packet.NewLevel >
                packet.PreviousLevel)
            {
                progressDialog?.RemoveSkill(
                    packet.ClassId
                );

                levelUpQueue.Enqueue(packet);
                TryShowNextLevelUp();
                return;
            }

            if (progressDialogFailed)
            {
                ShowChatFallback(packet);
                return;
            }

            try
            {
                progressDialog ??=
                    new ExperienceProgressDialog(
                        api,
                        config
                    );

                progressDialog.ShowGain(packet);

                if (progressDialog.HasFailed)
                {
                    progressDialogFailed = true;
                    ShowChatFallback(packet);
                }
            }
            catch (Exception exception)
            {
                progressDialogFailed = true;

                api.Logger.Error(
                    "[Apprentice] Text progress dialog failed. " +
                    "Using chat fallback."
                );

                api.Logger.Error(exception);

                try
                {
                    progressDialog?.TryClose();
                    progressDialog?.Dispose();
                }
                catch (Exception disposeException)
                {
                    api.Logger.Error(disposeException);
                }

                progressDialog = null;
                ShowChatFallback(packet);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            levelUpQueue.Clear();

            progressDialog?.TryClose();
            progressDialog?.Dispose();
            progressDialog = null;
        }

        private void TryShowNextLevelUp()
        {
            if (disposed ||
                levelUpDiscoveryActive ||
                levelUpQueue.Count == 0)
            {
                return;
            }

            ExperienceNotificationPacket packet =
                levelUpQueue.Dequeue();

            levelUpDiscoveryActive = true;

            string displayName =
                ResolveDisplayName(packet);

            string message =
                $"Level up {displayName} to {packet.NewLevel}";

            try
            {
                api.TriggerIngameDiscovery(
                    this,
                    "apprentice-levelup-" +
                    packet.ClassId +
                    "-" +
                    api.ElapsedMilliseconds,
                    message
                );
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    "[Apprentice] Level-up discovery failed."
                );

                api.Logger.Error(exception);
                ShowChatFallback(packet);
            }

            api.Event.RegisterCallback(
                _ =>
                {
                    if (disposed)
                    {
                        return;
                    }

                    levelUpDiscoveryActive = false;
                    TryShowNextLevelUp();
                },
                LevelUpDiscoveryDurationMs,
                permittedWhilePaused: true
            );
        }

        private void ShowChatFallback(
            ExperienceNotificationPacket packet)
        {
            string displayName =
                ResolveDisplayName(packet);

            if (packet.NewLevel >
                packet.PreviousLevel)
            {
                api.ShowChatMessage(
                    $"[Apprentice] {displayName} — LEVEL UP! " +
                    $"{packet.PreviousLevel} → {packet.NewLevel}"
                );

                return;
            }

            int level =
                ExpMath.GetLevel(
                    packet.NewTotalExperience
                );

            double intoLevel =
                ExpMath.GetExperienceIntoLevel(
                    packet.NewTotalExperience
                );

            double cost =
                ExpMath.GetExperienceSpanForLevel(
                    level
                );

            api.ShowChatMessage(
                $"[Apprentice] {displayName} — Level {level}: " +
                $"+{packet.GainedExperience:0.###} XP " +
                $"({intoLevel:0.##}/{cost:0.##})"
            );
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
