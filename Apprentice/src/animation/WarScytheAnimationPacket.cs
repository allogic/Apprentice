using ProtoBuf;

namespace Apprentice
{
    [ProtoContract]
    public sealed class WarScytheAnimationPacket
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public int ItemId { get; set; }

        [ProtoMember(3)]
        public int Sequence { get; set; }

        [ProtoMember(4)]
        public string AnimationCode { get; set; } = string.Empty;

        [ProtoMember(5)]
        public string Category { get; set; } = string.Empty;

        [ProtoMember(6)]
        public float Speed { get; set; } = 1f;

        [ProtoMember(7)]
        public int EaseInMilliseconds { get; set; }

        [ProtoMember(8)]
        public int EaseOutMilliseconds { get; set; }

        [ProtoMember(9)]
        public bool Stop { get; set; }
    }
}
