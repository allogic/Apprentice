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
		private void DrawBackground(
			Context ctx,
			double width,
			double height)
		{
			ctx.SetSourceRGBA(
				0.035,
				0.045,
				0.065,
				0.98
			);

			ctx.Rectangle(
				0,
				0,
				width,
				height
			);

			ctx.Fill();

			// Subtle layered background.
			ctx.SetSourceRGBA(
				0.11,
				0.12,
				0.15,
				0.24
			);

			for (int index = 0;
				 index < 7;
				 index++)
			{
				double inset =
					index * 18;

				RoundRectangle(
					ctx,
					inset,
					inset,
					width -
						inset * 2,
					height -
						inset * 2,
					18
				);

				ctx.Stroke();
			}
		}

		private void DrawHeader(
			Context ctx,
			double x,
			double y,
			double width,
			double height)
		{
			ThemePalette theme =
				GetTheme(
					CurrentTree
				);

			DrawPanel(
				ctx,
				x,
				y,
				width,
				height,
				theme.PanelR,
				theme.PanelG,
				theme.PanelB,
				0.95,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				0.78
			);

			double iconSize =
				height -
				20;

			double iconX =
				x +
				12;

			double iconY =
				y +
				10;

			DrawClassIcon(
				ctx,
				CurrentTree,
				iconX,
				iconY,
				iconSize,
				theme
			);

			double titleX =
				iconX +
				iconSize +
				14;

			DrawFittedText(
				ctx,
				titleFont,
				CurrentTree.DisplayName,
				titleX,
				y + 10,
				Math.Max(
					120,
					x + width - 350 - titleX
				)
			);

			Entity? entity =
				api.World?
					.Player?
					.Entity;

			int level =
				entity == null
					? 1
					: ExpMath.GetLevel(
						ProgressionData
							.GetExperience(
								entity,
								CurrentTree.ClassId
							)
					);

			int mastery =
				Math.Clamp(
					level - 1,
					0,
					MasteryCap
				);

			int available =
				entity == null
					? 0
					: GetAvailablePoints(
						CurrentTree
					);

			int spent =
				entity == null
					? 0
					: GetSpentPoints(
						CurrentTree
					);

			DrawBadge(
				ctx,
				x + width - 330,
				y + 13,
				92,
				27,
				$"LEVEL {level}",
				theme
			);

			DrawBadge(
				ctx,
				x + width - 228,
				y + 13,
				102,
				27,
				$"POINTS {available}",
				theme
			);

			DrawBadge(
				ctx,
				x + width - 116,
				y + 13,
				102,
				27,
				$"SPENT {spent}",
				theme
			);

			double totalExperience =
				entity == null
					? 0
					: ProgressionData
						.GetExperience(
							entity,
							CurrentTree.ClassId
						);

			double levelStart =
				ExpMath.GetLevelStartExp(
					level
				);

			double levelEnd =
				ExpMath.GetLevelEndExp(
					level
				);

			double intoLevel =
				Math.Max(
					0,
					totalExperience -
					levelStart
				);

			double required =
				Math.Max(
					1,
					levelEnd -
					levelStart
				);

			double progress =
				Math.Clamp(
					intoLevel /
					required,
					0,
					1
				);

			double barX =
				titleX;

			double barY =
				y +
				height -
				22;

			const double xpCounterWidth =
				210;

			double barWidth =
				width -
				(
					barX -
					x
				) -
				16 -
				xpCounterWidth;

			DrawProgressBar(
				ctx,
				barX,
				barY,
				barWidth,
				10,
				progress,
				theme
			);

			DrawFittedText(
				ctx,
				tinyFont,
				$"XP {intoLevel:0.##} / {required:0.##}",
				barX +
					barWidth +
					12,
				barY - 2,
				xpCounterWidth -
					12
			);
		}

		private void DrawSidebar(
			Context ctx,
			double x,
			double y,
			double width,
			double height)
		{
			ThemePalette theme =
				GetTheme(
					CurrentTree
				);

			DrawPanel(
				ctx,
				x,
				y,
				width,
				height,
				0.07,
				0.08,
				0.11,
				0.97,
				0.20,
				0.22,
				0.27,
				0.9
			);

			DrawText(
				ctx,
				sectionFont,
				"PROFESSIONS",
				x + 15,
				y + 13
			);

			double categoryY =
				y +
				45;

			foreach (
				SkillTreeCategory category
				in categories)
			{
				bool selected =
					category.Id.Equals(
						selectedCategoryId,
						StringComparison.OrdinalIgnoreCase
					);

				bool hovered =
					IsHovered(
						SkillTreeHitKind.Category,
						category.Id
					);

				DrawSidebarEntry(
					ctx,
					x + 11,
					categoryY,
					width - 22,
					34,
					category.Id,
					category.DisplayName,
					selected,
					hovered,
					theme
				);

				hitRegions.Add(
					new SkillTreeHitRegion(
						SkillTreeHitKind.Category,
						category.Id,
						x + 11,
						categoryY,
						width - 22,
						34
					)
				);

				categoryY +=
					39;
			}

			DrawDivider(
				ctx,
				x + 12,
				categoryY + 4,
				width - 24
			);

			SkillTreeCategory selectedCategory =
				categories.First(
					category =>
						category.Id.Equals(
							selectedCategoryId,
							StringComparison.OrdinalIgnoreCase
						)
				);

			double classY =
				categoryY +
				18;

			foreach (
				string classId
				in selectedCategory.ClassIds)
			{
				SkillTreeDefinition tree =
					treesById[classId];

				bool selected =
					classId.Equals(
						selectedClassId,
						StringComparison.OrdinalIgnoreCase
					);

				bool hovered =
					IsHovered(
						SkillTreeHitKind.Class,
						classId
					);

				Entity? entity =
					api.World?
						.Player?
						.Entity;

				int level =
					entity == null
						? 1
						: ExpMath.GetLevel(
							ProgressionData.GetExperience(
								entity,
								classId
							)
						);

				DrawClassEntry(
					ctx,
					x + 11,
					classY,
					width - 22,
					39,
					tree,
					level,
					selected,
					hovered,
					theme
				);

				hitRegions.Add(
					new SkillTreeHitRegion(
						SkillTreeHitKind.Class,
						classId,
						x + 11,
						classY,
						width - 22,
						39
					)
				);

				classY +=
					44;
			}
		}

		private void DrawTreePanel(
			Context ctx,
			double x,
			double y,
			double width,
			double height)
		{
			ThemePalette theme =
				GetTheme(
					CurrentTree
				);

			DrawPanel(
				ctx,
				x,
				y,
				width,
				height,
				0.045,
				0.055,
				0.075,
				0.985,
				0.15,
				0.17,
				0.22,
				0.95
			);

			hitRegions.Add(
				new SkillTreeHitRegion(
					SkillTreeHitKind.TreeArea,
					"tree",
					x,
					y,
					width,
					height
				)
			);

			DrawText(
				ctx,
				sectionFont,
				"SKILL TREE",
				x + 16,
				y + 13
			);

			DrawText(
				ctx,
				tinyFont,
				$"Zoom {zoom * 100:0}%",
				x + width - 104,
				y + 16
			);

			const double resetWidth = 104;

			bool resetHovered =
				IsHovered(
					SkillTreeHitKind.ResetView,
					"reset"
				);

			DrawMiniButton(
				ctx,
				x + width -
					resetWidth -
					12,
				y + 39,
				resetWidth,
				24,
				"RESET VIEW",
				resetHovered,
				true,
				theme
			);

			hitRegions.Add(
				new SkillTreeHitRegion(
					SkillTreeHitKind.ResetView,
					"reset",
					x + width -
						resetWidth -
						12,
					y + 39,
					resetWidth,
					24
				)
			);

			BuildNodeLayouts(
				x,
				y,
				width,
				height
			);

			DrawConnections(
				ctx,
				theme
			);

			foreach (
				SkillNodeDefinition node
				in CurrentTree.Nodes)
			{
				if (!nodeLayouts.TryGetValue(
					node.Id,
					out SkillNodeLayout? layout))
				{
					continue;
				}

				NodeEvaluation evaluation =
					EvaluateNode(
						CurrentTree,
						node
					);

				DrawNode(
					ctx,
					layout,
					evaluation,
					theme
				);

				hitRegions.Add(
					new SkillTreeHitRegion(
						SkillTreeHitKind.Node,
						node.Id,
						layout.X -
							layout.Radius -
							8,
						layout.Y -
							layout.Radius -
							8,
						(
							layout.Radius +
							8
						) * 2,
						(
							layout.Radius +
							8
						) * 2
					)
				);
			}
		}

		private void BuildNodeLayouts(
			double x,
			double y,
			double width,
			double height)
		{
			double contentX =
				x +
				28;

			double contentY =
				y +
				70;

			double contentWidth =
				width -
				56;

			double contentHeight =
				height -
				95;

			double centerX =
				contentX +
				contentWidth / 2;

			double centerY =
				contentY +
				contentHeight / 2;

			double[] columnPositions =
			{
				0.17,
				0.50,
				0.83
			};

			foreach (
				SkillNodeDefinition node
				in CurrentTree.Nodes)
			{
				int column =
					Math.Clamp(
						node.Column,
						0,
						2
					);

				int row =
					Math.Clamp(
						node.Row,
						0,
						4
					);

				double baseX =
					contentX +
					contentWidth *
					columnPositions[column];

				double baseY =
					contentY +
					36 +
					(
						contentHeight -
						72
					) *
					row /
					4.0;

				double transformedX =
					centerX +
					(
						baseX -
						centerX
					) *
					zoom +
					panX;

				double transformedY =
					centerY +
					(
						baseY -
						centerY
					) *
					zoom +
					panY;

				double radius =
					(
						node.Capstone
							? 42
							: node.Row >= 2
								? 34
								: 31
					) *
					zoom;

				nodeLayouts[node.Id] =
					new SkillNodeLayout(
						node,
						transformedX,
						transformedY,
						radius
					);
			}
		}

		private void DrawConnections(
			Context ctx,
			ThemePalette theme)
		{
			foreach (
				SkillNodeDefinition child
				in CurrentTree.Nodes)
			{
				if (!nodeLayouts.TryGetValue(
						child.Id,
						out SkillNodeLayout? childLayout))
				{
					continue;
				}

				List<string> parents =
					child.Requires
						.Concat(
							child.RequiresAny
						)
						.Distinct(
							StringComparer.OrdinalIgnoreCase
						)
						.ToList();

				foreach (
					string parentId
					in parents)
				{
					if (!nodeLayouts.TryGetValue(
							parentId,
							out SkillNodeLayout? parentLayout))
					{
						continue;
					}

					int parentRank =
						GetNodeRank(
							parentId
						);

					int childRank =
						GetNodeRank(
							child.Id
						);

					if (childRank > 0)
					{
						SetColor(
							ctx,
							theme.SuccessR,
							theme.SuccessG,
							theme.SuccessB,
							0.95
						);
					}
					else if (parentRank > 0)
					{
						SetColor(
							ctx,
							theme.AccentR,
							theme.AccentG,
							theme.AccentB,
							0.40
						);
					}
					else
					{
						SetColor(
							ctx,
							0.23,
							0.25,
							0.30,
							0.78
						);
					}

					ctx.LineWidth = 2.2;

					double midY =
						(
							parentLayout.Y +
							childLayout.Y
						) /
						2;

					ctx.NewPath();

					ctx.MoveTo(
						parentLayout.X,
						parentLayout.Y +
							parentLayout.Radius
					);

					ctx.CurveTo(
						parentLayout.X,
						midY,
						childLayout.X,
						midY,
						childLayout.X,
						childLayout.Y -
							childLayout.Radius
					);

					ctx.Stroke();
				}
			}
		}

		private void DrawNode(
			Context ctx,
			SkillNodeLayout layout,
			NodeEvaluation evaluation,
			ThemePalette theme)
		{
			ctx.NewPath();

			SkillNodeDefinition node =
				layout.Node;

			bool selected =
				node.Id.Equals(
					selectedNodeId,
					StringComparison.OrdinalIgnoreCase
				);

			bool hovered =
				IsHovered(
					SkillTreeHitKind.Node,
					node.Id
				);

			(double fillR,
			 double fillG,
			 double fillB,
			 double borderR,
			 double borderG,
			 double borderB) =
				GetNodeColors(
					evaluation.State,
					theme
				);

			double radius =
				layout.Radius;

			if (hovered)
			{
				radius += 4;
			}

			if (selected)
			{
				DrawGlow(
					ctx,
					layout.X,
					layout.Y,
					radius + 9,
					theme.AccentR,
					theme.AccentG,
					theme.AccentB,
					0.30
				);
			}
			else if (evaluation.State ==
					 SkillNodeVisualState.Available)
			{
				DrawGlow(
					ctx,
					layout.X,
					layout.Y,
					radius + 6,
					theme.AccentR,
					theme.AccentG,
					theme.AccentB,
					0.18
				);
			}

			DrawNodeShape(
				ctx,
				node,
				layout.X,
				layout.Y,
				radius
			);

			SetColor(
				ctx,
				fillR,
				fillG,
				fillB,
				0.96
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				selected
					? 0.98
					: borderR,
				selected
					? 0.93
					: borderG,
				selected
					? 0.72
					: borderB,
				1
			);

			ctx.LineWidth =
				selected
					? 4
					: hovered
						? 3
						: 2;

			ctx.Stroke();

			// Cairo paths are not part of Save/Restore state. Clear any
			// unfinished path before and after icon rendering so one node's
			// vector path cannot connect to a later node as a stray line.
			ctx.NewPath();

			DrawIsolatedSkillNodeIcon(
				ctx,
				CurrentTree.ClassId,
				node,
				layout.X,
				layout.Y - 5,
				Math.Min(
					36,
					radius * 1.18
				),
				0.98,
				0.95,
				0.88,
				1
			);

			ctx.NewPath();

			int rank =
				GetNodeRank(
					node.Id
				);

			DrawCenteredText(
				ctx,
				tinyFont,
				$"{rank}/{node.MaxRank}",
				layout.X,
				layout.Y + 10
			);

			string label =
				Shorten(
					node.DisplayName,
					20
				);

			double labelY =
				layout.Y +
				radius +
				8;

			// The two specialization labels sit directly on the vertical
			// connector leading to their expert nodes. Raise them slightly;
			// also lift capstone labels so long names stay inside the panel.
			if (node.Id.Equals(
					"path-a",
					StringComparison.OrdinalIgnoreCase) ||
				node.Id.Equals(
					"path-b",
					StringComparison.OrdinalIgnoreCase))
			{
				labelY -= 8;
			}
			else if (node.Capstone)
			{
				labelY -= 8;
			}

			// Connections are rendered first, but their strokes can still be
			// visible through glyph gaps. A compact panel-colored backing
			// keeps every node label legible without altering the tree lines.
			double labelWidth =
				MeasureTextWidth(
					tinyFont,
					label
				);

			RoundRectangle(
				ctx,
				layout.X -
					labelWidth / 2 -
					4,
				labelY - 1,
				labelWidth + 8,
				17,
				3
			);

			SetColor(
				ctx,
				0.045,
				0.055,
				0.075,
				0.94
			);

			ctx.Fill();

			DrawCenteredText(
				ctx,
				tinyFont,
				label,
				layout.X,
				labelY,
				evaluation.State ==
					SkillNodeVisualState.Locked
					? 0.50
					: 0.84,
				evaluation.State ==
					SkillNodeVisualState.Locked
					? 0.52
					: 0.87,
				evaluation.State ==
					SkillNodeVisualState.Locked
					? 0.57
					: 0.91,
				1
			);

			if (node.Capstone)
			{
				DrawCenteredText(
					ctx,
					tinyFont,
					"CAPSTONE",
					layout.X,
					layout.Y -
						radius -
						19,
					theme.AccentR,
					theme.AccentG,
					theme.AccentB,
					1
				);
			}

			ctx.NewPath();
		}

		private void DrawDetailsPanel(
			Context ctx,
			double x,
			double y,
			double width,
			double height)
		{
			ThemePalette theme =
				GetTheme(
					CurrentTree
				);

			DrawPanel(
				ctx,
				x,
				y,
				width,
				height,
				0.065,
				0.075,
				0.105,
				0.985,
				0.17,
				0.19,
				0.24,
				0.96
			);

			SkillNodeDefinition? displayNode =
				HoveredNode ??
				CurrentNode;

			DrawText(
				ctx,
				sectionFont,
				displayNode?.Capstone == true
					? "CAPSTONE DETAILS"
					: "NODE DETAILS",
				x + 17,
				y + 15
			);

			if (displayNode == null)
			{
				DrawText(
					ctx,
					bodyFont,
					"No node selected.",
					x + 17,
					y + 53
				);

				return;
			}

			NodeEvaluation evaluation =
				EvaluateNode(
					CurrentTree,
					displayNode
				);

			double cursorY =
				y +
				54;

			DrawFittedText(
				ctx,
				titleFont,
				displayNode.DisplayName,
				x + 17,
				cursorY,
				width - 34
			);

			cursorY +=
				39;

			int rank =
				GetNodeRank(
					displayNode.Id
				);

			DrawStatePill(
				ctx,
				x + 17,
				cursorY,
				width - 34,
				28,
				evaluation,
				rank,
				displayNode,
				theme
			);

			cursorY +=
				43;

			cursorY +=
				DrawWrappedText(
					ctx,
					bodyFont,
					displayNode.Description,
					x + 17,
					cursorY,
					width - 34
				);

			cursorY +=
				18;

			DrawText(
				ctx,
				sectionFont,
				"EFFECTS",
				x + 17,
				cursorY
			);

			cursorY +=
				27;

			if (displayNode.Effects.Count == 0)
			{
				DrawText(
					ctx,
					smallFont,
					"No configured effect.",
					x + 17,
					cursorY
				);

				cursorY +=
					22;
			}
			else
			{
				foreach (
					SkillEffectDefinition effect
					in displayNode.Effects)
				{
					string effectText =
						DescribeEffect(
							effect,
							rank,
							displayNode.MaxRank
						);

					cursorY +=
						DrawRequirementLine(
							ctx,
							x + 17,
							cursorY,
							width - 34,
							true,
							effectText,
							theme
						);
				}
			}

			cursorY +=
				8;

			DrawText(
				ctx,
				sectionFont,
				"REQUIREMENTS",
				x + 17,
				cursorY
			);

			cursorY +=
				27;

			foreach (
				RequirementView requirement
				in BuildRequirements(
					CurrentTree,
					displayNode
				))
			{
				cursorY +=
					DrawRequirementLine(
						ctx,
						x + 17,
						cursorY,
						width - 34,
						requirement.Met,
						requirement.Text,
						theme
					);
			}

			double buttonHeight =
				44;

			double buttonX =
				x +
				17;

			double buttonY =
				y +
				height -
				94;

			double buttonWidth =
				width -
				34;

			bool purchaseHovered =
				IsHovered(
					SkillTreeHitKind.Purchase,
					"purchase"
				);

			DrawPurchaseButton(
				ctx,
				buttonX,
				buttonY,
				buttonWidth,
				buttonHeight,
				displayNode,
				evaluation,
				purchaseHovered,
				theme
			);

			hitRegions.Add(
				new SkillTreeHitRegion(
					SkillTreeHitKind.Purchase,
					"purchase",
					buttonX,
					buttonY,
					buttonWidth,
					buttonHeight
				)
			);

			DrawStatusBar(
				ctx,
				x + 17,
				y + height - 39,
				width - 34,
				24,
				statusMessage,
				theme
			);
		}

		private void DrawPurchaseButton(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			SkillNodeDefinition node,
			NodeEvaluation evaluation,
			bool hovered,
			ThemePalette theme)
		{
			int rank =
				GetNodeRank(
					node.Id
				);

			string label;

			if (purchasePending)
			{
				label =
					"WAITING FOR SERVER...";
			}
			else if (rank >=
				node.MaxRank)
			{
				label =
					"MAXIMUM RANK";
			}
			else if (evaluation.CanPurchase)
			{
				label =
					node.Capstone
						? $"UNLOCK CAPSTONE - {node.Cost} PTS"
						: $"LEARN RANK {rank + 1} - {node.Cost} PTS";
			}
			else
			{
				label =
					"LOCKED";
			}

			bool enabled =
				evaluation.CanPurchase &&
				!purchasePending;

			double fillR =
				enabled
					? theme.AccentR
					: 0.16;

			double fillG =
				enabled
					? theme.AccentG
					: 0.17;

			double fillB =
				enabled
					? theme.AccentB
					: 0.20;

			if (hovered &&
				enabled)
			{
				fillR =
					Math.Min(
						1,
						fillR +
						0.12
					);

				fillG =
					Math.Min(
						1,
						fillG +
						0.12
					);

				fillB =
					Math.Min(
						1,
						fillB +
						0.12
					);
			}

			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				9
			);

			SetColor(
				ctx,
				fillR,
				fillG,
				fillB,
				enabled
					? 0.88
					: 0.72
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				enabled
					? 0.98
					: 0.35,
				enabled
					? 0.93
					: 0.37,
				enabled
					? 0.74
					: 0.41,
				1
			);

			ctx.LineWidth =
				hovered
					? 3
					: 2;

			ctx.Stroke();

			DrawCenteredText(
				ctx,
				sectionFont,
				label,
				x +
					width / 2,
				y +
					12,
				enabled
					? 0.06
					: 0.64,
				enabled
					? 0.07
					: 0.66,
				enabled
					? 0.09
					: 0.70,
				1
			);
		}
	}
}

