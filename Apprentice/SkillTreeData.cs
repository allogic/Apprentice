using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Apprentice
{
	internal static class SkillTreeData
	{
		private const string SkillTreeKey = "skilltree";
		private const string NodesKey = "nodes";

		public static int GetNodeRank(Entity entity, string classId, string nodeId)
		{
			return ProgressionData.GetClassProgress(entity, classId)?
				.GetTreeAttribute(SkillTreeKey)?
				.GetTreeAttribute(NodesKey)?
				.GetInt(nodeId, 0) ?? 0;
		}

		public static void SetNodeRank(
			IServerPlayer player,
			string classId,
			string nodeId,
			int rank)
		{
			ArgumentNullException.ThrowIfNull(player);

			ITreeAttribute classProgress =
				ProgressionData.GetOrCreateClassProgress(player, classId);
			ITreeAttribute skillTree =
				classProgress.GetOrAddTreeAttribute(SkillTreeKey);
			ITreeAttribute nodes =
				skillTree.GetOrAddTreeAttribute(NodesKey);

			nodes.SetInt(nodeId, Math.Max(0, rank));
			player.Entity.WatchedAttributes.MarkPathDirty(
				$"{ApprenticeConstants.ProgressionRootKey}/" +
				$"{ApprenticeConstants.ClassesTreeKey}/{classId}/" +
				$"{SkillTreeKey}/{NodesKey}/{nodeId}"
			);
		}

		public static int GetSpentPoints(
			Entity entity,
			SkillTreeDefinition tree)
		{
			int spent = 0;
			foreach (SkillNodeDefinition node in tree.Nodes)
			{
				spent += GetNodeRank(entity, tree.ClassId, node.Id) * node.Cost;
			}
			return spent;
		}

		public static int GetEarnedPoints(Entity entity, string classId)
		{
			int level = ExpMath.GetLevel(
				ProgressionData.GetExperience(entity, classId)
			);
			return Math.Max(0, level - 1);
		}

		public static int GetAvailablePoints(
			Entity entity,
			SkillTreeDefinition tree)
		{
			return Math.Max(
				0,
				GetEarnedPoints(entity, tree.ClassId) -
				GetSpentPoints(entity, tree)
			);
		}
	}
}
