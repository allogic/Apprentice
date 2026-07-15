using System.Collections.Generic;
using Newtonsoft.Json;

namespace Apprentice
{
    internal sealed class SkillTreeConfig
    {
        [JsonProperty("Version")]
        public int Version { get; set; }

        [JsonProperty("Trees")]
        public Dictionary<string, SkillTreeDefinition> Trees { get; set; }
            = new();
    }

    internal sealed class SkillTreeDefinition
    {
        [JsonProperty("ClassId")]
        public string ClassId { get; set; } = string.Empty;

        [JsonProperty("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("Theme")]
        public string Theme { get; set; } = string.Empty;

        [JsonProperty("PassiveDescription")]
        public string PassiveDescription { get; set; } = string.Empty;

        [JsonProperty("PassiveEffect")]
        public SkillEffectDefinition? PassiveEffect { get; set; }

        [JsonProperty("WeaponPatterns")]
        public List<string> WeaponPatterns { get; set; } = new();

        [JsonProperty("Nodes")]
        public List<SkillNodeDefinition> Nodes { get; set; } = new();
    }

    internal sealed class SkillNodeDefinition
    {
        [JsonProperty("Id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("Cost")]
        public int Cost { get; set; } = 1;

        [JsonProperty("MaxRank")]
        public int MaxRank { get; set; } = 1;

        [JsonProperty("RequiredClassLevel")]
        public int RequiredClassLevel { get; set; } = 1;

        [JsonProperty("RequiredPointsSpent")]
        public int RequiredPointsSpent { get; set; }

        [JsonProperty("Requires")]
        public List<string> Requires { get; set; } = new();

        [JsonProperty("RequiresAny")]
        public List<string> RequiresAny { get; set; } = new();

        [JsonProperty("ExclusiveGroup")]
        public string? ExclusiveGroup { get; set; }

        [JsonProperty("Column")]
        public int Column { get; set; }

        [JsonProperty("Row")]
        public int Row { get; set; }

        [JsonProperty("Capstone")]
        public bool Capstone { get; set; }

        [JsonProperty("Effects")]
        public List<SkillEffectDefinition> Effects { get; set; } = new();
    }

    internal sealed class SkillEffectDefinition
    {
        [JsonProperty("Type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("ValuePerRank")]
        public double ValuePerRank { get; set; }

        [JsonProperty("Stat")]
        public string? Stat { get; set; }

        [JsonProperty("Code")]
        public string? Code { get; set; }
    }
}
