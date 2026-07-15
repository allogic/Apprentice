using System;

namespace Apprentice
{
    /// <summary>
    /// Generic behavior belongs in the base config.
    /// Class IDs, class names, interactions and XP rewards belong only
    /// in assets/apprentice/config/class.json.
    /// </summary>
    public sealed class BaseConfig
    {
        /// <summary>
        /// When true, completed actions performed in creative mode
        /// are allowed to grant class experience.
        /// </summary>
        public bool AllowCreativeExperience { get; set; } = true;

        /// <summary>
        /// Client-side switch for temporary XP notifications.
        /// </summary>
        public bool EnableExperienceNotifications { get; set; } = true;

        /// <summary>
        /// Base duration of the animated XP-bar fill.
        /// </summary>
        public int ExperienceNotificationFillDurationMs { get; set; } = 1400;

        /// <summary>
        /// Extra fill time per gained level, capped internally so a
        /// large level jump cannot block the notification queue forever.
        /// </summary>
        public int ExperienceNotificationLevelUpExtraDurationMs { get; set; } = 260;

        /// <summary>
        /// Time the completed notification remains visible.
        /// </summary>
        public int ExperienceNotificationHoldDurationMs { get; set; } = 1500;

        /// <summary>
        /// Interval for the level-up text blink.
        /// </summary>
        public int LevelUpBlinkIntervalMs { get; set; } = 160;

        /// <summary>
        /// Writes awarded XP events to the server log.
        /// </summary>
        public bool LogExperienceGains { get; set; } = false;

        internal void Normalize()
        {
            ExperienceNotificationFillDurationMs = Math.Clamp(
                ExperienceNotificationFillDurationMs,
                100,
                10_000
            );

            ExperienceNotificationLevelUpExtraDurationMs = Math.Clamp(
                ExperienceNotificationLevelUpExtraDurationMs,
                0,
                2_000
            );

            ExperienceNotificationHoldDurationMs = Math.Clamp(
                ExperienceNotificationHoldDurationMs,
                0,
                10_000
            );

            LevelUpBlinkIntervalMs = Math.Clamp(
                LevelUpBlinkIntervalMs,
                50,
                2_000
            );
        }
    }
}
