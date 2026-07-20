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
        private sealed class EyeHeightScaleState
        {
            public EyeHeightScaleState(
                EntityProperties properties,
                double eyeHeight,
                float collisionHeight)
            {
                Properties = properties;
                EyeHeight = eyeHeight;
                CollisionHeight = collisionHeight;
            }

            public EntityProperties Properties { get; }
            public double EyeHeight { get; }
            public float CollisionHeight { get; }

            public void Restore()
            {
                Properties.EyeHeight = EyeHeight;
                Properties.CollisionBoxSize.Y = CollisionHeight;
            }
        }

        internal static void ApplyRaceAppearance(EntityPlayer player, string classCode)
        {
            RaceProfile profile = GetProfile(classCode);
            ApplyPhysicalProportions(player, profile);

            EntityBehavior? skin = player.GetBehavior("skinnableplayer");
            if (skin == null) return;

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );

            if (!RestoreInProgress.Contains(player))
            {
                ApplyRaceSkin(player, skin, profile);
                ApplyFacialIdentity(player, skin, profile);
            }
            RefreshHiddenRaceParts(player, profile);
        }

        private static void RefreshHiddenRaceParts(
            EntityPlayer player,
            RaceProfile profile)
        {
            EntityBehavior? skin = player.GetBehavior("skinnableplayer");
            if (skin == null) return;

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );
            string hornColor = GetHornColorChoice(player);
            SelectHiddenPart(
                skin,
                FaceSkinPartCode,
                GetAppliedSkinPartCode(skin, "baseskin", "skin4")
            );
            SelectHiddenPart(skin, HornColorPartCode, hornColor);
            SelectHiddenPart(skin, HornPartCode, GetHornChoice(player, profile));
            SelectHiddenPart(skin, TeethPartCode, GetTeethChoice(player, profile));
            SelectHiddenPart(skin, SkinPartCode, profile.Code);
        }

        private static void RestoreAppearanceParts(
            EntityPlayer player,
            RaceProfile profile,
            Dictionary<string, string>? appearanceParts)
        {
            if (appearanceParts == null || appearanceParts.Count == 0 ||
                player.GetBehavior("skinnableplayer") is not EntityBehaviorExtraSkinnable skin)
            {
                return;
            }

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );
            if (selectSkinPartMethod == null) return;

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            string[] skinCodes = subclass?.SkinCodes ?? profile.SkinCodes;
            string[] eyeCodes = subclass?.EyeColors ?? profile.EyeCodes;
            string[] hairCodes = subclass?.HairColors ?? profile.HairCodes;

            foreach (string partCode in PersistentAppearancePartCodes)
            {
                if (!appearanceParts.TryGetValue(partCode, out string? value) ||
                    string.IsNullOrEmpty(value) ||
                    !skin.AvailableSkinPartsByCode.TryGetValue(
                        partCode,
                        out SkinnablePart? part
                    ) ||
                    !part.VariantsByCode.ContainsKey(value))
                {
                    continue;
                }

                if ((partCode == "baseskin" && !skinCodes.Contains(value, StringComparer.Ordinal)) ||
                    (partCode == "eyecolor" && !eyeCodes.Contains(value, StringComparer.Ordinal)) ||
                    (partCode == "haircolor" && !hairCodes.Contains(value, StringComparer.Ordinal)))
                {
                    continue;
                }

                selectSkinPartMethod.Invoke(
                    skin,
                    new object[] { partCode, value, true, false }
                );
            }
        }

        private static void SelectHiddenPart(
            EntityBehavior skin,
            string partCode,
            string variantCode)
        {
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, variantCode, true, false }
            );
        }

        private static string GetAppliedSkinPartCode(
            EntityBehavior skin,
            string partCode,
            string fallback)
        {
            if (skin is EntityBehaviorExtraSkinnable extra)
            {
                string? selected = extra.AppliedSkinParts
                    .FirstOrDefault(part => part.PartCode == partCode)?.Code;
                if (!string.IsNullOrEmpty(selected)) return selected;
            }
            return fallback;
        }

        private static void ApplyFacialIdentity(
            EntityPlayer player,
            EntityBehavior skin,
            RaceProfile profile)
        {
            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            SelectRacePart(player, skin, "facialexpression", profile.Face);
            if (subclass != null)
            {
                SelectAllowedRacePart(
                    player,
                    skin,
                    "eyecolor",
                    subclass.EyeColors
                );
                SelectAllowedRacePart(
                    player,
                    skin,
                    "haircolor",
                    subclass.HairColors
                );
            }
            else
            {
                SelectAllowedRacePart(player, skin, "eyecolor", profile.EyeCodes);
                SelectAllowedRacePart(player, skin, "haircolor", profile.HairCodes);
            }

            string defaultsKey = profile.Code + ":" + (subclass?.Code ?? "base");
            string appliedDefaults = player.WatchedAttributes.GetString(
                AppearanceDefaultsAttribute,
                ""
            );
            if (appliedDefaults != defaultsKey)
            {
                string? hairStyle = subclass?.HairStyle ?? profile.Hair;
                if (hairStyle != null)
                {
                    SelectRacePart(player, skin, "hairbase", hairStyle);
                }
                if (subclass != null)
                {
                    SelectRacePart(player, skin, "hairextra", subclass.HairExtra);
                }
                if (profile.Beard != null)
                {
                    SelectRacePart(player, skin, "beard", profile.Beard);
                }
                player.WatchedAttributes.SetString(
                    AppearanceDefaultsAttribute,
                    defaultsKey
                );
            }
        }

        private static void SelectAllowedRacePart(
            EntityPlayer player,
            EntityBehavior skin,
            string partCode,
            string[] allowed)
        {
            if (allowed.Length == 0) return;
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            string? current = appliedParts?.GetString(partCode, null);
            string selected = current != null && allowed.Contains(
                current,
                StringComparer.Ordinal
            ) ? current : allowed[0];
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, selected, true, false }
            );
        }

        private static void SelectRacePart(
            EntityPlayer player,
            EntityBehavior skin,
            string partCode,
            string raceValue)
        {
            string savedKey = "apprenticeOriginal-" + partCode;
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");

            if (!player.WatchedAttributes.HasAttribute(savedKey))
            {
                string? original = appliedParts?.GetString(partCode, null);
                if (original != null)
                {
                    player.WatchedAttributes.SetString(savedKey, original);
                }
            }

            string selected = profileIsHuman(player)
                ? player.WatchedAttributes.GetString(savedKey, raceValue)
                : raceValue;
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, selected, true, false }
            );
        }

        private static bool profileIsHuman(EntityPlayer player) =>
            GetClassCode(player) == "apprentice-race-human";

        private static void ApplyRaceSkin(
            EntityPlayer player,
            EntityBehavior skin,
            RaceProfile profile)
        {
            const string savedSkinKey = "apprenticeOriginalBaseSkin";
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            string currentSkin = appliedParts?.GetString("baseskin", "skin4") ?? "skin4";

            if (!player.WatchedAttributes.HasAttribute(savedSkinKey))
            {
                player.WatchedAttributes.SetString(savedSkinKey, currentSkin);
            }

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            string skinCode;
            if (subclass != null)
            {
                skinCode = subclass.SkinCodes.Contains(
                    currentSkin,
                    StringComparer.Ordinal
                ) ? currentSkin : subclass.SkinCodes[0];
            }
            else
            {
                string savedSkin = player.WatchedAttributes.GetString(
                    savedSkinKey,
                    profile.SkinCodes[0]
                );
                skinCode = profile.SkinCode ??
                    (profile.SkinCodes.Contains(savedSkin, StringComparer.Ordinal)
                        ? savedSkin
                        : profile.SkinCodes[0]);
            }
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { "baseskin", skinCode, true, false }
            );
        }

        private static void ApplyNaturalPalette()
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player?.GetBehavior("skinnableplayer") is not EntityBehaviorExtraSkinnable skin)
                return;

            RestoreNaturalPalette(skin);
            RaceProfile profile = GetProfile(player);
            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            List<PaletteSnapshot> snapshots = new();
            FilterPalettePart(
                skin,
                "baseskin",
                subclass == null ? profile.SkinCodes : subclass.SkinCodes,
                snapshots
            );
            FilterPalettePart(
                skin,
                "eyecolor",
                subclass == null ? profile.EyeCodes : subclass.EyeColors,
                snapshots
            );
            FilterPalettePart(
                skin,
                "haircolor",
                subclass == null ? profile.HairCodes : subclass.HairColors,
                snapshots
            );
            PaletteSnapshots[skin] = snapshots;
        }

        private static void FilterPalettePart(
            EntityBehaviorExtraSkinnable skin,
            string partCode,
            string[] allowed,
            List<PaletteSnapshot> snapshots)
        {
            if (!skin.AvailableSkinPartsByCode.TryGetValue(partCode, out SkinnablePart? part))
                return;

            snapshots.Add(new PaletteSnapshot(part, part.Variants, part.VariantsByCode));
            HashSet<string> accepted = new(allowed, StringComparer.Ordinal);
            part.Variants = part.Variants
                .Where(variant => accepted.Contains(variant.Code))
                .GroupBy(variant => variant.Code, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            part.VariantsByCode = part.Variants.ToDictionary(
                variant => variant.Code,
                StringComparer.Ordinal
            );
        }

        private static void RestoreNaturalPalette()
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player?.GetBehavior("skinnableplayer") is EntityBehaviorExtraSkinnable skin)
            {
                RestoreNaturalPalette(skin);
            }
        }

        private static void RestoreNaturalPalette(EntityBehaviorExtraSkinnable skin)
        {
            if (!PaletteSnapshots.Remove(skin, out List<PaletteSnapshot>? snapshots))
                return;
            foreach (PaletteSnapshot snapshot in snapshots)
            {
                snapshot.Part.Variants = snapshot.Variants;
                snapshot.Part.VariantsByCode = snapshot.VariantsByCode;
            }
        }

        private static void ApplyPhysicalProportions(EntityPlayer player, RaceProfile profile)
        {
            float width = GetEffectiveWidth(profile, GetThicknessChoice(player));
            float height = GetEffectiveHeight(profile, GetHeightChoice(player));
            player.SetCollisionBox(0.6f * width, 1.85f * height);
            player.SetSelectionBox(0.6f * width, 1.85f * height);
            player.LocalEyePos.Y = 1.7 * height;
        }

        private static void BeforePlayerEyeHeightUpdate(
            EntityPlayer __instance,
            out EyeHeightScaleState? __state)
        {
            __state = null;
            EntityPlayer? localPlayer = clientApi?.World.Player?.Entity;
            if (!ReferenceEquals(localPlayer, __instance)) return;

            float heightScale = GetEffectiveHeight(
                GetProfile(__instance),
                GetHeightChoice(__instance)
            );
            if (!float.IsFinite(heightScale) ||
                heightScale <= 0f ||
                Math.Abs(heightScale - 1f) < 0.0001f)
            {
                return;
            }

            EntityProperties properties = __instance.Properties;
            __state = new EyeHeightScaleState(
                properties,
                properties.EyeHeight,
                properties.CollisionBoxSize.Y
            );

            // EntityPlayer.updateEyeHeight normally eases these fixed player
            // dimensions toward 1.7/1.85 every frame. Present scaled base
            // dimensions only for that calculation so all vanilla pose,
            // mount and head-bobbing adjustments remain intact.
            properties.EyeHeight *= heightScale;
            properties.CollisionBoxSize.Y *= heightScale;
        }

        private static Exception? FinalizePlayerEyeHeightUpdate(
            EyeHeightScaleState? __state,
            Exception? __exception)
        {
            __state?.Restore();
            return __exception;
        }

        private static float GetEffectiveHeight(RaceProfile profile, float choice) =>
            profile.Height * Lerp(
                profile.MinHeightScale,
                profile.MaxHeightScale,
                Math.Clamp(choice, 0f, 1f)
            );

        private static float GetEffectiveWidth(RaceProfile profile, float choice) =>
            profile.Width * Lerp(0.86f, 1.14f, Math.Clamp(choice, 0f, 1f));

        private static float Lerp(float min, float max, float value) =>
            min + (max - min) * value;

        private static float GetChosenHeightScale(RaceProfile profile, Entity entity) =>
            Lerp(profile.MinHeightScale, profile.MaxHeightScale, GetHeightChoice(entity));

        private static float GetMinimumPreviewZoom(RaceProfile profile, Entity entity) =>
            profile.MinPreviewZoom / GetChosenHeightScale(profile, entity);

        private static float GetMaximumPreviewZoom(RaceProfile profile, Entity entity) =>
            profile.MaxPreviewZoom / GetChosenHeightScale(profile, entity);

        private static void ResetDialogZoom(object dialog)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            if (player == null || zoomField?.FieldType != typeof(float)) return;
            RaceProfile profile = GetProfile(player);
            zoomField.SetValue(dialog, GetMinimumPreviewZoom(profile, player));
        }

        private static int GetDialogTab(object dialog)
        {
            FieldInfo? tabField = AccessTools.Field(dialog.GetType(), "curTab");
            return tabField?.GetValue(dialog) is int tab ? tab : 1;
        }

        private static void SetDialogTab(object dialog, int tab)
        {
            AccessTools.Field(dialog.GetType(), "curTab")?.SetValue(dialog, tab);
        }

        private static void AfterPlayerModelMatrix(object __instance, Entity entity) =>
            ApplyRenderScale(__instance, entity);

        private static void AfterGuiModelMatrix(object __instance, Entity entity) =>
            ApplyRenderScale(__instance, entity);

        private static void ApplyRenderScale(object renderer, Entity entity)
        {
            RaceProfile profile = GetProfile(entity);
            FieldInfo? modelMatField = AccessTools.Field(renderer.GetType(), "ModelMat");
            if (modelMatField?.GetValue(renderer) is not float[] modelMat) return;

            float width = GetEffectiveWidth(profile, GetThicknessChoice(entity));
            float height = GetEffectiveHeight(profile, GetHeightChoice(entity));
            Mat4f.Scale(modelMat, modelMat, new[] { width, height, width });
        }

        private static string GetClassCode(Entity entity) =>
            entity.WatchedAttributes.GetString(
                "characterClass",
                "apprentice-race-human"
            );

        private static RaceProfile GetProfile(Entity entity) =>
            GetProfile(GetClassCode(entity));

        private static RaceProfile GetProfile(string classCode) =>
            RaceByClass.TryGetValue(classCode, out RaceProfile? profile)
                ? profile
                : HumanProfile;

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.PlayerJoin -= OnPlayerJoin;
                serverApi.Event.PlayerReady -= OnPlayerReady;
            }
            RestoreNaturalPalette();
            DestroyOptionsDialog();
            RestoreAllCharacterDialogLayouts();
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            clientApi = null;
            serverApi = null;
            clientChannel = null;
            serverChannel = null;
            activeCharacterDialog = null;
            pendingSkinConfirmDialog = null;
            pendingSkinConfirmRequestId = 0;
            skinCloseRequested = false;
            ConfirmedRaceDialogs.Clear();
            ApprovedCharacterDialogClosures.Clear();
            NativeFinalCharacterConfirmCallbacks.Clear();
            RestoreInProgress.Clear();
            PaletteSnapshots.Clear();
            base.Dispose();
        }
    }
}
