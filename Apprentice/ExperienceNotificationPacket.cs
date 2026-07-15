using ProtoBuf;

namespace Apprentice
{
	[ProtoContract]
	public sealed class ExperienceNotificationPacket
	{
		[ProtoMember(1)]
		public string ClassId { get; set; } = string.Empty;

		[ProtoMember(2)]
		public string ClassDisplayName { get; set; } = string.Empty;

		[ProtoMember(3)]
		public string Interaction { get; set; } = string.Empty;

		[ProtoMember(4)]
		public string TargetCode { get; set; } = string.Empty;

		[ProtoMember(5)]
		public double GainedExperience { get; set; }

		[ProtoMember(6)]
		public double PreviousTotalExperience { get; set; }

		[ProtoMember(7)]
		public double NewTotalExperience { get; set; }

		[ProtoMember(8)]
		public int PreviousLevel { get; set; }

		[ProtoMember(9)]
		public int NewLevel { get; set; }
	}
}
