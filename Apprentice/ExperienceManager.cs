using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal sealed class ExperienceManager
    {
        private sealed class CompiledPattern
        {
            public CompiledPattern(
                string sourcePattern,
                Regex matcher,
                double experience)
            {
                SourcePattern = sourcePattern;
                Matcher = matcher;
                Experience = experience;
            }

            public string SourcePattern { get; }
            public Regex Matcher { get; }
            public double Experience { get; }
        }

        private sealed class CompiledClass
        {
            public CompiledClass(ClassDefinition definition)
            {
                Definition = definition;
            }

            public ClassDefinition Definition { get; }

            public Dictionary<
                string,
                List<CompiledPattern>
            > Interactions { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly ICoreServerAPI serverApi;
        private readonly IServerNetworkChannel networkChannel;
        private readonly BaseConfig baseConfig;
        private readonly SkillTreeManager skillTreeManager;
        private readonly List<CompiledClass> compiledClasses =
            new();

        public ExperienceManager(
            ICoreServerAPI serverApi,
            IServerNetworkChannel networkChannel,
            ClassConfig classConfig,
            BaseConfig baseConfig,
            SkillTreeManager skillTreeManager)
        {
            this.serverApi = serverApi
                ?? throw new ArgumentNullException(
                    nameof(serverApi)
                );

            this.networkChannel = networkChannel
                ?? throw new ArgumentNullException(
                    nameof(networkChannel)
                );

            this.baseConfig = baseConfig
                ?? throw new ArgumentNullException(
                    nameof(baseConfig)
                );

            this.skillTreeManager = skillTreeManager
                ?? throw new ArgumentNullException(
                    nameof(skillTreeManager)
                );

            ArgumentNullException.ThrowIfNull(classConfig);

            CompileClassDefinitions(classConfig);
        }

        /// <summary>
        /// Preferred normalized entry point for all interaction adapters.
        /// Quantity and position are carried for future adapters; the
        /// current reward model awards one configured XP value per event.
        /// </summary>
        public int HandleInteraction(
            InteractionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return HandleInteraction(
                context.Player,
                context.Interaction,
                context.TargetCode,
                context.Quantity
            );
        }

        /// <summary>
        /// Generic entry point for all current and future event adapters.
        ///
        /// Every class is checked independently. Each matching class can
        /// receive its own configured XP amount. If several patterns
        /// inside one class match, only the matching pattern with the
        /// longest configured code string is used.
        /// </summary>
        public int HandleInteraction(
            IServerPlayer player,
            string interaction,
            AssetLocation targetCode)
        {
            return HandleInteraction(
                player,
                interaction,
                targetCode,
                quantity: 1
            );
        }

        private int HandleInteraction(
            IServerPlayer player,
            string interaction,
            AssetLocation targetCode,
            double quantity)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(targetCode);

            if (!double.IsFinite(quantity) ||
                quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    quantity,
                    "Interaction quantity must be a finite value above zero."
                );
            }

            if (string.IsNullOrWhiteSpace(interaction))
            {
                throw new ArgumentException(
                    "An interaction name is required.",
                    nameof(interaction)
                );
            }

            if (!baseConfig.AllowCreativeExperience &&
                player.WorldData.CurrentGameMode ==
                    EnumGameMode.Creative)
            {
                return 0;
            }

            string target =
                $"{targetCode.Domain}:{targetCode.Path}";

            int awardedClasses = 0;

            foreach (CompiledClass compiledClass
                     in compiledClasses)
            {
                if (!compiledClass.Interactions.TryGetValue(
                    interaction,
                    out List<CompiledPattern>? patterns))
                {
                    continue;
                }

                CompiledPattern? bestPattern =
                    FindLongestMatch(
                        patterns,
                        target
                    );

                if (bestPattern == null ||
                    bestPattern.Experience <= 0)
                {
                    continue;
                }

                double gainedExperience =
                    bestPattern.Experience *
                    quantity *
                    skillTreeManager.GetExperienceMultiplier(
                        player,
                        compiledClass.Definition.Id
                    );

                if (!double.IsFinite(gainedExperience) ||
                    gainedExperience <= 0)
                {
                    continue;
                }

                AwardExperience(
                    player,
                    compiledClass.Definition,
                    interaction,
                    target,
                    bestPattern,
                    gainedExperience,
                    quantity
                );

                awardedClasses++;
            }

            return awardedClasses;
        }

        private void AwardExperience(
            IServerPlayer player,
            ClassDefinition classDefinition,
            string interaction,
            string target,
            CompiledPattern pattern,
            double gainedExperience,
            double quantity)
        {
            ExperienceChange change =
                ProgressionData.AddExperience(
                    player,
                    classDefinition.Id,
                    gainedExperience
                );

            int previousLevel =
                ExpMath.GetLevel(change.PreviousTotal);

            int newLevel =
                ExpMath.GetLevel(change.NewTotal);

            if (newLevel != previousLevel)
            {
                skillTreeManager.OnClassLevelChanged(
                    player,
                    classDefinition.Id
                );
            }

            var packet =
                new ExperienceNotificationPacket
                {
                    ClassId = classDefinition.Id,
                    ClassDisplayName =
                        classDefinition.DisplayName,
                    Interaction = interaction,
                    TargetCode = target,
                    GainedExperience =
                        gainedExperience,
                    PreviousTotalExperience =
                        change.PreviousTotal,
                    NewTotalExperience =
                        change.NewTotal,
                    PreviousLevel = previousLevel,
                    NewLevel = newLevel
                };

            networkChannel.SendPacket(packet, player);

            if (baseConfig.LogExperienceGains)
            {
                serverApi.Logger.Notification(
                    $"{player.PlayerName} gained " +
                    $"{gainedExperience:0.###} XP in " +
                    $"'{classDefinition.Id}' from " +
                    $"{interaction}: {target}. " +
                    $"Matched '{pattern.SourcePattern}' " +
                    $"for quantity {quantity:0.###}. " +
                    $"Total: {change.NewTotal:0.###}; " +
                    $"level: {newLevel}."
                );
            }
        }

        private void CompileClassDefinitions(
            ClassConfig classConfig)
        {
            foreach (ClassDefinition classDefinition
                     in classConfig.ClassTypes.Values)
            {
                var compiledClass =
                    new CompiledClass(classDefinition);

                foreach (
                    KeyValuePair<
                        string,
                        Dictionary<string, double>
                    > interactionEntry
                    in classDefinition.Interactions)
                {
                    var compiledPatterns =
                        new List<CompiledPattern>();

                    foreach (
                        KeyValuePair<string, double> rewardEntry
                        in interactionEntry.Value)
                    {
                        string pattern = rewardEntry.Key;

                        Regex matcher = new(
                            "^" +
                            Regex.Escape(pattern)
                                .Replace(@"\*", ".*") +
                            "$",
                            RegexOptions.Compiled |
                            RegexOptions.CultureInvariant |
                            RegexOptions.IgnoreCase
                        );

                        compiledPatterns.Add(
                            new CompiledPattern(
                                pattern,
                                matcher,
                                rewardEntry.Value
                            )
                        );
                    }

                    compiledClass.Interactions[
                        interactionEntry.Key
                    ] = compiledPatterns;
                }

                compiledClasses.Add(compiledClass);
            }
        }

        private static CompiledPattern? FindLongestMatch(
            IEnumerable<CompiledPattern> patterns,
            string target)
        {
            CompiledPattern? bestMatch = null;

            foreach (CompiledPattern pattern in patterns)
            {
                if (!pattern.Matcher.IsMatch(target))
                {
                    continue;
                }

                // The user-selected rule is based on the complete
                // configured block-code pattern string length.
                // Equal-length ties keep the first entry in class.json.
                if (bestMatch == null ||
                    pattern.SourcePattern.Length >
                    bestMatch.SourcePattern.Length)
                {
                    bestMatch = pattern;
                }
            }

            return bestMatch;
        }
    }
}
