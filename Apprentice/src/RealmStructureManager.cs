using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
    public enum RealmStructureType
    {
        BossStructure = 0,
        Dungeon = 1
    }

    [ProtoContract]
    public sealed class RealmStructureLocation
    {
        [ProtoMember(1)]
        public string Name = string.Empty;

        [ProtoMember(2)]
        public int PositionX;

        [ProtoMember(3)]
        public int PositionY;

        [ProtoMember(4)]
        public int PositionZ;

        [ProtoMember(5)]
        public int SizeX;

        [ProtoMember(6)]
        public int SizeY;

        [ProtoMember(7)]
        public int SizeZ;

        [ProtoMember(8)]
        public double Distance;

        [ProtoMember(9)]
        public RealmStructureType Type;
    }

    /// <summary>
    /// Public registration and query surface for Apprentice-owned boss
    /// structures and dungeons. Vanilla structures are never indexed.
    /// </summary>
    public static class RealmStructureManager
    {
        private static readonly object ClientGate = new();
        private static RealmStructureRegistryRuntime? serverRuntime;
        private static List<RealmStructureLocation> clientSnapshot =
            new();

        /// <summary>
        /// Registers one completed Apprentice boss structure. Position is the
        /// structure center and size is its complete block bounding size.
        /// The stable id makes repeated worldgen calls idempotent.
        /// </summary>
        public static bool RegisterBossStructure(
            string stableId,
            int level,
            string name,
            int positionX,
            int positionY,
            int positionZ,
            int sizeX,
            int sizeY,
            int sizeZ) =>
            Register(
                stableId,
                level,
                name,
                positionX,
                positionY,
                positionZ,
                sizeX,
                sizeY,
                sizeZ,
                RealmStructureType.BossStructure
            );

        /// <summary>
        /// Registers one completed Apprentice dungeon. Position is the
        /// dungeon center and size is its complete block bounding size.
        /// The stable id makes repeated worldgen calls idempotent.
        /// </summary>
        public static bool RegisterDungeon(
            string stableId,
            int level,
            string name,
            int positionX,
            int positionY,
            int positionZ,
            int sizeX,
            int sizeY,
            int sizeZ) =>
            Register(
                stableId,
                level,
                name,
                positionX,
                positionY,
                positionZ,
                sizeX,
                sizeY,
                sizeZ,
                RealmStructureType.Dungeon
            );

        public static bool Register(
            string stableId,
            int level,
            string name,
            int positionX,
            int positionY,
            int positionZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            RealmStructureType type)
        {
            RealmStructureRegistryRuntime? runtime =
                System.Threading.Volatile.Read(
                    ref serverRuntime
                );
            return runtime?.Register(
                stableId,
                level,
                name,
                positionX,
                positionY,
                positionZ,
                sizeX,
                sizeY,
                sizeZ,
                type
            ) == true;
        }

        /// <summary>
        /// Returns every registered Apprentice boss structure and dungeon in
        /// the player's current realm, nearest first.
        /// </summary>
        public static List<RealmStructureLocation>
            GetNearestCoordinates(IServerPlayer player)
        {
            if (player?.Entity == null)
            {
                return new List<RealmStructureLocation>();
            }

            DangerWorldState? state =
                DangerTierRuntime.WorldState;
            if (state == null)
            {
                return new List<RealmStructureLocation>();
            }

            int level = WorldZoneLayout.GetLevelAt(
                state,
                player.Entity.Pos.X,
                player.Entity.Pos.Z
            );
            return GetNearestCoordinates(
                level,
                player.Entity.Pos.X,
                player.Entity.Pos.Y,
                player.Entity.Pos.Z
            );
        }

        /// <summary>
        /// Returns every registered Apprentice boss structure and dungeon in
        /// one explicit realm, nearest to the supplied origin first.
        /// </summary>
        public static List<RealmStructureLocation>
            GetNearestCoordinates(
                int level,
                double originX,
                double originY,
                double originZ)
        {
            RealmStructureRegistryRuntime? runtime =
                System.Threading.Volatile.Read(
                    ref serverRuntime
                );
            return runtime?.GetNearestCoordinates(
                level,
                originX,
                originY,
                originZ
            ) ?? new List<RealmStructureLocation>();
        }

        /// <summary>
        /// Client-side snapshot delivered with the most recent confirmed realm
        /// discovery. This is the overload used by discovery callbacks and
        /// cutscenes.
        /// </summary>
        public static List<RealmStructureLocation>
            GetNearestCoordinates()
        {
            lock (ClientGate)
            {
                return clientSnapshot
                    .Select(Clone)
                    .ToList();
            }
        }

        internal static void AttachServer(
            RealmStructureRegistryRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            System.Threading.Volatile.Write(
                ref serverRuntime,
                runtime
            );
        }

        internal static void DetachServer(
            RealmStructureRegistryRuntime runtime)
        {
            if (ReferenceEquals(
                    System.Threading.Volatile.Read(
                        ref serverRuntime),
                    runtime))
            {
                System.Threading.Volatile.Write(
                    ref serverRuntime,
                    null
                );
            }
        }

        internal static void ReceiveClientSnapshot(
            IEnumerable<RealmStructureLocation>? locations)
        {
            lock (ClientGate)
            {
                clientSnapshot = (locations ??
                        Array.Empty<RealmStructureLocation>())
                    .Where(location => location != null)
                    .Select(Clone)
                    .OrderBy(location => location.Distance)
                    .ThenBy(
                        location => location.Name,
                        StringComparer.Ordinal)
                    .ThenBy(location => location.PositionX)
                    .ThenBy(location => location.PositionY)
                    .ThenBy(location => location.PositionZ)
                    .ToList();
            }
        }

        internal static void ResetClientSnapshot()
        {
            lock (ClientGate)
            {
                clientSnapshot.Clear();
            }
        }

        private static RealmStructureLocation Clone(
            RealmStructureLocation source) =>
            new()
            {
                Name = source.Name,
                PositionX = source.PositionX,
                PositionY = source.PositionY,
                PositionZ = source.PositionZ,
                SizeX = source.SizeX,
                SizeY = source.SizeY,
                SizeZ = source.SizeZ,
                Distance = source.Distance,
                Type = source.Type
            };
    }

    /// <summary>
    /// Independent persistent owner for the Apprentice-only registry. It does
    /// not modify ApprenticeModSystem or any second-developer system.
    /// </summary>
    public sealed class RealmStructureRegistrySystem : ModSystem
    {
        private const string SaveKey =
            "apprentice:realm-major-structures-v1";
        private RealmStructureRegistryRuntime? runtime;
        private ICoreServerAPI? serverApi;

        public override double ExecuteOrder() => 0.114;

        public override void StartServerSide(
            ICoreServerAPI api)
        {
            serverApi = api;
            runtime = new RealmStructureRegistryRuntime(
                api,
                SaveKey
            );
            RealmStructureManager.AttachServer(runtime);
            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
        }

        private void OnSaveGameLoaded()
        {
            runtime?.Load();
        }

        private void OnGameWorldSave()
        {
            runtime?.Save();
        }

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.SaveGameLoaded -=
                    OnSaveGameLoaded;
                serverApi.Event.GameWorldSave -=
                    OnGameWorldSave;
            }
            if (runtime != null)
            {
                RealmStructureManager.DetachServer(runtime);
            }
            runtime = null;
            serverApi = null;
            base.Dispose();
        }
    }

    internal sealed class RealmStructureRegistryRuntime
    {
        private const int SchemaVersion = 1;
        private readonly ICoreServerAPI api;
        private readonly string saveKey;
        private readonly object gate = new();
        private Dictionary<string, PersistedRealmStructure>
            entries = new(StringComparer.Ordinal);

        internal RealmStructureRegistryRuntime(
            ICoreServerAPI api,
            string saveKey)
        {
            this.api = api ??
                throw new ArgumentNullException(nameof(api));
            this.saveKey = saveKey ??
                throw new ArgumentNullException(nameof(saveKey));
        }

        internal bool Register(
            string stableId,
            int level,
            string name,
            int positionX,
            int positionY,
            int positionZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            RealmStructureType type)
        {
            string normalizedId = stableId?.Trim() ??
                string.Empty;
            string normalizedName = name?.Trim() ??
                string.Empty;
            if (!IsValid(
                    normalizedId,
                    level,
                    normalizedName,
                    positionX,
                    positionY,
                    positionZ,
                    sizeX,
                    sizeY,
                    sizeZ,
                    type,
                    out string error))
            {
                api.Logger.Error(
                    "[Apprentice] Rejected boss structure/dungeon registration {0}: {1}.",
                    normalizedId.Length == 0
                        ? "<empty>"
                        : normalizedId,
                    error
                );
                return false;
            }

            PersistedRealmStructure next = new()
            {
                StableId = normalizedId,
                Level = level,
                Name = normalizedName,
                PositionX = positionX,
                PositionY = positionY,
                PositionZ = positionZ,
                SizeX = sizeX,
                SizeY = sizeY,
                SizeZ = sizeZ,
                Type = type
            };
            lock (gate)
            {
                entries[normalizedId] = next;
            }
            return true;
        }

        internal List<RealmStructureLocation>
            GetNearestCoordinates(
                int level,
                double originX,
                double originY,
                double originZ)
        {
            if (level < 0 ||
                !double.IsFinite(originX) ||
                !double.IsFinite(originY) ||
                !double.IsFinite(originZ))
            {
                return new List<RealmStructureLocation>();
            }

            PersistedRealmStructure[] snapshot;
            lock (gate)
            {
                snapshot = entries.Values
                    .Where(entry => entry.Level == level)
                    .Select(entry => entry.Clone())
                    .ToArray();
            }

            return snapshot
                .Select(entry =>
                {
                    double dx = entry.PositionX - originX;
                    double dy = entry.PositionY - originY;
                    double dz = entry.PositionZ - originZ;
                    return new RealmStructureLocation
                    {
                        Name = entry.Name,
                        PositionX = entry.PositionX,
                        PositionY = entry.PositionY,
                        PositionZ = entry.PositionZ,
                        SizeX = entry.SizeX,
                        SizeY = entry.SizeY,
                        SizeZ = entry.SizeZ,
                        Distance = Math.Sqrt(
                            dx * dx +
                            dy * dy +
                            dz * dz
                        ),
                        Type = entry.Type
                    };
                })
                .OrderBy(location => location.Distance)
                .ThenBy(
                    location => location.Name,
                    StringComparer.Ordinal)
                .ThenBy(location => location.PositionX)
                .ThenBy(location => location.PositionY)
                .ThenBy(location => location.PositionZ)
                .ToList();
        }

        internal void Load()
        {
            byte[]? bytes =
                api.WorldManager.SaveGame.GetData(saveKey);
            if (bytes == null || bytes.Length == 0)
            {
                lock (gate)
                {
                    entries.Clear();
                }
                return;
            }

            try
            {
                PersistedRealmStructureCatalog? catalog =
                    JsonConvert.DeserializeObject<
                        PersistedRealmStructureCatalog>(
                        Encoding.UTF8.GetString(bytes)
                    );
                if (catalog == null ||
                    catalog.SchemaVersion != SchemaVersion)
                {
                    throw new InvalidOperationException(
                        "unsupported or missing schema");
                }

                Dictionary<string, PersistedRealmStructure>
                    loaded = new(StringComparer.Ordinal);
                foreach (PersistedRealmStructure entry in
                    catalog.Entries ??
                    new List<PersistedRealmStructure>())
                {
                    if (!IsValid(
                            entry.StableId,
                            entry.Level,
                            entry.Name,
                            entry.PositionX,
                            entry.PositionY,
                            entry.PositionZ,
                            entry.SizeX,
                            entry.SizeY,
                            entry.SizeZ,
                            entry.Type,
                            out string error))
                    {
                        api.Logger.Warning(
                            "[Apprentice] Ignored invalid saved boss structure/dungeon {0}: {1}.",
                            entry.StableId ?? "<empty>",
                            error
                        );
                        continue;
                    }
                    loaded[entry.StableId] = entry.Clone();
                }
                lock (gate)
                {
                    entries = loaded;
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    entries.Clear();
                }
                api.Logger.Error(
                    "[Apprentice] Boss structure/dungeon registry is unreadable; the registry is empty for this session: {0}",
                    exception.Message
                );
            }
        }

        internal void Save()
        {
            PersistedRealmStructureCatalog catalog;
            lock (gate)
            {
                catalog = new PersistedRealmStructureCatalog
                {
                    SchemaVersion = SchemaVersion,
                    Entries = entries.Values
                        .OrderBy(entry => entry.StableId,
                            StringComparer.Ordinal)
                        .Select(entry => entry.Clone())
                        .ToList()
                };
            }

            api.WorldManager.SaveGame.StoreData(
                saveKey,
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(catalog)
                )
            );
        }

        private bool IsValid(
            string? stableId,
            int level,
            string? name,
            int positionX,
            int positionY,
            int positionZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            RealmStructureType type,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                error = "stable id is empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "name is empty";
                return false;
            }
            if (level < 0 || level > 30)
            {
                error = "level is outside 0..30";
                return false;
            }
            if (positionX < 0 ||
                positionX >= api.WorldManager.MapSizeX ||
                positionY < 0 ||
                positionY >= api.WorldManager.MapSizeY ||
                positionZ < 0 ||
                positionZ >= api.WorldManager.MapSizeZ)
            {
                error = "center position is outside the world";
                return false;
            }
            if (sizeX <= 0 ||
                sizeX > api.WorldManager.MapSizeX ||
                sizeY <= 0 ||
                sizeY > api.WorldManager.MapSizeY ||
                sizeZ <= 0 ||
                sizeZ > api.WorldManager.MapSizeZ)
            {
                error = "size is not a positive world-sized bounding box";
                return false;
            }
            if (type != RealmStructureType.BossStructure &&
                type != RealmStructureType.Dungeon)
            {
                error = "type is not BossStructure or Dungeon";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    internal sealed class PersistedRealmStructureCatalog
    {
        public int SchemaVersion { get; set; } = 1;
        public List<PersistedRealmStructure> Entries
            { get; set; } = new();
    }

    internal sealed class PersistedRealmStructure
    {
        public string StableId { get; set; } = string.Empty;
        public int Level { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int PositionZ { get; set; }
        public int SizeX { get; set; }
        public int SizeY { get; set; }
        public int SizeZ { get; set; }
        public RealmStructureType Type { get; set; }

        internal PersistedRealmStructure Clone() =>
            new()
            {
                StableId = StableId,
                Level = Level,
                Name = Name,
                PositionX = PositionX,
                PositionY = PositionY,
                PositionZ = PositionZ,
                SizeX = SizeX,
                SizeY = SizeY,
                SizeZ = SizeZ,
                Type = Type
            };
    }
}
