using System;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Apprentice
{
    /// <summary>
    /// Safety boundary for authoritative player hit tracks. Vintage Story's
    /// ordinary item controller normally removes these tracks. Custom attack
    /// lifecycles must provide the matching stop themselves.
    /// </summary>
    internal static class OneShotMeleeAnimation
    {
        private const int StopGraceMilliseconds = 80;

        public static void ScheduleStop(
            ICoreAPI api,
            EntityAgent entity,
            string? animationCode,
            float durationSeconds,
            string? resumeAnimationCode = null)
        {
            if (string.IsNullOrWhiteSpace(animationCode)) return;

            int delayMilliseconds = Math.Max(
                1,
                (int)Math.Ceiling(durationSeconds * 1000f) +
                StopGraceMilliseconds
            );

            api.Event.RegisterCallback(
                _ =>
                {
                    Stop(entity, animationCode);

                    if (!string.IsNullOrWhiteSpace(resumeAnimationCode))
                    {
                        entity.AnimManager.StartAnimation(resumeAnimationCode);
                    }
                },
                delayMilliseconds
            );
        }

        public static void Stop(EntityAgent entity, string? animationCode)
        {
            if (!string.IsNullOrWhiteSpace(animationCode))
            {
                entity.StopAnimation(animationCode);
            }
        }
    }
}
