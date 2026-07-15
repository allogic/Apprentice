using ProtoBuf;

namespace Apprentice
{
    [ProtoContract]
    public sealed class SkillPurchaseRequestPacket
    {
        [ProtoMember(1)]
        public string ClassId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public string NodeId { get; set; } = string.Empty;
    }

    [ProtoContract]
    public sealed class SkillPurchaseResultPacket
    {
        [ProtoMember(1)]
        public bool Success { get; set; }

        [ProtoMember(2)]
        public string ClassId { get; set; } = string.Empty;

        [ProtoMember(3)]
        public string NodeId { get; set; } = string.Empty;

        [ProtoMember(4)]
        public string Message { get; set; } = string.Empty;

        [ProtoMember(5)]
        public int NewRank { get; set; }

        [ProtoMember(6)]
        public int AvailablePoints { get; set; }
    }
}
