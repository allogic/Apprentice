using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal sealed record HiddenClassDefinition(
        string Id,
        string DisplayName,
        string Description,
        IReadOnlyList<string> RequiredClasses,
        IReadOnlyList<SkillEffectDefinition> Effects
    );

    internal static class HiddenClassCatalog
    {
        public static readonly IReadOnlyList<HiddenClassDefinition> All =
            new[]
            {
                new HiddenClassDefinition(
                    "lumberjack",
                    "Lumberjack",
                    "A master of timber work who wastes neither edge nor log.",
                    new[] { "woodworker", "builder" },
                    new[]
                    {
                        Effect("DurabilitySave", 0.10, "Axe durability saved", "*:axe-*|*:*axe*"),
                        Effect("CraftYield", 0.10, "Log and plank crafting output", "*log*|*plank*")
                    }
                ),
                new HiddenClassDefinition(
                    "weaponmaster",
                    "Weapon Master",
                    "Every weapon feels familiar in the hands of a true master.",
                    new[] { "warrior", "spearman", "blacksmith" },
                    new[]
                    {
                        Effect("DurabilitySave", 0.10, "Weapon and tool durability saved", "*")
                    }
                ),
                new HiddenClassDefinition(
                    "shadowarcher",
                    "Shadow Archer",
                    "An unseen shot becomes a perfect strike.",
                    new[] { "ranger", "hunter", "animalhusbandry" },
                    new[]
                    {
                        Effect("ShadowArrowCrit", 1.00, "Critical chance with arrows against unaware PvE targets", "*:arrow-*|*:bow-*|*:crossbow-*|*:bolt-*")
                    }
                ),
                new HiddenClassDefinition(
                    "berserk",
                    "Berserk",
                    "Once every 48 in-game hours, lethal damage awakens a violent second life.",
                    new[] { "tank", "warrior", "cook" },
                    new[]
                    {
                        Effect("CheatDeath", 0.50, "Health restored when lethal damage is prevented", null)
                    }
                ),
                new HiddenClassDefinition(
                    "deepwarden",
                    "Deepwarden",
                    "Stone, darkness and pressure have become familiar allies.",
                    new[] { "miner", "shield", "builder" },
                    new[]
                    {
                        Effect("UndergroundDamageReduction", 0.15, "Damage taken while underground", null),
                        Effect("OreYield", 0.15, "Ore yield while underground", "*ore*")
                    }
                ),
                new HiddenClassDefinition(
                    "wildheart",
                    "Wildheart",
                    "Predator and caretaker instincts combine into unnatural command of the wild.",
                    new[] { "farmer", "hunter", "animalhusbandry" },
                    new[]
                    {
                        Effect("AnimalDamageReduction", 0.20, "Damage taken from animals", null),
                        Effect("MeatYield", 0.15, "Meat recovered from carcasses", "*meat*")
                    }
                ),
                new HiddenClassDefinition(
                    "grandartisan",
                    "Grand Artisan",
                    "Four crafts converge into impossible economy of material.",
                    new[] { "cook", "potter", "tailor", "leatherworker" },
                    new[]
                    {
                        Effect("CraftYield", 0.20, "Crafted item output", "*")
                    }
                ),
                new HiddenClassDefinition(
                    "stormforged",
                    "Stormforged",
                    "Metal, string and swarm-speed combine into relentless ranged pressure.",
                    new[] { "blacksmith", "ranger", "beekeeper" },
                    new[]
                    {
                        Effect("RangedDamage", 0.15, "Ranged weapon damage", null),
                        Effect("DurabilitySave", 0.15, "Ranged weapon durability saved", "*:bow-*|*:crossbow-*|*:sling-*|*:arrow-*|*:bolt-*")
                    }
                )
            };

        private static SkillEffectDefinition Effect(
            string type,
            double value,
            string stat,
            string? code)
        {
            return new SkillEffectDefinition
            {
                Type = type,
                ValuePerRank = value,
                Stat = stat,
                Code = code
            };
        }

        public static HiddenClassDefinition? Get(string id) =>
            All.FirstOrDefault(definition =>
                definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    internal static class HiddenClassData
    {
        private const string HiddenClassesKey = "hiddenclasses";
        private const string LastBerserkHourKey = "berserkLastHour";

        public static bool IsUnlocked(Entity entity, string hiddenClassId)
        {
            return entity.WatchedAttributes
                .GetTreeAttribute(ApprenticeConstants.ProgressionRootKey)?
                .GetTreeAttribute(HiddenClassesKey)?
                .GetBool(hiddenClassId, false) ?? false;
        }

        public static void Unlock(IServerPlayer player, string hiddenClassId)
        {
            ITreeAttribute root = player.Entity.WatchedAttributes
                .GetOrAddTreeAttribute(ApprenticeConstants.ProgressionRootKey);
            ITreeAttribute hidden = root.GetOrAddTreeAttribute(HiddenClassesKey);
            hidden.SetBool(hiddenClassId, true);
            player.Entity.WatchedAttributes.MarkPathDirty(
                $"{ApprenticeConstants.ProgressionRootKey}/{HiddenClassesKey}/{hiddenClassId}"
            );
        }

        public static double GetLastBerserkHour(Entity entity)
        {
            return entity.WatchedAttributes
                .GetTreeAttribute(ApprenticeConstants.ProgressionRootKey)?
                .GetDouble(LastBerserkHourKey, -100000) ?? -100000;
        }

        public static void SetLastBerserkHour(IServerPlayer player, double hour)
        {
            ITreeAttribute root = player.Entity.WatchedAttributes
                .GetOrAddTreeAttribute(ApprenticeConstants.ProgressionRootKey);
            root.SetDouble(LastBerserkHourKey, hour);
            player.Entity.WatchedAttributes.MarkPathDirty(
                $"{ApprenticeConstants.ProgressionRootKey}/{LastBerserkHourKey}"
            );
        }
    }
}
