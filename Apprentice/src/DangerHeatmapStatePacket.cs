using System;
using ProtoBuf;

namespace Apprentice
{
    [ProtoContract]
    public sealed class DangerHeatmapRequestPacket
    {
        [ProtoMember(1)]
        public bool Request { get; set; } = true;
    }

    internal static class DangerHeatmapClientRuntime
    {
        internal static Action? RequestState { get; set; }
        internal static DangerHeatmapStatePacket? LatestState { get; set; }
        internal static DangerHeatmapLayer? Layer { get; set; }
    }

    /// <summary>
    /// Explicit server-to-client snapshot for the map overlay. World-map
    /// layer view packets are not guaranteed to run after a new world's
    /// deferred danger-anchor initialization, so the gameplay network channel
    /// owns the initial state delivery.
    /// </summary>
    [ProtoContract]
    public sealed class DangerHeatmapStatePacket
    {
        [ProtoMember(1)]
        public int SchemaVersion { get; set; }

        [ProtoMember(2)]
        public bool Enabled { get; set; }

        [ProtoMember(3)]
        public double AnchorX { get; set; }

        [ProtoMember(4)]
        public double AnchorZ { get; set; }

        [ProtoMember(5)]
        public double BaseRadius { get; set; }

        [ProtoMember(6)]
        public double RingWidth { get; set; }

        [ProtoMember(7)]
        public int MaximumTier { get; set; }

        [ProtoMember(8)]
        public double HealthPerTier { get; set; }

        [ProtoMember(9)]
        public double DamagePerTier { get; set; }

        [ProtoMember(10)]
        public string[] Palette { get; set; } = Array.Empty<string>();

        internal static DangerHeatmapStatePacket FromState(
            DangerWorldState state)
        {
            return new DangerHeatmapStatePacket
            {
                SchemaVersion = state.SchemaVersion,
                Enabled = state.Enabled,
                AnchorX = state.AnchorX,
                AnchorZ = state.AnchorZ,
                BaseRadius = state.BaseRadius,
                RingWidth = state.RingWidth,
                MaximumTier = state.MaximumTier,
                HealthPerTier = state.HealthPerTier,
                DamagePerTier = state.DamagePerTier,
                Palette = state.Palette ?? Array.Empty<string>()
            };
        }

        internal DangerWorldState ToState()
        {
            return new DangerWorldState
            {
                SchemaVersion = SchemaVersion,
                Enabled = Enabled,
                AnchorX = AnchorX,
                AnchorZ = AnchorZ,
                BaseRadius = BaseRadius,
                RingWidth = RingWidth,
                MaximumTier = MaximumTier,
                HealthPerTier = HealthPerTier,
                DamagePerTier = DamagePerTier,
                Palette = Palette ?? Array.Empty<string>()
            };
        }
    }
}
