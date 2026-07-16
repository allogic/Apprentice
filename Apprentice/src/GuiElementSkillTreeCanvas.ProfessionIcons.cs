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
		private static void DrawCategoryIcon(
			Context ctx,
			string categoryId,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				ctx.Translate(
					centerX,
					centerY
				);

				ctx.Scale(
					size,
					size
				);

				ctx.LineWidth = 0.082;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				switch (
					categoryId
						.ToLowerInvariant()
				)
				{
					case "combat":
						ctx.Save();
						ctx.Scale(0.82, 0.82);
						DrawShieldGlyph(ctx);
						ctx.Restore();
						DrawCrossedWeaponsGlyph(
							ctx
						);
						break;

					case "gathering":
						DrawLeafGlyph(
							ctx
						);
						break;

					case "crafting":
						DrawHammerGlyph(
							ctx
						);
						break;

					case "production":
						DrawGearGlyph(
							ctx
						);
						break;

					default:
						DrawStarGlyph(
							ctx,
							true
						);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawProfessionIcon(
			Context ctx,
			string classId,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				ctx.Translate(
					centerX,
					centerY
				);

				ctx.Scale(
					size,
					size
				);

				ctx.LineWidth = 0.075;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				switch (
					classId
						.ToLowerInvariant()
				)
				{
					case "miner":
						DrawPickaxeGlyph(
							ctx
						);
						break;

					case "woodworker":
						DrawAxeGlyph(
							ctx
						);
						break;

					case "farmer":
						DrawWheatGlyph(
							ctx
						);
						break;

					case "builder":
						DrawBrickGlyph(
							ctx
						);
						break;

					case "blacksmith":
						DrawAnvilGlyph(
							ctx
						);
						break;

					case "cook":
						DrawPotGlyph(
							ctx
						);
						break;

					case "potter":
						DrawVaseGlyph(
							ctx
						);
						break;

					case "leatherworker":
						DrawHideGlyph(
							ctx
						);
						break;

					case "tailor":
						DrawNeedleGlyph(
							ctx
						);
						break;

					case "hunter":
						DrawHunterGlyph(
							ctx
						);
						break;

					case "warrior":
						DrawCrossedSwordsGlyph(
							ctx
						);
						break;

					case "ranger":
						DrawRangerGlyph(
							ctx
						);
						break;

					case "fisher":
						DrawFishGlyph(
							ctx
						);
						break;

					case "animalhusbandry":
						DrawPawGlyph(
							ctx
						);
						break;

					case "beekeeper":
						DrawBeeGlyph(
							ctx
						);
						break;

					case "spearman":
						DrawSpearGlyph(
							ctx
						);
						break;

					case "shield":
						DrawShieldGlyph(
							ctx
						);
						break;

					case "tank":
						DrawHelmetGlyph(
							ctx
						);
						break;

					default:
						DrawStarGlyph(
							ctx,
							false
						);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawPickaxeGlyph(
			Context ctx)
		{
			// Handle.
			ctx.LineWidth = 0.105;
			StrokeLine(ctx, -0.31, 0.41, 0.04, -0.13);

			// Balanced, double-sided pick head. Both wings have the same reach.
			ctx.LineWidth = 0.095;
			ctx.MoveTo(-0.40, -0.14);
			ctx.CurveTo(-0.27, -0.31, -0.10, -0.35, 0.04, -0.23);
			ctx.CurveTo(0.18, -0.35, 0.35, -0.31, 0.48, -0.14);
			ctx.Stroke();

			// Lower edge gives the head thickness and keeps it from reading as a scythe.
			ctx.MoveTo(-0.40, -0.14);
			ctx.CurveTo(-0.25, -0.22, -0.08, -0.23, 0.04, -0.16);
			ctx.CurveTo(0.16, -0.23, 0.33, -0.22, 0.48, -0.14);
			ctx.Stroke();

			// Pick eye / socket.
			ctx.Rectangle(-0.035, -0.245, 0.15, 0.15);
			ctx.Stroke();
		}

		private static void DrawAxeGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.23,
				0.38,
				0.13,
				-0.31
			);

			FillPolygon(
				ctx,
				-0.01,
				-0.34,
				0.34,
				-0.23,
				0.25,
				0.05,
				0.02,
				-0.04
			);
		}

		private static void DrawWheatGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				0,
				0.42,
				0,
				-0.39
			);

			double[] levels =
			{
				-0.28,
				-0.14,
				0,
				0.14
			};

			foreach (
				double level
				in levels
			)
			{
				StrokeLine(
					ctx,
					0,
					level,
					-0.22,
					level - 0.12
				);

				StrokeLine(
					ctx,
					0,
					level + 0.07,
					0.22,
					level - 0.05
				);
			}

			FillCircle(
				ctx,
				0,
				-0.39,
				0.07
			);
		}

		private static void DrawBrickGlyph(
			Context ctx)
		{
			StrokeRectangle(
				ctx,
				-0.39,
				-0.28,
				0.78,
				0.56
			);

			StrokeLine(
				ctx,
				-0.39,
				0,
				0.39,
				0
			);

			StrokeLine(
				ctx,
				-0.12,
				-0.28,
				-0.12,
				0
			);

			StrokeLine(
				ctx,
				0.14,
				0,
				0.14,
				0.28
			);
		}

		private static void DrawAnvilGlyph(
			Context ctx)
		{
			FillPolygon(
				ctx,
				-0.40,
				-0.22,
				0.40,
				-0.22,
				0.23,
				-0.02,
				0.12,
				-0.02,
				0.09,
				0.20,
				0.27,
				0.34,
				-0.27,
				0.34,
				-0.09,
				0.20,
				-0.12,
				-0.02,
				-0.28,
				-0.02
			);
		}

		private static void DrawPotGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.34,
				-0.16,
				0.34,
				-0.16
			);

			ctx.MoveTo(
				-0.29,
				-0.12
			);
			ctx.CurveTo(
				-0.27,
				0.28,
				-0.15,
				0.37,
				0,
				0.37
			);
			ctx.CurveTo(
				0.15,
				0.37,
				0.27,
				0.28,
				0.29,
				-0.12
			);
			ctx.Stroke();

			StrokeLine(
				ctx,
				-0.42,
				-0.05,
				-0.29,
				0.02
			);

			StrokeLine(
				ctx,
				0.42,
				-0.05,
				0.29,
				0.02
			);

			DrawSteam(
				ctx,
				-0.13
			);
			DrawSteam(
				ctx,
				0.13
			);
		}

		private static void DrawVaseGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.16,
				-0.39
			);
			ctx.LineTo(
				0.16,
				-0.39
			);
			ctx.LineTo(
				0.12,
				-0.22
			);
			ctx.CurveTo(
				0.34,
				-0.03,
				0.31,
				0.31,
				0.12,
				0.39
			);
			ctx.LineTo(
				-0.12,
				0.39
			);
			ctx.CurveTo(
				-0.31,
				0.31,
				-0.34,
				-0.03,
				-0.12,
				-0.22
			);
			ctx.ClosePath();
			ctx.Stroke();
		}

		private static void DrawHideGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				0,
				-0.40
			);
			ctx.CurveTo(
				0.12,
				-0.31,
				0.25,
				-0.36,
				0.35,
				-0.22
			);
			ctx.CurveTo(
				0.27,
				-0.06,
				0.39,
				0.09,
				0.25,
				0.21
			);
			ctx.CurveTo(
				0.11,
				0.23,
				0.10,
				0.39,
				0,
				0.42
			);
			ctx.CurveTo(
				-0.10,
				0.39,
				-0.11,
				0.23,
				-0.25,
				0.21
			);
			ctx.CurveTo(
				-0.39,
				0.09,
				-0.27,
				-0.06,
				-0.35,
				-0.22
			);
			ctx.CurveTo(
				-0.25,
				-0.36,
				-0.12,
				-0.31,
				0,
				-0.40
			);
			ctx.ClosePath();
			ctx.Stroke();

			StrokeLine(
				ctx,
				-0.15,
				-0.20,
				0.15,
				0.22
			);
		}

		private static void DrawNeedleGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.31,
				0.34,
				0.28,
				-0.34
			);

			StrokeCircle(
				ctx,
				0.28,
				-0.34,
				0.07
			);

			ctx.MoveTo(
				-0.31,
				0.34
			);
			ctx.CurveTo(
				-0.02,
				0.17,
				0.05,
				0.43,
				0.34,
				0.25
			);
			ctx.Stroke();
		}

		private static void DrawBowGlyph(
			Context ctx)
		{
			// Bow limbs.
			ctx.MoveTo(-0.22, -0.38);
			ctx.CurveTo(-0.43, -0.24, -0.43, 0.24, -0.22, 0.38);
			ctx.Stroke();

			// String pulled back to the nocking hand.
			StrokeLine(ctx, -0.22, -0.38, 0.14, 0.00);
			StrokeLine(ctx, 0.14, 0.00, -0.22, 0.38);

			// Nocked arrow, ready to release.
			StrokeLine(ctx, -0.34, 0.00, 0.40, 0.00);
			FillPolygon(
				ctx,
				0.46,
				0.00,
				0.34,
				-0.07,
				0.34,
				0.07
			);
			StrokeLine(ctx, -0.34, 0.00, -0.25, -0.065);
			StrokeLine(ctx, -0.34, 0.00, -0.25, 0.065);
		}

		private static void DrawHunterGlyph(
			Context ctx)
		{
			FillCircle(
				ctx,
				-0.05,
				0.12,
				0.14
			);

			FillCircle(
				ctx,
				-0.20,
				-0.12,
				0.075
			);

			FillCircle(
				ctx,
				-0.07,
				-0.25,
				0.075
			);

			FillCircle(
				ctx,
				0.08,
				-0.24,
				0.075
			);

			StrokeLine(
				ctx,
				-0.02,
				0.26,
				0.30,
				-0.10
			);

			FillPolygon(
				ctx,
				0.38,
				-0.19,
				0.24,
				-0.11,
				0.31,
				0.00
			);
		}

		private static void DrawSwordGlyph(
			Context ctx)
		{
			FillPolygon(
				ctx,
				0,
				-0.43,
				0.10,
				-0.27,
				0.05,
				0.20,
				-0.05,
				0.20,
				-0.10,
				-0.27
			);

			StrokeLine(
				ctx,
				-0.20,
				0.20,
				0.20,
				0.20
			);

			StrokeLine(
				ctx,
				0,
				0.20,
				0,
				0.40
			);
		}

		private static void DrawCompassGlyph(
			Context ctx)
		{
			StrokeCircle(
				ctx,
				0,
				0,
				0.39
			);

			FillPolygon(
				ctx,
				0.08,
				-0.31,
				-0.05,
				0.03,
				-0.08,
				0.31,
				0.05,
				-0.03
			);

			FillCircle(
				ctx,
				0,
				0,
				0.055
			);
		}

		private static void DrawFishGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.28,
				0
			);
			ctx.CurveTo(
				-0.08,
				-0.28,
				0.22,
				-0.28,
				0.35,
				0
			);
			ctx.CurveTo(
				0.22,
				0.28,
				-0.08,
				0.28,
				-0.28,
				0
			);
			ctx.ClosePath();
			ctx.Stroke();

			FillPolygon(
				ctx,
				-0.28,
				0,
				-0.43,
				-0.22,
				-0.43,
				0.22
			);

			FillCircle(
				ctx,
				0.20,
				-0.06,
				0.04
			);
		}

		private static void DrawPawGlyph(
			Context ctx)
		{
			FillCircle(
				ctx,
				0,
				0.16,
				0.20
			);

			FillCircle(
				ctx,
				-0.25,
				-0.13,
				0.105
			);

			FillCircle(
				ctx,
				-0.08,
				-0.28,
				0.105
			);

			FillCircle(
				ctx,
				0.12,
				-0.28,
				0.105
			);

			FillCircle(
				ctx,
				0.28,
				-0.10,
				0.105
			);
		}

		private static void DrawBeeGlyph(
			Context ctx)
		{
			StrokeCircle(
				ctx,
				0,
				0.05,
				0.22
			);

			StrokeLine(
				ctx,
				-0.18,
				-0.03,
				0.18,
				-0.03
			);

			StrokeLine(
				ctx,
				-0.18,
				0.09,
				0.18,
				0.09
			);

			ctx.Arc(
				-0.20,
				-0.18,
				0.17,
				0,
				Math.PI * 2
			);
			ctx.Stroke();

			ctx.Arc(
				0.20,
				-0.18,
				0.17,
				0,
				Math.PI * 2
			);
			ctx.Stroke();

			StrokeLine(
				ctx,
				-0.09,
				-0.17,
				-0.18,
				-0.36
			);

			StrokeLine(
				ctx,
				0.09,
				-0.17,
				0.18,
				-0.36
			);
		}

		private static void DrawSpearGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.28,
				0.37,
				0.19,
				-0.23
			);

			FillPolygon(
				ctx,
				0.38,
				-0.43,
				0.20,
				-0.16,
				0.09,
				-0.27
			);
		}

		private static void DrawShieldGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				0,
				-0.42
			);
			ctx.LineTo(
				0.34,
				-0.28
			);
			ctx.LineTo(
				0.27,
				0.17
			);
			ctx.CurveTo(
				0.20,
				0.32,
				0.08,
				0.40,
				0,
				0.44
			);
			ctx.CurveTo(
				-0.08,
				0.40,
				-0.20,
				0.32,
				-0.27,
				0.17
			);
			ctx.LineTo(
				-0.34,
				-0.28
			);
			ctx.ClosePath();
			ctx.Stroke();

			StrokeLine(
				ctx,
				0,
				-0.33,
				0,
				0.31
			);
		}

		private static void DrawHelmetGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.35,
				0.28
			);
			ctx.LineTo(
				-0.30,
				-0.06
			);
			ctx.CurveTo(
				-0.25,
				-0.32,
				-0.10,
				-0.42,
				0.09,
				-0.42
			);
			ctx.CurveTo(
				0.29,
				-0.42,
				0.38,
				-0.18,
				0.34,
				0.08
			);
			ctx.LineTo(
				0.17,
				0.08
			);
			ctx.LineTo(
				0.12,
				0.35
			);
			ctx.LineTo(
				-0.06,
				0.35
			);
			ctx.LineTo(
				-0.09,
				0.06
			);
			ctx.LineTo(
				-0.35,
				0.06
			);
			ctx.ClosePath();
			ctx.Stroke();

			StrokeLine(
				ctx,
				-0.04,
				-0.39,
				-0.04,
				0.02
			);
		}

		private static void DrawCrossedWeaponsGlyph(
			Context ctx)
		{
			DrawCrossedSwordsGlyph(ctx);
		}

		private static void DrawCrossedSwordsGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.27,
				0.34,
				0.22,
				-0.20
			);
			StrokeLine(
				ctx,
				0.27,
				0.34,
				-0.22,
				-0.20
			);

			FillPolygon(
				ctx,
				0.28,
				-0.30,
				0.20,
				-0.09,
				0.10,
				-0.18
			);
			FillPolygon(
				ctx,
				-0.28,
				-0.30,
				-0.20,
				-0.09,
				-0.10,
				-0.18
			);

			StrokeLine(
				ctx,
				-0.14,
				0.14,
				0.02,
				0.29
			);
			StrokeLine(
				ctx,
				0.14,
				0.14,
				-0.02,
				0.29
			);
			StrokeLine(
				ctx,
				-0.22,
				0.20,
				-0.08,
				0.20
			);
			StrokeLine(
				ctx,
				0.22,
				0.20,
				0.08,
				0.20
			);
		}

		private static void DrawLeafGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.36,
				0.31
			);
			ctx.CurveTo(
				-0.31,
				-0.30,
				0.19,
				-0.43,
				0.39,
				-0.28
			);
			ctx.CurveTo(
				0.32,
				0.23,
				-0.02,
				0.42,
				-0.36,
				0.31
			);
			ctx.ClosePath();
			ctx.Stroke();

			StrokeLine(
				ctx,
				-0.31,
				0.27,
				0.27,
				-0.24
			);
		}

		private static void DrawHammerGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.25,
				0.38,
				0.12,
				-0.16
			);

			FillPolygon(
				ctx,
				-0.10,
				-0.38,
				0.35,
				-0.08,
				0.21,
				0.12,
				-0.24,
				-0.18
			);
		}

		private static void DrawGearGlyph(
			Context ctx)
		{
			StrokeCircle(
				ctx,
				0,
				0,
				0.24
			);

			FillCircle(
				ctx,
				0,
				0,
				0.07
			);

			for (
				int index = 0;
				index < 8;
				index++
			)
			{
				double angle =
					index *
					Math.PI /
					4;

				StrokeLine(
					ctx,
					Math.Cos(
						angle
					) * 0.27,
					Math.Sin(
						angle
					) * 0.27,
					Math.Cos(
						angle
					) * 0.43,
					Math.Sin(
						angle
					) * 0.43
				);
			}
		}

		private static void DrawCrownIcon(
			Context ctx,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				ctx.Translate(
					centerX,
					centerY
				);

				ctx.Scale(
					size,
					size
				);

				ctx.LineWidth = 0.13;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				FillPolygon(
					ctx,
					-0.48,
					0.26,
					-0.41,
					-0.31,
					-0.14,
					-0.02,
					0,
					-0.42,
					0.14,
					-0.02,
					0.41,
					-0.31,
					0.48,
					0.26
				);

				StrokeLine(
					ctx,
					-0.44,
					0.35,
					0.44,
					0.35
				);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawRingBadge(
			Context ctx,
			double centerX,
			double centerY,
			double radius,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				ctx.LineWidth =
					Math.Max(
						1.3,
						radius * 0.34
					);

				ctx.Arc(
					centerX,
					centerY,
					radius,
					0,
					Math.PI * 2
				);

				ctx.Stroke();
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawTriangleBadge(
			Context ctx,
			double centerX,
			double centerY,
			double size,
			bool pointLeft,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				if (pointLeft)
				{
					FillPolygon(
						ctx,
						centerX - size,
						centerY,
						centerX + size,
						centerY - size,
						centerX + size,
						centerY + size
					);
				}
				else
				{
					FillPolygon(
						ctx,
						centerX + size,
						centerY,
						centerX - size,
						centerY - size,
						centerX - size,
						centerY + size
					);
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawDiamondBadge(
			Context ctx,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.Save();

			try
			{
				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				FillPolygon(
					ctx,
					centerX,
					centerY - size,
					centerX + size,
					centerY,
					centerX,
					centerY + size,
					centerX - size,
					centerY
				);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawStarIcon(
			Context ctx,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a,
			bool filled)
		{
			ctx.Save();

			try
			{
				ctx.Translate(
					centerX,
					centerY
				);

				ctx.Scale(
					size,
					size
				);

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				ctx.LineWidth = 0.16;

				DrawStarGlyph(
					ctx,
					filled
				);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawStarGlyph(
			Context ctx,
			bool filled)
		{
			const int points = 10;

			for (
				int index = 0;
				index < points;
				index++
			)
			{
				double angle =
					-Math.PI /
					2 +
					index *
					Math.PI /
					5;

				double radius =
					index %
					2 ==
					0
						? 0.46
						: 0.20;

				double x =
					Math.Cos(
						angle
					) *
					radius;

				double y =
					Math.Sin(
						angle
					) *
					radius;

				if (index == 0)
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

			if (filled)
			{
				ctx.Fill();
			}
			else
			{
				ctx.Stroke();
			}
		}

		private static void DrawSteam(
			Context ctx,
			double x)
		{
			ctx.MoveTo(
				x,
				-0.23
			);
			ctx.CurveTo(
				x - 0.08,
				-0.32,
				x + 0.08,
				-0.39,
				x,
				-0.48
			);
			ctx.Stroke();
		}

		private static void StrokeLine(
			Context ctx,
			double x1,
			double y1,
			double x2,
			double y2)
		{
			ctx.NewPath();
			ctx.MoveTo(
				x1,
				y1
			);

			ctx.LineTo(
				x2,
				y2
			);

			ctx.Stroke();
		}

		private static void StrokeCircle(
			Context ctx,
			double centerX,
			double centerY,
			double radius)
		{
			ctx.NewPath();
			ctx.Arc(
				centerX,
				centerY,
				radius,
				0,
				Math.PI * 2
			);

			ctx.Stroke();
		}

		private static void FillCircle(
			Context ctx,
			double centerX,
			double centerY,
			double radius)
		{
			ctx.NewPath();
			ctx.Arc(
				centerX,
				centerY,
				radius,
				0,
				Math.PI * 2
			);

			ctx.Fill();
		}

		private static void StrokeRectangle(
			Context ctx,
			double x,
			double y,
			double width,
			double height)
		{
			ctx.NewPath();
			ctx.Rectangle(
				x,
				y,
				width,
				height
			);

			ctx.Stroke();
		}

		private static void FillPolygon(
			Context ctx,
			params double[] points)
		{
			ctx.NewPath();

			if (points.Length < 6)
			{
				return;
			}

			ctx.MoveTo(
				points[0],
				points[1]
			);

			for (
				int index = 2;
				index < points.Length - 1;
				index += 2
			)
			{
				ctx.LineTo(
					points[index],
					points[index + 1]
				);
			}

			ctx.ClosePath();
			ctx.Fill();
		}
	}
}

