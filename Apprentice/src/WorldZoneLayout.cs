using System;

namespace Apprentice
{
    /// <summary>
    /// Persisted, server-authoritative concentric realm layout. The same
    /// object drives danger scaling, ecology, world generation and the map
    /// overlay so those systems cannot disagree about a boundary.
    /// </summary>
    internal sealed class DangerWorldState
    {
        public int SchemaVersion { get; set; } = WorldZoneLayout.CurrentSchema;
        public bool Enabled { get; set; } = true;
        public double AnchorX { get; set; }
        public double AnchorZ { get; set; }
        public double BaseRadius { get; set; }
        public double RingWidth { get; set; }
        public int MaximumTier { get; set; }
        public double HealthPerTier { get; set; }
        public double DamagePerTier { get; set; }
        public string[] Palette { get; set; } = Array.Empty<string>();
        public string[] RealmNames { get; set; } = Array.Empty<string>();
        public string WorldgenProfile { get; set; } =
            WorldZoneLayout.LegacyProfile;
        public bool RealmWorldgenEnabled { get; set; }
        public int DesertTemperatureCelsius { get; set; } = 38;
        public int DesertRainfall { get; set; } = 4;
        public int DeepSeaDepth { get; set; } = 48;
        public int DeepSeaShoreWidth { get; set; } = 128;
    }

    internal static class WorldZoneLayout
    {
        internal const int CurrentSchema = 3;
        internal const string ConcentricRealmsProfile =
            "concentric-realms-v1";
        internal const string LegacyProfile = "legacy-danger-rings";

        internal static int GetLevelAt(
            DangerWorldState? state,
            double x,
            double z)
        {
            if (state == null || !state.Enabled ||
                state.RingWidth <= 0 ||
                !double.IsFinite(x) ||
                !double.IsFinite(z))
            {
                return 0;
            }

            double dx = x - state.AnchorX;
            double dz = z - state.AnchorZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double rings = Math.Ceiling(
                (distance - state.BaseRadius) / state.RingWidth
            );
            return Math.Clamp((int)rings, 0, state.MaximumTier);
        }

        internal static bool IsLevelAt(
            DangerWorldState? state,
            int level,
            double x,
            double z) =>
            state != null &&
            level >= 0 &&
            level <= state.MaximumTier &&
            GetLevelAt(state, x, z) == level;

        internal static bool IsInsideLevelCore(
            DangerWorldState? state,
            int level,
            double inset,
            double x,
            double z)
        {
            if (state == null || inset < 0 ||
                !double.IsFinite(inset) ||
                !IsLevelAt(state, level, x, z))
            {
                return false;
            }

            double dx = x - state.AnchorX;
            double dz = z - state.AnchorZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double inner = GetInnerRadius(state, level) + inset;
            double outer = GetOuterRadius(state, level);
            return distance > inner &&
                (double.IsPositiveInfinity(outer) ||
                 distance <= outer - inset);
        }

        internal static double GetOuterRadius(
            DangerWorldState state,
            int level)
        {
            if (level <= 0)
            {
                return state.BaseRadius;
            }

            if (level >= state.MaximumTier)
            {
                return double.PositiveInfinity;
            }

            return state.BaseRadius + state.RingWidth * level;
        }

        internal static double GetInnerRadius(
            DangerWorldState state,
            int level) =>
            level <= 0
                ? 0
                : state.BaseRadius + state.RingWidth * (level - 1);

        internal static bool ChunkIntersectsLevel(
            DangerWorldState? state,
            int level,
            int chunkX,
            int chunkZ,
            int chunkSize)
        {
            if (chunkSize <= 0)
            {
                return false;
            }

            return RectangleIntersectsLevel(
                state,
                level,
                (double)chunkX * chunkSize,
                (double)chunkZ * chunkSize,
                (double)(chunkX + 1) * chunkSize,
                (double)(chunkZ + 1) * chunkSize
            );
        }

        internal static bool ChunkFullyInsideLevel(
            DangerWorldState? state,
            int level,
            int chunkX,
            int chunkZ,
            int chunkSize)
        {
            if (chunkSize <= 0)
            {
                return false;
            }

            return RectangleFullyInsideLevel(
                state,
                level,
                (double)chunkX * chunkSize,
                (double)chunkZ * chunkSize,
                (double)(chunkX + 1) * chunkSize,
                (double)(chunkZ + 1) * chunkSize
            );
        }

