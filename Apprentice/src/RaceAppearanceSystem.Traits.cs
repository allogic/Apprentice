using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed partial class RaceAppearanceSystem
    {
        internal static string DescribeBodyEffects(
            RaceProfile profile,
            float height,
            float thickness)
        {
            BodyEffects effects = CalculateBodyEffects(profile, height, thickness);
            return string.Format(
                Lang.Get("apprentice:race-body-effects"),
                Signed(effects.Health, 1),
                SignedPercent(effects.Melee),
                SignedPercent(effects.Movement),
                SignedPercent(effects.Hunger),
                SignedPercent(effects.ToolSpeed),
                SignedPercent(effects.Yield)
            );
        }

        private static string DescribeBodyTrait(
            RaceProfile profile,
            float height,
            float thickness)
        {
            BodyEffects effects = CalculateBodyEffects(profile, height, thickness);
            List<string> parts = new();
            if (Math.Abs(effects.Health) >= 0.05f)
                parts.Add($"{Signed(effects.Health, 1)} HP");
            if (Math.Abs(effects.Melee) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Melee)} melee");
            if (Math.Abs(effects.Movement) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Movement)} move");
            if (Math.Abs(effects.Hunger) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Hunger)} hunger");

            if (Math.Abs(effects.ToolSpeed - effects.Yield) < 0.0005f &&
                Math.Abs(effects.ToolSpeed) >= 0.0005f)
            {
                parts.Add($"{SignedPercentCompact(effects.ToolSpeed)} tools/yield");
            }
            else
            {
                if (Math.Abs(effects.ToolSpeed) >= 0.0005f)
                    parts.Add($"{SignedPercentCompact(effects.ToolSpeed)} tools");
                if (Math.Abs(effects.Yield) >= 0.0005f)
                    parts.Add($"{SignedPercentCompact(effects.Yield)} yield");
            }

            if (parts.Count == 0) parts.Add("no change");
            return $"{BodyTraitFallback} ({string.Join(", ", parts)})";
        }

        private static string CompactTraitText(string text)
        {
            string compact = Regex.Replace(
                text,
                @"Animals detect you from ([+\-]?\d+(?:[\.,]\d+)?)% farther away",
                "Animal detection +$1%",
                RegexOptions.IgnoreCase
            );
            compact = Regex.Replace(
                compact,
                @"Animals detect you from ([+\-]?\d+(?:[\.,]\d+)?)% less distance",
                "Animal detection -$1%",
                RegexOptions.IgnoreCase
            );
            compact = Regex.Replace(
                compact,
                @"([+\-]?\d+(?:[\.,]\d+)?)% less armor slowdown",
                "-$1% armor slow",
                RegexOptions.IgnoreCase
            );

            return compact
                .Replace("health points", "HP", StringComparison.OrdinalIgnoreCase)
                .Replace("health point", "HP", StringComparison.OrdinalIgnoreCase)
                .Replace("ranged damage", "ranged dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("melee damage", "melee dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("mechanical damage", "mech dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("movement speed", "move", StringComparison.OrdinalIgnoreCase)
                .Replace("mining speed", "mining", StringComparison.OrdinalIgnoreCase)
                .Replace("ranged accuracy", "ranged acc.", StringComparison.OrdinalIgnoreCase)
                .Replace("temporal repair cost", "repair cost", StringComparison.OrdinalIgnoreCase)
                .Replace("hunger rate", "hunger", StringComparison.OrdinalIgnoreCase)
                .Replace("rusty-gear yield", "gear yield", StringComparison.OrdinalIgnoreCase)
                .Replace("forage yield", "forage", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceBodyTrait(string text, Entity entity)
        {
            int start = text.IndexOf(BodyTraitFallback, StringComparison.Ordinal);
            if (start < 0) return text;

            int lineEnd = text.IndexOf('\n', start);
            RaceProfile profile = GetProfile(entity);
            string bodyTrait = DescribeBodyTrait(
                profile,
                GetHeightChoice(entity),
                GetThicknessChoice(entity)
            );
            return lineEnd < 0
                ? text[..start] + bodyTrait
                : text[..start] + bodyTrait + text[lineEnd..];
        }

        private static string InsertSubclassTrait(string text, Entity entity)
        {
            RaceProfile profile = GetProfile(entity);
            if (!HasSubclasses(profile)) return text;

            const string color = "#84c7ff";
            SubclassProfile? subclass = GetSelectedSubclass(entity, profile);
            string line = subclass == null
                ? $"<font color=\"{color}\">• {Lang.Get("apprentice:select-subclass")}</font>"
                : $"<font color=\"{color}\">• {subclass.Name}</font> ({subclass.TraitSummary})";

            int bodyStart = text.IndexOf(BodyTraitFallback, StringComparison.Ordinal);
            if (bodyStart < 0) return text;
            int lineStart = text.LastIndexOf('\n', bodyStart);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            return text[..lineStart] + line + "\n" + text[lineStart..];
        }

        private static void AfterGetClassTraitText(ref string __result)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player != null)
            {
                __result = CompactTraitText(ReplaceBodyTrait(__result, player));
            }
        }

        private static void UpdateBodyTrait(object dialog)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null ||
                GetCharacterComposer(dialog) is not GuiComposer composer ||
                composer.GetRichtext("characterDesc") is not GuiElementRichtext richtext)
            {
                return;
            }

            if (AccessTools.Field(dialog.GetType(), "modSys")?.GetValue(dialog)
                    is not CharacterSystem characterSystem ||
                AccessTools.Method(characterSystem.GetType(), "getClassTraitText")
                    ?.Invoke(characterSystem, null) is not string traitText)
            {
                return;
            }

            string updated =
                Lang.Get("characterdesc-" + GetClassCode(player)) +
                "\n\n" + Lang.Get("traits-title") + "\n" +
                CompactTraitText(
                    InsertSubclassTrait(ReplaceBodyTrait(traitText, player), player)
                );
            // Use the compact Human-sized trait text for every race and
            // subclass. This keeps long subclass/body lines above the options.
            CairoFont font = CairoFont.WhiteDetailText();
            richtext.SetNewText(updated, font, null);
        }

        private static string Signed(float value, int decimals) =>
            value.ToString(value >= 0 ? "+0." + new string('0', decimals) : "0." + new string('0', decimals));

        private static string SignedPercent(float value) =>
            $"{value * 100:+0.0;-0.0;0.0}%";

        private static string SignedPercentCompact(float value)
        {
            float percent = value * 100;
            return Math.Abs(percent - MathF.Round(percent)) < 0.05f
                ? $"{percent:+0;-0;0}%"
                : $"{percent:+0.0;-0.0;0.0}%";
        }

        private static BodyEffects CalculateBodyEffects(
            RaceProfile profile,
            float height,
            float thickness)
        {
            float effectiveHeight = GetEffectiveHeight(profile, height);
            float sizeScore = effectiveHeight >= 1f
                ? (effectiveHeight - 1f) / (GlobalMaximumHeight - 1f)
                : (effectiveHeight - 1f) / (1f - GlobalMinimumHeight);
            sizeScore = Math.Clamp(sizeScore, -1f, 1f);

            float health = 4f * sizeScore;
            float melee = 0.10f * sizeScore;
            float movement = 0.04f * sizeScore;
            float hunger = sizeScore > 0 ? 0.20f * sizeScore : 0f;
            float toolSpeed = sizeScore < 0 ? 0.10f * -sizeScore : 0f;
            float yield = toolSpeed;

            float buildScore = (Math.Clamp(thickness, 0f, 1f) - 0.5f) * 2f;
            if (buildScore > 0)
            {
                hunger -= 0.10f * buildScore;
                movement -= 0.06f * buildScore;
            }
            else
            {
                hunger += 0.05f * -buildScore;
                movement += 0.03f * -buildScore;
            }

            return new BodyEffects(
                health,
                melee,
                movement,
                hunger,
                toolSpeed,
                yield
            );
        }

        private static void ApplyBodyStats(EntityPlayer player, RaceProfile profile)
        {
            BodyEffects effects = CalculateBodyEffects(
                profile,
                GetHeightChoice(player),
                GetThicknessChoice(player)
            );

            player.Stats.Set("maxhealthExtraPoints", BodyStatSource, effects.Health, false);
            player.Stats.Set("meleeWeaponsDamage", BodyStatSource, effects.Melee, false);
            player.Stats.Set("walkspeed", BodyStatSource, effects.Movement, false);
            player.Stats.Set("hungerrate", BodyStatSource, effects.Hunger, false);
            player.Stats.Set("miningSpeedMul", BodyStatSource, effects.ToolSpeed, false);
            player.Stats.Set("animalHarvestingTime", BodyStatSource, -effects.ToolSpeed, false);

            foreach (string stat in new[]
            {
                "forageDropRate", "oreDropRate", "wildCropDropRate",
                "vesselContentsDropRate", "rustyGearDropRate", "animalLootDropRate"
            })
            {
                player.Stats.Set(stat, BodyStatSource, effects.Yield, false);
            }

            ApplySubclassStats(player, profile);

            if (player.Api.Side == EnumAppSide.Server)
            {
                RefreshHealth(player);
            }
        }

        private static void ApplySubclassStats(EntityPlayer player, RaceProfile profile)
        {
            foreach (string stat in SubclassStatNames)
            {
                player.Stats.Set(stat, SubclassStatSource, 0f, false);
            }

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            if (subclass == null) return;
            foreach ((string stat, float value) in subclass.Stats)
            {
                player.Stats.Set(stat, SubclassStatSource, value, false);
            }
        }

        private static void RefreshHealth(EntityPlayer player)
        {
            IEnumerable<EntityBehavior> behaviors = player.ServerBehaviorsMainThread
                .Concat(player.ServerBehaviorsThreadsafe);
            foreach (EntityBehavior behavior in behaviors)
            {
                if (!behavior.GetType().Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    AccessTools.Method(behavior.GetType(), "MarkDirty")?.Invoke(behavior, null);
                }
                catch
                {
                    // A later join, race confirmation or skill refresh retries this.
                }
                return;
            }
        }

    }
}
