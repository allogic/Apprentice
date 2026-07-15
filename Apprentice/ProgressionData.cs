using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal readonly struct ExperienceChange
    {
        public ExperienceChange(
            double previousTotal,
            double newTotal)
        {
            PreviousTotal = previousTotal;
            NewTotal = newTotal;
        }

        public double PreviousTotal { get; }
        public double NewTotal { get; }
    }

    /// <summary>
    /// Generic server-owned class progression storage.
    ///
    /// apprentice
    /// └── classes
    ///     └── {classId from class.json}
    ///         └── experience
    /// </summary>
    internal static class ProgressionData
    {
        public static ITreeAttribute GetOrCreateClassProgress(
            IServerPlayer player,
            string classId)
        {
            ArgumentNullException.ThrowIfNull(player);
            ValidateClassId(classId);

            SyncedTreeAttribute watchedAttributes =
                player.Entity.WatchedAttributes;

            ITreeAttribute progressionRoot =
                watchedAttributes.GetOrAddTreeAttribute(
                    ApprenticeConstants.ProgressionRootKey
                );

            ITreeAttribute classesTree =
                progressionRoot.GetOrAddTreeAttribute(
                    ApprenticeConstants.ClassesTreeKey
                );

            ITreeAttribute classProgress =
                classesTree.GetOrAddTreeAttribute(classId);

            if (!classProgress.HasAttribute(
                ApprenticeConstants.ExperienceKey))
            {
                classProgress.SetDouble(
                    ApprenticeConstants.ExperienceKey,
                    0
                );

                watchedAttributes.MarkPathDirty(
                    GetClassPath(classId)
                );
            }

            return classProgress;
        }

        public static ITreeAttribute? GetClassProgress(
            Entity entity,
            string classId)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ValidateClassId(classId);

            return entity.WatchedAttributes
                .GetTreeAttribute(
                    ApprenticeConstants.ProgressionRootKey
                )?
                .GetTreeAttribute(
                    ApprenticeConstants.ClassesTreeKey
                )?
                .GetTreeAttribute(classId);
        }

        public static double GetExperience(
            Entity entity,
            string classId)
        {
            return GetClassProgress(entity, classId)?
                .GetDouble(
                    ApprenticeConstants.ExperienceKey,
                    0
                )
                ?? 0;
        }

        public static ExperienceChange AddExperience(
            IServerPlayer player,
            string classId,
            double amount)
        {
            ArgumentNullException.ThrowIfNull(player);
            ValidateClassId(classId);

            if (!double.IsFinite(amount) ||
                amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    amount,
                    "Experience gain must be a finite, " +
                    "non-negative number."
                );
            }

            ITreeAttribute classProgress =
                GetOrCreateClassProgress(
                    player,
                    classId
                );

            double previousTotal =
                classProgress.GetDouble(
                    ApprenticeConstants.ExperienceKey,
                    0
                );

            double newTotal = previousTotal + amount;

            classProgress.SetDouble(
                ApprenticeConstants.ExperienceKey,
                newTotal
            );

            player.Entity.WatchedAttributes.MarkPathDirty(
                GetExperiencePath(classId)
            );

            return new ExperienceChange(
                previousTotal,
                newTotal
            );
        }

        private static string GetClassPath(string classId)
        {
            return
                $"{ApprenticeConstants.ProgressionRootKey}/" +
                $"{ApprenticeConstants.ClassesTreeKey}/" +
                classId;
        }

        private static string GetExperiencePath(
            string classId)
        {
            return
                $"{GetClassPath(classId)}/" +
                ApprenticeConstants.ExperienceKey;
        }

        private static void ValidateClassId(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException(
                    "A class ID is required.",
                    nameof(classId)
                );
            }

            if (classId.Contains('/'))
            {
                throw new ArgumentException(
                    "Class IDs cannot contain '/'.",
                    nameof(classId)
                );
            }
        }
    }
}
