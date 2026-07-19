using System.Collections.Generic;
using ProtoBuf;

namespace Apprentice
{
    [ProtoContract]
    public sealed class RaceBodyPacket
    {
        [ProtoMember(1)]
        public float Height { get; set; } = 0.5f;

        [ProtoMember(2)]
        public float Thickness { get; set; } = 0.5f;

        [ProtoMember(3)]
        public string Horns { get; set; } = "none";

        [ProtoMember(4)]
        public string Teeth { get; set; } = "none";

        [ProtoMember(5)]
        public string Subclass { get; set; } = "select";

        [ProtoMember(6)]
        public string Profession { get; set; } = "select";

        [ProtoMember(7)]
        public string HornColor { get; set; } = "yellowish-white";

        [ProtoMember(8)]
        public string RaceClass { get; set; } = "apprentice-race-human";

        [ProtoMember(9)]
        public Dictionary<string, string> AppearanceParts { get; set; } = new();

        [ProtoMember(10)]
        public int SchemaVersion { get; set; } = 2;

        [ProtoMember(11)]
        public long RequestId { get; set; }
    }

    [ProtoContract]
    public sealed class RaceSaveResultPacket
    {
        [ProtoMember(1)]
        public long RequestId { get; set; }

        [ProtoMember(2)]
        public bool Success { get; set; }

        [ProtoMember(3)]
        public string Error { get; set; } = "";
    }
}
