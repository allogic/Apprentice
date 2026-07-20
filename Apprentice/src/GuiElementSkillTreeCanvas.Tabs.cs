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
		private void DrawWindowTabs(
			Context ctx,
			double width)
		{
			const double tabY = 5;
			const double tabHeight = 34;
			const double tabWidth = 150;
			const double gap = 8;

			DrawWindowTabButton(ctx, 14, tabY, tabWidth, tabHeight,
				"SKILLTREE", "skilltree", activeTab == ApprenticeWindowTab.SkillTree);
			DrawWindowTabButton(ctx, 14 + tabWidth + gap, tabY, tabWidth, tabHeight,
				"STATS", "stats", activeTab == ApprenticeWindowTab.Stats);
			DrawWindowTabButton(ctx, 14 + (tabWidth + gap) * 2, tabY, tabWidth, tabHeight,
				"HIDDEN", "hidden", activeTab == ApprenticeWindowTab.HiddenClasses);
		}

		private void DrawWindowTabButton(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string label,
			string id,
			bool selected)
		{
			RoundRectangle(ctx, x, y, width, height, 8);
			SetColor(ctx,
				selected ? 0.34 : 0.10,
				selected ? 0.24 : 0.11,
				selected ? 0.08 : 0.14,
				0.96);
			ctx.FillPreserve();
			SetColor(ctx,
				selected ? 0.95 : 0.30,
				selected ? 0.72 : 0.32,
				selected ? 0.25 : 0.37,
				1);
			ctx.LineWidth = selected ? 2.5 : 1.2;
			ctx.Stroke();
			DrawCenteredText(ctx, sectionFont, label, x + width / 2, y + 9);

			hitRegions.Add(new SkillTreeHitRegion(
				SkillTreeHitKind.Tab,
				id,
				x,
				y,
				width,
				height));
		}

		private void DrawStatsTab(
			Context ctx,
			double width,
			double height)
		{
			const double margin = 14;
			ThemePalette theme = GetTheme(CurrentTree);
			DrawPanel(ctx, margin, margin, width - margin * 2, height - margin * 2,
				0.05, 0.06, 0.085, 0.985,
				theme.AccentR, theme.AccentG, theme.AccentB, 0.85);

			DrawText(ctx, titleFont, "ACTIVE SKILLTREE BONUSES", margin + 20, margin + 16);
			DrawText(ctx, smallFont,
				"Every line below is currently active on your character.",
				margin + 20, margin + 50);

			Entity? entity = api.World?.Player?.Entity;
			if (entity == null)
			{
				DrawText(ctx, bodyFont, "Player data is not available.", margin + 20, margin + 86);
				return;
			}

			var entries = new List<(string Group, string Text)>();

			foreach (SkillTreeDefinition tree in skillConfig.Trees.Values
				.OrderBy(tree => tree.DisplayName, StringComparer.OrdinalIgnoreCase))
			{
				foreach (SkillNodeDefinition node in tree.Nodes)
				{
					int rank = SkillTreeData.GetNodeRank(entity, tree.ClassId, node.Id);
					if (rank <= 0)
					{
						continue;
					}

					foreach (SkillEffectDefinition effect in node.Effects)
					{
						if (effect.Type.Equals("UnlockRecipe", StringComparison.OrdinalIgnoreCase))
						{
							entries.Add((tree.DisplayName,
								$"{node.DisplayName}: unlocks {effect.Code ?? "a special recipe"}"));
						}
						else
						{
							double value = effect.ValuePerRank * rank;
							entries.Add((tree.DisplayName,
								$"{node.DisplayName}: {FormatStatEffect(effect, value)}"));
						}
					}
				}
			}

			foreach (HiddenClassDefinition hidden in HiddenClassCatalog.All)
			{
				if (!HiddenClassData.IsUnlocked(entity, hidden.Id))
				{
					continue;
				}

				foreach (SkillEffectDefinition effect in hidden.Effects)
				{
					entries.Add((hidden.DisplayName,
						FormatStatEffect(effect, effect.ValuePerRank)));
				}
			}

			double leftX = margin + 22;
			double rightX = width / 2 + 8;
			double columnWidth = width / 2 - margin - 34;
			double leftY = margin + 86;
			double rightY = margin + 86;
			string? lastLeftGroup = null;
			string? lastRightGroup = null;

			for (int index = 0; index < entries.Count; index++)
			{
				bool right = index % 2 == 1;
				double x = right ? rightX : leftX;
				double y = right ? rightY : leftY;
				string? lastGroup = right ? lastRightGroup : lastLeftGroup;

				if (!string.Equals(lastGroup, entries[index].Group, StringComparison.Ordinal))
				{
					DrawText(ctx, sectionFont, entries[index].Group.ToUpperInvariant(), x, y);
					y += 24;
					if (right) lastRightGroup = entries[index].Group;
					else lastLeftGroup = entries[index].Group;
				}

				double used = DrawWrappedText(ctx, smallFont,
					"• " + entries[index].Text,
					x, y, columnWidth);
				y += Math.Max(24, used + 8);

				if (right) rightY = y;
				else leftY = y;
			}

			if (entries.Count == 0)
			{
				DrawText(ctx, bodyFont, "No skill bonuses have been learned yet.", leftX, leftY);
			}
		}

		private void DrawHiddenClassesTab(
			Context ctx,
			double width,
			double height)
		{
			const double margin = 14;
			ThemePalette theme = GetTheme(CurrentTree);
			DrawPanel(ctx, margin, margin, width - margin * 2, height - margin * 2,
				0.045, 0.055, 0.08, 0.99,
				0.38, 0.28, 0.55, 0.95);

			DrawText(ctx, titleFont, "HIDDEN CLASS DISCOVERIES", margin + 20, margin + 16);
			DrawText(ctx, smallFont,
				"Capstone combinations reveal powerful hybrid classes. Undiscovered classes remain secret.",
				margin + 20, margin + 50);

			Entity? entity = api.World?.Player?.Entity;
			// The 2.7 registry contains many more discoveries than the original
			// fixed eight-card view. Page the texture-backed canvas so cards never
			// render outside the dialog and mouse regions remain deterministic.
			int columns = width >= 1000 ? 4 : 3;
			int rows = 2;
			int pageSize = columns * rows;
			int pageCount = Math.Max(
				1,
				(int)Math.Ceiling(HiddenClassCatalog.All.Count / (double)pageSize)
			);
			hiddenPage = Math.Clamp(hiddenPage, 0, pageCount - 1);
			double gap = 14;
			double cardWidth = (width - margin * 2 - 40 - gap * (columns - 1)) / columns;
			double cardHeight = 210;
			double startX = margin + 20;
			double startY = margin + 84;

			int firstIndex = hiddenPage * pageSize;
			int lastIndex = Math.Min(HiddenClassCatalog.All.Count, firstIndex + pageSize);
			for (int index = firstIndex; index < lastIndex; index++)
			{
				HiddenClassDefinition definition = HiddenClassCatalog.All[index];
				int pageIndex = index - firstIndex;
				int column = pageIndex % columns;
				int row = pageIndex / columns;
				double x = startX + column * (cardWidth + gap);
				double y = startY + row * (cardHeight + gap);
				bool unlocked = entity != null && HiddenClassData.IsUnlocked(entity, definition.Id);

				RoundRectangle(ctx, x, y, cardWidth, cardHeight, 12);
				SetColor(ctx,
					unlocked ? 0.12 : 0.055,
					unlocked ? 0.09 : 0.06,
					unlocked ? 0.18 : 0.08,
					0.98);
				ctx.FillPreserve();
				SetColor(ctx,
					unlocked ? 0.72 : 0.22,
					unlocked ? 0.50 : 0.24,
					unlocked ? 0.95 : 0.30,
					unlocked ? 0.95 : 0.75);
				ctx.LineWidth = unlocked ? 2.5 : 1.5;
				ctx.Stroke();

				if (!unlocked)
				{
					DrawCenteredText(ctx, titleFont, "?", x + cardWidth / 2, y + 40,
						0.62, 0.58, 0.72, 1);
					DrawCenteredText(ctx, sectionFont, "UNDISCOVERED", x + cardWidth / 2, y + 96);
					DrawCenteredText(ctx, smallFont, "Requirements hidden", x + cardWidth / 2, y + 130);
					continue;
				}

				DrawHiddenClassIcon(
					ctx,
					definition.Id,
					x + cardWidth / 2,
					y + 34,
					34,
					0.94,
					0.78,
					1.0,
					1
				);

				DrawCenteredText(ctx, titleFont, definition.DisplayName,
					x + cardWidth / 2, y + 66);
				double cursorY = y + 98;
				cursorY += DrawWrappedText(ctx, smallFont, definition.Description,
					x + 14, cursorY, cardWidth - 28) + 8;

				List<string> requirementParts = definition.RequiredClasses.Select(classId =>
					skillConfig.Trees.TryGetValue(classId, out SkillTreeDefinition? tree)
						? tree.DisplayName
						: classId).ToList();
				if (definition.RequiredProfession != null)
				{
					requirementParts.Add("Profession: " + definition.RequiredProfession);
				}
				if (definition.AllowedRaces.Count > 0)
				{
					requirementParts.Add("Race: " + string.Join("/", definition.AllowedRaces));
				}
				if (definition.AllowedSubraces.Count > 0)
				{
					requirementParts.Add("Heritage: " + string.Join("/", definition.AllowedSubraces));
				}
				if (definition.MinimumCapstones > 0)
				{
					requirementParts.Add($"{definition.MinimumCapstones} Grandmasters");
				}
				string requirements = string.Join(" + ", requirementParts);
				cursorY += DrawWrappedText(ctx, tinyFont,
					"Discovered from: " + requirements,
					x + 14, cursorY, cardWidth - 28) + 8;

				foreach (SkillEffectDefinition effect in definition.Effects)
				{
					cursorY += DrawWrappedText(ctx, smallFont,
						"• " + FormatStatEffect(effect, effect.ValuePerRank),
						x + 14, cursorY, cardWidth - 28) + 5;
				}
			}

			if (pageCount > 1)
			{
				double buttonY = height - 45;
				DrawHiddenPageButton(ctx, startX, buttonY, 116, 30, "Previous", "previous", hiddenPage > 0);
				DrawCenteredText(ctx, smallFont,
					$"Page {hiddenPage + 1} / {pageCount}",
					width / 2,
					buttonY + 7);
				DrawHiddenPageButton(ctx, width - startX - 116, buttonY, 116, 30, "Next", "next", hiddenPage + 1 < pageCount);
			}
		}

		private void DrawHiddenPageButton(
			Context ctx,
			double x,
			double y,
			double width,
			double height,
			string label,
			string id,
			bool enabled)
		{
			RoundRectangle(ctx, x, y, width, height, 6);
			SetColor(ctx, enabled ? 0.24 : 0.08, enabled ? 0.16 : 0.08, enabled ? 0.30 : 0.10, 0.98);
			ctx.FillPreserve();
			SetColor(ctx, enabled ? 0.75 : 0.25, enabled ? 0.55 : 0.25, enabled ? 0.95 : 0.30, 0.9);
			ctx.LineWidth = 1.5;
			ctx.Stroke();
			DrawCenteredText(ctx, smallFont, label, x + width / 2, y + 6);
			if (enabled)
			{
				hitRegions.Add(new SkillTreeHitRegion(
					SkillTreeHitKind.HiddenPage,
					id,
					x,
					y,
					width,
					height
				));
			}
		}

		private static void DrawHiddenClassIcon(
			Context ctx,
			string hiddenClassId,
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
				ctx.Translate(centerX, centerY);
				ctx.Scale(size, size);
				ctx.LineWidth = 0.075;
				SetColor(ctx, r, g, b, a);

				switch (hiddenClassId)
				{
					case "lumberjack":
						DrawLogGlyph(ctx);
						DrawAxeGlyph(ctx);
						break;
					case "weaponmaster":
						DrawCrossedSwordsGlyph(ctx);
						DrawAnvilGlyph(ctx);
						break;
					case "shadowarcher":
						DrawEyeGlyph(ctx);
						StrokeLine(ctx, -0.34, 0.24, 0.34, -0.24);
						break;
					case "berserk":
						DrawHelmetGlyph(ctx);
						DrawFlameGlyph(ctx);
						break;
					case "deepwarden":
						DrawShieldGlyph(ctx);
						DrawPickaxeGlyph(ctx);
						break;
					case "wildheart":
						DrawHeartGlyph(ctx);
						DrawPawGlyph(ctx);
						break;
					case "grandartisan":
						DrawGearGlyph(ctx);
						DrawHammerGlyph(ctx);
						break;
					case "stormforged":
						DrawRangerGlyph(ctx);
						DrawBeeGlyph(ctx);
						break;
					default:
						DrawStarGlyph(ctx, false);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private static string FormatStatEffect(
			SkillEffectDefinition effect,
			double value)
		{
			string name = string.IsNullOrWhiteSpace(effect.Stat)
				? effect.Type
				: effect.Stat;

			if (effect.Type.Equals("MaxHealth", StringComparison.OrdinalIgnoreCase) ||
				effect.Type.Equals("TrackingRange", StringComparison.OrdinalIgnoreCase))
			{
				return $"{name}: +{value:0.##}";
			}

			if (effect.Type.Contains("Reduction", StringComparison.OrdinalIgnoreCase))
			{
				return $"{name}: -{value * 100:0.##}%";
			}

			if (effect.Type.Equals("CheatDeath", StringComparison.OrdinalIgnoreCase))
			{
				return $"{name}: restores {value * 100:0.##}% health, 48-hour cooldown";
			}

			if (effect.Type.Equals("ShadowArrowCrit", StringComparison.OrdinalIgnoreCase))
			{
				return $"{name}: {value * 100:0}%";
			}

			return $"{name}: +{value * 100:0.##}%";
		}
	}
}
