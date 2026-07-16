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
		private static ThemePalette GetTheme(
			SkillTreeDefinition tree)
		{
			return tree.ClassId switch
			{
				"warrior" or
				"ranger" or
				"spearman" or
				"shield" or
				"tank" or
				"hunter" =>
					new ThemePalette(
						0.82,
						0.48,
						0.20,
						0.18,
						0.075,
						0.055,
						0.18,
						0.76,
						0.64
					),

				"miner" or
				"woodworker" or
				"farmer" or
				"fisher" =>
					new ThemePalette(
						0.34,
						0.72,
						0.44,
						0.055,
						0.14,
						0.09,
						0.22,
						0.77,
						0.58
					),

				"builder" or
				"blacksmith" or
				"potter" or
				"leatherworker" or
				"tailor" =>
					new ThemePalette(
						0.82,
						0.64,
						0.30,
						0.15,
						0.11,
						0.055,
						0.28,
						0.74,
						0.78
					),

				_ =>
					new ThemePalette(
						0.30,
						0.67,
						0.75,
						0.055,
						0.12,
						0.15,
						0.25,
						0.78,
						0.68
					)
			};
		}

		private static (
			double FillR,
			double FillG,
			double FillB,
			double BorderR,
			double BorderG,
			double BorderB)
			GetNodeColors(
				SkillNodeVisualState state,
				ThemePalette theme)
		{
			return state switch
			{
				SkillNodeVisualState.Available =>
					(
						theme.AccentR *
							0.45,
						theme.AccentG *
							0.45,
						theme.AccentB *
							0.45,
						theme.AccentR,
						theme.AccentG,
						theme.AccentB
					),

				SkillNodeVisualState.Purchased =>
					(
						theme.SuccessR *
							0.55,
						theme.SuccessG *
							0.55,
						theme.SuccessB *
							0.55,
						theme.SuccessR,
						theme.SuccessG,
						theme.SuccessB
					),

				SkillNodeVisualState.Maximum =>
					(
						0.28,
						0.62,
						0.56,
						0.55,
						0.98,
						0.86
					),

				SkillNodeVisualState.ExclusiveConflict =>
					(
						0.30,
						0.075,
						0.085,
						0.88,
						0.24,
						0.27
					),

				_ =>
					(
						0.105,
						0.115,
						0.14,
						0.32,
						0.34,
						0.39
					)
			};
		}

		private static void DrawIsolatedSkillNodeIcon(
			Context target,
			string classId,
			SkillNodeDefinition node,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			int surfaceSize =
				Math.Max(
					72,
					(int)Math.Ceiling(
						size * 3.2
					)
				);

			using var surface =
				new ImageSurface(
					Format.ARGB32,
					surfaceSize,
					surfaceSize
				);

			using var iconContext =
				new Context(surface);

			iconContext.NewPath();

			DrawSkillNodeIcon(
				iconContext,
				classId,
				node,
				surfaceSize / 2.0,
				surfaceSize / 2.0,
				size,
				r,
				g,
				b,
				a
			);

			iconContext.NewPath();
			surface.Flush();

			target.Save();

			try
			{
				target.NewPath();
				target.SetSourceSurface(
					surface,
					(int)Math.Round(
						centerX - surfaceSize / 2.0
					),
					(int)Math.Round(
						centerY - surfaceSize / 2.0
					)
				);
				target.Paint();
				target.NewPath();
			}
			finally
			{
				target.Restore();
				target.NewPath();
			}
		}

		private static void DrawSkillNodeIcon(
			Context ctx,
			string classId,
			SkillNodeDefinition node,
			double centerX,
			double centerY,
			double size,
			double r,
			double g,
			double b,
			double a)
		{
			if (node.Capstone ||
				node.Id.Equals(
					"capstone",
					StringComparison.Ordinal))
			{
				DrawProfessionIcon(
					ctx,
					classId,
					centerX,
					centerY + size * 0.05,
					size * 0.80,
					r,
					g,
					b,
					a
				);

				DrawCrownIcon(
					ctx,
					centerX,
					centerY - size * 0.34,
					size * 0.34,
					r,
					g,
					b,
					a
				);
				return;
			}

			DrawArchetypeNodeIcon(
				ctx,
				classId,
				node.Id,
				centerX,
				centerY,
				size,
				r,
				g,
				b,
				a
			);
		}

		private static void DrawArchetypeNodeIcon(
			Context ctx,
			string classId,
			string nodeId,
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
					size * 0.84,
					size * 0.84
				);

				ctx.LineWidth = 0.085;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				switch (classId.ToLowerInvariant())
				{
					case "miner":
						switch (nodeId)
						{
							case "foundation":
								DrawPickaxeGlyph(ctx);
								break;
							case "discipline":
								DrawPickaxeGlyph(ctx);
								FillCircle(ctx, 0.28, 0.05, 0.05);
								FillCircle(ctx, 0.18, 0.18, 0.04);
								break;
							case "path-a":
								DrawSurveyorGlyph(ctx);
								break;
							case "path-b":
								DrawLanternGlyph(ctx);
								break;
							case "expert-a":
								DrawMasterSurveyorGlyph(ctx);
								break;
							case "expert-b":
								DrawLanternGlyph(ctx);
								StrokeLine(ctx, -0.22, 0.24, -0.10, 0.08);
								StrokeLine(ctx, -0.03, 0.30, 0.09, 0.12);
								break;
							default:
								DrawPickaxeGlyph(ctx);
								break;
						}
						break;

					case "woodworker":
						switch (nodeId)
						{
							case "foundation":
								DrawAxeGlyph(ctx);
								break;
							case "discipline":
								DrawAxeGlyph(ctx);
								DrawPlankGlyph(ctx);
								break;
							case "path-a":
								DrawPlankGlyph(ctx);
								DrawHammerGlyph(ctx);
								break;
							case "path-b":
								DrawLogGlyph(ctx);
								DrawAxeGlyph(ctx);
								break;
							case "expert-a":
								DrawPlankGlyph(ctx);
								DrawHammerGlyph(ctx);
								StrokeLine(ctx, -0.12, -0.26, 0.12, -0.26);
								break;
							case "expert-b":
								DrawLogGlyph(ctx);
								DrawAxeGlyph(ctx);
								StrokeLine(ctx, 0.18, -0.20, 0.31, -0.29);
								break;
							default:
								DrawAxeGlyph(ctx);
								break;
						}
						break;

					case "farmer":
						switch (nodeId)
						{
							case "foundation":
								DrawSeedlingGlyph(ctx);
								break;
							case "discipline":
								DrawWheatGlyph(ctx);
								DrawSickleGlyph(ctx);
								break;
							case "path-a":
								DrawSeedlingGlyph(ctx);
								break;
							case "path-b":
								DrawWheatGlyph(ctx);
								DrawSickleGlyph(ctx);
								break;
							case "expert-a":
								DrawSeedlingGlyph(ctx);
								FillCircle(ctx, -0.24, -0.12, 0.04);
								FillCircle(ctx, -0.17, -0.22, 0.03);
								break;
							case "expert-b":
								DrawWheatGlyph(ctx);
								DrawSickleGlyph(ctx);
								StrokeLine(ctx, 0.12, 0.26, 0.28, 0.20);
								break;
							default:
								DrawWheatGlyph(ctx);
								break;
						}
						break;

					case "builder":
						switch (nodeId)
						{
							case "foundation":
								DrawTrowelGlyph(ctx);
								break;
							case "discipline":
								DrawBrickGlyph(ctx);
								DrawTrowelGlyph(ctx);
								break;
							case "path-a":
								DrawDraftingGlyph(ctx);
								break;
							case "path-b":
								DrawBrickGlyph(ctx);
								DrawTrowelGlyph(ctx);
								break;
							case "expert-a":
								DrawDraftingGlyph(ctx);
								StrokeLine(ctx, -0.22, 0.24, 0.22, 0.24);
								break;
							case "expert-b":
								DrawBrickGlyph(ctx);
								DrawWallGlyph(ctx);
								break;
							default:
								DrawBrickGlyph(ctx);
								break;
						}
						break;

					case "blacksmith":
						switch (nodeId)
						{
							case "foundation":
								DrawAnvilGlyph(ctx);
								break;
							case "discipline":
								DrawAnvilGlyph(ctx);
								DrawHammerGlyph(ctx);
								break;
							case "path-a":
								DrawSwordGlyph(ctx);
								break;
							case "path-b":
								DrawBreastplateGlyph(ctx);
								break;
							case "expert-a":
								DrawSwordGlyph(ctx);
								StrokeLine(ctx, -0.18, -0.16, 0.15, -0.30);
								break;
							case "expert-b":
								DrawBreastplateGlyph(ctx);
								StrokeLine(ctx, -0.24, -0.18, 0.24, -0.18);
								break;
							default:
								DrawAnvilGlyph(ctx);
								break;
						}
						break;

					case "cook":
						switch (nodeId)
						{
							case "foundation":
								DrawPotGlyph(ctx);
								break;
							case "discipline":
								DrawClocheGlyph(ctx);
								break;
							case "path-a":
								DrawClocheGlyph(ctx);
								break;
							case "path-b":
								DrawPlatterGlyph(ctx);
								break;
							case "expert-a":
								DrawClocheGlyph(ctx);
								StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
								break;
							case "expert-b":
								DrawPlatterGlyph(ctx);
								StrokeLine(ctx, -0.20, -0.16, -0.08, -0.28);
								StrokeLine(ctx, 0.20, -0.16, 0.08, -0.28);
								break;
							default:
								DrawPotGlyph(ctx);
								break;
						}
						break;

					case "potter":
						switch (nodeId)
						{
							case "foundation":
								DrawVaseGlyph(ctx);
								break;
							case "discipline":
								DrawVaseGlyph(ctx);
								StrokeLine(ctx, -0.22, 0.20, 0.22, 0.20);
								break;
							case "path-a":
								DrawVaseGlyph(ctx);
								break;
							case "path-b":
								DrawVaseGlyph(ctx);
								DrawFlameGlyph(ctx);
								break;
							case "expert-a":
								DrawVaseGlyph(ctx);
								StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
								break;
							case "expert-b":
								DrawVaseGlyph(ctx);
								DrawFlameGlyph(ctx);
								StrokeLine(ctx, 0.20, -0.22, 0.33, -0.32);
								break;
							default:
								DrawVaseGlyph(ctx);
								break;
						}
						break;

					case "leatherworker":
						switch (nodeId)
						{
							case "foundation":
								DrawHideGlyph(ctx);
								break;
							case "discipline":
								DrawHideGlyph(ctx);
								StrokeLine(ctx, -0.16, -0.08, 0.16, 0.08);
								break;
							case "path-a":
								DrawSaddleGlyph(ctx);
								break;
							case "path-b":
								DrawHideGlyph(ctx);
								break;
							case "expert-a":
								DrawSaddleGlyph(ctx);
								StrokeLine(ctx, -0.18, 0.24, 0.18, 0.24);
								break;
							case "expert-b":
								DrawHideGlyph(ctx);
								StrokeLine(ctx, -0.24, -0.08, 0.24, 0.08);
								StrokeLine(ctx, 0.00, -0.20, 0.00, 0.20);
								break;
							default:
								DrawHideGlyph(ctx);
								break;
						}
						break;

					case "tailor":
						switch (nodeId)
						{
							case "foundation":
								DrawNeedleGlyph(ctx);
								break;
							case "discipline":
								DrawNeedleGlyph(ctx);
								StrokeLine(ctx, -0.12, 0.20, 0.18, 0.30);
								break;
							case "path-a":
								DrawNeedleGlyph(ctx);
								break;
							case "path-b":
								DrawLoomGlyph(ctx);
								break;
							case "expert-a":
								DrawNeedleGlyph(ctx);
								StrokeLine(ctx, -0.20, -0.22, 0.12, -0.26);
								break;
							case "expert-b":
								DrawLoomGlyph(ctx);
								StrokeLine(ctx, -0.18, 0.28, 0.18, 0.28);
								break;
							default:
								DrawNeedleGlyph(ctx);
								break;
						}
						break;

					case "hunter":
						switch (nodeId)
						{
							case "foundation":
								DrawHunterGlyph(ctx);
								break;
							case "discipline":
								DrawHunterGlyph(ctx);
								DrawTargetGlyph(ctx);
								break;
							case "path-a":
								DrawTrackerGlyph(ctx);
								break;
							case "path-b":
								DrawCleaverGlyph(ctx);
								break;
							case "expert-a":
								DrawMasterTrackerGlyph(ctx);
								break;
							case "expert-b":
								DrawHunterGlyph(ctx);
								StrokeLine(ctx, -0.22, 0.22, 0.22, -0.22);
								break;
							default:
								DrawHunterGlyph(ctx);
								break;
						}
						break;

					case "warrior":
						switch (nodeId)
						{
							case "foundation":
							case "discipline":
							case "path-b":
							case "expert-b":
								DrawCrossedSwordsGlyph(ctx);
								break;
							case "path-a":
								DrawSwordGlyph(ctx);
								break;
							case "expert-a":
								DrawSwordGlyph(ctx);
								StrokeLine(ctx, -0.14, -0.26, 0.14, -0.26);
								break;
							default:
								DrawCrossedSwordsGlyph(ctx);
								break;
						}
						break;

					case "ranger":
						switch (nodeId)
						{
							case "foundation":
								DrawEyeGlyph(ctx);
								break;
							case "discipline":
								DrawStringCareGlyph(ctx);
								break;
							case "path-a":
								DrawSharpshooterGlyph(ctx);
								break;
							case "path-b":
								DrawSkirmisherGlyph(ctx);
								break;
							case "expert-a":
								DrawMasterSharpshooterGlyph(ctx);
								break;
							case "expert-b":
								DrawArrowstormGlyph(ctx);
								break;
							default:
								DrawRangerGlyph(ctx);
								break;
						}
						break;

					case "fisher":
						switch (nodeId)
						{
							case "foundation":
								DrawFishGlyph(ctx);
								break;
							case "discipline":
								DrawHookGlyph(ctx);
								break;
							case "path-a":
								DrawHookGlyph(ctx);
								break;
							case "path-b":
								DrawNetGlyph(ctx);
								break;
							case "expert-a":
								DrawHookGlyph(ctx);
								StrokeLine(ctx, -0.14, -0.24, 0.14, -0.24);
								break;
							case "expert-b":
								DrawNetGlyph(ctx);
								StrokeLine(ctx, -0.20, -0.24, 0.20, -0.24);
								break;
							default:
								DrawFishGlyph(ctx);
								break;
						}
						break;

					case "animalhusbandry":
						switch (nodeId)
						{
							case "foundation":
								DrawPawGlyph(ctx);
								break;
							case "discipline":
								DrawCrookGlyph(ctx);
								break;
							case "path-a":
								DrawHeartGlyph(ctx);
								FillCircle(ctx, 0.18, 0.18, 0.08);
								break;
							case "path-b":
								DrawCrookGlyph(ctx);
								break;
							case "expert-a":
								DrawHeartGlyph(ctx);
								FillCircle(ctx, 0.18, 0.18, 0.08);
								StrokeLine(ctx, -0.18, -0.24, 0.18, -0.24);
								break;
							case "expert-b":
								DrawCrookGlyph(ctx);
								StrokeLine(ctx, -0.14, 0.28, 0.14, 0.28);
								break;
							default:
								DrawPawGlyph(ctx);
								break;
						}
						break;

					case "beekeeper":
						switch (nodeId)
						{
							case "foundation":
								DrawBeeGlyph(ctx);
								break;
							case "discipline":
								DrawHoneycombGlyph(ctx);
								break;
							case "path-a":
								DrawHoneycombGlyph(ctx);
								break;
							case "path-b":
								DrawHoneyJarGlyph(ctx);
								break;
							case "expert-a":
								DrawHoneycombGlyph(ctx);
								StrokeLine(ctx, -0.18, -0.24, 0.18, -0.24);
								break;
							case "expert-b":
								DrawHoneyJarGlyph(ctx);
								StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
								break;
							default:
								DrawBeeGlyph(ctx);
								break;
						}
						break;

					case "spearman":
						switch (nodeId)
						{
							case "foundation":
							case "discipline":
								DrawSpearGlyph(ctx);
								break;
							case "path-a":
								DrawSpearGlyph(ctx);
								FillPolygon(ctx, -0.06, -0.02, -0.25, 0.00, -0.06, 0.14);
								break;
							case "path-b":
								DrawSpearGlyph(ctx);
								FillCircle(ctx, 0.21, -0.20, 0.06);
								break;
							case "expert-a":
								DrawSpearGlyph(ctx);
								FillPolygon(ctx, -0.06, -0.02, -0.25, 0.00, -0.06, 0.14);
								StrokeLine(ctx, -0.20, -0.22, 0.20, -0.22);
								break;
							case "expert-b":
								DrawSpearGlyph(ctx);
								FillCircle(ctx, 0.21, -0.20, 0.06);
								StrokeLine(ctx, -0.20, -0.22, 0.20, -0.22);
								break;
							default:
								DrawSpearGlyph(ctx);
								break;
						}
						break;

					case "shield":
						switch (nodeId)
						{
							case "foundation":
							case "discipline":
							case "path-a":
							case "expert-a":
								DrawShieldGlyph(ctx);
								break;
							case "path-b":
							case "expert-b":
								DrawWallGlyph(ctx);
								break;
							default:
								DrawShieldGlyph(ctx);
								break;
						}
						break;

					case "tank":
						switch (nodeId)
						{
							case "foundation":
							case "discipline":
								DrawHelmetGlyph(ctx);
								break;
							case "path-a":
							case "expert-a":
								DrawBreastplateGlyph(ctx);
								break;
							case "path-b":
							case "expert-b":
								DrawHelmetGlyph(ctx);
								StrokeLine(ctx, 0.20, -0.10, 0.38, -0.10);
								StrokeLine(ctx, 0.18, 0.03, 0.35, 0.03);
								break;
							default:
								DrawHelmetGlyph(ctx);
								break;
						}
						break;

					default:
						DrawProfessionIcon(
							ctx,
							classId,
							0,
							0,
							0.92,
							r,
							g,
							b,
							a
						);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawEyeGlyph(
			Context ctx)
		{
			ctx.MoveTo(-0.42, 0);
			ctx.CurveTo(-0.22, -0.28, 0.22, -0.28, 0.42, 0);
			ctx.CurveTo(0.22, 0.28, -0.22, 0.28, -0.42, 0);
			ctx.ClosePath();
			ctx.Stroke();
			StrokeCircle(ctx, 0, 0, 0.14);
			FillCircle(ctx, 0, 0, 0.055);
		}

		private static void DrawRangerGlyph(
			Context ctx)
		{
			DrawBowGlyph(ctx);
		}

		private static void DrawStringCareGlyph(
			Context ctx)
		{
			DrawBowGlyph(ctx);
			StrokeLine(ctx, -0.20, -0.31, 0.16, 0.00);
			StrokeLine(ctx, 0.16, 0.00, -0.20, 0.31);
		}

		private static void DrawSharpshooterGlyph(
			Context ctx)
		{
			DrawTargetGlyph(ctx);
		}

		private static void DrawMasterSharpshooterGlyph(
			Context ctx)
		{
			DrawTargetGlyph(ctx);
			StrokeCircle(ctx, 0, 0, 0.38);
			FillCircle(ctx, 0, 0, 0.03);
		}

		private static void DrawSkirmisherGlyph(
			Context ctx)
		{
			DrawBowGlyph(ctx);
			StrokeLine(ctx, -0.43, -0.22, -0.27, -0.22);
			StrokeLine(ctx, -0.46, -0.05, -0.28, -0.05);
			StrokeLine(ctx, -0.43, 0.12, -0.27, 0.12);
		}

		private static void DrawArrowstormGlyph(
			Context ctx)
		{
			DrawQuiverGlyph(ctx);
			DrawSingleArrowGlyph(ctx, -0.22, -0.40, -0.10, 0.16);
			DrawSingleArrowGlyph(ctx, 0.00, -0.44, 0.04, 0.14);
			DrawSingleArrowGlyph(ctx, 0.22, -0.39, 0.17, 0.16);
		}

		private static void DrawQuiverGlyph(
			Context ctx)
		{
			ctx.MoveTo(-0.22, -0.12);
			ctx.LineTo(0.18, -0.12);
			ctx.LineTo(0.11, 0.36);
			ctx.LineTo(-0.12, 0.36);
			ctx.ClosePath();
			ctx.Stroke();
			StrokeLine(ctx, -0.17, 0.08, 0.15, 0.08);
			StrokeLine(ctx, -0.13, 0.22, 0.12, 0.22);
		}

		private static void DrawSingleArrowGlyph(
			Context ctx,
			double x1,
			double y1,
			double x2,
			double y2)
		{
			StrokeLine(ctx, x1, y1, x2, y2);

			double dx = x2 - x1;
			double dy = y2 - y1;
			double length = Math.Max(0.0001, Math.Sqrt(dx * dx + dy * dy));
			double ux = dx / length;
			double uy = dy / length;
			double px = -uy;
			double py = ux;

			FillPolygon(
				ctx,
				x1,
				y1,
				x1 + ux * 0.11 + px * 0.055,
				y1 + uy * 0.11 + py * 0.055,
				x1 + ux * 0.11 - px * 0.055,
				y1 + uy * 0.11 - py * 0.055
			);

			StrokeLine(
				ctx,
				x2,
				y2,
				x2 - ux * 0.10 + px * 0.06,
				y2 - uy * 0.10 + py * 0.06
			);
			StrokeLine(
				ctx,
				x2,
				y2,
				x2 - ux * 0.10 - px * 0.06,
				y2 - uy * 0.10 - py * 0.06
			);
		}

		private static void DrawTrackerGlyph(
			Context ctx)
		{
			DrawFootprintGlyph(ctx, -0.13, 0.10, 0.95);
			DrawFootprintGlyph(ctx, 0.14, -0.12, 0.95);
			StrokeLine(ctx, -0.26, 0.26, 0.02, 0.02);
		}

		private static void DrawMasterTrackerGlyph(
			Context ctx)
		{
			DrawTrackerGlyph(ctx);
			ctx.Arc(0.24, -0.22, 0.11, 0, Math.PI * 2);
			ctx.Stroke();
			StrokeLine(ctx, 0.24, -0.33, 0.24, -0.11);
			StrokeLine(ctx, 0.13, -0.22, 0.35, -0.22);
		}

		private static void DrawFootprintGlyph(
			Context ctx,
			double offsetX,
			double offsetY,
			double scale)
		{
			FillCircle(ctx, offsetX, offsetY + 0.08 * scale, 0.08 * scale);
			FillCircle(ctx, offsetX - 0.07 * scale, offsetY - 0.05 * scale, 0.03 * scale);
			FillCircle(ctx, offsetX - 0.01 * scale, offsetY - 0.10 * scale, 0.03 * scale);
			FillCircle(ctx, offsetX + 0.05 * scale, offsetY - 0.11 * scale, 0.028 * scale);
			FillCircle(ctx, offsetX + 0.10 * scale, offsetY - 0.07 * scale, 0.025 * scale);
		}

		private static void DrawSurveyorGlyph(
			Context ctx)
		{
			StrokeLine(ctx, -0.22, 0.28, 0.00, -0.02);
			StrokeLine(ctx, 0.00, -0.02, 0.22, 0.28);
			StrokeLine(ctx, 0.00, -0.02, 0.00, 0.28);
			ctx.Rectangle(-0.16, -0.18, 0.32, 0.12);
			ctx.Stroke();
			StrokeLine(ctx, -0.20, -0.02, 0.20, -0.02);
		}

		private static void DrawMasterSurveyorGlyph(
			Context ctx)
		{
			DrawSurveyorGlyph(ctx);
			ctx.MoveTo(-0.28, 0.10);
			ctx.CurveTo(-0.12, 0.02, 0.02, 0.02, 0.18, 0.10);
			ctx.CurveTo(0.28, 0.15, 0.30, 0.22, 0.18, 0.28);
			ctx.Stroke();
			ctx.MoveTo(-0.24, 0.18);
			ctx.CurveTo(-0.08, 0.12, 0.04, 0.12, 0.20, 0.18);
			ctx.Stroke();
		}

		private static void DrawOpenBookIcon(
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

				ctx.LineWidth = 0.08;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				DrawOpenBookGlyph(ctx);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawDisciplineMedalIcon(
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

				ctx.LineWidth = 0.08;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				DrawRosetteGlyph(ctx);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawMiniProfessionBadge(
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
				ctx.Arc(
					centerX,
					centerY,
					size * 0.58,
					0,
					Math.PI * 2
				);

				SetColor(
					ctx,
					r * 0.28,
					g * 0.28,
					b * 0.28,
					0.96 * a
				);
				ctx.FillPreserve();

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);
				ctx.LineWidth = Math.Max(1.0, size * 0.10);
				ctx.Stroke();

				DrawProfessionIcon(
					ctx,
					classId,
					centerX,
					centerY,
					size * 0.48,
					r,
					g,
					b,
					a
				);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawUpgradeSparkIcon(
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

				ctx.LineWidth = 0.10;

				SetColor(
					ctx,
					r,
					g,
					b,
					a
				);

				DrawSparkBurstGlyph(ctx);
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static void DrawSpecializationNodeIcon(
			Context ctx,
			string classId,
			bool pathA,
			bool expert,
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
					size * 0.72,
					size * 0.72
				);

				ctx.LineWidth = 0.08;

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
						if (pathA)
						{
							DrawCompassGlyph(ctx);
						}
						else
						{
							DrawLanternGlyph(ctx);
						}
						break;

					case "woodworker":
						if (pathA)
						{
							DrawPlankGlyph(ctx);
							DrawHammerGlyph(ctx);
						}
						else
						{
							DrawLogGlyph(ctx);
							DrawAxeGlyph(ctx);
						}
						break;

					case "farmer":
						if (pathA)
						{
							DrawSeedlingGlyph(ctx);
						}
						else
						{
							DrawWheatGlyph(ctx);
							DrawSickleGlyph(ctx);
						}
						break;

					case "builder":
						if (pathA)
						{
							DrawDraftingGlyph(ctx);
						}
						else
						{
							DrawBrickGlyph(ctx);
							DrawTrowelGlyph(ctx);
						}
						break;

					case "blacksmith":
						if (pathA)
						{
							DrawSwordGlyph(ctx);
						}
						else
						{
							DrawBreastplateGlyph(ctx);
						}
						break;

					case "cook":
						if (pathA)
						{
							DrawClocheGlyph(ctx);
						}
						else
						{
							DrawPlatterGlyph(ctx);
						}
						break;

					case "potter":
						if (pathA)
						{
							DrawVaseGlyph(ctx);
						}
						else
						{
							DrawVaseGlyph(ctx);
							DrawFlameGlyph(ctx);
						}
						break;

					case "leatherworker":
						if (pathA)
						{
							DrawSaddleGlyph(ctx);
						}
						else
						{
							DrawHideGlyph(ctx);
						}
						break;

					case "tailor":
						if (pathA)
						{
							DrawNeedleGlyph(ctx);
						}
						else
						{
							DrawLoomGlyph(ctx);
						}
						break;

					case "hunter":
						if (pathA)
						{
							DrawTargetGlyph(ctx);
							DrawPawGlyph(ctx);
						}
						else
						{
							DrawCleaverGlyph(ctx);
						}
						break;

					case "warrior":
						if (pathA)
						{
							DrawSwordGlyph(ctx);
						}
						else
						{
							DrawCrossedSwordsGlyph(ctx);
						}
						break;

					case "ranger":
						if (pathA)
						{
							DrawTargetGlyph(ctx);
						}
						else
						{
							DrawBowGlyph(ctx);
							StrokeLine(
								ctx,
								-0.32,
								-0.18,
								0.35,
								-0.34
							);
						}
						break;

					case "fisher":
						if (pathA)
						{
							DrawHookGlyph(ctx);
						}
						else
						{
							DrawNetGlyph(ctx);
						}
						break;

					case "animalhusbandry":
						if (pathA)
						{
							DrawHeartGlyph(ctx);
							FillCircle(
								ctx,
								0.18,
								0.18,
								0.08
							);
						}
						else
						{
							DrawCrookGlyph(ctx);
						}
						break;

					case "beekeeper":
						if (pathA)
						{
							DrawHoneycombGlyph(ctx);
						}
						else
						{
							DrawHoneyJarGlyph(ctx);
						}
						break;

					case "spearman":
						if (pathA)
						{
							DrawSpearGlyph(ctx);
							FillPolygon(
								ctx,
								-0.06,
								-0.02,
								-0.25,
								0.00,
								-0.06,
								0.14
							);
						}
						else
						{
							DrawSpearGlyph(ctx);
							FillCircle(
								ctx,
								0.21,
								-0.20,
								0.06
							);
						}
						break;

					case "shield":
						if (pathA)
						{
							DrawShieldGlyph(ctx);
						}
						else
						{
							DrawWallGlyph(ctx);
						}
						break;

					case "tank":
						if (pathA)
						{
							DrawBreastplateGlyph(ctx);
						}
						else
						{
							DrawHelmetGlyph(ctx);
							StrokeLine(
								ctx,
								0.20,
								-0.10,
								0.38,
								-0.10
							);
							StrokeLine(
								ctx,
								0.18,
								0.03,
								0.35,
								0.03
							);
						}
						break;

					default:
						DrawProfessionIcon(
							ctx,
							classId,
							0,
							0,
							0.85,
							r,
							g,
							b,
							a
						);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}

			DrawMiniProfessionBadge(
				ctx,
				classId,
				centerX - size * 0.28,
				centerY - size * 0.26,
				size * 0.21,
				r,
				g,
				b,
				a
			);

			if (expert)
			{
				DrawUpgradeSparkIcon(
					ctx,
					centerX + size * 0.28,
					centerY - size * 0.26,
					size * 0.15,
					r,
					g,
					b,
					a
				);

				DrawUpgradeSparkIcon(
					ctx,
					centerX + size * 0.08,
					centerY + size * 0.26,
					size * 0.10,
					r,
					g,
					b,
					a * 0.92
				);
			}
		}
	}
}

