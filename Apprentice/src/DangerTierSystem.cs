using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class DangerTierRuntime
    {
        private const string RootKey = "apprentice:danger";

        public static DangerWorldState? WorldState { get; set; }

        public static int GetTierAt(double x, double z) =>
            WorldZoneLayout.GetLevelAt(WorldState, x, z);

        public static int GetTier(Entity entity) =>
            entity.WatchedAttributes.GetTreeAttribute(RootKey)?.GetInt("tier", 0) ?? 0;

        public static double GetDamageMultiplier(Entity? entity)
        {
            if (entity == null || entity.World.Side != EnumAppSide.Server)
            {
                return 1;
            }

            return Math.Clamp(
                entity.WatchedAttributes.GetTreeAttribute(RootKey)?
                    .GetDouble("damageMultiplier", 1) ?? 1,
                1,
                20
            );
        }
    }

    /// <summary>
    /// Applies one bounded danger tier after successful server spawn. The
    /// permanent world anchor/config is save-game data; entity state is
    /// namespaced and idempotent across chunk unload and reload.
    /// </summary>
    internal sealed class DangerTierSystem : IDisposable
    {
        private const string SaveKey = "apprentice:danger-world-v1";
        private const string RootPath = "apprentice:danger";
        private const string HealthStatSource = "apprentice-danger";

        private readonly ICoreServerAPI api;
        private readonly IServerNetworkChannel networkChannel;
        private readonly DangerDefinition definition;
        private readonly ApprenticeContentRegistry registry;
        private DangerWorldState? state;
        private bool disposed;

        public DangerTierSystem(
            ICoreServerAPI api,
            ApprenticeContentRegistry registry,
            IServerNetworkChannel networkChannel)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.networkChannel = networkChannel ??
                throw new ArgumentNullException(nameof(networkChannel));
            definition = registry.Danger;
            if (!definition.Enabled)
            {
                return;
            }

            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
            api.Event.InitWorldGenerator(OnInitWorldGenerator, "standard");
            api.Event.PlayerReady += OnPlayerReady;
            api.Event.OnEntitySpawn += OnEntitySpawn;
            api.Event.OnEntityLoaded += OnEntityLoaded;
            api.Event.OnEntityDeath += OnEntityDeath;
        }

        public DangerWorldState? State => state;

        public int GetTierAt(double x, double z) =>
            WorldZoneLayout.GetLevelAt(state, x, z);

        private void OnSaveGameLoaded()
        {
            state = null;
            DangerTierRuntime.WorldState = null;

            if (!TryLoadPersistedState())
            {
                InitializeMissingState();
                OnGameWorldSave();
            }

            NormalizeLoadedState();
        }

        private bool TryLoadPersistedState()
        {
            byte[]? bytes = api.WorldManager.SaveGame.GetData(SaveKey);
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            try
            {
                state = JsonConvert.DeserializeObject<DangerWorldState>(
                    Encoding.UTF8.GetString(bytes)
                );
                return state != null;
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    "[Apprentice] Realm layout data is unreadable; danger scaling and realm world generation are disabled for this session to protect the save: {0}",
                    exception.Message
                );
                state = null;
                return true;
            }
        }

        private void NormalizeLoadedState()
        {
            if (state == null) return;
            bool migrated = false;

            if (state.SchemaVersion < 1)
            {
                state.SchemaVersion = 1;
                migrated = true;
                api.Logger.Notification("[Apprentice] Migrated danger world data to schema 1.");
            }

            if (state.SchemaVersion < 2)
            {
                state.Enabled = true;
                state.Palette = definition.Palette.ToArray();
                state.SchemaVersion = 2;
                migrated = true;
                api.Logger.Notification(
                    "[Apprentice] Migrated danger world data to schema 2 (heatmap palette)."
                );
            }

            if (state.SchemaVersion < WorldZoneLayout.CurrentSchema)
            {
                state.RealmNames = BuildRealmNames(state.MaximumTier);
                state.WorldgenProfile = WorldZoneLayout.LegacyProfile;
                state.RealmWorldgenEnabled = false;
                state.DesertTemperatureCelsius =
                    definition.DesertTemperatureCelsius;
                state.DesertRainfall = definition.DesertRainfall;
                state.SchemaVersion = WorldZoneLayout.CurrentSchema;
                migrated = true;
                api.Logger.Notification(
                    "[Apprentice] Migrated realm layout data to schema 3. Concentric biome generation remains disabled for this existing world."
                );
            }

            if (state.Palette == null ||
                state.Palette.Length != state.MaximumTier + 1)
            {
                state.Palette = definition.Palette.ToArray();
                migrated = true;
            }
            if (state.RealmNames == null ||
                state.RealmNames.Length != state.MaximumTier + 1)
            {
                state.RealmNames = BuildRealmNames(state.MaximumTier);
                migrated = true;
            }
            DangerTierRuntime.WorldState = state;

            if (state.BaseRadius != definition.BaseRadius ||
                state.RingWidth != definition.RingWidth ||
                state.MaximumTier != definition.MaximumTier)
            {
                api.Logger.Warning(
                    "[Apprentice] Danger config differs from this world's persisted ring layout. The persisted layout remains authoritative; use a future explicit admin migration instead of silently moving tiers."
                );
            }

            if (migrated)
            {
                OnGameWorldSave();
            }
        }

        private string[] BuildRealmNames(int maximumTier)
        {
            if (definition.RealmNames.Count == maximumTier + 1)
            {
                return definition.RealmNames.ToArray();
            }

            return Enumerable.Range(0, maximumTier + 1)
                .Select(level => $"Level {level}")
                .ToArray();
        }

        private void InitializeMissingState()
        {
            var savedSpawn = api.WorldManager.SaveGame.DefaultSpawn;
            bool existingWorld = !api.WorldManager.SaveGame.IsNew;
            double anchorX;
            double anchorZ;

            if (savedSpawn != null)
            {
                anchorX = savedSpawn.x;
                anchorZ = savedSpawn.z;
            }
            else
            {
                // ServerSystemSupplyChunks generates the 1.22 spawn area at
                // the exact world-map center before DefaultSpawn exists.
                anchorX = api.WorldManager.MapSizeX / 2d;
                anchorZ = api.WorldManager.MapSizeZ / 2d;
            }

            InitializeNewState(
                anchorX,
                anchorZ,
                enableRealmWorldgen: !existingWorld
            );
            if (existingWorld)
            {
                api.Logger.Warning(
                    "[Apprentice] No realm layout was found in an existing world. Danger rings were initialized at the saved spawn, but concentric biome generation is disabled to protect existing chunks."
                );
            }
        }

        private void OnInitWorldGenerator()
        {
            if (state == null)
            {
                if (!TryLoadPersistedState())
                {
                    InitializeMissingState();
                    OnGameWorldSave();
                }
                NormalizeLoadedState();
            }

            DangerTierRuntime.WorldState = state;
        }

        private void OnPlayerReady(IServerPlayer player)
        {
            if (state == null)
            {
                if (!TryLoadPersistedState())
                {
                    InitializeMissingState();
                }
                NormalizeLoadedState();
                OnGameWorldSave();

                foreach (Entity entity in
                    api.World.LoadedEntities.Values.ToArray())
                {
                    OnEntitySpawn(entity);
                }
            }

            SendHeatmapState(player);
        }

        internal void SendHeatmapState(IServerPlayer player)
        {
            if (state == null)
            {
                api.Logger.Error(
                    "[Apprentice] Cannot send danger heatmap state because the danger world state is unavailable."
                );
                return;
            }
            networkChannel.SendPacket(
                DangerHeatmapStatePacket.FromState(state),
                player
            );
            api.Logger.Notification(
                "[Apprentice] Sent danger heatmap state to {0}.",
                player.PlayerName
            );
        }

        private void InitializeNewState(
            double anchorX,
            double anchorZ,
            bool enableRealmWorldgen)
        {
            state = new DangerWorldState
            {
                SchemaVersion = Math.Max(
                    WorldZoneLayout.CurrentSchema,
                    definition.SchemaVersion
                ),
                Enabled = definition.Enabled,
                AnchorX = anchorX,
                AnchorZ = anchorZ,
                BaseRadius = definition.BaseRadius,
                RingWidth = definition.RingWidth,
                MaximumTier = definition.MaximumTier,
                HealthPerTier = definition.HealthPerTier,
                DamagePerTier = definition.DamagePerTier,
                Palette = definition.Palette.ToArray(),
                RealmNames = BuildRealmNames(
                    definition.MaximumTier
                ),
                WorldgenProfile = enableRealmWorldgen
                    ? definition.WorldgenProfile
                    : WorldZoneLayout.LegacyProfile,
                RealmWorldgenEnabled =
                    enableRealmWorldgen &&
                    definition.RealmWorldgenEnabled,
                DesertTemperatureCelsius =
                    definition.DesertTemperatureCelsius,
                DesertRainfall = definition.DesertRainfall,
                DeepSeaDepth = definition.DeepSeaDepth,
                DeepSeaShoreWidth = definition.DeepSeaShoreWidth
            };
            DangerTierRuntime.WorldState = state;
            api.Logger.Notification(
                "[Apprentice] Initialized permanent realm anchor at X={0:0}, Z={1:0} (base radius {2:0}, ring width {3:0}, profile {4}, biome generation {5}).",
                state.AnchorX,
                state.AnchorZ,
                state.BaseRadius,
                state.RingWidth,
                state.WorldgenProfile,
                state.RealmWorldgenEnabled ? "enabled" : "disabled"
            );
        }

        private void OnGameWorldSave()
        {
            if (state == null) return;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
            api.WorldManager.SaveGame.StoreData(SaveKey, bytes);
        }

        private void OnEntitySpawn(Entity entity)
        {
            if (state == null || !IsEligible(entity)) return;

            // Never multiply a tier after another mod reuses/reloads an entity.
            ITreeAttribute? existing = entity.WatchedAttributes
                .GetTreeAttribute(RootPath);
            if (existing?.HasAttribute("schema") == true)
            {
                RestoreHealthStat(entity);
                return;
            }

            int tier = GetTierAt(entity.Pos.X, entity.Pos.Z);
            EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            float originalMaxHealth = Math.Max(1, health.MaxHealth);
            double healthMultiplier = 1 + state.HealthPerTier * tier;
            double damageMultiplier = 1 + state.DamagePerTier * tier;

            ITreeAttribute danger = entity.WatchedAttributes
                .GetOrAddTreeAttribute(RootPath);
            danger.SetInt("schema", definition.SchemaVersion);
            danger.SetInt("tier", tier);
            danger.SetDouble("originalMaxHealth", originalMaxHealth);
            danger.SetDouble("healthMultiplier", healthMultiplier);
            danger.SetDouble("damageMultiplier", damageMultiplier);
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/schema");
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/tier");
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/originalMaxHealth");
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/healthMultiplier");
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/damageMultiplier");

            ApplyHealthStat(entity, health, originalMaxHealth, healthMultiplier, preserveRatio: true);
        }

        private void OnEntityLoaded(Entity entity)
        {
            if (state == null || entity.WatchedAttributes
                .GetTreeAttribute(RootPath)?.HasAttribute("schema") != true)
            {
                return;
            }

            RestoreHealthStat(entity);
        }

        private void RestoreHealthStat(Entity entity)
        {
            EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            ITreeAttribute? danger = entity.WatchedAttributes
                .GetTreeAttribute(RootPath);
            double original = danger?.GetDouble("originalMaxHealth", health.MaxHealth) ?? health.MaxHealth;
            double multiplier = danger?.GetDouble("healthMultiplier", 1) ?? 1;
            ApplyHealthStat(entity, health, original, multiplier, preserveRatio: false);
        }

        private static void ApplyHealthStat(
            Entity entity,
            EntityBehaviorHealth health,
            double originalMaxHealth,
            double multiplier,
            bool preserveRatio)
        {
            float ratio = health.MaxHealth > 0
                ? Math.Clamp(health.Health / health.MaxHealth, 0, 1)
                : 1;
            float bonus = (float)Math.Max(0, originalMaxHealth * (multiplier - 1));
            entity.Stats.Set("maxhealthExtraPoints", HealthStatSource, bonus, false);
            health.MarkDirty();
            if (preserveRatio)
            {
                health.Health = Math.Max(1, health.MaxHealth * ratio);
            }
        }

        private bool IsEligible(Entity entity)
        {
            if (entity == null || entity is EntityPlayer || entity is not EntityAgent ||
                entity.Code == null || !entity.Alive)
            {
                return false;
            }

            string code = CanonicalCode(entity.Code).ToLowerInvariant();
            if (definition.ExcludeCodePatterns.Any(pattern => WildcardMatch(pattern, code)))
            {
                return false;
            }

            // Explicit entity assets may opt out even when a broad code pattern
            // would otherwise match. This protects story/NPC entities supplied
            // by optional mods without referencing their assemblies.
            if (entity.Properties.Attributes?["apprenticeDangerEligible"].AsBool(true) == false ||
                entity.WatchedAttributes.GetBool("domesticated", false) ||
                entity.WatchedAttributes.GetBool("tamed", false) ||
                !string.IsNullOrEmpty(entity.WatchedAttributes.GetString("ownerUid", string.Empty)))
            {
                return false;
            }

            return definition.IncludeCodePatterns.Any(pattern => WildcardMatch(pattern, code));
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            ITreeAttribute? danger = entity?.WatchedAttributes
                .GetTreeAttribute(RootPath);
            if (state == null || entity == null ||
                danger?.GetBool("lootRolled", false) == true)
            {
                return;
            }

            int tier = danger?.GetInt("tier", 0) ?? 0;
            if (tier <= 0) return;

            danger!.SetBool("lootRolled", true);
            entity.WatchedAttributes.MarkPathDirty(RootPath + "/lootRolled");

            foreach (EcologyDefinition loot in registry.Ecology)
            {
                // World-generated plants are not generic creature body loot.
                // A future humanoid alchemist/assassin loot table must opt in
                // through its own explicit entity profile instead of weakening
                // this ecology boundary.
                if (!loot.EntityDropEnabled) continue;

                int allowedLevelOrdinal =
                    loot.GetAllowedLevelOrdinal(tier);
                if (allowedLevelOrdinal <= 0) continue;
                double chance = Math.Clamp(
                    loot.ChancePerTier * allowedLevelOrdinal,
                    0,
                    0.75
                );
                if (api.World.Rand.NextDouble() >= chance) continue;

                CollectibleObject? collectible = api.World.GetItem(new AssetLocation(loot.DropCode));
                if (collectible == null) continue;
                int quantity = Math.Min(
                    loot.MaximumQuantity,
                    1 + (allowedLevelOrdinal - 1) / 3
                );
                api.World.SpawnItemEntity(
                    new ItemStack(collectible, quantity),
                    entity.Pos.XYZ.AddCopy(0, 0.25, 0)
                );
            }
        }

        private static bool WildcardMatch(string pattern, string value)
        {
            string[] parts = pattern.ToLowerInvariant().Split('*');
            int position = 0;
            bool anchoredStart = !pattern.StartsWith('*');
            bool anchoredEnd = !pattern.EndsWith('*');
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length == 0) continue;
                int found = value.IndexOf(parts[index], position, StringComparison.Ordinal);
                if (found < 0 || (anchoredStart && index == 0 && found != 0)) return false;
                position = found + parts[index].Length;
            }
            return !anchoredEnd || parts.Length == 0 || value.EndsWith(parts[^1], StringComparison.Ordinal);
        }

        private static string CanonicalCode(AssetLocation location) =>
            $"{location.Domain}:{location.Path}";

        public void Dispose()
        {
            if (disposed || !definition.Enabled) return;
            disposed = true;
            api.Event.SaveGameLoaded -= OnSaveGameLoaded;
            api.Event.GameWorldSave -= OnGameWorldSave;
            api.Event.PlayerReady -= OnPlayerReady;
            api.Event.OnEntitySpawn -= OnEntitySpawn;
            api.Event.OnEntityLoaded -= OnEntityLoaded;
            api.Event.OnEntityDeath -= OnEntityDeath;
            if (ReferenceEquals(DangerTierRuntime.WorldState, state))
            {
                DangerTierRuntime.WorldState = null;
            }
        }
    }
}
