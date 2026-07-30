using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace Apprentice
{
    internal sealed class ShatteredHighlandsRuinsGenerator
    {
        internal const int ShatteredHighlandsLevel = 7;
        internal const int BoundaryExclusionWidth = 192;
        internal const int CultureCount = 6;
        internal const int MinimumCitySpacing = 3600;
        internal const int CorruptionRadius = 720;

        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const int CorruptionWarpMargin = 160;
        private const int CityGridSize = 4096;
        private const int CityGridJitter = 160;
        private const int CityRiftSearchRadiusCells = 2;
        private const int CityFootprintRadius = 224;
        private const int MaximumTerraceRelief = 40;
        private const int LandformCellSize = 768;
        private const int RiftLandformPercent = 34;
        private const string CityGroup = "apprentice-highlands-city";
        private const string AnchorPrefix =
            "apprenticehighlands:city-anchor/";
        private const string SectorPrefix =
            "apprenticehighlands:city-sector/";
        private const string LootChestPrefix =
            "apprenticehighlands:city-loot/";
        private const ulong CandidateSalt = 0x43495459414E4348UL;
        private const ulong CorruptionSalt = 0x484F4C4C4F57574EUL;
        private const ulong LootSectorSalt = 0x4C4F4F5453454354UL;
        private const ulong LootContentSalt = 0x4C4F4F544954454DUL;
        private const int MinimumChestsPerCity = 9;
        private const int ChestCountVariation = 5;

        private static readonly string[] CultureCodes =
        {
            "crownless",
            "basilica",
            "aqueduct",
            "forum",
            "foundry",
            "necropolis"
        };

        private static readonly string[] CityPartCodes =
        {
            "landmark",
            "district",
            "infrastructure",
            "remnant"
        };

        private readonly ICoreServerAPI api;
        private readonly object generationGate = new();
        private IWorldGenBlockAccessor? worldgenBlockAccessor;
        private static IWorldGenBlockAccessor?
            sharedWorldgenBlockAccessor;
        private static System.Func<
            DangerWorldState,
            int,
            int,
            int,
            bool>? plannedCityExclusion;
        private DangerWorldState? activeState;
        private WorldGenVillage[,] cityParts =
            new WorldGenVillage[0, 0];
        private BlockPos? spawnPos;
        private int obsidianId;
        private int basaltId;
        private int basaltGravelId;
        private int basaltCrackedId;
        private int blackVeinId;
        private int gloomId;
        private int lootMarkerId;
        private int coolingMagmaSourceId;
        private Block[] chestBlocks = Array.Empty<Block>();
        private Dictionary<int, ResolvedLootTable> lootTables =
            new();
        private bool initialized;
        private int loggedCities;
        private int loggedCorruptionChunks;
        private int loggedPlacementFailures;
        private static long generatedCities;
        private static long generatedModules;
        private static long corruptedColumns;
        private static long scrubbedFlora;
        private static long cityGenerationTicks;
        private static long evaluatedCityChunks;
        private static long candidateHashMatches;
        private static long valleyCandidateMatches;
        private static long nativePlacementFailures;
        private static long generatedLootChests;

        internal ShatteredHighlandsRuinsGenerator(
            ICoreServerAPI api)
        {
            this.api = api ??
                throw new ArgumentNullException(nameof(api));
        }

        internal bool Initialized => initialized;

        internal static IWorldGenBlockAccessor?
            SharedWorldgenBlockAccessor =>
                sharedWorldgenBlockAccessor;

        internal static bool IsWithinPlannedCityFootprint(
            DangerWorldState state,
            int worldX,
            int worldZ,
            int additionalRadius)
        {
            System.Func<
                DangerWorldState,
                int,
                int,
                int,
                bool>? resolver =
                    System.Threading.Volatile.Read(
                        ref plannedCityExclusion
                    );
            return resolver?.Invoke(
                state,
                worldX,
                worldZ,
                additionalRadius
            ) == true;
        }

        internal static long GeneratedCities =>
            System.Threading.Interlocked.Read(ref generatedCities);

        internal static long GeneratedModules =>
            System.Threading.Interlocked.Read(ref generatedModules);

        internal static long CorruptedColumns =>
            System.Threading.Interlocked.Read(ref corruptedColumns);

        internal static long ScrubbedFlora =>
            System.Threading.Interlocked.Read(ref scrubbedFlora);

        internal static long GeneratedLootChests =>
            System.Threading.Interlocked.Read(
                ref generatedLootChests
            );

        internal void OnWorldgenBlockAccessor(
            IChunkProviderThread chunkProvider)
        {
            worldgenBlockAccessor =
                chunkProvider.GetBlockAccessor(false);
            sharedWorldgenBlockAccessor =
                worldgenBlockAccessor;
        }

        internal bool Initialize(
            DangerWorldState state,
            out string error)
        {
            Reset();
            if (state == null)
            {
                error = "the persisted realm layout is missing";
                return false;
            }

            try
            {
                IAsset structureAsset = api.Assets.Get(
                    new AssetLocation(
                        "apprenticehighlands",
                        "worldgen/structures.json"
                    )
                );
                IAsset cityAsset = api.Assets.Get(
                    new AssetLocation(
                        "apprenticehighlands",
                        "worldgen/cities.json"
                    )
                );
                WorldGenStructuresConfig structureConfig =
                    structureAsset
                        .ToObject<WorldGenStructuresConfig>();
                WorldGenVillageConfig cityConfig =
                    cityAsset.ToObject<WorldGenVillageConfig>();
                structureConfig.Structures ??=
                    Array.Empty<WorldGenStructure>();
                structureConfig.SchematicYOffsets ??=
                    new Dictionary<string, int>();
                structureConfig.RocktypeRemapGroups ??=
                    new Dictionary<
                        string,
                        Dictionary<AssetLocation, AssetLocation>
                    >();
                structureConfig.LoadedSchematicsCache =
                    new Dictionary<
                        string,
                        BlockSchematicStructure[]
                    >();

                BlockLayerConfig blockLayers =
                    BlockLayerConfig.GetInstance(api);
                structureConfig.ResolveRemaps(
                    api,
                    blockLayers.RockStrata
                );
                WorldGenVillage[] loadedCityParts =
                    cityConfig.VillageTypes ??
                    Array.Empty<WorldGenVillage>();
                if (loadedCityParts.Length !=
                    CultureCount * CityPartCodes.Length)
                {
                    throw new InvalidOperationException(
                        $"expected {CultureCount * CityPartCodes.Length} " +
                        $"city part groups, loaded {loadedCityParts.Length}"
                    );
                }

                HashSet<string> seenCultures =
                    new(StringComparer.Ordinal);
                cityParts = new WorldGenVillage[
                    CultureCount,
                    CityPartCodes.Length
                ];
                for (int cultureIndex = 0;
                    cultureIndex < CultureCount;
                    cultureIndex++)
                {
                    for (int partIndex = 0;
                        partIndex < CityPartCodes.Length;
                        partIndex++)
                    {
                        string culture =
                            CultureCodes[cultureIndex];
                        string part =
                            CityPartCodes[partIndex];
                        string expectedCode =
                            "highlands-city-" +
                            culture + "-" + part;
                        WorldGenVillage city =
                            loadedCityParts.FirstOrDefault(
                                candidate =>
                                    string.Equals(
                                        candidate.Code,
                                        expectedCode,
                                        StringComparison.Ordinal
                                    )
                            ) ??
                            throw new InvalidOperationException(
                                $"missing city part {expectedCode}"
                            );
                        bool invalidDistance =
                            partIndex == 0
                                ? city.MinGroupDistance <
                                    MinimumCitySpacing
                                : city.MinGroupDistance != 0;
                        if (!string.Equals(
                                city.Group,
                                CityGroup,
                                StringComparison.Ordinal) ||
                            invalidDistance ||
                            !seenCultures.Add(city.Code))
                        {
                            throw new InvalidOperationException(
                                $"invalid city part {expectedCode}"
                            );
                        }

                        cityParts[
                            cultureIndex,
                            partIndex
                        ] = city;
                        city.Init(
                            api,
                            blockLayers,
                            structureConfig,
                            structureConfig
                                .resolvedRocktypeRemapGroups,
                            structureConfig.SchematicYOffsets,
                            null,
                            blockLayers.RockStrata,
                            new LCGRandom(
                                api.WorldManager.Seed +
                                cultureIndex * 31 +
                                partIndex +
                                0x48534C
                            )
                        );
                    }
                }

                obsidianId = ResolveBlockId(
                    "game:rock-obsidian"
                );
                basaltId = ResolveBlockId(
                    "game:rock-basalt"
                );
                basaltGravelId = ResolveBlockId(
                    "game:gravel-basalt"
                );
                basaltCrackedId = ResolveBlockId(
                    "game:crackedrock-basalt"
                );
                blackVeinId = ResolveBlockId(
                    "apprenticehighlands:blackvein"
                );
                gloomId = ResolveBlockId(
                    "apprenticehighlands:gloom"
                );
                lootMarkerId = ResolveBlockId(
                    "apprenticehighlands:lootmarker"
                );
                coolingMagmaSourceId = ResolveBlockId(
                    "apprenticehighlands:coolingmagma-still-7"
                );
                if (obsidianId <= 0 ||
                    basaltId <= 0 ||
                    basaltGravelId <= 0 ||
                    basaltCrackedId <= 0 ||
                    blackVeinId <= 0 ||
                    gloomId <= 0 ||
                    lootMarkerId <= 0 ||
                    coolingMagmaSourceId <= 0)
                {
                    throw new InvalidOperationException(
                        "one or more Highlands city blocks did not load"
                    );
                }
                chestBlocks = new[]
                    {
                        "game:chest-north",
                        "game:chest-east",
                        "game:chest-south",
                        "game:chest-west"
                    }
                    .Select(code =>
                        api.World.GetBlock(
                            new AssetLocation(code)
                        )
                    )
                    .Where(block =>
                        block != null &&
                        block.Id > 0
                    )
                    .Cast<Block>()
                    .ToArray();
                if (chestBlocks.Length != 4)
                {
                    throw new InvalidOperationException(
                        "the four oriented vanilla chest blocks did not load"
                    );
                }
                lootTables = LoadLootTables();

                PlayerSpawnPos? defaultSpawn =
                    api.WorldManager.SaveGame.DefaultSpawn;
                spawnPos = defaultSpawn != null
                    ? new BlockPos(
                        defaultSpawn.x,
                        defaultSpawn.y.GetValueOrDefault(),
                        defaultSpawn.z
                    )
                    : new BlockPos(
                        api.WorldManager.MapSizeX / 2,
                        TerraGenConfig.seaLevel,
                        api.WorldManager.MapSizeZ / 2
                    );
                activeState = state;
                plannedCityExclusion =
                    IsInsidePlannedCityFootprint;
                initialized = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                cityParts = new WorldGenVillage[0, 0];
                error =
                    exception.GetType().Name + ": " +
                    exception.Message;
                return false;
            }
        }

        internal void Reset()
        {
            activeState = null;
            plannedCityExclusion = null;
            cityParts = new WorldGenVillage[0, 0];
            spawnPos = null;
            obsidianId = 0;
            basaltId = 0;
            basaltGravelId = 0;
            basaltCrackedId = 0;
            blackVeinId = 0;
            gloomId = 0;
            lootMarkerId = 0;
            coolingMagmaSourceId = 0;
            chestBlocks = Array.Empty<Block>();
            lootTables = new Dictionary<
                int,
                ResolvedLootTable
            >();
            initialized = false;
            loggedCities = 0;
            loggedCorruptionChunks = 0;
            loggedPlacementFailures = 0;
            System.Threading.Interlocked.Exchange(
                ref generatedCities,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref generatedModules,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref corruptedColumns,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref scrubbedFlora,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref cityGenerationTicks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref evaluatedCityChunks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref candidateHashMatches,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref valleyCandidateMatches,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref nativePlacementFailures,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref generatedLootChests,
                0
            );
        }

        internal void OnCityChunkColumnGeneration(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            IWorldGenBlockAccessor? blockAccessor =
                worldgenBlockAccessor;
            if (!initialized ||
                state == null ||
                blockAccessor == null ||
                !TerraGenConfig.GenerateStructures)
            {
                return;
            }

            int centerX =
                request.ChunkX * ChunkSize + ChunkSize / 2;
            int centerZ =
                request.ChunkZ * ChunkSize + ChunkSize / 2;
            if (!TryGetPlannedCity(
                    state,
                    centerX,
                    centerZ,
                    CityFootprintRadius,
                    out PlannedCity? city))
            {
                return;
            }
            System.Threading.Interlocked.Increment(
                ref evaluatedCityChunks
            );
            System.Threading.Interlocked.Increment(
                ref candidateHashMatches
            );

            int partIndex = SelectPlannedCityPart(
                city!,
                request.ChunkX,
                request.ChunkZ,
                centerX,
                centerZ
            );
            if (partIndex < 0)
            {
                return;
            }
            bool isPrimaryCityChunk =
                IsPrimaryCityChunk(
                    city!,
                    request.ChunkX,
                    request.ChunkZ
                );
            ushort[] heights =
                request.Chunks[0]
                    .MapChunk
                    .WorldGenTerrainHeightMap;
            int centerY = heights[
                (ChunkSize / 2) * ChunkSize +
                ChunkSize / 2
            ];
            if (centerY <= TerraGenConfig.seaLevel + 5 ||
                centerY >=
                    api.WorldManager.MapSizeY - 48)
            {
                return;
            }
            System.Threading.Interlocked.Increment(
                ref valleyCandidateMatches
            );

            int cultureIndex = city!.Culture;
            ulong signature = city.Signature;
            IMapRegion mapRegion =
                request.Chunks[0].MapChunk.MapRegion;
            GetClimateCorners(
                mapRegion,
                request.ChunkX,
                request.ChunkZ,
                out int climateUpLeft,
                out int climateUpRight,
                out int climateBotLeft,
                out int climateBotRight
            );

            List<GeneratedStructure> modules = new();
            string signatureCode =
                signature.ToString("x16");
            string sectorCode =
                SectorPrefix +
                request.ChunkX + "/" +
                request.ChunkZ;
            long started = Stopwatch.GetTimestamp();
            bool generated = false;
            lock (generationGate)
            {
                lock (mapRegion.GeneratedStructures)
                {
                    if (mapRegion.GeneratedStructures.Any(
                            existing =>
                                string.Equals(
                                    existing.Code,
                                    sectorCode,
                                    StringComparison.Ordinal
                                )
                        ))
                    {
                        return;
                    }
                }
                blockAccessor.BeginColumn();
                generated = TryGenerateCityPart(
                    cityParts[
                        cultureIndex,
                        partIndex
                    ],
                    blockAccessor,
                    mapRegion,
                    centerX,
                    centerY,
                    centerZ,
                    climateUpLeft,
                    climateUpRight,
                    climateBotLeft,
                    climateBotRight,
                    modules
                );
                if (generated &&
                    modules.Count > 0 &&
                    partIndex == 0)
                {
                    FinalizeLandmarkFountain(
                        blockAccessor,
                        modules[0],
                        isPrimaryCityChunk,
                        request.ChunkX,
                        request.ChunkZ
                    );
                }
                if (generated &&
                    modules.Count > 0 &&
                    partIndex == 1)
                {
                    bool selectedForLoot =
                        IsLootChestSector(
                            city!,
                            request.ChunkX,
                            request.ChunkZ
                        );
                    FinalizeDistrictLoot(
                        city!,
                        blockAccessor,
                        mapRegion,
                        request.ChunkX,
                        request.ChunkZ,
                        modules[0],
                        selectedForLoot
                    );
                }
                if (generated && isPrimaryCityChunk)
                {
                    string anchorCode =
                        AnchorPrefix +
                        CultureCodes[cultureIndex] +
                        "/" +
                        signatureCode;
                    bool alreadyRegistered;
                    lock (mapRegion.GeneratedStructures)
                    {
                        alreadyRegistered =
                            mapRegion.GeneratedStructures
                                .Any(existing =>
                                    string.Equals(
                                        existing.Code,
                                        anchorCode,
                                        StringComparison.Ordinal
                                    )
                                );
                    }
                    if (!alreadyRegistered)
                    {
                        mapRegion.AddGeneratedStructure(
                            new GeneratedStructure
                            {
                                Code = anchorCode,
                                Group = CityGroup,
                                Location = new Cuboidi(
                                    city.CenterX,
                                    centerY,
                                    city.CenterZ,
                                    city.CenterX + 1,
                                    centerY + 1,
                                    city.CenterZ + 1
                                ),
                                SuppressTreesAndShrubs =
                                    true,
                                SuppressRivulets = true
                            }
                        );
                    }
                }
                if (generated && modules.Count > 0)
                {
                    mapRegion.AddGeneratedStructure(
                        new GeneratedStructure
                        {
                            Code = sectorCode,
                            Group = CityGroup,
                            Location = new Cuboidi(
                                centerX,
                                centerY,
                                centerZ,
                                centerX + 1,
                                centerY + 1,
                                centerZ + 1
                            ),
                            SuppressTreesAndShrubs =
                                true,
                            SuppressRivulets = true
                        }
                    );
                }
            }

            if (!generated || modules.Count == 0)
            {
                System.Threading.Interlocked.Increment(
                    ref nativePlacementFailures
                );
                if (System.Threading.Interlocked.Increment(
                        ref loggedPlacementFailures
                    ) <= 12)
                {
                    api.Logger.Notification(
                        "[Apprentice] Level 7 city sector at {0},{1} found no supported {2} site (culture={3}, generated={4}, modules={5}).",
                        centerX,
                        centerZ,
                        CityPartCodes[partIndex],
                        CultureCodes[cultureIndex],
                        generated,
                        modules.Count
                    );
                }
                return;
            }

            long elapsed =
                Stopwatch.GetTimestamp() - started;
            if (isPrimaryCityChunk)
            {
                System.Threading.Interlocked.Increment(
                    ref generatedCities
                );
            }
            System.Threading.Interlocked.Add(
                ref generatedModules,
                modules.Count
            );
            System.Threading.Interlocked.Add(
                ref cityGenerationTicks,
                elapsed
            );
            if (isPrimaryCityChunk &&
                System.Threading.Interlocked.Increment(
                    ref loggedCities
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Founded unique Level 7 {0} city at {1},{2}: signature={3}, landmark modules={4}, distributed footprint={5} blocks, generator={6:0.0} ms.",
                    CultureCodes[cultureIndex],
                    city.CenterX,
                    city.CenterZ,
                    signatureCode,
                    modules.Count,
                    CityFootprintRadius * 2,
                    Stopwatch.GetElapsedTime(started)
                        .TotalMilliseconds
                );
            }
        }

        private bool TryGetPlannedCity(
            DangerWorldState state,
            int worldX,
            int worldZ,
            int maximumDistance,
            out PlannedCity? plannedCity)
        {
            plannedCity = null;
            long nearestDistance = long.MaxValue;
            int gridX = (int)Math.Floor(
                worldX / (double)CityGridSize
            );
            int gridZ = (int)Math.Floor(
                worldZ / (double)CityGridSize
            );
            for (int offsetZ = -1;
                offsetZ <= 1;
                offsetZ++)
            {
                for (int offsetX = -1;
                    offsetX <= 1;
                    offsetX++)
                {
                    int cellX = gridX + offsetX;
                    int cellZ = gridZ + offsetZ;
                    if (!TryCreatePlannedCity(
                            state,
                            cellX,
                            cellZ,
                            out PlannedCity? candidate) ||
                        candidate == null)
                    {
                        continue;
                    }

                    long dx =
                        worldX - candidate.CenterX;
                    long dz =
                        worldZ - candidate.CenterZ;
                    long distance =
                        dx * dx + dz * dz;
                    if (distance >= nearestDistance)
                    {
                        continue;
                    }
                    nearestDistance = distance;
                    plannedCity = candidate;
                }
            }

            return plannedCity != null &&
                nearestDistance <=
                    (long)maximumDistance *
                    maximumDistance;
        }

        private bool IsInsidePlannedCityFootprint(
            DangerWorldState state,
            int worldX,
            int worldZ,
            int additionalRadius) =>
            TryGetPlannedCity(
                state,
                worldX,
                worldZ,
                CityFootprintRadius +
                    Math.Max(0, additionalRadius),
                out _
            );

        internal bool TryGetProbeCityCenter(
            DangerWorldState state,
            out int chunkX,
            out int chunkZ)
        {
            chunkX = 0;
            chunkZ = 0;
            double outer =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    ShatteredHighlandsLevel
                );
            int minimumCellX =
                (int)Math.Floor(
                    (state.AnchorX - outer) /
                    CityGridSize
                ) - 1;
            int maximumCellX =
                (int)Math.Ceiling(
                    (state.AnchorX + outer) /
                    CityGridSize
                ) + 1;
            int minimumCellZ =
                (int)Math.Floor(
                    (state.AnchorZ - outer) /
                    CityGridSize
                ) - 1;
            int maximumCellZ =
                (int)Math.Ceiling(
                    (state.AnchorZ + outer) /
                    CityGridSize
                ) + 1;
            for (int cellZ = minimumCellZ;
                cellZ <= maximumCellZ;
                cellZ++)
            {
                for (int cellX = minimumCellX;
                    cellX <= maximumCellX;
                    cellX++)
                {
                    if (!TryCreatePlannedCity(
                            state,
                            cellX,
                            cellZ,
                            out PlannedCity? city) ||
                        city == null)
                    {
                        continue;
                    }
                    chunkX = (int)Math.Floor(
                        city.CenterX /
                        (double)ChunkSize
                    );
                    chunkZ = (int)Math.Floor(
                        city.CenterZ /
                        (double)ChunkSize
                    );
                    return true;
                }
            }
            return false;
        }

        private bool TryCreatePlannedCity(
            DangerWorldState state,
            int cellX,
            int cellZ,
            out PlannedCity? plannedCity)
        {
            if (!TryCreateUnfilteredPlannedCity(
                    state,
                    cellX,
                    cellZ,
                    out PlannedCity? candidate) ||
                candidate == null)
            {
                plannedCity = null;
                return false;
            }

            long minimumSpacingSquared =
                (long)MinimumCitySpacing *
                MinimumCitySpacing;
            for (int offsetZ = -1;
                offsetZ <= 1;
                offsetZ++)
            {
                for (int offsetX = -1;
                    offsetX <= 1;
                    offsetX++)
                {
                    if (offsetX == 0 &&
                        offsetZ == 0)
                    {
                        continue;
                    }
                    int neighbourCellX =
                        cellX + offsetX;
                    int neighbourCellZ =
                        cellZ + offsetZ;
                    if (!TryCreateUnfilteredPlannedCity(
                            state,
                            neighbourCellX,
                            neighbourCellZ,
                            out PlannedCity? neighbour) ||
                        neighbour == null)
                    {
                        continue;
                    }

                    long dx =
                        candidate.CenterX -
                        neighbour.CenterX;
                    long dz =
                        candidate.CenterZ -
                        neighbour.CenterZ;
                    if (dx * dx + dz * dz >=
                        minimumSpacingSquared)
                    {
                        continue;
                    }

                    bool neighbourWins =
                        neighbour.Signature <
                            candidate.Signature ||
                        (neighbour.Signature ==
                            candidate.Signature &&
                         (neighbourCellZ < cellZ ||
                          (neighbourCellZ == cellZ &&
                           neighbourCellX < cellX)));
                    if (neighbourWins)
                    {
                        plannedCity = null;
                        return false;
                    }
                }
            }

            plannedCity = candidate;
            return true;
        }

        private bool TryCreateUnfilteredPlannedCity(
            DangerWorldState state,
            int cellX,
            int cellZ,
            out PlannedCity? plannedCity)
        {
            ulong signature = StableHash(
                cellX,
                cellZ,
                CandidateSalt ^
                    (ulong)(uint)
                        api.WorldManager.Seed
            );
            int preferredX =
                cellX * CityGridSize +
                CityGridSize / 2 +
                (int)(signature % 321UL) -
                CityGridJitter;
            int preferredZ =
                cellZ * CityGridSize +
                CityGridSize / 2 +
                (int)((signature >> 16) %
                    321UL) -
                CityGridJitter;
            if (!TryFindRiftCityCenter(
                    state,
                    preferredX,
                    preferredZ,
                    signature,
                    out int centerX,
                    out int centerZ))
            {
                plannedCity = null;
                return false;
            }

            plannedCity = new PlannedCity(
                centerX,
                centerZ,
                (int)((signature >> 32) %
                    CultureCount),
                signature
            );
            return true;
        }

        private static bool TryFindRiftCityCenter(
            DangerWorldState state,
            int preferredX,
            int preferredZ,
            ulong signature,
            out int centerX,
            out int centerZ)
        {
            int preferredCellX = (int)Math.Floor(
                preferredX / (double)LandformCellSize
            );
            int preferredCellZ = (int)Math.Floor(
                preferredZ / (double)LandformCellSize
            );
            int localJitterX =
                (int)((signature >> 40) % 193UL) -
                96;
            int localJitterZ =
                (int)((signature >> 48) % 193UL) -
                96;
            long bestDistance = long.MaxValue;
            ulong bestTieBreaker = ulong.MaxValue;
            centerX = 0;
            centerZ = 0;

            for (int offsetZ =
                    -CityRiftSearchRadiusCells;
                offsetZ <=
                    CityRiftSearchRadiusCells;
                offsetZ++)
            {
                for (int offsetX =
                        -CityRiftSearchRadiusCells;
                    offsetX <=
                        CityRiftSearchRadiusCells;
                    offsetX++)
                {
                    int landformCellX =
                        preferredCellX + offsetX;
                    int landformCellZ =
                        preferredCellZ + offsetZ;
                    int candidateX =
                        landformCellX *
                            LandformCellSize +
                        LandformCellSize / 2 +
                        localJitterX;
                    int candidateZ =
                        landformCellZ *
                            LandformCellSize +
                        LandformCellSize / 2 +
                        localJitterZ;
                    if (!IsRiftLandformCell(
                            candidateX,
                            candidateZ) ||
                        !WorldZoneLayout.IsInsideLevelCore(
                            state,
                            ShatteredHighlandsLevel,
                            BoundaryExclusionWidth +
                                CorruptionRadius +
                                CorruptionWarpMargin,
                            candidateX + 0.5,
                            candidateZ + 0.5))
                    {
                        continue;
                    }

                    long dx =
                        candidateX - preferredX;
                    long dz =
                        candidateZ - preferredZ;
                    long distance =
                        dx * dx + dz * dz;
                    ulong tieBreaker =
                        StableLandformHash(
                            landformCellX,
                            landformCellZ
                        ) ^ signature;
                    if (distance > bestDistance ||
                        (distance == bestDistance &&
                         tieBreaker >= bestTieBreaker))
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestTieBreaker = tieBreaker;
                    centerX = candidateX;
                    centerZ = candidateZ;
                }
            }

            return bestDistance != long.MaxValue;
        }

        internal TextCommandResult LocateNearestPlannedCity(
            TextCommandCallingArgs args,
            DangerWorldState? state)
        {
            if (!initialized ||
                state == null ||
                !state.Enabled ||
                !state.RealmWorldgenEnabled)
            {
                return TextCommandResult.Error(
                    "Shattered Highlands ruin generation is not active.",
                    "apprentice-shattered-highlands-ruins-disabled"
                );
            }
            if (args.Caller.Player is not
                IServerPlayer player)
            {
                return TextCommandResult.Error(
                    "Run this command as a player.",
                    "apprentice-shattered-highlands-ruins-player"
                );
            }

            int playerX =
                (int)Math.Floor(player.Entity.Pos.X);
            int playerZ =
                (int)Math.Floor(player.Entity.Pos.Z);
            double outer =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    ShatteredHighlandsLevel
                );
            int minimumCellX =
                (int)Math.Floor(
                    (state.AnchorX - outer) /
                    CityGridSize
                ) - 1;
            int maximumCellX =
                (int)Math.Ceiling(
                    (state.AnchorX + outer) /
                    CityGridSize
                ) + 1;
            int minimumCellZ =
                (int)Math.Floor(
                    (state.AnchorZ - outer) /
                    CityGridSize
                ) - 1;
            int maximumCellZ =
                (int)Math.Ceiling(
                    (state.AnchorZ + outer) /
                    CityGridSize
                ) + 1;

            IBlockAccessor accessor =
                api.World.BlockAccessor;
            PlannedCity? nearestGenerated = null;
            long nearestGeneratedDistance =
                long.MaxValue;
            PlannedCity? nearestFresh = null;
            long nearestFreshDistance =
                long.MaxValue;
            PlannedCity? nearestFallback = null;
            long nearestFallbackDistance =
                long.MaxValue;
            for (int cellZ = minimumCellZ;
                cellZ <= maximumCellZ;
                cellZ++)
            {
                for (int cellX = minimumCellX;
                    cellX <= maximumCellX;
                    cellX++)
                {
                    if (!TryCreatePlannedCity(
                            state,
                            cellX,
                            cellZ,
                            out PlannedCity? city) ||
                        city == null)
                    {
                        continue;
                    }

                    long dx =
                        city.CenterX - playerX;
                    long dz =
                        city.CenterZ - playerZ;
                    long distance =
                        dx * dx + dz * dz;
                    if (distance <
                        nearestFallbackDistance)
                    {
                        nearestFallback = city;
                        nearestFallbackDistance =
                            distance;
                    }

                    bool hasSavedAnchor =
                        HasSavedCityAnchor(
                            city,
                            accessor,
                            out bool regionExists
                        );
                    if (hasSavedAnchor &&
                        distance <
                            nearestGeneratedDistance)
                    {
                        nearestGenerated = city;
                        nearestGeneratedDistance =
                            distance;
                    }
                    else if (!regionExists &&
                        distance <
                            nearestFreshDistance)
                    {
                        nearestFresh = city;
                        nearestFreshDistance =
                            distance;
                    }
                }
            }

            PlannedCity? nearest =
                nearestGenerated ??
                nearestFresh ??
                nearestFallback;
            long nearestDistance =
                nearestGenerated != null
                    ? nearestGeneratedDistance
                    : nearestFresh != null
                        ? nearestFreshDistance
                        : nearestFallbackDistance;
            if (nearest == null)
            {
                return TextCommandResult.Error(
                    "No valid Level 7 ruined-city plan exists for this save.",
                    "apprentice-shattered-highlands-ruins-no-plan"
                );
            }

            string signatureCode =
                nearest.Signature.ToString("x16");
            bool selectedCityGenerated =
                nearestGenerated != null;
            bool definitelyFresh =
                !selectedCityGenerated &&
                nearestFresh != null;
            int displayX =
                nearest.CenterX -
                api.WorldManager.MapSizeX / 2;
            int displayZ =
                nearest.CenterZ -
                api.WorldManager.MapSizeZ / 2;
            int distanceBlocks =
                (int)Math.Round(
                    Math.Sqrt(nearestDistance)
                );
            string culture =
                CultureCodes[nearest.Culture];
            return TextCommandResult.Success(
                $"Nearest planned Level 7 ruined city: {culture} at " +
                $"map X={displayX}, Z={displayZ} " +
                $"(distance {distanceBlocks} blocks, " +
                $"signature {signatureCode}, " +
                $"saved landmark anchor={(selectedCityGenerated ? "present" : "not present")}, " +
                $"region={(selectedCityGenerated ? "generated city" : definitelyFresh ? "fresh" : "already mapped")}). " +
                (selectedCityGenerated
                    ? "This city has generated in the save."
                    : definitelyFresh
                        ? "Approaching this fresh region will run the corrected city generator."
                        : "No fresh city region remains in this save; previously generated chunks are not retrofitted.")
            );
        }

        private static bool HasSavedCityAnchor(
            PlannedCity city,
            IBlockAccessor accessor,
            out bool regionExists)
        {
            int regionX = FloorDiv(
                city.CenterX,
                accessor.RegionSize
            );
            int regionZ = FloorDiv(
                city.CenterZ,
                accessor.RegionSize
            );
            IMapRegion? region =
                accessor.GetMapRegion(
                    regionX,
                    regionZ
                );
            regionExists = region != null;
            if (region == null)
            {
                return false;
            }

            string anchorCode =
                AnchorPrefix +
                CultureCodes[city.Culture] +
                "/" +
                city.Signature.ToString("x16");
            return SnapshotStructures(region).Any(
                structure =>
                    string.Equals(
                        structure.Code,
                        anchorCode,
                        StringComparison.Ordinal
                    )
            );
        }

        private static int SelectPlannedCityPart(
            PlannedCity city,
            int chunkX,
            int chunkZ,
            int worldX,
            int worldZ)
        {
            int cityChunkX =
                (int)Math.Floor(
                    city.CenterX /
                    (double)ChunkSize
                );
            int cityChunkZ =
                (int)Math.Floor(
                    city.CenterZ /
                    (double)ChunkSize
                );
            if (chunkX == cityChunkX &&
                chunkZ == cityChunkZ)
            {
                return 0;
            }
            int chunkOffsetX = chunkX - cityChunkX;
            int chunkOffsetZ = chunkZ - cityChunkZ;
            int chunkRadiusSquared =
                chunkOffsetX * chunkOffsetX +
                chunkOffsetZ * chunkOffsetZ;
            if (chunkRadiusSquared == 4 &&
                (chunkOffsetX == 0 ||
                    chunkOffsetZ == 0))
            {
                return 0;
            }
            if (chunkRadiusSquared > 49)
            {
                return -1;
            }

            int dx = worldX - city.CenterX;
            int dz = worldZ - city.CenterZ;
            int quarterTurns =
                (int)((city.Signature >> 44) & 3UL);
            if (((city.Signature >> 46) & 1UL) != 0)
            {
                dx = -dx;
            }
            for (int turn = 0;
                turn < quarterTurns;
                turn++)
            {
                (dx, dz) = (-dz, dx);
            }

            double radius = Math.Sqrt(
                (double)dx * dx +
                (double)dz * dz
            );
            double angle = Math.Atan2(dz, dx);
            bool infrastructure =
                city.Culture switch
                {
                    0 =>
                        Math.Abs(radius - 160) < 19 ||
                        Math.Abs(dx) < 18 ||
                        Math.Abs(dz) < 18,
                    1 =>
                        Math.Abs(
                            Math.Sin(angle * 3)
                        ) * radius < 18,
                    2 =>
                        Math.Abs(dz) < 18 ||
                        Math.Abs(Math.Abs(dz) - 96) <
                            15,
                    3 =>
                        DistanceToGridLine(dx, 64) < 12 ||
                        DistanceToGridLine(dz, 64) < 12,
                    4 =>
                        StableHash(
                            chunkX,
                            chunkZ,
                            city.Signature
                        ) % 100 < 22,
                    _ =>
                        Math.Abs(dx) < 18 ||
                        Math.Abs(Math.Abs(dx) - 96) <
                            15
                };
            if (infrastructure)
            {
                return 2;
            }

            ulong hash = StableHash(
                chunkX,
                chunkZ,
                city.Signature ^
                    0x534543544F52504CUL
            );
            int roll = (int)(hash % 100);
            if (chunkRadiusSquared <= 16)
            {
                return roll < 88
                    ? 1
                    : 3;
            }
            if (((chunkOffsetX +
                    chunkOffsetZ) & 1) != 0 &&
                roll >= 28)
            {
                return -1;
            }
            return roll < 62
                ? 1
                : 3;
        }

        private static int GetCityChestCount(
            ulong signature) =>
            MinimumChestsPerCity +
            (int)(signature %
                ChestCountVariation);

        private static bool IsLootChestSector(
            PlannedCity city,
            int chunkX,
            int chunkZ)
        {
            int cityChunkX =
                (int)Math.Floor(
                    city.CenterX /
                    (double)ChunkSize
                );
            int cityChunkZ =
                (int)Math.Floor(
                    city.CenterZ /
                    (double)ChunkSize
                );
            List<LootSectorCandidate> candidates =
                new();
            for (int offsetZ = -8;
                offsetZ <= 8;
                offsetZ++)
            {
                for (int offsetX = -8;
                    offsetX <= 8;
                    offsetX++)
                {
                    int candidateChunkX =
                        cityChunkX + offsetX;
                    int candidateChunkZ =
                        cityChunkZ + offsetZ;
                    int candidateWorldX =
                        candidateChunkX *
                        ChunkSize +
                        ChunkSize / 2;
                    int candidateWorldZ =
                        candidateChunkZ *
                        ChunkSize +
                        ChunkSize / 2;
                    if (SelectPlannedCityPart(
                            city,
                            candidateChunkX,
                            candidateChunkZ,
                            candidateWorldX,
                            candidateWorldZ
                        ) != 1)
                    {
                        continue;
                    }
                    candidates.Add(
                        new LootSectorCandidate(
                            candidateChunkX,
                            candidateChunkZ,
                            StableHash(
                                candidateChunkX,
                                candidateChunkZ,
                                city.Signature ^
                                    LootSectorSalt
                            )
                        )
                    );
                }
            }
            candidates.Sort(
                (left, right) =>
                {
                    int score =
                        left.Score.CompareTo(
                            right.Score
                        );
                    if (score != 0)
                    {
                        return score;
                    }
                    int z = left.ChunkZ.CompareTo(
                        right.ChunkZ
                    );
                    return z != 0
                        ? z
                        : left.ChunkX.CompareTo(
                            right.ChunkX
                        );
                }
            );
            int target = Math.Min(
                GetCityChestCount(city.Signature),
                candidates.Count
            );
            for (int index = 0;
                index < target;
                index++)
            {
                if (candidates[index].ChunkX ==
                        chunkX &&
                    candidates[index].ChunkZ ==
                        chunkZ)
                {
                    return true;
                }
            }
            return false;
        }

        private void FinalizeDistrictLoot(
            PlannedCity city,
            IWorldGenBlockAccessor blockAccessor,
            IMapRegion mapRegion,
            int chunkX,
            int chunkZ,
            GeneratedStructure district,
            bool selectedForLoot)
        {
            List<BlockPos> markers = new();
            BlockPos sample = new(0);
            Cuboidi location = district.Location;
            for (int y = location.Y1;
                y < location.Y2;
                y++)
            {
                for (int z = location.Z1;
                    z < location.Z2;
                    z++)
                {
                    for (int x = location.X1;
                        x < location.X2;
                        x++)
                    {
                        sample.Set(x, y, z);
                        if (blockAccessor
                                .GetBlock(sample)
                                .Id ==
                            lootMarkerId)
                        {
                            markers.Add(
                                new BlockPos(
                                    x,
                                    y,
                                    z
                                )
                            );
                        }
                    }
                }
            }

            BlockPos? selectedMarker = null;
            if (selectedForLoot &&
                markers.Count > 0)
            {
                markers.Sort(
                    (left, right) =>
                        StableHash(
                            left.X,
                            left.Z,
                            city.Signature ^
                                LootContentSalt ^
                                (ulong)(uint)left.Y
                        ).CompareTo(
                            StableHash(
                                right.X,
                                right.Z,
                                city.Signature ^
                                    LootContentSalt ^
                                    (ulong)(uint)right.Y
                            )
                        )
                );
                selectedMarker = markers[0];
            }

            foreach (BlockPos marker in markers)
            {
                blockAccessor.SetBlock(
                    0,
                    marker
                );
            }
            if (!selectedForLoot)
            {
                return;
            }
            if (selectedMarker == null)
            {
                api.Logger.Error(
                    "[Apprentice] Selected Level 7 loot district at {0},{1} contained no interior loot marker.",
                    chunkX,
                    chunkZ
                );
                return;
            }

            ulong chestHash = StableHash(
                selectedMarker.X,
                selectedMarker.Z,
                city.Signature ^
                    LootContentSalt
            );
            Block chest = chestBlocks[
                (int)(chestHash %
                    (ulong)chestBlocks.Length)
            ];
            bool placed =
                chest.TryPlaceBlockForWorldGen(
                    blockAccessor,
                    selectedMarker,
                    BlockFacing.UP,
                    new LCGRandom(
                        (long)(
                            chestHash &
                            0x7FFFFFFFFFFFFFFFUL
                        )
                    )
                );
            BlockEntity? blockEntity =
                placed
                    ? blockAccessor.GetBlockEntity(
                        selectedMarker
                    )
                    : null;
            if (!placed || blockEntity == null)
            {
                api.Logger.Error(
                    "[Apprentice] Could not create the Level 7 loot chest at {0},{1},{2} in district {3},{4}.",
                    selectedMarker.X,
                    selectedMarker.Y,
                    selectedMarker.Z,
                    chunkX,
                    chunkZ
                );
                return;
            }

            IBlockEntityContainer? container =
                blockEntity as IBlockEntityContainer;
            if (container?.Inventory == null)
            {
                // The worldgen block accessor creates the block entity before
                // its normal chunk-load initialization. Vintage Story's
                // worldgen chest pattern explicitly initializes it before the
                // inventory is read or populated.
                blockEntity.Initialize(api);
                container =
                    blockEntity as IBlockEntityContainer;
            }
            if (container?.Inventory == null)
            {
                api.Logger.Error(
                    "[Apprentice] Level 7 loot chest at {0},{1},{2} has no initialized inventory in district {3},{4}.",
                    selectedMarker.X,
                    selectedMarker.Y,
                    selectedMarker.Z,
                    chunkX,
                    chunkZ
                );
                blockAccessor.SetBlock(0, selectedMarker);
                return;
            }

            int level = Math.Clamp(
                WorldZoneLayout.GetLevelAt(
                    activeState!,
                    selectedMarker.X,
                    selectedMarker.Z
                ),
                1,
                9
            );
            PopulateLootChest(
                container.Inventory,
                level,
                chestHash
            );
            blockEntity.MarkDirty(true);
            blockAccessor.MarkBlockEntityDirty(
                selectedMarker
            );
            mapRegion.AddGeneratedStructure(
                new GeneratedStructure
                {
                    Code =
                        LootChestPrefix +
                        city.Signature
                            .ToString("x16") +
                        "/" +
                        chunkX +
                        "/" +
                        chunkZ,
                    Group = CityGroup,
                    Location = new Cuboidi(
                        selectedMarker.X,
                        selectedMarker.Y,
                        selectedMarker.Z,
                        selectedMarker.X + 1,
                        selectedMarker.Y + 1,
                        selectedMarker.Z + 1
                    ),
                    SuppressTreesAndShrubs = true,
                    SuppressRivulets = true
                }
            );
            System.Threading.Interlocked.Increment(
                ref generatedLootChests
            );
        }

        private void FinalizeLandmarkFountain(
            IWorldGenBlockAccessor blockAccessor,
            GeneratedStructure landmark,
            bool isPrimaryCityChunk,
            int chunkX,
            int chunkZ)
        {
            // Every landmark template contains the complete fountain court so
            // whichever variant is selected for the city center is valid.
            // Only that primary landmark keeps poison. Secondary monuments
            // keep their basins but may not multiply the city's poison source.
            if (isPrimaryCityChunk)
            {
                return;
            }

            Cuboidi location = landmark.Location;
            BlockPos sample = new(0);
            int convertedCells = 0;
            for (int y = location.Y1;
                y < location.Y2;
                y++)
            {
                for (int z = location.Z1;
                    z < location.Z2;
                    z++)
                {
                    for (int x = location.X1;
                        x < location.X2;
                        x++)
                    {
                        sample.Set(x, y, z);
                        Block solid =
                            blockAccessor.GetBlock(
                                sample
                            );
                        Block fluid =
                            blockAccessor.GetBlock(
                                sample,
                                2
                            );
                        bool toxicSolid =
                            IsToxicWater(solid);
                        bool toxicFluid =
                            IsToxicWater(fluid);
                        if (!toxicSolid &&
                            !toxicFluid)
                        {
                            continue;
                        }
                        if (toxicSolid)
                        {
                            blockAccessor.SetBlock(
                                0,
                                sample
                            );
                        }
                        blockAccessor.SetBlock(
                            coolingMagmaSourceId,
                            sample,
                            2
                        );
                        convertedCells++;
                    }
                }
            }

            if (convertedCells <= 0)
            {
                api.Logger.Warning(
                    "[Apprentice] Secondary Level 7 landmark at {0},{1} contained no poisonous fountain cells to normalize.",
                    chunkX,
                    chunkZ
                );
            }
        }

        private static bool IsToxicWater(
            Block block) =>
            block?.Code != null &&
            string.Equals(
                block.Code.Domain,
                "apprenticemire",
                StringComparison.Ordinal
            ) &&
            block.Code.Path.StartsWith(
                "toxicwater",
                StringComparison.Ordinal
            );

        private void PopulateLootChest(
            IInventory inventory,
            int level,
            ulong seed)
        {
            if (!lootTables.TryGetValue(
                    level,
                    out ResolvedLootTable? table))
            {
                throw new InvalidOperationException(
                    $"missing resolved loot table for level {level}"
                );
            }
            ulong randomState =
                seed ^
                LootContentSalt ^
                (ulong)(uint)level;
            int rolls = NextInclusive(
                ref randomState,
                table.MinimumRolls,
                table.MaximumRolls
            );
            int slotCount = Math.Min(
                rolls,
                inventory.Count
            );
            for (int slotIndex = 0;
                slotIndex < slotCount;
                slotIndex++)
            {
                double selection =
                    NextUnit(ref randomState) *
                    table.TotalWeight;
                ResolvedLootEntry selected =
                    table.Entries[^1];
                double cumulative = 0;
                foreach (ResolvedLootEntry entry
                    in table.Entries)
                {
                    cumulative += entry.Weight;
                    if (selection < cumulative)
                    {
                        selected = entry;
                        break;
                    }
                }
                int quantity = NextInclusive(
                    ref randomState,
                    selected.MinimumQuantity,
                    selected.MaximumQuantity
                );
                ItemSlot? slot =
                    inventory[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                slot.Itemstack = new ItemStack(
                    selected.Collectible,
                    quantity
                );
                inventory.MarkSlotDirty(
                    slotIndex
                );
            }
        }

        private static int NextInclusive(
            ref ulong state,
            int minimum,
            int maximum)
        {
            ulong value = NextRandom(ref state);
            return minimum +
                (int)(value %
                    (ulong)(
                        maximum -
                        minimum +
                        1
                    ));
        }

        private static double NextUnit(
            ref ulong state) =>
            (NextRandom(ref state) >> 11) *
            (1.0 / 9007199254740992.0);

        private static ulong NextRandom(
            ref ulong state)
        {
            unchecked
            {
                state +=
                    0x9E3779B97F4A7C15UL;
                ulong value = state;
                value =
                    (value ^ (value >> 30)) *
                    0xBF58476D1CE4E5B9UL;
                value =
                    (value ^ (value >> 27)) *
                    0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        private static bool IsPrimaryCityChunk(
            PlannedCity city,
            int chunkX,
            int chunkZ) =>
            chunkX ==
                (int)Math.Floor(
                    city.CenterX /
                    (double)ChunkSize
                ) &&
            chunkZ ==
                (int)Math.Floor(
                    city.CenterZ /
                    (double)ChunkSize
                );

        private static int DistanceToGridLine(
            int value,
            int spacing)
        {
            int normalized =
                Math.Abs(value) % spacing;
            return Math.Min(
                normalized,
                spacing - normalized
            );
        }

        private bool TryGenerateCityPart(
            WorldGenVillage cityPart,
            IWorldGenBlockAccessor blockAccessor,
            IMapRegion mapRegion,
            int x,
            int fallbackY,
            int z,
            int climateUpLeft,
            int climateUpRight,
            int climateBotLeft,
            int climateBotRight,
            List<GeneratedStructure> cityModules)
        {
            return TryPlaceSupportedCityPart(
                cityPart,
                blockAccessor,
                mapRegion,
                x,
                z,
                climateUpLeft,
                climateUpRight,
                climateBotLeft,
                climateBotRight,
                cityModules
            );
        }

        private bool TryPlaceSupportedCityPart(
            WorldGenVillage cityPart,
            IWorldGenBlockAccessor blockAccessor,
            IMapRegion mapRegion,
            int anchorX,
            int anchorZ,
            int climateUpLeft,
            int climateUpRight,
            int climateBotLeft,
            int climateBotRight,
            List<GeneratedStructure> cityModules)
        {
            VillageSchematic? pool =
                cityPart.Schematics.FirstOrDefault();
            if (pool == null ||
                pool.Structures == null ||
                pool.Structures.Length == 0)
            {
                return false;
            }

            ulong selectionHash = StableHash(
                anchorX,
                anchorZ,
                CandidateSalt ^
                    (ulong)(uint)api.WorldManager.Seed
            );
            BlockSchematicStructure schematic =
                pool.Structures[
                    (int)(selectionHash %
                        (ulong)pool.Structures.Length)
                ];
            int halfX = schematic.SizeX / 2;
            int halfZ = schematic.SizeZ / 2;
            BlockPos sample = new(0);
            int[] offsets = { 0 };
            int unavailableSamples = 0;
            int reliefRejections = 0;
            int liquidRejections = 0;
            int overlapRejections = 0;
            int placementExceptions = 0;
            int bestRelief = int.MaxValue;
            string lastPlacementError = "none";
            int reliefLimit =
                cityPart.Code.EndsWith(
                    "-landmark",
                    StringComparison.Ordinal)
                    ? 64
                    : MaximumTerraceRelief;
            foreach (int offsetZ in offsets)
            {
                foreach (int offsetX in offsets)
                {
                    int centerX =
                        anchorX + offsetX;
                    int centerZ =
                        anchorZ + offsetZ;
                    int x1 = centerX - halfX;
                    int z1 = centerZ - halfZ;
                    int x2 = x1 + schematic.SizeX;
                    int z2 = z1 + schematic.SizeZ;
                    if (!TryMeasureTerrainRelief(
                            blockAccessor,
                            sample,
                            x1,
                            z1,
                            x2,
                            z2,
                            out int minimum,
                            out int maximum))
                    {
                        unavailableSamples++;
                        continue;
                    }
                    bestRelief = Math.Min(
                        bestRelief,
                        maximum - minimum
                    );
                    if (maximum - minimum >
                        reliefLimit)
                    {
                        reliefRejections++;
                        continue;
                    }
                    if (IsLiquidSurface(
                            blockAccessor,
                            sample,
                            x1,
                            blockAccessor
                                .GetTerrainMapheightAt(
                                    sample.Set(
                                        x1,
                                        0,
                                        z1
                                    )
                                ),
                            z1
                        ) ||
                        IsLiquidSurface(
                            blockAccessor,
                            sample,
                            x2 - 1,
                            blockAccessor
                                .GetTerrainMapheightAt(
                                    sample.Set(
                                        x2 - 1,
                                        0,
                                        z1
                                    )
                                ),
                            z1
                        ) ||
                        IsLiquidSurface(
                            blockAccessor,
                            sample,
                            x1,
                            blockAccessor
                                .GetTerrainMapheightAt(
                                    sample.Set(
                                        x1,
                                        0,
                                        z2 - 1
                                    )
                                ),
                            z2 - 1
                        ) ||
                        IsLiquidSurface(
                            blockAccessor,
                            sample,
                            x2 - 1,
                            blockAccessor
                                .GetTerrainMapheightAt(
                                    sample.Set(
                                        x2 - 1,
                                        0,
                                        z2 - 1
                                    )
                                ),
                            z2 - 1
                        ))
                    {
                        liquidRejections++;
                        continue;
                    }

                    int y1 =
                        maximum +
                        schematic.OffsetY + 1;
                    if (y1 <= 0 ||
                        y1 + schematic.SizeY >=
                            api.WorldManager.MapSizeY - 1)
                    {
                        unavailableSamples++;
                        continue;
                    }
                    Cuboidi location = new(
                        x1,
                        y1,
                        z1,
                        x2,
                        y1 + schematic.SizeY,
                        z2
                    );
                    bool overlaps;
                    lock (mapRegion.GeneratedStructures)
                    {
                        overlaps =
                            mapRegion.GeneratedStructures
                                .Any(existing =>
                                    existing.Location
                                        .Intersects(
                                            location
                                        )
                                );
                    }
                    if (overlaps)
                    {
                        overlapRejections++;
                        continue;
                    }

                    try
                    {
                        BuildTerraceFoundation(
                            blockAccessor,
                            sample,
                            x1,
                            z1,
                            x2,
                            z2,
                            maximum,
                            selectionHash
                        );
                        schematic.PlaceRespectingBlockLayers(
                            blockAccessor,
                            blockAccessor
                                .WorldgenWorldAccessor,
                            new BlockPos(x1, y1, z1),
                            climateUpLeft,
                            climateUpRight,
                            climateBotLeft,
                            climateBotRight,
                            null!,
                            new[]
                            {
                                basaltId,
                                basaltCrackedId,
                                basaltGravelId
                            },
                            true
                        );
                    }
                    catch (Exception exception)
                    {
                        placementExceptions++;
                        lastPlacementError =
                            exception.GetType().Name +
                            ": " +
                            exception.Message;
                        continue;
                    }

                    GeneratedStructure generated =
                        new()
                        {
                            Code =
                                "apprenticehighlands:" +
                                (schematic.FromFile?.Path ??
                                    cityPart.Code),
                            Group = CityGroup,
                            Location = location,
                            SuppressTreesAndShrubs = true,
                            SuppressRivulets = true
                        };
                    mapRegion.AddGeneratedStructure(
                        generated
                    );
                    cityModules.Add(generated);
                    return true;
                }
            }
            if (loggedPlacementFailures < 12)
            {
                api.Logger.Debug(
                    "[Apprentice] Level 7 city support diagnostics at {0},{1}: unavailable={2}, relief={3}, best-relief={4}, liquid={5}, overlap={6}, placement-exceptions={7}, last-error={8}, schematic={9}, size={10}x{11}x{12}.",
                    anchorX,
                    anchorZ,
                    unavailableSamples,
                    reliefRejections,
                    bestRelief == int.MaxValue
                        ? -1
                        : bestRelief,
                    liquidRejections,
                    overlapRejections,
                    placementExceptions,
                    lastPlacementError,
                    schematic.FromFile?.Path ??
                        cityPart.Code,
                    schematic.SizeX,
                    schematic.SizeY,
                    schematic.SizeZ
                );
            }
            return false;
        }

        private void BuildTerraceFoundation(
            IWorldGenBlockAccessor blockAccessor,
            BlockPos sample,
            int x1,
            int z1,
            int x2,
            int z2,
            int topY,
            ulong signature)
        {
            for (int z = z1; z < z2; z++)
            {
                for (int x = x1; x < x2; x++)
                {
                    int surfaceY =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x, 0, z)
                        );
                    int gap = topY - surfaceY;
                    if (gap <= 0)
                    {
                        continue;
                    }
                    int localX = x - x1;
                    int localZ = z - z1;
                    bool edge =
                        x == x1 ||
                        z == z1 ||
                        x == x2 - 1 ||
                        z == z2 - 1;
                    bool pier =
                        localX % 5 <= 1 &&
                        localZ % 5 <= 1;
                    bool nearGround = gap <= 2;
                    bool buttress =
                        gap >= 5 &&
                        StableHash(
                            x,
                            z,
                            signature ^
                                0x4255545452455353UL
                        ) % 19 == 0;
                    bool brokenEdge =
                        edge &&
                        StableHash(
                            x,
                            z,
                            signature ^
                                0x42524F4B454E4544UL
                        ) % 100 < 14;
                    if ((!edge || brokenEdge) &&
                        !pier &&
                        !nearGround &&
                        !buttress)
                    {
                        continue;
                    }
                    int foundationId =
                        StableHash(
                            x,
                            z,
                            signature
                        ) % 5 == 0
                            ? basaltCrackedId
                            : basaltId;
                    for (int y = surfaceY + 1;
                        y <= topY;
                        y++)
                    {
                        blockAccessor.SetBlock(
                            foundationId,
                            sample.Set(x, y, z),
                            1
                        );
                    }
                }
            }
        }

        private static bool TryMeasureTerrainRelief(
            IWorldGenBlockAccessor blockAccessor,
            BlockPos sample,
            int x1,
            int z1,
            int x2,
            int z2,
            out int minimum,
            out int maximum)
        {
            minimum = int.MaxValue;
            maximum = int.MinValue;
            for (int z = z1;
                z < z2;
                z += 3)
            {
                for (int x = x1;
                    x < x2;
                    x += 3)
                {
                    int height =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x, 0, z)
                        );
                    if (height <= 0)
                    {
                        return false;
                    }
                    minimum = Math.Min(
                        minimum,
                        height
                    );
                    maximum = Math.Max(
                        maximum,
                        height
                    );
                }
            }

            int lastX = x2 - 1;
            int lastZ = z2 - 1;
            int[] edgeHeights =
            {
                blockAccessor.GetTerrainMapheightAt(
                    sample.Set(lastX, 0, z1)
                ),
                blockAccessor.GetTerrainMapheightAt(
                    sample.Set(x1, 0, lastZ)
                ),
                blockAccessor.GetTerrainMapheightAt(
                    sample.Set(lastX, 0, lastZ)
                ),
                blockAccessor.GetTerrainMapheightAt(
                    sample.Set(
                        (x1 + lastX) / 2,
                        0,
                        (z1 + lastZ) / 2
                    )
                )
            };
            foreach (int height in edgeHeights)
            {
                if (height <= 0)
                {
                    return false;
                }
                minimum = Math.Min(
                    minimum,
                    height
                );
                maximum = Math.Max(
                    maximum,
                    height
                );
            }
            return minimum != int.MaxValue &&
                maximum != int.MinValue;
        }

        private static bool IsLiquidSurface(
            IWorldGenBlockAccessor blockAccessor,
            BlockPos sample,
            int x,
            int y,
            int z)
        {
            return blockAccessor
                .GetBlock(
                    sample.Set(x, y, z),
                    2
                )
                .IsLiquid();
        }

        private static int
            FindBestLandmarkHeightDifference(
                IWorldGenBlockAccessor blockAccessor,
                int centerX,
                int centerZ)
        {
            int best = int.MaxValue;
            BlockPos sample = new(0);
            for (int offsetZ = -48;
                offsetZ <= 48;
                offsetZ += 4)
            {
                for (int offsetX = -48;
                    offsetX <= 48;
                    offsetX += 4)
                {
                    int x = centerX + offsetX;
                    int z = centerZ + offsetZ;
                    int h1 =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x - 16, 0, z - 16)
                        );
                    int h2 =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x + 16, 0, z - 16)
                        );
                    int h3 =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x - 16, 0, z + 16)
                        );
                    int h4 =
                        blockAccessor.GetTerrainMapheightAt(
                            sample.Set(x + 16, 0, z + 16)
                        );
                    if (h1 == 0 ||
                        h2 == 0 ||
                        h3 == 0 ||
                        h4 == 0)
                    {
                        continue;
                    }
                    int difference =
                        Math.Max(
                            Math.Max(h1, h2),
                            Math.Max(h3, h4)
                        ) -
                        Math.Min(
                            Math.Min(h1, h2),
                            Math.Min(h3, h4)
                        );
                    best = Math.Min(best, difference);
                }
            }
            return best;
        }

        private static List<(int X, int Z)>
            CreatePartSites(
                int centerX,
                int centerZ,
                ulong signature,
                int culture,
                int part)
        {
            List<(int X, int Z)> offsets = new();
            if (part == 0)
            {
                offsets.Add((0, 0));
                AddRingOffsets(offsets, 22, 8, 0.0);
            }
            else if (part == 1)
            {
                switch (culture)
                {
                    case 0:
                        AddRingOffsets(
                            offsets,
                            86,
                            8,
                            Math.PI / 8.0
                        );
                        AddRingOffsets(
                            offsets,
                            162,
                            12,
                            0.0
                        );
                        break;
                    case 1:
                        AddSpokeOffsets(
                            offsets,
                            6,
                            new[] { 72, 122, 172 },
                            Math.PI / 6.0
                        );
                        break;
                    case 2:
                        AddAvenueOffsets(
                            offsets,
                            7,
                            50,
                            72
                        );
                        AddAvenueOffsets(
                            offsets,
                            5,
                            68,
                            146
                        );
                        break;
                    case 3:
                        AddGridOffsets(
                            offsets,
                            3,
                            58
                        );
                        break;
                    case 4:
                        AddClusterOffsets(offsets);
                        break;
                    default:
                        AddProcessionalOffsets(offsets);
                        break;
                }
            }
            else if (part == 2)
            {
                AddRingOffsets(
                    offsets,
                    54,
                    6,
                    Math.PI / 6.0
                );
                AddRingOffsets(
                    offsets,
                    124,
                    10,
                    Math.PI / 10.0
                );
                AddCardinalOffsets(offsets, 172);
            }
            else
            {
                for (int index = 0;
                    index < 26;
                    index++)
                {
                    ulong hash = StableHash(
                        index,
                        culture,
                        signature ^
                            0x52454D4E414E5453UL
                    );
                    double angle =
                        ((hash & 0xffffUL) /
                            65535.0) *
                        Math.PI * 2.0;
                    double radius =
                        66.0 +
                        ((hash >> 16) & 0xffffUL) /
                            65535.0 *
                        112.0;
                    offsets.Add(
                        (
                            (int)Math.Round(
                                Math.Cos(angle) *
                                radius
                            ),
                            (int)Math.Round(
                                Math.Sin(angle) *
                                radius
                            )
                        )
                    );
                }
            }

            int quarterTurns =
                (int)((signature >> 40) & 3UL);
            bool mirror =
                ((signature >> 42) & 1UL) != 0;
            List<(int X, int Z)> sites =
                new(offsets.Count);
            foreach ((int X, int Z) offset in offsets)
            {
                int x = mirror
                    ? -offset.X
                    : offset.X;
                int z = offset.Z;
                for (int turn = 0;
                    turn < quarterTurns;
                    turn++)
                {
                    (x, z) = (-z, x);
                }
                sites.Add(
                    (centerX + x, centerZ + z)
                );
            }
            return sites;
        }

        private static void AddRingOffsets(
            List<(int X, int Z)> offsets,
            int radius,
            int count,
            double startAngle)
        {
            for (int index = 0;
                index < count;
                index++)
            {
                double angle =
                    startAngle +
                    Math.PI * 2.0 * index / count;
                offsets.Add(
                    (
                        (int)Math.Round(
                            Math.Cos(angle) * radius
                        ),
                        (int)Math.Round(
                            Math.Sin(angle) * radius
                        )
                    )
                );
            }
        }

        private static void AddSpokeOffsets(
            List<(int X, int Z)> offsets,
            int spokeCount,
            int[] radii,
            double startAngle)
        {
            for (int spoke = 0;
                spoke < spokeCount;
                spoke++)
            {
                double angle =
                    startAngle +
                    Math.PI * 2.0 *
                    spoke / spokeCount;
                foreach (int radius in radii)
                {
                    offsets.Add(
                        (
                            (int)Math.Round(
                                Math.Cos(angle) *
                                radius
                            ),
                            (int)Math.Round(
                                Math.Sin(angle) *
                                radius
                            )
                        )
                    );
                }
            }
        }

        private static void AddAvenueOffsets(
            List<(int X, int Z)> offsets,
            int count,
            int step,
            int lateral)
        {
            int half = count / 2;
            for (int index = -half;
                index <= half;
                index++)
            {
                offsets.Add((index * step, lateral));
                offsets.Add((index * step, -lateral));
            }
        }

        private static void AddGridOffsets(
            List<(int X, int Z)> offsets,
            int radius,
            int step)
        {
            for (int x = -radius;
                x <= radius;
                x++)
            {
                for (int z = -radius;
                    z <= radius;
                    z++)
                {
                    if ((Math.Abs(x) <= 1 &&
                            Math.Abs(z) <= 1) ||
                        (x + z) % 2 != 0)
                    {
                        continue;
                    }
                    offsets.Add((x * step, z * step));
                }
            }
        }

        private static void AddClusterOffsets(
            List<(int X, int Z)> offsets)
        {
            (int X, int Z)[] hubs =
            {
                (-118, -72),
                (104, -86),
                (-82, 112),
                (126, 98)
            };
            foreach ((int X, int Z) hub in hubs)
            {
                offsets.Add(hub);
                offsets.Add((hub.X + 44, hub.Z));
                offsets.Add((hub.X - 44, hub.Z));
                offsets.Add((hub.X, hub.Z + 44));
                offsets.Add((hub.X, hub.Z - 44));
            }
        }

        private static void AddProcessionalOffsets(
            List<(int X, int Z)> offsets)
        {
            for (int row = -2; row <= 2; row++)
            {
                for (int column = -3;
                    column <= 3;
                    column++)
                {
                    if (row == 0 &&
                        Math.Abs(column) <= 1)
                    {
                        continue;
                    }
                    offsets.Add(
                        (column * 52, row * 72)
                    );
                }
            }
        }

        private static void AddCardinalOffsets(
            List<(int X, int Z)> offsets,
            int distance)
        {
            offsets.Add((distance, 0));
            offsets.Add((-distance, 0));
            offsets.Add((0, distance));
            offsets.Add((0, -distance));
        }

        internal void OnCorruptionChunkColumnGeneration(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            IWorldGenBlockAccessor? blockAccessor =
                worldgenBlockAccessor;
            if (!initialized ||
                state == null ||
                blockAccessor == null ||
                !WorldZoneLayout.ChunkIntersectsLevel(
                    state,
                    ShatteredHighlandsLevel,
                    request.ChunkX,
                    request.ChunkZ,
                    ChunkSize))
            {
                return;
            }

            List<CityAnchor> anchors =
                FindNearbyAnchors(
                    blockAccessor,
                    request.Chunks[0]
                        .MapChunk
                        .MapRegion,
                    request.ChunkX,
                    request.ChunkZ
                );
            int chunkCenterX =
                request.ChunkX * ChunkSize +
                ChunkSize / 2;
            int chunkCenterZ =
                request.ChunkZ * ChunkSize +
                ChunkSize / 2;
            if (TryGetPlannedCity(
                    state,
                    chunkCenterX,
                    chunkCenterZ,
                    CorruptionRadius +
                        CorruptionWarpMargin,
                    out PlannedCity? planned) &&
                planned != null &&
                !anchors.Any(existing =>
                    existing.X == planned.CenterX &&
                    existing.Z == planned.CenterZ))
            {
                anchors.Add(
                    new CityAnchor(
                        planned.CenterX,
                        planned.CenterZ,
                        planned.Culture,
                        planned.Signature
                    )
                );
            }
            if (anchors.Count == 0)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            ushort[] heights =
                mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights = mapChunk.RainHeightMap;
            int changedColumns = 0;
            int removedFlora = 0;
            for (int localZ = 0;
                localZ < ChunkSize;
                localZ++)
            {
                int worldZ =
                    request.ChunkZ * ChunkSize + localZ;
                int row = localZ * ChunkSize;
                for (int localX = 0;
                    localX < ChunkSize;
                    localX++)
                {
                    int worldX =
                        request.ChunkX * ChunkSize + localX;
                    if (!WorldZoneLayout.IsInsideLevelCore(
                            state,
                            ShatteredHighlandsLevel,
                            BoundaryExclusionWidth,
                            worldX + 0.5,
                            worldZ + 0.5))
                    {
                        continue;
                    }

                    CityAnchor? nearest =
                        FindNearestAnchor(
                            anchors,
                            worldX,
                            worldZ
                        );
                    if (nearest == null)
                    {
                        continue;
                    }

                    double distance = Math.Sqrt(
                        HorizontalDistanceSquared(
                            nearest,
                            worldX,
                            worldZ
                        )
                    );
                    int radius =
                        CorruptionRadius -
                        nearest.Style * 5;
                    if (distance >
                        radius + CorruptionWarpMargin)
                    {
                        continue;
                    }

                    ulong hash = StableHash(
                        worldX,
                        worldZ,
                        CorruptionSalt ^
                            nearest.Signature
                    );
                    double strength =
                        GetIrregularCorruptionStrength(
                            nearest,
                            worldX,
                            worldZ,
                            radius,
                            hash
                        );
                    if (strength <= 0)
                    {
                        continue;
                    }
                    double pattern =
                        GetCulturePattern(
                            nearest.Style,
                            worldX - nearest.X,
                            worldZ - nearest.Z,
                            hash
                        );
                    if (strength * 0.68 +
                            pattern * 0.32 <
                        0.28)
                    {
                        continue;
                    }

                    int mapIndex = row + localX;
                    int terrainY = heights[mapIndex];
                    if (terrainY <= 1 ||
                        terrainY >=
                            api.WorldManager.MapSizeY - 2)
                    {
                        continue;
                    }

                    int surfaceId = GetGeneratedBlockId(
                        request.Chunks,
                        localX,
                        terrainY,
                        localZ
                    );
                    if (!IsNaturalSurface(
                            BlockPath(surfaceId)))
                    {
                        continue;
                    }

                    int replacementId =
                        SelectCorruptionBlock(
                            nearest.Style,
                            strength,
                            hash
                        );
                    SetGeneratedBlock(
                        request.Chunks,
                        localX,
                        terrainY,
                        localZ,
                        replacementId
                    );
                    changedColumns++;

                    int maximumFloraY = Math.Min(
                        Math.Max(
                            terrainY + 1,
                            rainHeights[mapIndex]
                        ),
                        terrainY + 12
                    );
                    for (int y = terrainY + 1;
                        y <= maximumFloraY;
                        y++)
                    {
                        int aboveId = GetGeneratedBlockId(
                            request.Chunks,
                            localX,
                            y,
                            localZ
                        );
                        if (aboveId <= 0)
                        {
                            continue;
                        }
                        if (!IsLivingFlora(
                                BlockPath(aboveId)))
                        {
                            break;
                        }
                        SetGeneratedBlock(
                            request.Chunks,
                            localX,
                            y,
                            localZ,
                            0
                        );
                        removedFlora++;
                    }

                    if ((hash >> 20) % 1000 <
                            (ulong)(
                                10 +
                                (int)(strength * 35)
                            ) &&
                        GetGeneratedBlockId(
                            request.Chunks,
                            localX,
                            terrainY + 1,
                            localZ
                        ) == 0)
                    {
                        SetGeneratedBlock(
                            request.Chunks,
                            localX,
                            terrainY + 1,
                            localZ,
                            blackVeinId
                        );
                    }
                    if ((hash >> 36) % 1000 <
                            (ulong)(
                                1 +
                                (int)(strength * 4)
                            ) &&
                        GetGeneratedBlockId(
                            request.Chunks,
                            localX,
                            terrainY + 2,
                            localZ
                        ) == 0)
                    {
                        SetGeneratedBlock(
                            request.Chunks,
                            localX,
                            terrainY + 2,
                            localZ,
                            gloomId
                        );
                    }
                }
            }

            if (changedColumns == 0)
            {
                return;
            }

            System.Threading.Interlocked.Add(
                ref corruptedColumns,
                changedColumns
            );
            System.Threading.Interlocked.Add(
                ref scrubbedFlora,
                removedFlora
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedCorruptionChunks
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Corrupted Level 7 city landscape in chunk {0},{1}: surfaces={2}, living flora={3}, anchors={4}, generator={5:0.0} ms.",
                    request.ChunkX,
                    request.ChunkZ,
                    changedColumns,
                    removedFlora,
                    anchors.Count,
                    Stopwatch.GetElapsedTime(started)
                        .TotalMilliseconds
                );
            }
        }

        internal TextCommandResult RunProbe(
            TextCommandCallingArgs args,
            DangerWorldState? state)
        {
            if (!initialized ||
                state == null ||
                !state.Enabled ||
                !state.RealmWorldgenEnabled)
            {
                return TextCommandResult.Error(
                    "Shattered Highlands ruin generation is not active.",
                    "apprentice-shattered-highlands-ruins-disabled"
                );
            }
            if (args.Caller.Player is not
                IServerPlayer player)
            {
                return TextCommandResult.Error(
                    "Run this probe as a player near generated Level 7 valleys.",
                    "apprentice-shattered-highlands-ruins-player"
                );
            }

            int playerX =
                (int)Math.Floor(player.Entity.Pos.X);
            int playerZ =
                (int)Math.Floor(player.Entity.Pos.Z);
            IBlockAccessor accessor =
                api.World.BlockAccessor;
            int searchRadius = 6144;
            int minRegionX = FloorDiv(
                playerX - searchRadius,
                accessor.RegionSize
            );
            int maxRegionX = FloorDiv(
                playerX + searchRadius,
                accessor.RegionSize
            );
            int minRegionZ = FloorDiv(
                playerZ - searchRadius,
                accessor.RegionSize
            );
            int maxRegionZ = FloorDiv(
                playerZ + searchRadius,
                accessor.RegionSize
            );

            List<GeneratedStructure> anchors = new();
            List<GeneratedStructure> modules = new();
            List<GeneratedStructure> lootChests =
                new();
            for (int regionZ = minRegionZ;
                regionZ <= maxRegionZ;
                regionZ++)
            {
                for (int regionX = minRegionX;
                    regionX <= maxRegionX;
                    regionX++)
                {
                    IMapRegion? region =
                        accessor.GetMapRegion(
                            regionX,
                            regionZ
                        );
                    if (region == null)
                    {
                        continue;
                    }
                    foreach (GeneratedStructure structure
                        in SnapshotStructures(region))
                    {
                        if (structure.Code?.StartsWith(
                                AnchorPrefix,
                                StringComparison.Ordinal) ==
                            true)
                        {
                            anchors.Add(structure);
                        }
                        else if (structure.Code?.StartsWith(
                                LootChestPrefix,
                                StringComparison.Ordinal) ==
                            true)
                        {
                            lootChests.Add(structure);
                        }
                        else if (structure.Code?.StartsWith(
                                "apprenticehighlands:" +
                                "apprentice-highlands/",
                                StringComparison.Ordinal) ==
                            true)
                        {
                            modules.Add(structure);
                        }
                    }
                }
            }

            int realmLeaks = 0;
            int boundaryLeaks = 0;
            HashSet<string> signatures =
                new(StringComparer.Ordinal);
            HashSet<string> cultures =
                new(StringComparer.Ordinal);
            foreach (GeneratedStructure anchor
                in anchors)
            {
                int x = anchor.Location.CenterX;
                int z = anchor.Location.CenterZ;
                if (WorldZoneLayout.GetLevelAt(
                        state,
                        x,
                        z) !=
                    ShatteredHighlandsLevel)
                {
                    realmLeaks++;
                }
                if (!WorldZoneLayout.IsInsideLevelCore(
                        state,
                        ShatteredHighlandsLevel,
                            BoundaryExclusionWidth +
                            CorruptionRadius +
                            CorruptionWarpMargin,
                        x,
                        z))
                {
                    boundaryLeaks++;
                }

                string[] parts =
                    anchor.Code.Split('/');
                if (parts.Length >= 3)
                {
                    cultures.Add(parts[^2]);
                    signatures.Add(parts[^1]);
                }
            }

            bool unique =
                signatures.Count == anchors.Count;
            int landmarkModules = modules.Count(
                module =>
                    module.Code?.Contains(
                        "/landmarks/",
                        StringComparison.Ordinal) ==
                    true
            );
            Dictionary<string, int>
                chestsBySignature =
                    new(StringComparer.Ordinal);
            foreach (GeneratedStructure chest
                in lootChests)
            {
                string code = chest.Code ?? string.Empty;
                string remainder = code.Substring(
                    LootChestPrefix.Length
                );
                string signature =
                    remainder.Split('/')[0];
                chestsBySignature.TryGetValue(
                    signature,
                    out int count
                );
                chestsBySignature[signature] =
                    count + 1;
            }
            int expectedLootChests = 0;
            int lootContractViolations = 0;
            foreach (string signature
                in signatures)
            {
                if (!ulong.TryParse(
                        signature,
                        System.Globalization
                            .NumberStyles
                            .HexNumber,
                        System.Globalization
                            .CultureInfo
                            .InvariantCulture,
                        out ulong citySignature))
                {
                    lootContractViolations++;
                    continue;
                }
                int expected =
                    GetCityChestCount(
                        citySignature
                    );
                expectedLootChests += expected;
                chestsBySignature.TryGetValue(
                    signature,
                    out int actual
                );
                if (actual != expected)
                {
                    lootContractViolations++;
                }
            }
            int spacingViolations = 0;
            for (int first = 0;
                first < anchors.Count;
                first++)
            {
                for (int second = first + 1;
                    second < anchors.Count;
                    second++)
                {
                    long dx =
                        anchors[first].Location.CenterX -
                        anchors[second].Location.CenterX;
                    long dz =
                        anchors[first].Location.CenterZ -
                        anchors[second].Location.CenterZ;
                    if (dx * dx + dz * dz <
                        (long)MinimumCitySpacing *
                        MinimumCitySpacing)
                    {
                        spacingViolations++;
                    }
                }
            }
            bool passed =
                anchors.Count > 0 &&
                landmarkModules >= anchors.Count * 5 &&
                modules.Count >= anchors.Count * 60 &&
                realmLeaks == 0 &&
                boundaryLeaks == 0 &&
                unique &&
                spacingViolations == 0 &&
                lootContractViolations == 0;
            double cityMilliseconds =
                System.Threading.Interlocked.Read(
                    ref cityGenerationTicks
                ) * 1000d /
                Stopwatch.Frequency;
            StringBuilder result = new();
            result.Append(
                passed
                    ? "PASS"
                    : "FAIL"
            );
            result.Append(
                " — Shattered Highlands ruined valleys: "
            );
            result.Append(
                $"cities={anchors.Count}, cultures={cultures.Count}/{CultureCount}, "
            );
            result.Append(
                $"unique signatures={signatures.Count}/{anchors.Count}, "
            );
            result.Append(
                $"landmarks/fountains={landmarkModules}, modules={modules.Count}, loot chests={lootChests.Count}/{expectedLootChests}, loot contract violations={lootContractViolations}, realm leaks={realmLeaks}, "
            );
            result.Append(
                $"border leaks={boundaryLeaks}, spacing violations={spacingViolations}, "
            );
            result.Append(
                $"corrupted columns={CorruptedColumns}, "
            );
            result.Append(
                $"living flora removed={ScrubbedFlora}, "
            );
            result.Append(
                $"city generation={cityMilliseconds:0.0} ms total."
            );
            result.Append(
                $" Candidate telemetry: evaluated chunks={System.Threading.Interlocked.Read(ref evaluatedCityChunks)}, " +
                $"hash matches={System.Threading.Interlocked.Read(ref candidateHashMatches)}, " +
                $"valley matches={System.Threading.Interlocked.Read(ref valleyCandidateMatches)}, " +
                $"native placement failures={System.Threading.Interlocked.Read(ref nativePlacementFailures)}."
            );
            if (anchors.Count == 0)
            {
                result.Append(
                    " Explore fresh Level 7 valley terrain, then run the probe nearby."
                );
            }

            return passed
                ? TextCommandResult.Success(
                    result.ToString()
                )
                : TextCommandResult.Error(
                    result.ToString(),
                    "apprentice-shattered-highlands-ruins-probe-failed"
                );
        }

        private bool IsValleyCandidate(
            ushort[] heights,
            int centerX,
            int centerZ,
            out int centerY)
        {
            centerY = heights[
                (ChunkSize / 2) * ChunkSize +
                ChunkSize / 2
            ];
            if (centerY <= TerraGenConfig.seaLevel + 5 ||
                centerY >=
                    api.WorldManager.MapSizeY - 48)
            {
                return false;
            }

            if (!IsRiftLandformCell(centerX, centerZ))
            {
                return false;
            }

            return true;
        }

        private static bool IsRiftLandformCell(
            int worldX,
            int worldZ)
        {
            int cellX = (int)Math.Floor(
                worldX / (double)LandformCellSize
            );
            int cellZ = (int)Math.Floor(
                worldZ / (double)LandformCellSize
            );
            return StableLandformHash(
                cellX,
                cellZ
            ) % 100 < RiftLandformPercent;
        }

        private static ulong StableLandformHash(
            int cellX,
            int cellZ)
        {
            unchecked
            {
                ulong value =
                    (uint)cellX *
                        0x9E3779B185EBCA87UL ^
                    (uint)cellZ *
                        0xC2B2AE3D27D4EB4FUL ^
                    0x5348415454455245UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private void GetClimateCorners(
            IMapRegion mapRegion,
            int chunkX,
            int chunkZ,
            out int upLeft,
            out int upRight,
            out int botLeft,
            out int botRight)
        {
            int regionChunkSize =
                api.WorldManager.RegionSize / ChunkSize;
            int localChunkX =
                GameMath.Mod(chunkX, regionChunkSize);
            int localChunkZ =
                GameMath.Mod(chunkZ, regionChunkSize);
            IntDataMap2D climate = mapRegion.ClimateMap;
            float step =
                (float)climate.InnerSize /
                regionChunkSize;
            int x1 = (int)(localChunkX * step);
            int z1 = (int)(localChunkZ * step);
            int x2 = (int)(localChunkX * step + step);
            int z2 = (int)(localChunkZ * step + step);
            upLeft = climate.GetUnpaddedInt(x1, z1);
            upRight = climate.GetUnpaddedInt(x2, z1);
            botLeft = climate.GetUnpaddedInt(x1, z2);
            botRight = climate.GetUnpaddedInt(x2, z2);
        }

        private List<CityAnchor> FindNearbyAnchors(
            IWorldGenBlockAccessor accessor,
            IMapRegion currentRegion,
            int chunkX,
            int chunkZ)
        {
            int centerX =
                chunkX * ChunkSize + ChunkSize / 2;
            int centerZ =
                chunkZ * ChunkSize + ChunkSize / 2;
            int regionSize = accessor.RegionSize;
            int minRegionX = FloorDiv(
                centerX -
                    CorruptionRadius -
                    CorruptionWarpMargin,
                regionSize
            );
            int maxRegionX = FloorDiv(
                centerX +
                    CorruptionRadius +
                    CorruptionWarpMargin,
                regionSize
            );
            int minRegionZ = FloorDiv(
                centerZ -
                    CorruptionRadius -
                    CorruptionWarpMargin,
                regionSize
            );
            int maxRegionZ = FloorDiv(
                centerZ +
                    CorruptionRadius +
                    CorruptionWarpMargin,
                regionSize
            );
            List<CityAnchor> anchors = new();
            HashSet<string> anchorCodes =
                new(StringComparer.Ordinal);
            AddAnchorsFromRegion(
                currentRegion,
                anchors,
                anchorCodes,
                centerX,
                centerZ
            );
            for (int regionZ = minRegionZ;
                regionZ <= maxRegionZ;
                regionZ++)
            {
                for (int regionX = minRegionX;
                    regionX <= maxRegionX;
                    regionX++)
                {
                    IMapRegion? region =
                        accessor.GetMapRegion(
                            regionX,
                            regionZ
                        );
                    if (region == null)
                    {
                        continue;
                    }
                    AddAnchorsFromRegion(
                        region,
                        anchors,
                        anchorCodes,
                        centerX,
                        centerZ
                    );
                }
            }
            return anchors;
        }

        private static void AddAnchorsFromRegion(
            IMapRegion region,
            ICollection<CityAnchor> anchors,
            ISet<string> anchorCodes,
            int centerX,
            int centerZ)
        {
            foreach (GeneratedStructure structure
                in SnapshotStructures(region))
            {
                if (!anchorCodes.Add(
                        structure.Code ??
                        string.Empty) ||
                    !TryParseAnchor(
                        structure,
                        out CityAnchor? anchor) ||
                    anchor == null)
                {
                    continue;
                }
                if (HorizontalDistanceSquared(
                        anchor,
                        centerX,
                        centerZ) <=
                    (CorruptionRadius +
                        CorruptionWarpMargin +
                        ChunkSize) *
                    (CorruptionRadius +
                        CorruptionWarpMargin +
                        ChunkSize))
                {
                    anchors.Add(anchor);
                }
            }
        }

        private static GeneratedStructure[]
            SnapshotStructures(IMapRegion region)
        {
            lock (region.GeneratedStructures)
            {
                return region.GeneratedStructures.ToArray();
            }
        }

        private static bool TryParseAnchor(
            GeneratedStructure structure,
            out CityAnchor? anchor)
        {
            anchor = null;
            if (structure.Code?.StartsWith(
                    AnchorPrefix,
                    StringComparison.Ordinal) != true)
            {
                return false;
            }

            string[] parts = structure.Code.Split('/');
            if (parts.Length < 3)
            {
                return false;
            }
            int style = Array.IndexOf(
                CultureCodes,
                parts[^2]
            );
            if (style < 0 ||
                !ulong.TryParse(
                    parts[^1],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out ulong signature))
            {
                return false;
            }
            anchor = new CityAnchor(
                structure.Location.CenterX,
                structure.Location.CenterZ,
                style,
                signature
            );
            return true;
        }

        private static CityAnchor? FindNearestAnchor(
            IEnumerable<CityAnchor> anchors,
            int worldX,
            int worldZ)
        {
            CityAnchor? nearest = null;
            long nearestDistance = long.MaxValue;
            foreach (CityAnchor anchor in anchors)
            {
                long distance =
                    HorizontalDistanceSquared(
                        anchor,
                        worldX,
                        worldZ
                    );
                if (distance >= nearestDistance)
                {
                    continue;
                }
                nearest = anchor;
                nearestDistance = distance;
            }
            return nearest;
        }

        private static long HorizontalDistanceSquared(
            CityAnchor anchor,
            int worldX,
            int worldZ)
        {
            long dx = worldX - anchor.X;
            long dz = worldZ - anchor.Z;
            return dx * dx + dz * dz;
        }

        private static double
            GetIrregularCorruptionStrength(
                CityAnchor anchor,
                int worldX,
                int worldZ,
                int radius,
                ulong hash)
        {
            double dx = worldX - anchor.X;
            double dz = worldZ - anchor.Z;
            double distance = Math.Sqrt(
                dx * dx + dz * dz
            );
            if (distance >
                radius + CorruptionWarpMargin)
            {
                return 0;
            }

            double orientation =
                (anchor.Signature & 0xffff) /
                65535d * Math.PI * 2;
            double cosine = Math.Cos(orientation);
            double sine = Math.Sin(orientation);
            double along =
                dx * cosine + dz * sine;
            double across =
                -dx * sine + dz * cosine;
            double ellipseDistance = Math.Sqrt(
                (along / 1.22) *
                    (along / 1.22) +
                (across / 0.79) *
                    (across / 0.79)
            );
            double noise =
                ((hash >> 10) & 0xffff) /
                    65535d - 0.5;
            double wave =
                Math.Sin(
                    along * 0.027 +
                    Math.Cos(across * 0.019) *
                        2.3 +
                    anchor.Style
                ) * 72;
            double warpedDistance =
                ellipseDistance +
                wave +
                noise * 150;
            double core = Math.Clamp(
                1 - warpedDistance / radius,
                0,
                1
            );

            double angle = Math.Atan2(dz, dx);
            double phase =
                ((anchor.Signature >> 20) &
                    0xffff) /
                65535d * Math.PI * 2;
            double branchAlignment =
                1 -
                Math.Min(
                    1,
                    Math.Abs(
                        Math.Sin(
                            angle *
                                (3 + anchor.Style % 3) +
                            phase
                        )
                    ) * 4.2
                );
            double branchReach = Math.Clamp(
                1 -
                distance /
                    (radius +
                        CorruptionWarpMargin),
                0,
                1
            );
            return Math.Clamp(
                core * 0.78 +
                branchAlignment *
                    branchReach *
                    0.38,
                0,
                1
            );
        }

        private int SelectCorruptionBlock(
            int style,
            double strength,
            ulong hash)
        {
            if (strength > 0.88 ||
                (style == 3 &&
                    strength > 0.72) ||
                hash % 100 <
                    (ulong)(3 + style))
            {
                return obsidianId;
            }
            if (style == 4 ||
                strength > 0.52)
            {
                return basaltCrackedId;
            }
            if (style == 2 ||
                (hash >> 8) % 100 < 28)
            {
                return basaltGravelId;
            }
            return basaltId;
        }

        private static double GetCulturePattern(
            int style,
            int dx,
            int dz,
            ulong hash)
        {
            double noise =
                (hash & 0xFFFF) / 65535d;
            return style switch
            {
                0 => Math.Max(
                    noise,
                    1 - Math.Min(
                        Math.Abs(dx) % 29,
                        Math.Abs(dz) % 29
                    ) / 14d
                ),
                1 => Math.Max(
                    noise,
                    1 - Math.Abs(
                        Math.Sqrt(
                            (double)dx * dx +
                            (double)dz * dz
                        ) % 36 - 18
                    ) / 18
                ),
                2 => Math.Max(
                    noise,
                    1 - Math.Abs(
                        dx + dz * 2
                    ) % 43 / 21d
                ),
                3 => Math.Max(
                    noise,
                    1 - Math.Abs(
                        Math.Atan2(dz, dx) * 12
                    ) % 1
                ),
                4 => Math.Max(
                    noise,
                    ((Math.Abs(dx) / 11 +
                        Math.Abs(dz) / 11) & 1) == 0
                        ? 0.82
                        : 0.2
                ),
                _ => Math.Max(
                    noise,
                    1 - Math.Abs(
                        Math.Abs(dx) -
                        Math.Abs(dz)
                    ) % 31 / 15d
                )
            };
        }

        private Dictionary<int, ResolvedLootTable>
            LoadLootTables()
        {
            IAsset lootAsset = api.Assets.Get(
                new AssetLocation(
                    "apprenticehighlands",
                    "config/city-loot.json"
                )
            );
            CityLootConfig config =
                lootAsset.ToObject<CityLootConfig>();
            if (config == null ||
                config.SchemaVersion != 1 ||
                config.Levels == null)
            {
                throw new InvalidOperationException(
                    "invalid Highlands city-loot schema"
                );
            }

            Dictionary<int, ResolvedLootTable>
                resolved = new();
            int previousMinimumRolls = 0;
            int previousMaximumRolls = 0;
            for (int level = 1;
                level <= 9;
                level++)
            {
                string levelCode =
                    level.ToString(
                        System.Globalization
                            .CultureInfo
                            .InvariantCulture
                    );
                if (!config.Levels.TryGetValue(
                        levelCode,
                        out CityLootTableConfig? table) ||
                    table == null ||
                    table.MinimumRolls <= 0 ||
                    table.MaximumRolls <
                        table.MinimumRolls ||
                    table.MaximumRolls > 16 ||
                    table.MinimumRolls <
                        previousMinimumRolls ||
                    table.MaximumRolls <
                        previousMaximumRolls ||
                    table.Entries == null ||
                    table.Entries.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"invalid Highlands city-loot table for level {level}"
                    );
                }

                List<ResolvedLootEntry> entries =
                    new();
                double totalWeight = 0;
                foreach (CityLootEntryConfig entry
                    in table.Entries)
                {
                    if (entry == null ||
                        string.IsNullOrWhiteSpace(
                            entry.Code) ||
                        entry.MinimumQuantity <= 0 ||
                        entry.MaximumQuantity <
                            entry.MinimumQuantity ||
                        entry.Weight <= 0)
                    {
                        throw new InvalidOperationException(
                            $"invalid Highlands city-loot entry for level {level}"
                        );
                    }
                    AssetLocation location =
                        new(entry.Code);
                    CollectibleObject? collectible =
                        api.World.GetItem(location);
                    collectible ??=
                        api.World.GetBlock(location);
                    if (collectible == null)
                    {
                        throw new InvalidOperationException(
                            $"missing Highlands city-loot collectible {entry.Code} for level {level}"
                        );
                    }
                    entries.Add(
                        new ResolvedLootEntry(
                            collectible,
                            entry.MinimumQuantity,
                            entry.MaximumQuantity,
                            entry.Weight
                        )
                    );
                    totalWeight += entry.Weight;
                }

                resolved[level] =
                    new ResolvedLootTable(
                        table.MinimumRolls,
                        table.MaximumRolls,
                        entries.ToArray(),
                        totalWeight
                    );
                previousMinimumRolls =
                    table.MinimumRolls;
                previousMaximumRolls =
                    table.MaximumRolls;
            }
            return resolved;
        }

        private int ResolveBlockId(string code) =>
            api.World.GetBlock(
                new AssetLocation(code)
            )?.Id ?? 0;

        private string BlockPath(int blockId) =>
            blockId > 0 &&
            blockId < api.World.Blocks.Count
                ? api.World.Blocks[blockId]
                    ?.Code?.Path ??
                    string.Empty
                : string.Empty;

        private static bool IsNaturalSurface(
            string path) =>
            path.StartsWith("soil-", StringComparison.Ordinal) ||
            path.StartsWith("gravel-", StringComparison.Ordinal) ||
            path.StartsWith("sand-", StringComparison.Ordinal) ||
            path.StartsWith("clay-", StringComparison.Ordinal) ||
            path.StartsWith("peat-", StringComparison.Ordinal) ||
            path.StartsWith("rock-", StringComparison.Ordinal) ||
            path.StartsWith(
                "crackedrock-",
                StringComparison.Ordinal
            );

        private static bool IsLivingFlora(
            string path) =>
            path.StartsWith("tallgrass", StringComparison.Ordinal) ||
            path.StartsWith("flower", StringComparison.Ordinal) ||
            path.StartsWith("plant-", StringComparison.Ordinal) ||
            path.StartsWith("fern", StringComparison.Ordinal) ||
            path.StartsWith("sapling", StringComparison.Ordinal) ||
            path.StartsWith("leaves", StringComparison.Ordinal) ||
            path.StartsWith("branchy", StringComparison.Ordinal) ||
            path.StartsWith("bush", StringComparison.Ordinal) ||
            path.StartsWith("mushroom", StringComparison.Ordinal) ||
            path.StartsWith("log-", StringComparison.Ordinal);

        private static int GetGeneratedBlockId(
            IServerChunk[] chunks,
            int localX,
            int y,
            int localZ)
        {
            if (y < 0)
            {
                return 0;
            }
            int chunkY = y / ChunkSize;
            if (chunkY < 0 ||
                chunkY >= chunks.Length)
            {
                return 0;
            }
            IServerChunk? chunk = chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return 0;
            }
            return chunk.Data.GetBlockIdUnsafe(
                ChunkIndex3d(
                    localX,
                    y % ChunkSize,
                    localZ
                )
            );
        }

        private static void SetGeneratedBlock(
            IServerChunk[] chunks,
            int localX,
            int y,
            int localZ,
            int blockId)
        {
            if (y < 0)
            {
                return;
            }
            int chunkY = y / ChunkSize;
            if (chunkY < 0 ||
                chunkY >= chunks.Length)
            {
                return;
            }
            IServerChunk? chunk = chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return;
            }
            int index = ChunkIndex3d(
                localX,
                y % ChunkSize,
                localZ
            );
            chunk.Data[index] = blockId;
            chunk.Data.SetFluid(index, 0);
        }

        private static int ChunkIndex3d(
            int x,
            int y,
            int z) =>
            (y * ChunkSize + z) * ChunkSize + x;

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0
                ? quotient - 1
                : quotient;
        }

        private static ulong StableHash(
            int worldX,
            int worldZ,
            ulong salt)
        {
            unchecked
            {
                ulong value =
                    (uint)worldX *
                        0x9E3779B185EBCA87UL ^
                    (uint)worldZ *
                        0xC2B2AE3D27D4EB4FUL ^
                    salt;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private sealed class LootSectorCandidate
        {
            internal LootSectorCandidate(
                int chunkX,
                int chunkZ,
                ulong score)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
                Score = score;
            }

            internal int ChunkX { get; }
            internal int ChunkZ { get; }
            internal ulong Score { get; }
        }

        private sealed class CityLootConfig
        {
            public int SchemaVersion { get; set; }

            public Dictionary<
                string,
                CityLootTableConfig
            > Levels { get; set; } = new();
        }

        private sealed class CityLootTableConfig
        {
            public int MinimumRolls { get; set; }
            public int MaximumRolls { get; set; }

            public CityLootEntryConfig[] Entries
                { get; set; } =
                Array.Empty<CityLootEntryConfig>();
        }

        private sealed class CityLootEntryConfig
        {
            public string Code { get; set; } =
                string.Empty;
            public int MinimumQuantity { get; set; }
            public int MaximumQuantity { get; set; }
            public double Weight { get; set; }
        }

        private sealed class ResolvedLootTable
        {
            internal ResolvedLootTable(
                int minimumRolls,
                int maximumRolls,
                ResolvedLootEntry[] entries,
                double totalWeight)
            {
                MinimumRolls = minimumRolls;
                MaximumRolls = maximumRolls;
                Entries = entries;
                TotalWeight = totalWeight;
            }

            internal int MinimumRolls { get; }
            internal int MaximumRolls { get; }
            internal ResolvedLootEntry[] Entries
                { get; }
            internal double TotalWeight { get; }
        }

        private sealed class ResolvedLootEntry
        {
            internal ResolvedLootEntry(
                CollectibleObject collectible,
                int minimumQuantity,
                int maximumQuantity,
                double weight)
            {
                Collectible = collectible;
                MinimumQuantity = minimumQuantity;
                MaximumQuantity = maximumQuantity;
                Weight = weight;
            }

            internal CollectibleObject Collectible
                { get; }
            internal int MinimumQuantity { get; }
            internal int MaximumQuantity { get; }
            internal double Weight { get; }
        }

        private sealed class CityAnchor
        {
            internal CityAnchor(
                int x,
                int z,
                int style,
                ulong signature)
            {
                X = x;
                Z = z;
                Style = style;
                Signature = signature;
            }

            internal int X { get; }
            internal int Z { get; }
            internal int Style { get; }
            internal ulong Signature { get; }
        }

        private sealed class PlannedCity
        {
            internal PlannedCity(
                int centerX,
                int centerZ,
                int culture,
                ulong signature)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                Culture = culture;
                Signature = signature;
            }

            internal int CenterX { get; }
            internal int CenterZ { get; }
            internal int Culture { get; }
            internal ulong Signature { get; }
        }
    }
}
