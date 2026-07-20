using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Apprentice
{
    internal sealed class ApprenticeContentConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public List<GrandmasterArtifactDefinition> GrandmasterArtifacts { get; set; } = new();
        public List<HiddenClassDefinition> Discoveries { get; set; } = new();
        public List<CementationChargeDefinition> CementationCharges { get; set; } = new();
        public List<PoisonDefinition> Poisons { get; set; } = new();
        public DangerDefinition Danger { get; set; } = new();
        public List<EcologyDefinition> Ecology { get; set; } = new();
    }

    internal sealed class GrandmasterArtifactDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Code { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public string Kind { get; set; } = "item";
        public string Feature { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }

    internal sealed class CementationIngredientDefinition
    {
        public string Code { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    internal sealed class CementationChargeDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public double DurationDays { get; set; }
        public string Output { get; set; } = string.Empty;
        public int OutputQuantity { get; set; }
        public List<CementationIngredientDefinition> Inputs { get; set; } = new();
        public List<string> RequiredDiscoveries { get; set; } = new();
        public List<string> RequiredItems { get; set; } = new();
        public string FuelCode { get; set; } = "game:charcoal";
        public int FuelQuantity { get; set; }
        public string RefractoryCode { get; set; } = string.Empty;
        public int RefractoryQuantity { get; set; } = 1;
    }

    internal sealed class PoisonDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string ArrowCode { get; set; } = string.Empty;
        public double DamagePerSecond { get; set; }
        public int DurationSeconds { get; set; }
        public int MaximumDurationSeconds { get; set; }
        public string? RequiredDiscovery { get; set; }
    }

    internal sealed class DangerDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public double BaseRadius { get; set; } = 4000;
        public double RingWidth { get; set; } = 2000;
        public int MaximumTier { get; set; } = 10;
        public double HealthPerTier { get; set; } = 0.35;
        public double DamagePerTier { get; set; } = 0.18;
        public List<string> IncludeCodePatterns { get; set; } = new();
        public List<string> ExcludeCodePatterns { get; set; } = new();
        public List<string> Palette { get; set; } = new();
    }

    internal sealed class EcologyDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string DropCode { get; set; } = string.Empty;
        public int MinimumTier { get; set; }
        public double ChancePerTier { get; set; }
        public int MaximumQuantity { get; set; } = 1;
        public string? WorldgenBlockCode { get; set; }
        public double WorldgenChancePerTier { get; set; }
        public int WorldgenAttemptsPerChunk { get; set; } = 1;
    }

    /// <summary>
    /// Immutable, validated 2.7 content definitions. A malformed child entry is
    /// rejected without taking unrelated Apprentice systems down with it.
    /// Collectible resolution is intentionally deferred until AssetsFinalize.
    /// </summary>
    internal sealed class ApprenticeContentRegistry
    {
        private static readonly AssetLocation ConfigAsset =
            new("apprentice", "config/content-2.7.json");

        private readonly List<GrandmasterArtifactDefinition> artifacts;
        private readonly List<HiddenClassDefinition> discoveries;
        private readonly List<CementationChargeDefinition> charges;
        private readonly List<PoisonDefinition> poisons;
        private readonly List<EcologyDefinition> ecology;

        private ApprenticeContentRegistry(
            int schemaVersion,
            List<GrandmasterArtifactDefinition> artifacts,
            List<HiddenClassDefinition> discoveries,
            List<CementationChargeDefinition> charges,
            List<PoisonDefinition> poisons,
            DangerDefinition danger,
            List<EcologyDefinition> ecology)
        {
            SchemaVersion = schemaVersion;
            this.artifacts = artifacts;
            this.discoveries = discoveries;
            this.charges = charges;
            this.poisons = poisons;
            Danger = danger;
            this.ecology = ecology;
        }

        public static ApprenticeContentRegistry Empty { get; } = new(
            1,
            new(),
            new(),
            new(),
            new(),
            new DangerDefinition { Enabled = false },
            new()
        );

        public int SchemaVersion { get; }
        public IReadOnlyList<GrandmasterArtifactDefinition> Artifacts => artifacts;
        public IReadOnlyList<HiddenClassDefinition> Discoveries => discoveries;
        public IReadOnlyList<CementationChargeDefinition> Charges => charges;
        public IReadOnlyList<PoisonDefinition> Poisons => poisons;
        public DangerDefinition Danger { get; private set; }
        public IReadOnlyList<EcologyDefinition> Ecology => ecology;

        /// <summary>
        /// Makes every Apprentice collectible visible in both the normal
        /// creative inventory and a dedicated Apprentice tab.  The JSON
        /// declarations remain the primary source; this finalization pass is
        /// deliberately redundant so a third-party asset patch cannot
        /// accidentally make all 2.7 content undiscoverable.
        /// </summary>
        public static int EnsureCreativeInventoryPresence(ICoreAPI api)
        {
            ArgumentNullException.ThrowIfNull(api);
            int exposed = 0;

            foreach (Item item in api.World.Items)
            {
                if (ExposeInCreativeInventory(item, "items"))
                {
                    exposed++;
                }
            }

            foreach (Block block in api.World.Blocks)
            {
                if (ExposeInCreativeInventory(block, "construction"))
                {
                    exposed++;
                }
            }

            return exposed;
        }

        private static bool ExposeInCreativeInventory(
            CollectibleObject collectible,
            string category)
        {
            if (collectible?.Code?.Domain != "apprentice")
            {
                return false;
            }

            // Intermediate trap frames are replicated implementation state,
            // not placeable creative content. Exposing them lets players
            // bypass the kit and create permanently half-animated traps.
			if (collectible.Code.Path == "advancedtrap-opening1" ||
				collectible.Code.Path == "advancedtrap-opening2" ||
				collectible.Code.Path == "advancedtrap-opening3" ||
				collectible.Code.Path == "advancedtrap-opening4" ||
				collectible.Code.Path == "advancedtrap-triggered")
            {
                collectible.CreativeInventoryTabs = Array.Empty<string>();
                return false;
            }

            string[] existing = collectible.CreativeInventoryTabs
                ?? Array.Empty<string>();
            collectible.CreativeInventoryTabs = existing
                .Concat(new[] { "general", category, "apprentice" })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        public static ApprenticeContentRegistry Load(ICoreAPI api)
        {
            ArgumentNullException.ThrowIfNull(api);
            IAsset? asset = api.Assets.TryGet(ConfigAsset);
            if (asset == null)
            {
                api.Logger.Error("[Apprentice] Registry file {0} is missing; 2.3-2.7 content is disabled.", ConfigAsset);
                return Empty;
            }

            ApprenticeContentConfig? source;
            try
            {
                source = JsonConvert.DeserializeObject<ApprenticeContentConfig>(asset.ToText());
            }
            catch (Exception exception)
            {
                api.Logger.Error("[Apprentice] Registry file {0} could not be parsed; 2.3-2.7 content is disabled: {1}", ConfigAsset, exception.Message);
                return Empty;
            }

            if (source == null || source.SchemaVersion < 1)
            {
                api.Logger.Error("[Apprentice] Registry file {0} has no supported SchemaVersion; 2.3-2.7 content is disabled.", ConfigAsset);
                return Empty;
            }

            List<GrandmasterArtifactDefinition> artifacts = ValidateMany(
                api,
                "artifact",
                source.GrandmasterArtifacts,
                value => value.Code,
                ValidateArtifact
            );
            List<HiddenClassDefinition> discoveries = ValidateMany(
                api,
                "discovery",
                source.Discoveries,
                value => value.Id,
                ValidateDiscovery
            );
            List<CementationChargeDefinition> charges = ValidateMany(
                api,
                "cementation charge",
                source.CementationCharges,
                value => value.Id,
                ValidateCharge
            );
            List<PoisonDefinition> poisons = ValidateMany(
                api,
                "poison",
                source.Poisons,
                value => value.Id,
                ValidatePoison
            );
            List<EcologyDefinition> ecology = ValidateMany(
                api,
                "ecology",
                source.Ecology,
                value => value.Id,
                ValidateEcology
            );

            DangerDefinition danger = source.Danger ?? new DangerDefinition { Enabled = false };
            string? dangerError = ValidateDanger(danger);
            if (dangerError != null)
            {
                api.Logger.Error("[Apprentice] Disabled registry danger definition in {0}: {1}", ConfigAsset, dangerError);
                danger.Enabled = false;
            }

            api.Logger.Notification(
                "[Apprentice] Registry schema {0}: {1} artifacts, {2} discoveries, {3} charges, {4} poisons and {5} ecology entries enabled.",
                source.SchemaVersion,
                artifacts.Count,
                discoveries.Count,
                charges.Count,
                poisons.Count,
                ecology.Count
            );

            return new ApprenticeContentRegistry(
                source.SchemaVersion,
                artifacts,
                discoveries,
                charges,
                poisons,
                danger,
                ecology
            );
        }

        public void ResolveCollectibles(ICoreAPI api)
        {
            artifacts.RemoveAll(value =>
                !CollectibleExists(api, value.Code, "artifact", value.Code));

            charges.RemoveAll(value =>
            {
                IEnumerable<string> codes = value.Inputs.Select(input => input.Code)
                    .Concat(value.RequiredItems)
                    .Append(value.FuelCode)
                    .Append(value.RefractoryCode)
                    .Append(value.Output);
                return codes.Any(code =>
                    !CollectibleExists(api, code, "cementation charge", value.Id));
            });

            poisons.RemoveAll(value =>
                !CollectibleExists(api, value.ArrowCode, "poison", value.Id));

            ecology.RemoveAll(value =>
                !CollectibleExists(api, value.DropCode, "ecology", value.Id) ||
                (!string.IsNullOrWhiteSpace(value.WorldgenBlockCode) &&
                 !CollectibleExists(
                    api,
                    value.WorldgenBlockCode!,
                    "ecology worldgen",
                    value.Id
                 )));
        }

        private static bool CollectibleExists(
            ICoreAPI api,
            string code,
            string kind,
            string id)
        {
            AssetLocation location = new(code);
            if (api.World.GetItem(location) != null || api.World.GetBlock(location) != null)
            {
                return true;
            }

            api.Logger.Error(
                "[Apprentice] Disabled registry {0} '{1}' in {2}: collectible '{3}' does not resolve.",
                kind,
                id,
                ConfigAsset,
                code
            );
            return false;
        }

        private static List<T> ValidateMany<T>(
            ICoreAPI api,
            string kind,
            IEnumerable<T>? source,
            System.Func<T, string> getId,
            System.Func<T, string?> validate)
        {
            List<T> result = new();
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            foreach (T definition in source ?? Enumerable.Empty<T>())
            {
                string id = getId(definition)?.Trim() ?? string.Empty;
                string? error = validate(definition);
                if (error == null && !ids.Add(id))
                {
                    error = $"duplicate code '{id}'";
                }

                if (error != null)
                {
                    api.Logger.Error(
                        "[Apprentice] Disabled registry {0} '{1}' in {2}: {3}.",
                        kind,
                        string.IsNullOrWhiteSpace(id) ? "<empty>" : id,
                        ConfigAsset,
                        error
                    );
                    continue;
                }

                result.Add(definition);
            }
            return result;
        }

        private static string? ValidateArtifact(GrandmasterArtifactDefinition value)
        {
            value.Code = NormalizeCode(value.Code);
            value.Profession = NormalizeId(value.Profession);
            value.Kind = NormalizeId(value.Kind);
            if (!value.Enabled) return "definition is disabled";
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (!IsApprenticeCode(value.Code)) return "Code must be namespaced under apprentice";
            if (string.IsNullOrWhiteSpace(value.Profession)) return "Profession is required";
            if (value.Kind != "item" && value.Kind != "block") return "Kind must be item or block";
            return null;
        }

        private static string? ValidateDiscovery(HiddenClassDefinition value)
        {
            value.Normalize();
            if (!value.Enabled) return "definition is disabled";
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (string.IsNullOrWhiteSpace(value.Id)) return "Id is required";
            if (string.IsNullOrWhiteSpace(value.DisplayName)) return "DisplayName is required";
            if (value.RequiredClasses.Count == 0 && value.MinimumCapstones < 1)
                return "at least one RequiredClass or MinimumCapstones is required";
            return null;
        }

        private static string? ValidateCharge(CementationChargeDefinition value)
        {
            value.Id = NormalizeId(value.Id);
            value.Output = NormalizeCode(value.Output);
            value.RequiredDiscoveries = value.RequiredDiscoveries.Select(NormalizeId).ToList();
            value.RequiredItems = value.RequiredItems.Select(NormalizeCode).ToList();
            value.FuelCode = NormalizeCode(value.FuelCode);
            value.RefractoryCode = NormalizeCode(value.RefractoryCode);
            foreach (CementationIngredientDefinition input in value.Inputs)
            {
                input.Code = NormalizeCode(input.Code);
            }
            if (!value.Enabled) return "definition is disabled";
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (string.IsNullOrWhiteSpace(value.Id)) return "Id is required";
            if (value.DurationDays <= 0 || !double.IsFinite(value.DurationDays)) return "DurationDays must be finite and positive";
            if (!IsApprenticeCode(value.Output)) return "Output must be namespaced under apprentice";
            if (value.OutputQuantity < 1) return "OutputQuantity must be positive";
            if (value.Inputs.Count == 0) return "Inputs cannot be empty";
            if (value.Inputs.Any(input => string.IsNullOrWhiteSpace(input.Code) || input.Quantity < 1))
                return "every input needs a code and positive quantity";
            if (value.Inputs.GroupBy(input => input.Code, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                return "input codes must be unique";
            if (value.Inputs.Sum(input => input.Quantity) != value.OutputQuantity)
                return "the exact input count must equal OutputQuantity";
            if (value.FuelQuantity < 1 || string.IsNullOrWhiteSpace(value.FuelCode))
                return "a positive fuel quantity and code are required";
            if (value.RefractoryQuantity < 1 ||
                string.IsNullOrWhiteSpace(value.RefractoryCode))
                return "a positive refractory quantity and code are required";
            if (!value.RequiredItems.Contains(
                value.RefractoryCode,
                StringComparer.OrdinalIgnoreCase))
                return "RefractoryCode must also be listed in RequiredItems";
            return null;
        }

        private static string? ValidatePoison(PoisonDefinition value)
        {
            value.Id = NormalizeId(value.Id);
            value.ArrowCode = NormalizeCode(value.ArrowCode);
            value.RequiredDiscovery = string.IsNullOrWhiteSpace(value.RequiredDiscovery)
                ? null
                : NormalizeId(value.RequiredDiscovery);
            if (!value.Enabled) return "definition is disabled";
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (string.IsNullOrWhiteSpace(value.Id)) return "Id is required";
            if (!IsApprenticeCode(value.ArrowCode)) return "ArrowCode must be namespaced under apprentice";
            if (value.DamagePerSecond <= 0 || !double.IsFinite(value.DamagePerSecond)) return "DamagePerSecond must be finite and positive";
            if (value.DurationSeconds < 1) return "DurationSeconds must be positive";
            if (value.MaximumDurationSeconds < value.DurationSeconds) return "MaximumDurationSeconds cannot be shorter than DurationSeconds";
            return null;
        }

        private static string? ValidateDanger(DangerDefinition value)
        {
            value.IncludeCodePatterns = value.IncludeCodePatterns.Select(NormalizeCode).ToList();
            value.ExcludeCodePatterns = value.ExcludeCodePatterns.Select(NormalizeCode).ToList();
            if (!value.Enabled) return null;
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (value.BaseRadius < 0 || !double.IsFinite(value.BaseRadius)) return "BaseRadius must be finite and non-negative";
            if (value.RingWidth <= 0 || !double.IsFinite(value.RingWidth)) return "RingWidth must be finite and positive";
            if (value.MaximumTier < 1 || value.MaximumTier > 100) return "MaximumTier must be between 1 and 100";
            if (value.HealthPerTier < 0 || value.DamagePerTier < 0 ||
                !double.IsFinite(value.HealthPerTier) || !double.IsFinite(value.DamagePerTier))
                return "tier multipliers must be finite and non-negative";
            if (value.Palette.Count != value.MaximumTier + 1) return "Palette needs exactly MaximumTier + 1 colors";
            if (value.Palette.Any(color =>
                color.Length != 7 ||
                color[0] != '#' ||
                !int.TryParse(
                    color.AsSpan(1),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out _
                )))
                return "Palette colors must use hexadecimal #RRGGBB";
            return null;
        }

        private static string? ValidateEcology(EcologyDefinition value)
        {
            value.Id = NormalizeId(value.Id);
            value.DropCode = NormalizeCode(value.DropCode);
            value.WorldgenBlockCode = string.IsNullOrWhiteSpace(value.WorldgenBlockCode)
                ? null
                : NormalizeCode(value.WorldgenBlockCode);
            if (!value.Enabled) return "definition is disabled";
            if (value.SchemaVersion < 1) return "SchemaVersion must be positive";
            if (string.IsNullOrWhiteSpace(value.Id)) return "Id is required";
            if (!IsApprenticeCode(value.DropCode)) return "DropCode must be namespaced under apprentice";
            if (value.MinimumTier < 1 || value.MinimumTier > 10) return "MinimumTier must be between 1 and 10";
            if (value.ChancePerTier <= 0 || value.ChancePerTier > 1 || !double.IsFinite(value.ChancePerTier))
                return "ChancePerTier must be finite and in (0, 1]";
            if (value.MaximumQuantity < 1 || value.MaximumQuantity > 64) return "MaximumQuantity must be between 1 and 64";
            if (value.WorldgenBlockCode != null &&
                !HasAssetDomain(value.WorldgenBlockCode))
                return "WorldgenBlockCode must contain an asset domain";
            if (value.WorldgenBlockCode != null &&
                (value.WorldgenChancePerTier <= 0 ||
                 value.WorldgenChancePerTier > 1 ||
                 !double.IsFinite(value.WorldgenChancePerTier)))
                return "WorldgenChancePerTier must be finite and in (0, 1] when worldgen is enabled";
            if (value.WorldgenAttemptsPerChunk < 1 ||
                value.WorldgenAttemptsPerChunk > 8)
                return "WorldgenAttemptsPerChunk must be between 1 and 8";
            return null;
        }

        private static bool IsApprenticeCode(string code) =>
            code.StartsWith("apprentice:", StringComparison.OrdinalIgnoreCase) &&
            code.Length > "apprentice:".Length;

        private static bool HasAssetDomain(string code)
        {
            int separator = code.IndexOf(':');
            return separator > 0 && separator < code.Length - 1;
        }

        private static string NormalizeId(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();

        private static string NormalizeCode(string? value)
        {
            string code = (value ?? string.Empty).Trim().ToLowerInvariant();
            return code.Contains(':') || code.Length == 0 ? code : "game:" + code;
        }
    }
}
