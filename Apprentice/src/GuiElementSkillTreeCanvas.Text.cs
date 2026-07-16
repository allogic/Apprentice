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
		private void UpdateCanvasScale(
			int canvasWidth,
			int canvasHeight)
		{
			canvasScaleX = Math.Max(
				0.01,
				canvasWidth / DesignCanvasWidth
			);

			canvasScaleY = Math.Max(
				0.01,
				canvasHeight / DesignCanvasHeight
			);

			double dialogScale = Math.Min(
				canvasScaleX,
				canvasScaleY
			);

			// CairoFont applies the game's GUI scale itself. Divide that out
			// before the shared canvas transform scales the complete dialog.
			// This keeps text, icons, panels and hit regions on one scale that
			// comes only from the Apprentice dialog's own inner bounds.
			textScale =
				Math.Clamp(
					1 / dialogScale,
					0.40,
					2.00
				);
		}

		private CairoFont CreateScaledFont(
			CairoFont font,
			double additionalScale = 1)
		{
			CairoFont drawFont =
				font.Clone();

			drawFont.UnscaledFontsize =
				Math.Max(
					6,
					font.UnscaledFontsize *
					textScale *
					Math.Clamp(
						additionalScale,
						0.55,
						1.25
					)
				);

			return drawFont;
		}

		private void DrawText(
			Context ctx,
			CairoFont font,
			string text,
			double x,
			double y)
		{
			DrawTextScaled(
				ctx,
				font,
				text,
				x,
				y,
				1
			);
		}

		private void DrawTextScaled(
			Context ctx,
			CairoFont font,
			string text,
			double x,
			double y,
			double additionalScale)
		{
			CairoFont drawFont =
				CreateScaledFont(
					font,
					additionalScale
				);

			ctx.Save();

			try
			{
				drawFont.SetupContext(
					ctx
				);

				textUtil.DrawTextLine(
					ctx,
					drawFont,
					SafeText(text),
					x,
					y
				);
			}
			finally
			{
				ctx.Restore();
				drawFont.Dispose();
			}
		}

		private void DrawText(
			Context ctx,
			CairoFont font,
			string text,
			double x,
			double y,
			double r,
			double g,
			double b,
			double a)
		{
			CairoFont drawFont =
				CreateScaledFont(
					font
				)
					.WithColor(
						new[]
						{
							r,
							g,
							b,
							a
						}
					);

			ctx.Save();

			try
			{
				drawFont.SetupContext(
					ctx
				);

				textUtil.DrawTextLine(
					ctx,
					drawFont,
					SafeText(text),
					x,
					y
				);
			}
			finally
			{
				ctx.Restore();
				drawFont.Dispose();
			}
		}

		private void DrawFittedText(
			Context ctx,
			CairoFont font,
			string text,
			double x,
			double y,
			double maximumWidth)
		{
			string safe =
				SafeText(
					text
				);

			double measuredWidth =
				MeasureTextWidth(
					font,
					safe
				);

			double fitScale =
				measuredWidth <= 0 ||
				measuredWidth <= maximumWidth
					? 1
					: Math.Max(
						0.62,
						maximumWidth / measuredWidth
					);

			DrawTextScaled(
				ctx,
				font,
				safe,
				x,
				y,
				fitScale
			);
		}

		private void DrawCenteredFittedText(
			Context ctx,
			CairoFont font,
			string text,
			double centerX,
			double y,
			double maximumWidth,
			double preferredScale = 1)
		{
			string safe =
				SafeText(
					text
				);

			double measuredWidth =
				MeasureTextWidth(
					font,
					safe
				);

			double fitScale =
				measuredWidth <= 0
					? preferredScale
					: Math.Min(
						preferredScale,
						maximumWidth / measuredWidth
					);

			fitScale =
				Math.Clamp(
					fitScale,
					0.62,
					1.25
				);

			DrawTextScaled(
				ctx,
				font,
				safe,
				centerX -
					measuredWidth *
					fitScale / 2,
				y,
				fitScale
			);
		}

		private void DrawCenteredText(
			Context ctx,
			CairoFont font,
			string text,
			double centerX,
			double y)
		{
			string safe =
				SafeText(
					text
				);

			double width =
				MeasureTextWidth(
					font,
					safe
				);

			DrawText(
				ctx,
				font,
				safe,
				centerX -
					width / 2,
				y
			);
		}

		private void DrawCenteredText(
			Context ctx,
			CairoFont font,
			string text,
			double centerX,
			double y,
			double r,
			double g,
			double b,
			double a)
		{
			string safe =
				SafeText(
					text
				);

			double width =
				MeasureTextWidth(
					font,
					safe
				);

			DrawText(
				ctx,
				font,
				safe,
				centerX -
					width / 2,
				y,
				r,
				g,
				b,
				a
			);
		}

		private double DrawWrappedText(
			Context ctx,
			CairoFont font,
			string text,
			double x,
			double y,
			double width)
		{
			CairoFont drawFont =
				CreateScaledFont(
					font
				);

			try
			{
				return textUtil
					.AutobreakAndDrawMultilineTextAt(
						ctx,
						drawFont,
						SafeText(text),
						x,
						y,
						width
					);
			}
			finally
			{
				drawFont.Dispose();
			}
		}

		private double MeasureTextWidth(
			CairoFont font,
			string text)
		{
			CairoFont measureFont =
				CreateScaledFont(
					font
				);

			try
			{
				return measureFont
					.GetTextExtents(
						SafeText(text)
					)
					.Width;
			}
			finally
			{
				measureFont.Dispose();
			}
		}

		private static void SetColor(
			Context ctx,
			double r,
			double g,
			double b,
			double a)
		{
			ctx.SetSourceRGBA(
				Math.Clamp(
					r,
					0,
					1
				),
				Math.Clamp(
					g,
					0,
					1
				),
				Math.Clamp(
					b,
					0,
					1
				),
				Math.Clamp(
					a,
					0,
					1
				)
			);
		}

		private static string SafeText(
			string? value)
		{
			if (string.IsNullOrEmpty(
					value))
			{
				return string.Empty;
			}

			var characters =
				new char[value.Length];

			for (int index = 0;
				 index < value.Length;
				 index++)
			{
				char character =
					value[index];

				characters[index] =
					character >= 32 &&
					character <= 126
						? character
						: '?';
			}

			return new string(
				characters
			);
		}

		private static string Shorten(
			string value,
			int maximumLength)
		{
			string safe =
				SafeText(
					value
				);

			if (safe.Length <=
				maximumLength)
			{
				return safe;
			}

			return safe.Substring(
					   0,
					   Math.Max(
						   1,
						   maximumLength -
						   3
					   )
				   ) +
				   "...";
		}

		public override void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			// The five field fonts are immutable templates. All Cairo work is
			// performed by short-lived initialized clones, so the templates do
			// not own native FontOptions and must not be disposed.
			canvasTexture.Dispose();

			base.Dispose();
		}
	}
}