        internal static bool RectangleIntersectsLevel(
            DangerWorldState? state,
            int level,
            double minX,
            double minZ,
            double maxX,
            double maxZ)
        {
            if (state == null ||
                level < 0 || level > state.MaximumTier)
            {
                return false;
            }

            GetDistanceRangeSquared(
                state,
                minX,
                minZ,
                maxX,
                maxZ,
                out double minimumDistanceSquared,
                out double maximumDistanceSquared
            );
            double inner = GetInnerRadius(state, level);
            double outer = GetOuterRadius(state, level);
            return maximumDistanceSquared > inner * inner &&
                (double.IsPositiveInfinity(outer) ||
                 minimumDistanceSquared <= outer * outer);
        }

        internal static bool RectangleFullyInsideLevel(
            DangerWorldState? state,
            int level,
            double minX,
            double minZ,
            double maxX,
            double maxZ)
        {
            if (state == null ||
                level < 0 || level > state.MaximumTier)
            {
                return false;
            }

            GetDistanceRangeSquared(
                state,
                minX,
                minZ,
                maxX,
                maxZ,
                out double minimumDistanceSquared,
                out double maximumDistanceSquared
            );
            double inner = GetInnerRadius(state, level);
            double outer = GetOuterRadius(state, level);
            return minimumDistanceSquared > inner * inner &&
                (double.IsPositiveInfinity(outer) ||
                 maximumDistanceSquared <= outer * outer);
        }

        internal static bool TryValidate(
            DangerWorldState? state,
            out string error)
        {
            if (state == null)
            {
                error = "the persisted realm layout is missing";
                return false;
            }

            if (!double.IsFinite(state.AnchorX) ||
                !double.IsFinite(state.AnchorZ))
            {
                error = "the realm anchor is not finite";
                return false;
            }

            if (state.BaseRadius < 0 ||
                !double.IsFinite(state.BaseRadius) ||
                state.RingWidth <= 0 ||
                !double.IsFinite(state.RingWidth))
            {
                error = "the realm radii are invalid";
                return false;
            }

            if (state.MaximumTier < 1 ||
                state.Palette == null ||
                state.Palette.Length != state.MaximumTier + 1 ||
                state.RealmNames == null ||
                state.RealmNames.Length != state.MaximumTier + 1)
            {
                error = "realm names or colors do not match MaximumTier";
                return false;
            }

            if (state.DesertTemperatureCelsius < -50 ||
                state.DesertTemperatureCelsius > 60 ||
                state.DesertRainfall < 0 ||
                state.DesertRainfall > 255)
            {
                error = "the persisted desert climate is invalid";
                return false;
            }

            if (state.DeepSeaDepth < 8 ||
                state.DeepSeaDepth > 256 ||
                state.DeepSeaShoreWidth < 0 ||
                state.DeepSeaShoreWidth * 2 >= state.RingWidth)
            {
                error = "the persisted deep-sea profile is invalid";
                return false;
            }

            if (state.MaximumTier >= 2 && state.BaseRadius > 0)
            {
                double outsideStep = Math.Min(
                    0.25,
                    state.RingWidth / 4
                );
                double firstBoundary = state.AnchorX +
                    state.BaseRadius;
                double secondBoundary = firstBoundary +
                    state.RingWidth;
                if (GetLevelAt(
                        state,
                        firstBoundary,
                        state.AnchorZ) != 0 ||
                    GetLevelAt(
                        state,
                        firstBoundary + outsideStep,
                        state.AnchorZ) != 1 ||
                    GetLevelAt(
                        state,
                        secondBoundary,
                        state.AnchorZ) != 1 ||
                    GetLevelAt(
                        state,
                        secondBoundary + outsideStep,
                        state.AnchorZ) != 2)
                {
                    error =
                        "the persisted realm boundaries fail their contract";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static void GetDistanceRangeSquared(
            DangerWorldState state,
            double minX,
            double minZ,
            double maxX,
            double maxZ,
            out double minimumDistanceSquared,
            out double maximumDistanceSquared)
        {
            double nearestX = Math.Clamp(state.AnchorX, minX, maxX);
            double nearestZ = Math.Clamp(state.AnchorZ, minZ, maxZ);
            double nearDx = nearestX - state.AnchorX;
            double nearDz = nearestZ - state.AnchorZ;
            minimumDistanceSquared = nearDx * nearDx + nearDz * nearDz;

            double farDx = Math.Max(
                Math.Abs(minX - state.AnchorX),
                Math.Abs(maxX - state.AnchorX)
            );
            double farDz = Math.Max(
                Math.Abs(minZ - state.AnchorZ),
                Math.Abs(maxZ - state.AnchorZ)
            );
            maximumDistanceSquared = farDx * farDx + farDz * farDz;
        }
    }
}
