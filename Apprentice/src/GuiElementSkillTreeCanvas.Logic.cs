using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Apprentice
{
	internal sealed partial class GuiElementSkillTreeCanvas
	{
		private NodeEvaluation EvaluateNode(
			SkillTreeDefinition tree,
			SkillNodeDefinition node)
		{
			Entity? entity =
				api.World?
					.Player?
					.Entity;

			if (entity == null)
			{
				return new NodeEvaluation(
					SkillNodeVisualState.Locked,
					false,
					"Player data is not available."
				);
			}

			int level =
				ExpMath.GetLevel(
					ProgressionData
						.GetExperience(
							entity,
							tree.ClassId
						)
				);

			int rank =
				GetNodeRank(
					tree,
					node.Id
				);

			int spent =
				GetSpentPoints(
					tree
				);

			int available =
				GetAvailablePoints(
					tree
				);

			if (rank >=
				node.MaxRank)
			{
				return new NodeEvaluation(
					SkillNodeVisualState.Maximum,
					false,
					"This node is already at maximum rank."
				);
			}

			if (!string.IsNullOrWhiteSpace(
					node.ExclusiveGroup))
			{
				bool conflict =
					tree.Nodes.Any(
						other =>
							!other.Id.Equals(
								node.Id,
								StringComparison.OrdinalIgnoreCase
							) &&
							string.Equals(
								other.ExclusiveGroup,
								node.ExclusiveGroup,
								StringComparison.OrdinalIgnoreCase
							) &&
							GetNodeRank(
								tree,
								other.Id
							) > 0
					);

				if (conflict)
				{
					return new NodeEvaluation(
						SkillNodeVisualState.ExclusiveConflict,
						false,
						"This specialization conflicts with your chosen path."
					);
				}
			}

			if (level <
				node.RequiredClassLevel)
			{
				return new NodeEvaluation(
					rank > 0
						? SkillNodeVisualState.Purchased
						: SkillNodeVisualState.Locked,
					false,
					$"Requires class level {node.RequiredClassLevel}."
				);
			}

			if (spent <
				node.RequiredPointsSpent)
			{
				return new NodeEvaluation(
					rank > 0
						? SkillNodeVisualState.Purchased
						: SkillNodeVisualState.Locked,
					false,
					$"Requires {node.RequiredPointsSpent} points spent."
				);
			}

			if (node.Requires.Any(
					required =>
						GetNodeRank(
							tree,
							required
						) <= 0))
			{
				return new NodeEvaluation(
					rank > 0
						? SkillNodeVisualState.Purchased
						: SkillNodeVisualState.Locked,
					false,
					"A prerequisite node is missing."
				);
			}

			if (node.RequiresAny.Count >
					0 &&
				!node.RequiresAny.Any(
					required =>
						GetNodeRank(
							tree,
							required
						) > 0))
			{
				return new NodeEvaluation(
					rank > 0
						? SkillNodeVisualState.Purchased
						: SkillNodeVisualState.Locked,
					false,
					"Complete one required specialization path."
				);
			}

			if (available <
				node.Cost)
			{
				return new NodeEvaluation(
					rank > 0
						? SkillNodeVisualState.Purchased
						: SkillNodeVisualState.Locked,
					false,
					$"Requires {node.Cost} available skill points."
				);
			}

			return new NodeEvaluation(
				rank > 0
					? SkillNodeVisualState.Purchased
					: SkillNodeVisualState.Available,
				true,
				"Ready to learn."
			);
		}

		private IReadOnlyList<RequirementView>
			BuildRequirements(
				SkillTreeDefinition tree,
				SkillNodeDefinition node)
		{
			Entity? entity =
				api.World?
					.Player?
					.Entity;

			if (entity == null)
			{
				return new[]
				{
					new RequirementView(
						false,
						"Player data unavailable"
					)
				};
			}

			int level =
				ExpMath.GetLevel(
					ProgressionData.GetExperience(
						entity,
						tree.ClassId
					)
				);

			int spent =
				GetSpentPoints(
					tree
				);

			int available =
				GetAvailablePoints(
					tree
				);

			var requirements =
				new List<RequirementView>
				{
					new(
						level >=
							node.RequiredClassLevel,
						$"Class level {node.RequiredClassLevel} " +
						$"(current {level})"
					),

					new(
						spent >=
							node.RequiredPointsSpent,
						$"{node.RequiredPointsSpent} points spent " +
						$"(current {spent})"
					),

					new(
						available >=
							node.Cost,
						$"{node.Cost} available points " +
						$"(current {available})"
					)
				};

			foreach (
				string required
				in node.Requires)
			{
				SkillNodeDefinition? requiredNode =
					tree.Nodes.FirstOrDefault(
						candidate =>
							candidate.Id.Equals(
								required,
								StringComparison.OrdinalIgnoreCase
							)
					);

				requirements.Add(
					new RequirementView(
						GetNodeRank(required) >
							0,
						requiredNode?.DisplayName ??
							required
					)
				);
			}

			if (node.RequiresAny.Count >
				0)
			{
				bool anyMet =
					node.RequiresAny.Any(
						required =>
							GetNodeRank(
								required
							) > 0
					);

				string names =
					string.Join(
						" or ",
						node.RequiresAny.Select(
							required =>
								tree.Nodes.FirstOrDefault(
									candidate =>
										candidate.Id.Equals(
											required,
											StringComparison.OrdinalIgnoreCase
										)
								)?.DisplayName ??
								required
						)
					);

				requirements.Add(
					new RequirementView(
						anyMet,
						names
					)
				);
			}

			if (!string.IsNullOrWhiteSpace(
					node.ExclusiveGroup))
			{
				bool conflict =
					tree.Nodes.Any(
						other =>
							!other.Id.Equals(
								node.Id,
								StringComparison.OrdinalIgnoreCase
							) &&
							string.Equals(
								other.ExclusiveGroup,
								node.ExclusiveGroup,
								StringComparison.OrdinalIgnoreCase
							) &&
							GetNodeRank(
								other.Id
							) > 0
					);

				requirements.Add(
					new RequirementView(
						!conflict,
						"No conflicting specialization"
					)
				);
			}

			return requirements;
		}

		private static string BuildNodeCacheKey(
			string classId,
			string nodeId)
		{
			return $"{classId}/{nodeId}";
		}

		private int GetNodeRank(
			string nodeId)
		{
			return GetNodeRank(
				CurrentTree,
				nodeId
			);
		}

		private int GetNodeRank(
			SkillTreeDefinition tree,
			string nodeId)
		{
			Entity? entity =
				api.World?
					.Player?
					.Entity;

			int watchedRank =
				entity == null
					? 0
					: SkillTreeData.GetNodeRank(
						entity,
						tree.ClassId,
						nodeId
					);

			string cacheKey =
				BuildNodeCacheKey(
					tree.ClassId,
					nodeId
				);

			if (!purchaseRankOverrides.TryGetValue(
					cacheKey,
					out int authoritativeRank))
			{
				return watchedRank;
			}

			if (watchedRank >= authoritativeRank)
			{
				purchaseRankOverrides.Remove(cacheKey);
				return watchedRank;
			}

			return authoritativeRank;
		}

		private int GetSpentPoints(
			SkillTreeDefinition tree)
		{
			int spent = 0;

			foreach (SkillNodeDefinition node in tree.Nodes)
			{
				spent +=
					GetNodeRank(tree, node.Id) *
					node.Cost;
			}

			return spent;
		}

		private int GetAvailablePoints(
			SkillTreeDefinition tree)
		{
			Entity? entity =
				api.World?
					.Player?
					.Entity;

			if (entity == null)
			{
				return 0;
			}

			int watchedAvailable =
				SkillTreeData.GetAvailablePoints(
					entity,
					tree
				);

			if (!purchaseAvailablePointOverrides.TryGetValue(
					tree.ClassId,
					out int authoritativeAvailable))
			{
				return Math.Max(
					0,
					SkillTreeData.GetEarnedPoints(
						entity,
						tree.ClassId
					) -
					GetSpentPoints(tree)
				);
			}

			if (watchedAvailable == authoritativeAvailable)
			{
				purchaseAvailablePointOverrides.Remove(
					tree.ClassId
				);

				return watchedAvailable;
			}

			return authoritativeAvailable;
		}

		private bool IsNodeOnSelectedPath(
			string parentId,
			string childId)
		{
			SkillNodeDefinition? selected =
				CurrentNode;

			if (selected == null)
			{
				return false;
			}

			if (selected.Id.Equals(
					childId,
					StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return selected.Requires.Contains(
					   childId,
					   StringComparer.OrdinalIgnoreCase
				   ) ||
				   selected.RequiresAny.Contains(
					   childId,
					   StringComparer.OrdinalIgnoreCase
				   ) ||
				   selected.Requires.Contains(
					   parentId,
					   StringComparer.OrdinalIgnoreCase
				   ) ||
				   selected.RequiresAny.Contains(
					   parentId,
					   StringComparer.OrdinalIgnoreCase
				   );
		}

		private SkillTreeDefinition CurrentTree
		{
			get
			{
				if (treesById.TryGetValue(
					selectedClassId,
					out SkillTreeDefinition? tree))
				{
					return tree;
				}

				return treesById.Values.First();
			}
		}

		private SkillNodeDefinition? CurrentNode
		{
			get
			{
				SkillNodeDefinition? node =
					CurrentTree.Nodes.FirstOrDefault(
						candidate =>
							candidate.Id.Equals(
								selectedNodeId,
								StringComparison.OrdinalIgnoreCase
							)
					);

				return node ??
					CurrentTree.Nodes
						.FirstOrDefault();
			}
		}

		private SkillNodeDefinition? HoveredNode
		{
			get
			{
				if (hoveredRegionKey == null ||
					!hoveredRegionKey.StartsWith(
						$"{SkillTreeHitKind.Node}:",
						StringComparison.Ordinal))
				{
					return null;
				}

				string nodeId =
					hoveredRegionKey.Substring(
						$"{SkillTreeHitKind.Node}:".Length
					);

				return CurrentTree.Nodes.FirstOrDefault(
					node =>
						node.Id.Equals(
							nodeId,
							StringComparison.OrdinalIgnoreCase
						)
				);
			}
		}

		private bool IsHovered(
			SkillTreeHitKind kind,
			string id)
		{
			return string.Equals(
				hoveredRegionKey,
				$"{kind}:{id}",
				StringComparison.Ordinal
			);
		}

		private SkillTreeHitRegion? FindRegion(
			double x,
			double y)
		{
			double designX =
				x / Math.Max(0.01, canvasScaleX);
			double designY =
				y / Math.Max(0.01, canvasScaleY);

			for (int index = hitRegions.Count - 1; index >= 0; index--)
			{
				SkillTreeHitRegion region = hitRegions[index];
				double testY = region.Kind == SkillTreeHitKind.Tab
					? designY
					: designY - 44;

				if (region.Contains(designX, testY))
				{
					return region;
				}
			}

			return null;
		}

		private void ClampPan()
		{
			double limitX =
				Math.Max(
					20,
					treePanelWidth *
					0.22
				);

			double limitY =
				Math.Max(
					20,
					treePanelHeight *
					0.18
				);

			panX =
				Math.Clamp(
					panX,
					-limitX,
					limitX
				);

			panY =
				Math.Clamp(
					panY,
					-limitY,
					limitY
				);
		}

		private static List<SkillTreeCategory>
			BuildCategories(
				IReadOnlyDictionary<
					string,
					SkillTreeDefinition
				> trees)
		{
			var definitions =
				new[]
				{
					new SkillTreeCategory(
						"combat",
						"Combat",
						new[]
						{
							"warrior",
							"ranger",
							"spearman",
							"shield",
							"tank",
							"hunter"
						}
					),

					new SkillTreeCategory(
						"gathering",
						"Gathering",
						new[]
						{
							"miner",
							"woodworker",
							"farmer",
							"fisher"
						}
					),

					new SkillTreeCategory(
						"crafting",
						"Crafting",
						new[]
						{
							"builder",
							"blacksmith",
							"potter",
							"leatherworker",
							"tailor"
						}
					),

					new SkillTreeCategory(
						"production",
						"Production",
						new[]
						{
							"cook",
							"animalhusbandry",
							"beekeeper"
						}
					)
				};

			var result =
				new List<SkillTreeCategory>();

			foreach (
				SkillTreeCategory category
				in definitions)
			{
				List<string> existing =
					category.ClassIds
						.Where(
							trees.ContainsKey
						)
						.ToList();

				if (existing.Count ==
					0)
				{
					continue;
				}

				result.Add(
					category with
					{
						ClassIds =
							existing
					}
				);
			}

			HashSet<string> included =
				result
					.SelectMany(
						category =>
							category.ClassIds
					)
					.ToHashSet(
						StringComparer.OrdinalIgnoreCase
					);

			List<string> remaining =
				trees.Keys
					.Where(
						classId =>
							!included.Contains(
								classId
							)
					)
					.OrderBy(
						classId =>
							trees[classId]
								.DisplayName,
						StringComparer.OrdinalIgnoreCase
					)
					.ToList();

			if (remaining.Count >
				0)
			{
				result.Add(
					new SkillTreeCategory(
						"other",
						"Other",
						remaining
					)
				);
			}

			return result;
		}
	}
}

