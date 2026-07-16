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
		private static void DrawOpenBookGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.40,
				-0.25
			);
			ctx.CurveTo(
				-0.26,
				-0.35,
				-0.09,
				-0.34,
				0,
				-0.21
			);
			ctx.CurveTo(
				0.09,
				-0.34,
				0.26,
				-0.35,
				0.40,
				-0.25
			);
			ctx.LineTo(
				0.40,
				0.33
			);
			ctx.CurveTo(
				0.26,
				0.24,
				0.09,
				0.24,
				0,
				0.34
			);
			ctx.CurveTo(
				-0.09,
				0.24,
				-0.26,
				0.24,
				-0.40,
				0.33
			);
			ctx.ClosePath();
			ctx.Stroke();

			StrokeLine(
				ctx,
				0,
				-0.22,
				0,
				0.34
			);
		}

		private static void DrawRosetteGlyph(
			Context ctx)
		{
			StrokeCircle(
				ctx,
				0,
				-0.06,
				0.23
			);
			FillCircle(
				ctx,
				0,
				-0.06,
				0.08
			);
			FillPolygon(
				ctx,
				-0.12,
				0.16,
				-0.03,
				0.42,
				-0.21,
				0.31
			);
			FillPolygon(
				ctx,
				0.12,
				0.16,
				0.21,
				0.31,
				0.03,
				0.42
			);
		}

		private static void DrawSparkBurstGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				0,
				-0.36,
				0,
				0.36
			);
			StrokeLine(
				ctx,
				-0.36,
				0,
				0.36,
				0
			);
			StrokeLine(
				ctx,
				-0.25,
				-0.25,
				0.25,
				0.25
			);
			StrokeLine(
				ctx,
				-0.25,
				0.25,
				0.25,
				-0.25
			);
		}

		private static void DrawLanternGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.16,
				-0.25,
				0.16,
				-0.25
			);
			ctx.MoveTo(
				-0.24,
				-0.12
			);
			ctx.LineTo(
				-0.20,
				0.22
			);
			ctx.LineTo(
				0.20,
				0.22
			);
			ctx.LineTo(
				0.24,
				-0.12
			);
			ctx.ClosePath();
			ctx.Stroke();
			StrokeLine(
				ctx,
				0,
				-0.37,
				0,
				-0.25
			);
			DrawFlameGlyph(ctx);
		}

		private static void DrawPlankGlyph(
			Context ctx)
		{
			StrokeRectangle(
				ctx,
				-0.38,
				-0.12,
				0.76,
				0.24
			);
			StrokeLine(
				ctx,
				-0.18,
				-0.12,
				-0.18,
				0.12
			);
			StrokeLine(
				ctx,
				0.12,
				-0.12,
				0.12,
				0.12
			);
		}

		private static void DrawLogGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.35,
				-0.14
			);
			ctx.LineTo(
				0.28,
				-0.14
			);
			ctx.CurveTo(
				0.39,
				-0.14,
				0.39,
				0.14,
				0.28,
				0.14
			);
			ctx.LineTo(
				-0.35,
				0.14
			);
			ctx.ClosePath();
			ctx.Stroke();
			StrokeCircle(
				ctx,
				-0.35,
				0,
				0.14
			);
			StrokeCircle(
				ctx,
				-0.35,
				0,
				0.06
			);
		}

		private static void DrawSeedlingGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				0,
				0.38,
				0,
				-0.10
			);
			ctx.MoveTo(
				0,
				-0.06
			);
			ctx.CurveTo(
				-0.05,
				-0.28,
				-0.28,
				-0.24,
				-0.31,
				-0.03
			);
			ctx.Stroke();
			ctx.MoveTo(
				0,
				-0.01
			);
			ctx.CurveTo(
				0.05,
				-0.24,
				0.28,
				-0.28,
				0.31,
				-0.05
			);
			ctx.Stroke();
		}

		private static void DrawSickleGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				0.08,
				-0.20
			);
			ctx.CurveTo(
				-0.18,
				-0.08,
				-0.14,
				0.25,
				0.12,
				0.18
			);
			ctx.Stroke();
			StrokeLine(
				ctx,
				0.18,
				-0.28,
				0.06,
				-0.08
			);
		}

		private static void DrawDraftingGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.18,
				0.35,
				0.00,
				-0.14
			);
			StrokeLine(
				ctx,
				0.18,
				0.35,
				0.00,
				-0.14
			);
			StrokeLine(
				ctx,
				-0.28,
				0.35,
				0.28,
				0.35
			);
			StrokeLine(
				ctx,
				0.00,
				-0.14,
				0.14,
				-0.28
			);
		}

		private static void DrawTrowelGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.26,
				0.31,
				0.03,
				0.04
			);
			FillPolygon(
				ctx,
				0.03,
				0.04,
				0.30,
				-0.04,
				0.12,
				-0.24
			);
		}

		private static void DrawClocheGlyph(
			Context ctx)
		{
			ctx.Arc(
				0,
				0.08,
				0.28,
				Math.PI,
				Math.PI * 2
			);
			ctx.Stroke();
			StrokeLine(
				ctx,
				-0.34,
				0.08,
				0.34,
				0.08
			);
			FillCircle(
				ctx,
				0,
				-0.22,
				0.05
			);
		}

		private static void DrawPlatterGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.35,
				0.10,
				0.35,
				0.10
			);
			ctx.MoveTo(
				-0.22,
				0.10
			);
			ctx.CurveTo(
				-0.16,
				-0.16,
				0.16,
				-0.16,
				0.22,
				0.10
			);
			ctx.Stroke();
			DrawSteam(ctx, -0.11);
			DrawSteam(ctx, 0.11);
		}

		private static void DrawFlameGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				0,
				0.22
			);
			ctx.CurveTo(
				-0.14,
				0.08,
				-0.12,
				-0.10,
				0,
				-0.24
			);
			ctx.CurveTo(
				0.12,
				-0.10,
				0.14,
				0.08,
				0,
				0.22
			);
			ctx.ClosePath();
			ctx.Stroke();
		}

		private static void DrawSaddleGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.30,
				0.12
			);
			ctx.CurveTo(
				-0.26,
				-0.18,
				0.05,
				-0.28,
				0.26,
				-0.08
			);
			ctx.LineTo(
				0.18,
				0.16
			);
			ctx.LineTo(
				-0.08,
				0.16
			);
			ctx.LineTo(
				-0.10,
				0.34
			);
			ctx.LineTo(
				-0.20,
				0.34
			);
			ctx.LineTo(
				-0.22,
				0.16
			);
			ctx.LineTo(
				-0.30,
				0.12
			);
			ctx.Stroke();
		}

		private static void DrawLoomGlyph(
			Context ctx)
		{
			StrokeRectangle(
				ctx,
				-0.28,
				-0.30,
				0.56,
				0.60
			);
			StrokeLine(
				ctx,
				-0.16,
				-0.22,
				-0.16,
				0.22
			);
			StrokeLine(
				ctx,
				0,
				-0.22,
				0,
				0.22
			);
			StrokeLine(
				ctx,
				0.16,
				-0.22,
				0.16,
				0.22
			);
			StrokeLine(
				ctx,
				-0.28,
				0,
				0.28,
				0
			);
		}

		private static void DrawCleaverGlyph(
			Context ctx)
		{
			FillPolygon(
				ctx,
				-0.08,
				-0.28,
				0.26,
				-0.18,
				0.26,
				0.08,
				-0.04,
				0.08,
				-0.12,
				-0.05
			);
			StrokeLine(
				ctx,
				-0.08,
				0.08,
				-0.08,
				0.34
			);
		}

		private static void DrawTargetGlyph(
			Context ctx)
		{
			StrokeCircle(
				ctx,
				0,
				0,
				0.30
			);
			StrokeCircle(
				ctx,
				0,
				0,
				0.16
			);
			StrokeLine(
				ctx,
				-0.36,
				0,
				0.36,
				0
			);
			StrokeLine(
				ctx,
				0,
				-0.36,
				0,
				0.36
			);
		}

		private static void DrawHookGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				0.16,
				-0.36,
				0.02,
				-0.02
			);
			ctx.MoveTo(
				0.02,
				-0.02
			);
			ctx.CurveTo(
				-0.12,
				0.08,
				-0.12,
				0.30,
				0.06,
				0.26
			);
			ctx.Stroke();
			FillPolygon(
				ctx,
				0.08,
				0.18,
				0.19,
				0.20,
				0.12,
				0.29
			);
		}

		private static void DrawNetGlyph(
			Context ctx)
		{
			StrokeRectangle(
				ctx,
				-0.26,
				-0.28,
				0.52,
				0.56
			);
			StrokeLine(
				ctx,
				-0.09,
				-0.28,
				-0.09,
				0.28
			);
			StrokeLine(
				ctx,
				0.09,
				-0.28,
				0.09,
				0.28
			);
			StrokeLine(
				ctx,
				-0.26,
				-0.09,
				0.26,
				-0.09
			);
			StrokeLine(
				ctx,
				-0.26,
				0.09,
				0.26,
				0.09
			);
		}

		private static void DrawHeartGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				0,
				0.30
			);
			ctx.CurveTo(
				-0.36,
				0.06,
				-0.35,
				-0.22,
				-0.14,
				-0.24
			);
			ctx.CurveTo(
				-0.03,
				-0.24,
				0.00,
				-0.13,
				0.00,
				-0.13
			);
			ctx.CurveTo(
				0.00,
				-0.13,
				0.03,
				-0.24,
				0.14,
				-0.24
			);
			ctx.CurveTo(
				0.35,
				-0.22,
				0.36,
				0.06,
				0,
				0.30
			);
			ctx.ClosePath();
			ctx.Stroke();
		}

		private static void DrawCrookGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.02,
				0.36,
				-0.02,
				-0.03
			);
			ctx.MoveTo(
				-0.02,
				-0.03
			);
			ctx.CurveTo(
				-0.02,
				-0.24,
				0.24,
				-0.29,
				0.30,
				-0.08
			);
			ctx.Stroke();
		}

		private static void DrawHoneycombGlyph(
			Context ctx)
		{
			FillPolygon(
				ctx,
				0,
				-0.30,
				0.24,
				-0.16,
				0.24,
				0.16,
				0,
				0.30,
				-0.24,
				0.16,
				-0.24,
				-0.16
			);
			FillPolygon(
				ctx,
				-0.28,
				-0.02,
				-0.12,
				-0.16,
				0.04,
				-0.02,
				-0.12,
				0.12
			);
		}

		private static void DrawHoneyJarGlyph(
			Context ctx)
		{
			StrokeLine(
				ctx,
				-0.12,
				-0.28,
				0.12,
				-0.28
			);
			StrokeRectangle(
				ctx,
				-0.22,
				-0.20,
				0.44,
				0.42
			);
			StrokeLine(
				ctx,
				-0.18,
				-0.04,
				0.18,
				-0.04
			);
			StrokeLine(
				ctx,
				-0.18,
				0.08,
				0.18,
				0.08
			);
		}

		private static void DrawWallGlyph(
			Context ctx)
		{
			StrokeRectangle(
				ctx,
				-0.34,
				-0.26,
				0.68,
				0.52
			);
			StrokeLine(
				ctx,
				-0.34,
				0,
				0.34,
				0
			);
			StrokeLine(
				ctx,
				-0.12,
				-0.26,
				-0.12,
				0
			);
			StrokeLine(
				ctx,
				0.12,
				0,
				0.12,
				0.26
			);
		}

		private static void DrawBreastplateGlyph(
			Context ctx)
		{
			ctx.MoveTo(
				-0.26,
				-0.28
			);
			ctx.LineTo(
				-0.10,
				-0.36
			);
			ctx.LineTo(
				0.10,
				-0.36
			);
			ctx.LineTo(
				0.26,
				-0.28
			);
			ctx.LineTo(
				0.22,
				0.24
			);
			ctx.LineTo(
				0.06,
				0.34
			);
			ctx.LineTo(
				-0.06,
				0.34
			);
			ctx.LineTo(
				-0.22,
				0.24
			);
			ctx.ClosePath();
			ctx.Stroke();
			StrokeLine(
				ctx,
				0,
				-0.36,
				0,
				0.28
			);
		}
	}
}

