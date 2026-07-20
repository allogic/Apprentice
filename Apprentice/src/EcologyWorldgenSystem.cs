using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Bounded, deterministic new-chunk placement for Apprentice-owned plants.
    /// It never scans existing chunks and never touches foreign resources.
    /// </summary>
    internal sealed class EcologyWorldgenSystem : IDisposable
    {
        private const int ChunkSize = 32;
        private readonly ICoreServerAPI api;
        private readonly IReadOnlyList<EcologyDefinition> definitions;
        private readonly Dictionary<string, int> blockIds =
            new(StringComparer.OrdinalIgnoreCase);
        private bool initialized;

        public EcologyWorldgenSystem(
            ICoreServerAPI api,
            ApprenticeContentRegistry registry)
        {
            this.api = api;
            definitions = registry.Ecology
                .Where(value => !string.IsNullOrWhiteSpace(value.WorldgenBlockCode))
                .ToArray();
            if (definitions.Count == 0) return;

            api.Event.InitWorldGenerator(OnInitWorldGenerator, "standard");
            api.Event.ChunkColumnGeneration(
                OnChunkColumnGeneration,
                EnumWorldGenPass.Vegetation,
                "standard"
            );
        }

        private void OnInitWorldGenerator()
        {
            blockIds.Clear();
            foreach (EcologyDefinition definition in definitions)
            {
                Block? block = api.World.GetBlock(
                    new AssetLocation(definition.WorldgenBlockCode!)
                );
                if (block != null)
                {
                    blockIds[definition.Id] = block.Id;
                }
            }
            initialized = true;
        }

        private void OnChunkColumnGeneration(IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = DangerTierRuntime.WorldState;
            if (!initialized || state?.Enabled != true || blockIds.Count == 0)
            {
                return;
            }

            int worldX = request.ChunkX * ChunkSize + ChunkSize / 2;
            int worldZ = request.ChunkZ * ChunkSize + ChunkSize / 2;
            int tier = GetTierAt(state, worldX, worldZ);
            if (tier <= 0) return;

            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            foreach (EcologyDefinition definition in definitions)
            {
                if (tier < definition.MinimumTier ||
                    !blockIds.TryGetValue(definition.Id, out int blockId))
                {
                    continue;
                }

                LCGRandom random = new(
                    api.WorldManager.Seed ^ StableHash(definition.Id)
                );
                random.InitPositionSeed(request.ChunkX, request.ChunkZ);
                double chance = Math.Clamp(
                    definition.WorldgenChancePerTier *
                        (tier - definition.MinimumTier + 1),
                    0,
                    0.3
                );

                for (int attempt = 0;
                    attempt < definition.WorldgenAttemptsPerChunk;
                    attempt++)
                {
                    if (random.NextDouble() >= chance) continue;
                    int x = random.NextInt(ChunkSize);
                    int z = random.NextInt(ChunkSize);
                    int y = mapChunk.WorldGenTerrainHeightMap[z * ChunkSize + x] + 1;
                    if (y <= 0 || y >= api.WorldManager.MapSizeY) continue;

                    int chunkY = y / ChunkSize;
                    int localY = y % ChunkSize;
                    if (chunkY < 0 || chunkY >= request.Chunks.Length) continue;
                    int index = (ChunkSize * localY + z) * ChunkSize + x;
                    IChunkBlocks data = request.Chunks[chunkY].Data;
                    if (data.GetBlockIdUnsafe(index) != 0) continue;
                    data.SetBlockUnsafe(index, blockId);
                }
            }
        }

        private static int GetTierAt(DangerWorldState state, double x, double z)
        {
            double dx = x - state.AnchorX;
            double dz = z - state.AnchorZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            return Math.Clamp(
                (int)Math.Ceiling(
                    (distance - state.BaseRadius) / state.RingWidth
                ),
                0,
                state.MaximumTier
            );
        }

        private static long StableHash(string value)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211L;
                }
                return hash;
            }
        }

        public void Dispose()
        {
            // Worldgen handler collections are owned and discarded by the
            // server world lifecycle; this object holds no tick listeners or
            // thread-local accessors that need unregistering.
            blockIds.Clear();
            initialized = false;
        }
    }
}
