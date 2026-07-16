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
		private static string DescribeEffect(
			SkillEffectDefinition effect,
			int currentRank,
			int maxRank)
		{
			if (effect.Type.Equals(
					"UnlockRecipe",
					StringComparison.OrdinalIgnoreCase))
			{
				return
					"Unlocks recipe: " +
					(
						effect.Code ??
						"unknown"
					);
			}

			if (effect.Type.Equals(
					"TrackingDetail",
					StringComparison.OrdinalIgnoreCase))
			{
				return currentRank > 0
					? "Tracking readout: animal name and approximate distance"
					: "Next rank: tracking shows the animal name and approximate distance";
			}

			double current =
				effect.ValuePerRank *
				currentRank;

			double next =
				effect.ValuePerRank *
				Math.Min(
					maxRank,
					currentRank + 1
				);

			string effectName =
				!string.IsNullOrWhiteSpace(effect.Stat)
					? effect.Stat
					: effect.Type;

			bool flatPoints =
				effect.Type.Equals(
					"MaxHealth",
					StringComparison.OrdinalIgnoreCase
				);

			bool meters =
				effect.Type.Equals(
					"TrackingRange",
					StringComparison.OrdinalIgnoreCase
				);

			bool reduction =
				effect.Type.EndsWith(
					"Reduction",
					StringComparison.OrdinalIgnoreCase
				) ||
				effect.Type.Equals(
					"HungerReduction",
					StringComparison.OrdinalIgnoreCase
				) ||
				effect.Stat?.Contains(
					"damage taken",
					StringComparison.OrdinalIgnoreCase
				) == true ||
				effect.Stat?.Contains(
					"hunger drain",
					StringComparison.OrdinalIgnoreCase
				) == true;

			string FormatValue(double value)
			{
				if (meters)
				{
					return $"+{value:0.##}m";
				}

				if (flatPoints)
				{
					return $"+{value:0.##}";
				}

				return reduction
					? $"-{value * 100:0.##}%"
					: $"+{value * 100:0.##}%";
			}

			string currentText =
				FormatValue(current);

			string nextText =
				FormatValue(next);

			if (currentRank <= 0)
			{
				return
					$"{effectName}: {nextText} at next rank";
			}

			if (currentRank >= maxRank)
			{
				return
					$"{effectName}: {currentText}";
			}

			return
				$"{effectName}: {currentText} now -> {nextText} next rank";
		}

		private void DrawClassIcon(
			Context ctx,
			SkillTreeDefinition tree,
			double x,
			double y,
			double size,
			ThemePalette theme)
		{
			DrawGlow(
				ctx,
				x +
					size / 2,
				y +
					size / 2,
				size /
					2 +
					4,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				0.22
			);

			ctx.Arc(
				x +
					size / 2,
				y +
					size / 2,
				size / 2,
				0,
				Math.PI * 2
			);

			SetColor(
				ctx,
				theme.AccentR *
					0.34,
				theme.AccentG *
					0.34,
				theme.AccentB *
					0.34,
				0.98
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				0.95
			);

			ctx.LineWidth = 2.5;
			ctx.Stroke();

			DrawProfessionIcon(
				ctx,
				tree.ClassId,
				x +
					size / 2,
				y +
					size / 2,
				size * 0.50,
				0.98,
				0.95,
				0.88,
				1
			);
		}

		private void DrawSidebarEntry(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string categoryId,
			string label,
			bool selected,
			bool hovered,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				7
			);

			SetColor(
				ctx,
				selected
					? theme.AccentR *
						0.32
					: hovered
						? 0.15
						: 0.095,
				selected
					? theme.AccentG *
						0.32
					: hovered
						? 0.16
						: 0.105,
				selected
					? theme.AccentB *
						0.32
					: hovered
						? 0.19
						: 0.13,
				0.95
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				selected
					? theme.AccentR
					: 0.22,
				selected
					? theme.AccentG
					: 0.24,
				selected
					? theme.AccentB
					: 0.29,
				selected
					? 0.95
					: 0.80
			);

			ctx.LineWidth =
				selected
					? 2.2
					: 1;

			ctx.Stroke();

			DrawCategoryIcon(
				ctx,
				categoryId,
				x + 23,
				y + height / 2,
				20,
				selected
					? theme.AccentR
					: 0.63,
				selected
					? theme.AccentG
					: 0.66,
				selected
					? theme.AccentB
					: 0.70,
				1
			);

			DrawFittedText(
				ctx,
				smallFont,
				label,
				x + 46,
				y + 8,
				width - 56
			);
		}

		private void DrawClassEntry(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			SkillTreeDefinition tree,
			int level,
			bool selected,
			bool hovered,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				8
			);

			SetColor(
				ctx,
				selected
					? theme.PanelR *
						1.9
					: hovered
						? 0.13
						: 0.075,
				selected
					? theme.PanelG *
						1.9
					: hovered
						? 0.14
						: 0.085,
				selected
					? theme.PanelB *
						1.9
					: hovered
						? 0.17
						: 0.11,
				0.98
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				selected
					? theme.AccentR
					: 0.18,
				selected
					? theme.AccentG
					: 0.20,
				selected
					? theme.AccentB
					: 0.24,
				selected
					? 0.94
					: 0.82
			);

			ctx.LineWidth =
				selected
					? 2
					: 1;

			ctx.Stroke();

			ctx.Arc(
				x + 19,
				y + height / 2,
				12,
				0,
				Math.PI * 2
			);

			SetColor(
				ctx,
				theme.AccentR *
					0.45,
				theme.AccentG *
					0.45,
				theme.AccentB *
					0.45,
				selected
					? 1
					: 0.76
			);

			ctx.Fill();

			DrawProfessionIcon(
				ctx,
				tree.ClassId,
				x + 19,
				y + height / 2,
				18.5,
				0.98,
				0.95,
				0.88,
				selected
					? 1
					: 0.86
			);

			DrawCenteredFittedText(
				ctx,
				smallFont,
				tree.DisplayName,
				x + width / 2,
				y + 10,
				width - 104,
				1.10
			);

			// Center the level label inside the rightmost 58-unit column.
			// Its smaller font needs a slightly lower baseline than the
			// enlarged profession name to share the same visual center.
			DrawCenteredText(
				ctx,
				tinyFont,
				$"Lv {level}",
				x + width - 29,
				y + 12,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				1
			);
		}

		private void DrawStatePill(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			NodeEvaluation evaluation,
			int rank,
			SkillNodeDefinition node,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				7
			);

			(
				double fillR,
				double fillG,
				double fillB,
				double borderR,
				double borderG,
				double borderB
			) =
				GetNodeColors(
					evaluation.State,
					theme
				);

			SetColor(
				ctx,
				fillR,
				fillG,
				fillB,
				0.76
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				borderR,
				borderG,
				borderB,
				0.92
			);

			ctx.LineWidth = 1.5;
			ctx.Stroke();

			DrawText(
				ctx,
				tinyFont,
				evaluation.State
					.ToString()
					.ToUpperInvariant(),
				x + 10,
				y + 7
			);

			string rankText =
				$"RANK {rank}/{node.MaxRank}";

			double rankWidth =
				MeasureTextWidth(
					tinyFont,
					rankText
				);

			DrawText(
				ctx,
				tinyFont,
				rankText,
				x +
					width -
					rankWidth -
					10,
				y + 7
			);
		}

		private double DrawRequirementLine(
			Context ctx,
			double x,
			double y,
			double width,
			bool met,
			string text,
			ThemePalette theme)
		{
			double markerSize = 12;

			ctx.Arc(
				x +
					markerSize / 2,
				y +
					markerSize / 2 +
					2,
				markerSize / 2,
				0,
				Math.PI * 2
			);

			SetColor(
				ctx,
				met
					? theme.SuccessR
					: 0.74,
				met
					? theme.SuccessG
					: 0.23,
				met
					? theme.SuccessB
					: 0.26,
				0.95
			);

			ctx.Fill();

			DrawCenteredText(
				ctx,
				tinyFont,
				met
					? "+"
					: "-",
				x +
					markerSize / 2,
				y +
					1,
				0.04,
				0.05,
				0.07,
				1
			);

			double height =
				DrawWrappedText(
					ctx,
					smallFont,
					text,
					x + 20,
					y - 2,
					width - 20
				);

			return Math.Max(
				21,
				height + 4
			);
		}

		private void DrawStatusBar(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string text,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				6
			);

			SetColor(
				ctx,
				theme.PanelR *
					1.5,
				theme.PanelG *
					1.5,
				theme.PanelB *
					1.5,
				0.94
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				0.54
			);

			ctx.LineWidth = 1;
			ctx.Stroke();

			DrawFittedText(
				ctx,
				tinyFont,
				Shorten(
					text,
					49
				),
				x + 9,
				y + 6,
				width - 18
			);
		}

		private static void DrawPanel(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			double fillR,
			double fillG,
			double fillB,
			double fillA,
			double borderR,
			double borderG,
			double borderB,
			double borderA)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				11
			);

			SetColor(
				ctx,
				fillR,
				fillG,
				fillB,
				fillA
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				borderR,
				borderG,
				borderB,
				borderA
			);

			ctx.LineWidth = 1.4;
			ctx.Stroke();
		}

		private static void DrawProgressBar(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			double progress,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				height / 2
			);

			SetColor(
				ctx,
				0.035,
				0.04,
				0.055,
				0.88
			);

			ctx.Fill();

			if (progress >
				0)
			{
				RoundRectangle(
					ctx,
					x,
					y,
					Math.Max(
						height,
						width *
						progress
					),
					height,
					height / 2
				);

				SetColor(
					ctx,
					theme.AccentR,
					theme.AccentG,
					theme.AccentB,
					0.92
				);

				ctx.Fill();
			}

			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				height / 2
			);

			SetColor(
				ctx,
				0.68,
				0.70,
				0.75,
				0.36
			);

			ctx.LineWidth = 1;
			ctx.Stroke();
		}

		private void DrawBadge(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string text,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				7
			);

			SetColor(
				ctx,
				theme.AccentR *
					0.22,
				theme.AccentG *
					0.22,
				theme.AccentB *
					0.22,
				0.90
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				theme.AccentR,
				theme.AccentG,
				theme.AccentB,
				0.60
			);

			ctx.LineWidth = 1;
			ctx.Stroke();

			DrawCenteredText(
				ctx,
				tinyFont,
				text,
				x +
					width / 2,
				y + 6
			);
		}

		private void DrawMiniButton(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string text,
			bool hovered,
			bool enabled,
			ThemePalette theme)
		{
			RoundRectangle(
				ctx,
				x,
				y,
				width,
				height,
				6
			);

			SetColor(
				ctx,
				hovered
					? theme.AccentR *
						0.36
					: 0.095,
				hovered
					? theme.AccentG *
						0.36
					: 0.105,
				hovered
					? theme.AccentB *
						0.36
					: 0.13,
				enabled
					? 0.96
					: 0.48
			);

			ctx.FillPreserve();

			SetColor(
				ctx,
				hovered
					? theme.AccentR
					: 0.26,
				hovered
					? theme.AccentG
					: 0.28,
				hovered
					? theme.AccentB
					: 0.32,
				0.92
			);

			ctx.LineWidth = 1;
			ctx.Stroke();

			DrawCenteredText(
				ctx,
				tinyFont,
				text,
				x + width / 2,
				y + 5
			);
		}

		private static void DrawDivider(
			Context ctx,
			double x,
			double y,
			double width)
		{
			SetColor(
				ctx,
				0.38,
				0.40,
				0.45,
				0.34
			);

			ctx.LineWidth = 1;
			ctx.MoveTo(
				x,
				y
			);

			ctx.LineTo(
				x + width,
				y
			);

			ctx.Stroke();
		}

		private static void DrawNodeShape(
			Context ctx,
			SkillNodeDefinition node,
			double x,
			double y,
			double radius)
		{
			if (node.Capstone)
			{
				DrawPolygon(
					ctx,
					x,
					y,
					radius,
					8,
					Math.PI / 8
				);

				return;
			}

			if (node.Row ==
				2)
			{
				DrawPolygon(
					ctx,
					x,
					y,
					radius,
					4,
					Math.PI / 4
				);

				return;
			}

			ctx.Arc(
				x,
				y,
				radius,
				0,
				Math.PI * 2
			);
		}

		private static void DrawPolygon(
			Context ctx,
			double centerX,
			double centerY,
			double radius,
			int points,
			double rotation)
		{
			for (int index = 0;
				 index < points;
				 index++)
			{
				double angle =
					rotation +
					Math.PI *
					2 *
					index /
					points;

				double x =
					centerX +
					Math.Cos(
						angle
					) *
					radius;

				double y =
					centerY +
					Math.Sin(
						angle
					) *
					radius;

				if (index ==
					0)
				{
					ctx.MoveTo(
						x,
						y
					);
				}
				else
				{
					ctx.LineTo(
						x,
						y
					);
				}
			}

			ctx.ClosePath();
		}

		private static void DrawGlow(
			Context ctx,
			double x,
			double y,
			double radius,
			double r,
			double g,
			double b,
			double alpha)
		{
			for (int ring = 3;
				 ring >= 1;
				 ring--)
			{
				ctx.NewPath();
				ctx.Arc(
					x,
					y,
					radius +
						ring * 4,
					0,
					Math.PI * 2
				);

				SetColor(
					ctx,
					r,
					g,
					b,
					alpha /
					(
						ring *
						1.4
					)
				);

				ctx.LineWidth =
					ring * 3;

				ctx.Stroke();
			}
		}
	}
}

