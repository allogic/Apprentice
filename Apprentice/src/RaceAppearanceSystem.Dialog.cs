using System;
using System.Collections;
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
        private const string HornColorPickerKey = "apprentice-horn-color";

        private static readonly int[] HornSwatchColors =
        {
            ColorUtil.ToRgba(255, 60, 65, 70),
            ColorUtil.ToRgba(255, 167, 173, 178),
            ColorUtil.ToRgba(255, 231, 230, 224),
            ColorUtil.ToRgba(255, 231, 177, 188),
            ColorUtil.ToRgba(255, 227, 215, 172)
        };

        private sealed class CharacterComposeState
        {
            public FieldInfo? ModSystemField { get; set; }
            public object? ModSystem { get; set; }
            public bool Modified { get; set; }

            public void Restore(object dialog)
            {
                if (!Modified) return;
                ModSystemField?.SetValue(dialog, ModSystem);
            }
        }

        private sealed class CharacterDialogLayoutState
        {
            public Dictionary<SkinnablePart, bool> HiddenParts { get; } = new();
        }

        private static readonly Dictionary<object, CharacterDialogLayoutState>
            CharacterDialogLayouts = new();

        private static void BeforeHorizontalTabsConstructed(ref GuiTab[] tabs)
        {
            if (tabs.Length != 2 ||
                !tabs.Any(tab => tab.DataInt == 0) ||
                !tabs.Any(tab => tab.DataInt == 1))
            {
                return;
            }

            GuiTab? skin = tabs.FirstOrDefault(tab => tab.DataInt == 0);
            GuiTab? race = tabs.FirstOrDefault(tab => tab.DataInt == 1);
            if (skin == null || race == null) return;

            race.Name = Lang.Get("apprentice:tab-race");
            skin.Name = Lang.Get("apprentice:tab-skinvoice");
            tabs = new[] { race, skin };
        }

        private static bool BeforeCharacterSkinButtonAdded(
            GuiComposer __0,
            string __1,
            ref GuiComposer __result)
        {
            if (activeCharacterDialog == null ||
                GetDialogTab(activeCharacterDialog) != 0 ||
                !ReferenceEquals(GetCharacterComposer(activeCharacterDialog), __0) ||
                (__1 != Lang.Get("Randomize") &&
                 __1 != Lang.Get("Last selection")))
            {
                return true;
            }

            __result = __0;
            return false;
        }

        private static void BeforeDialogOpened(object __instance)
        {
            activeCharacterDialog = __instance;
            HookCharacterBeforeCompose(__instance);
        }

        private static void HookCharacterBeforeCompose(object dialog)
        {
            if (CharacterDialogLayouts.ContainsKey(dialog)) return;

            FieldInfo? field = AccessTools.Field(dialog.GetType(), "onBeforeCompose");
            if (field == null) return;

            CharacterDialogLayoutState state = new();
            if (clientApi?.World.Player?.Entity.GetBehavior("skinnableplayer")
                    is EntityBehaviorExtraSkinnable skin)
            {
                foreach (string partCode in new[] { "haircolor" })
                {
                    if (!skin.AvailableSkinPartsByCode.TryGetValue(
                            partCode,
                            out SkinnablePart? part))
                    {
                        continue;
                    }
                    state.HiddenParts[part] = part.Hidden;
                    part.Hidden = true;
                }
            }

            Action<GuiComposer>? original = field.GetValue(dialog) as Action<GuiComposer>;
            field.SetValue(
                dialog,
                (Action<GuiComposer>)(composer =>
                {
                    original?.Invoke(composer);
                    ComposeSkinColumnControls(composer);
                })
            );
            CharacterDialogLayouts[dialog] = state;
        }

        private static void RestoreCharacterDialogLayout(object dialog)
        {
            if (!CharacterDialogLayouts.Remove(
                    dialog,
                    out CharacterDialogLayoutState? state))
            {
                return;
            }
            foreach (KeyValuePair<SkinnablePart, bool> entry in state.HiddenParts)
            {
                entry.Key.Hidden = entry.Value;
            }
        }

        private static void RestoreAllCharacterDialogLayouts()
        {
            foreach (CharacterDialogLayoutState state in CharacterDialogLayouts.Values)
            {
                foreach (KeyValuePair<SkinnablePart, bool> entry in state.HiddenParts)
                {
                    entry.Key.Hidden = entry.Value;
                }
            }
            CharacterDialogLayouts.Clear();
        }

        private static void AfterDialogOpened(object __instance)
        {
            if (!ReferenceEquals(activeCharacterDialog, __instance))
            {
                pendingSkinConfirmDialog = null;
                pendingSkinConfirmRequestId = 0;
                skinCloseRequested = false;
            }
            activeCharacterDialog = __instance;
            HookCharacterBeforeCompose(__instance);
            ConfirmedRaceDialogs.Remove(__instance);
            SetDialogTab(__instance, 1);
            ResetDialogZoom(__instance);
            AccessTools.Method(__instance.GetType(), "ComposeGuis")?.Invoke(__instance, null);
            FixActiveTab(__instance);
        }

        private static void AfterDialogClosed(object __instance)
        {
            RestoreCharacterDialogLayout(__instance);
            ConfirmedRaceDialogs.Remove(__instance);
            if (ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                pendingSkinConfirmDialog = null;
                pendingSkinConfirmRequestId = 0;
                skinCloseRequested = false;
            }
            if (ReferenceEquals(activeCharacterDialog, __instance))
            {
                RestoreNaturalPalette();
                activeCharacterDialog = null;
                DestroyOptionsDialog();
            }
        }

        private static void AfterRaceChanged(object __instance)
        {
            activeCharacterDialog = __instance;
            ConfirmedRaceDialogs.Remove(__instance);
            ResetDialogZoom(__instance);
            UpdateBodyTrait(__instance);
            RefreshOptionsDialog(__instance);
        }

        private static bool BeforeTabClicked(object __instance, int tabid)
        {
            // GuiElementHorizontalTabs.SetValue already translates the visual
            // array index to GuiTab.DataInt before invoking this callback.
            if (tabid == 0 && !ConfirmedRaceDialogs.Contains(__instance))
            {
                clientApi?.TriggerIngameError(
                    typeof(RaceAppearanceSystem),
                    "race-first",
                    Lang.Get("apprentice:confirm-race-first")
                );
                return false;
            }
            return true;
        }

        private static void AfterTabClicked(object __instance)
        {
            activeCharacterDialog = __instance;
            RefreshOptionsDialog(__instance);
        }

        private static bool BeforeSkinConfirmNext(
            object __instance,
            ref bool __result)
        {
            if (GetDialogTab(__instance) != 0 ||
                !ConfirmedRaceDialogs.Contains(__instance))
            {
                return true;
            }

            if (!skinCloseRequested ||
                !ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                object dialog = __instance;
                pendingSkinConfirmDialog = dialog;
                pendingSkinConfirmRequestId = 0;
                skinCloseRequested = true;
                clientApi?.Event.EnqueueMainThreadTask(
                    () =>
                    {
                        if (!ReferenceEquals(activeCharacterDialog, dialog) ||
                            !ReferenceEquals(pendingSkinConfirmDialog, dialog) ||
                            GetDialogTab(dialog) != 0 ||
                            !ConfirmedRaceDialogs.Contains(dialog))
                        {
                            return;
                        }

                        EntityPlayer? player = clientApi?.World.Player?.Entity;
                        long requestId = player == null ? 0 : SendBodyPacket(player);
                        if (requestId <= 0)
                        {
                            AbortPendingSkinConfirmation(
                                dialog,
                                Lang.Get("apprentice:race-save-unavailable")
                            );
                            return;
                        }

                        pendingSkinConfirmRequestId = requestId;
                        clientApi?.Event.RegisterCallback(
                            _ =>
                            {
                                if (ReferenceEquals(pendingSkinConfirmDialog, dialog) &&
                                    pendingSkinConfirmRequestId == requestId)
                                {
                                    AbortPendingSkinConfirmation(
                                        dialog,
                                        Lang.Get("apprentice:race-save-timeout")
                                    );
                                }
                            },
                            8000
                        );
                    },
                    "apprentice-confirm-skin"
                );
            }
            __result = true;
            return false;
        }

        private static void OnRaceSaveResult(RaceSaveResultPacket result)
        {
            clientApi?.Event.EnqueueMainThreadTask(
                () => HandleRaceSaveResult(result),
                "apprentice-handle-race-save-result"
            );
        }

        private static void HandleRaceSaveResult(RaceSaveResultPacket result)
        {
            object? dialog = pendingSkinConfirmDialog;
            if (dialog == null ||
                result.RequestId <= 0 ||
                result.RequestId != pendingSkinConfirmRequestId)
            {
                return;
            }

            if (!result.Success)
            {
                string message = string.IsNullOrWhiteSpace(result.Error)
                    ? Lang.Get("apprentice:race-save-rejected")
                    : result.Error;
                AbortPendingSkinConfirmation(dialog, message);
                return;
            }

            CompletePendingSkinConfirmation(dialog, result.RequestId);
        }

        private static void CompletePendingSkinConfirmation(
            object dialog,
            long requestId)
        {
            if (!ReferenceEquals(activeCharacterDialog, dialog) ||
                !ReferenceEquals(pendingSkinConfirmDialog, dialog) ||
                pendingSkinConfirmRequestId != requestId ||
                !skinCloseRequested)
            {
                return;
            }

            AccessTools.Field(dialog.GetType(), "didSelect")?.SetValue(dialog, true);
            if (dialog is not GuiDialog guiDialog)
            {
                AfterDialogClosed(dialog);
                return;
            }

            bool wasOpened = guiDialog.IsOpened();
            bool closed = !wasOpened || guiDialog.TryClose();
            if (!closed)
            {
                clientApi?.Logger.Warning(
                    "[Apprentice] Character customization was saved, but TryClose() refused; forcing the close lifecycle and deregistration."
                );
                guiDialog.OnGuiClosed();
            }

            ForceReleaseCharacterDialog(guiDialog);
            AfterDialogClosed(dialog);
        }

        private static void ForceReleaseCharacterDialog(GuiDialog dialog)
        {
            dialog.UnFocus();
            foreach (GuiComposer composer in dialog.Composers.Values)
            {
                composer.UnfocusOwnElements();
            }

            object? world = clientApi?.World;
            MethodInfo? unregister = world == null
                ? null
                : AccessTools.Method(
                    world.GetType(),
                    "UnregisterDialog",
                    new[] { typeof(GuiDialog) }
                );
            unregister?.Invoke(world, new object[] { dialog });

            // The public GUI lists are the authoritative input/render chains.
            // Remove directly as well so cleanup does not depend on a private
            // ClientMain method name or a dialog's close-state bookkeeping.
            clientApi?.Gui.LoadedGuis.Remove(dialog);
            clientApi?.Gui.OpenedGuis.Remove(dialog);

            dialog.ClearComposers();
            dialog.Dispose();
        }

        private static void BeforeEscapeMenuOpened()
        {
            ICoreClientAPI? capi = clientApi;
            if (capi == null) return;

            GuiDialog[] staleDialogs = capi.Gui.LoadedGuis
                .Where(dialog =>
                {
                    string name = dialog.GetType().Name;
                    return name == "GuiDialogCreateCharacter" ||
                        name == "RaceOptionsDialog" ||
                        name == "HornColorDialog";
                })
                .ToArray();

            foreach (GuiDialog dialog in staleDialogs)
            {
                if (!capi.Gui.LoadedGuis.Contains(dialog) &&
                    !capi.Gui.OpenedGuis.Contains(dialog))
                {
                    continue;
                }
                if (dialog.IsOpened()) dialog.TryClose();
                ForceReleaseCharacterDialog(dialog);
            }

            if (staleDialogs.Length > 0)
            {
                activeCharacterDialog = null;
                pendingSkinConfirmDialog = null;
                pendingSkinConfirmRequestId = 0;
                skinCloseRequested = false;
                optionsDialog = null;
                clientApi.Logger.Warning(
                    "[Apprentice] Removed {0} stale character dialog(s) before opening the pause menu.",
                    staleDialogs.Length
                );
            }
        }

        private static void AbortPendingSkinConfirmation(
            object dialog,
            string message)
        {
            if (!ReferenceEquals(pendingSkinConfirmDialog, dialog)) return;

            pendingSkinConfirmDialog = null;
            pendingSkinConfirmRequestId = 0;
            skinCloseRequested = false;
            DestroyOptionsDialog();
            clientApi?.TriggerIngameError(
                typeof(RaceAppearanceSystem),
                "race-save-failed",
                message
            );
        }

        private static bool BeforeDialogConfirm(object __instance, ref bool __result)
        {
            if (GetDialogTab(__instance) != 1) return true;

            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player != null)
            {
                RaceProfile profile = GetProfile(player);
                if (HasSubclasses(profile) && GetSelectedSubclass(player, profile) == null)
                {
                    clientApi?.TriggerIngameError(
                        typeof(RaceAppearanceSystem),
                        "subclass-required",
                        Lang.Get("apprentice:select-subclass-first")
                    );
                    __result = false;
                    return false;
                }
                if (GetProfessionChoice(player) == "select")
                {
                    clientApi?.TriggerIngameError(
                        typeof(RaceAppearanceSystem),
                        "profession-required",
                        Lang.Get("apprentice:select-profession-first")
                    );
                    __result = false;
                    return false;
                }
            }

            ConfirmedRaceDialogs.Add(__instance);
            if (player != null)
            {
                SendBodyPacket(player);
            }
            SetDialogTab(__instance, 0);
            AccessTools.Method(__instance.GetType(), "ComposeGuis")?.Invoke(__instance, null);
            HideOptionsDialog();
            __result = true;
            return false;
        }

        private static void BeforeComposeGuis(
            object __instance,
            out CharacterComposeState __state)
        {
            activeCharacterDialog = __instance;
            __state = new CharacterComposeState();
            if (GetDialogTab(__instance) == 0)
            {
                ApplyNaturalPalette();

                // Vanilla gates both utility buttons on modSys != null. Hide it
                // only for the synchronous Skin & Voice composition, then
                // restore it immediately in the postfix.
                FieldInfo? modSystemField = AccessTools.Field(
                    __instance.GetType(),
                    "modSys"
                );
                object? modSystem = modSystemField?.GetValue(__instance);
                __state = new CharacterComposeState
                {
                    ModSystemField = modSystemField,
                    ModSystem = modSystem,
                    Modified = true
                };
                modSystemField?.SetValue(__instance, null);
            }
            else
            {
                RestoreNaturalPalette();
            }
        }

        private static void AfterComposeGuis(
            object __instance,
            CharacterComposeState __state)
        {
            __state.Restore(__instance);
            FixActiveTab(__instance);
            UpdateBodyTrait(__instance);
            RefreshOptionsDialog(__instance);
        }

        private static Exception? FinalizeComposeGuis(
            object __instance,
            CharacterComposeState __state,
            Exception? __exception)
        {
            __state?.Restore(__instance);
            return __exception;
        }

        private static void ComposeSkinColumnControls(GuiComposer composer)
        {
            object? dialog = activeCharacterDialog;
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (dialog == null ||
                player == null ||
                GetDialogTab(dialog) != 0 ||
                !ConfirmedRaceDialogs.Contains(dialog) ||
                player.GetBehavior("skinnableplayer")
                    is not EntityBehaviorExtraSkinnable skin ||
                !skin.AvailableSkinPartsByCode.TryGetValue(
                    "eyecolor",
                    out SkinnablePart? eyePart) ||
                !skin.AvailableSkinPartsByCode.TryGetValue(
                    "haircolor",
                    out SkinnablePart? hairPart) ||
                !TryGetPickerAnchor(
                    composer,
                    "picker-eyecolor",
                    eyePart.Variants.Length,
                    out double left,
                    out double eyeBottom))
            {
                return;
            }

            SkinnablePartVariant[] hairVariants = hairPart.Variants.ToArray();
            if (hairVariants.Length == 0) return;

            double nextY = eyeBottom + 8;
            RaceProfile profile = GetProfile(player);
            bool hasHornColor = profile.HornCodes.Any(code => code != "none");
            ShiftVanillaSkinColumn(
                composer,
                left,
                eyeBottom,
                hasHornColor ? 120 : 60
            );

            string selectedHair = GetAppliedSkinPartCode(
                skin,
                "haircolor",
                hairVariants[0].Code
            );
            int hairIndex = Math.Max(
                0,
                Array.FindIndex(
                    hairVariants,
                    variant => variant.Code == selectedHair
                )
            );
            ElementBounds hairLabel = ElementBounds.Fixed(left, nextY, 210, 22);
            ElementBounds hairPicker = hairLabel.BelowCopy().WithFixedSize(22, 22);
            composer
                .AddRichtext(
                    Lang.Get("skinpart-haircolor"),
                    CairoFont.WhiteSmallText(),
                    hairLabel,
                    "apprentice-hair-color-label"
                )
                .AddColorListPicker(
                    hairVariants.Select(variant => variant.Color).ToArray(),
                    index => OnCustomSkinColorChanged(
                        skin,
                        "haircolor",
                        hairVariants,
                        index
                    ),
                    hairPicker,
                    180,
                    "picker-haircolor"
                );
            AddColorTooltips(composer, "picker-haircolor", hairVariants);
            composer.ColorListPickerSetValue("picker-haircolor", hairIndex);
            nextY = hairPicker.fixedY + hairPicker.fixedHeight + 8;

            if (hasHornColor)
            {
                string[] hornCodes = GetHornColorCodes();
                string[] hornNames = GetHornColorNames();
                int hornIndex = Math.Max(
                    0,
                    Array.IndexOf(hornCodes, GetHornColorChoice(player))
                );
                ElementBounds hornLabel = ElementBounds.Fixed(left, nextY, 210, 22);
                ElementBounds hornPicker = hornLabel.BelowCopy().WithFixedSize(22, 22);
                composer
                    .AddRichtext(
                        Lang.Get("apprentice:race-horn-color"),
                        CairoFont.WhiteSmallText(),
                        hornLabel,
                        "apprentice-horn-color-label"
                    )
                    .AddColorListPicker(
                        HornSwatchColors,
                        OnHornColorPickerChanged,
                        hornPicker,
                        180,
                        HornColorPickerKey
                    );
                for (int index = 0; index < hornCodes.Length; index++)
                {
                    GuiElementColorListPicker picker = composer.GetColorListPicker(
                        HornColorPickerKey + "-" + index
                    );
                    picker.ShowToolTip = true;
                    picker.TooltipText = hornNames[index];
                }
                composer.ColorListPickerSetValue(HornColorPickerKey, hornIndex);
            }
        }

        private static void ShiftVanillaSkinColumn(
            GuiComposer composer,
            double left,
            double eyeBottom,
            double offset)
        {
            HashSet<GuiElement> shifted = new();
            foreach (GuiElement element in EnumerateComposerElements(composer))
            {
                if (!shifted.Add(element) ||
                    (element is not GuiElementRichtext &&
                     element is not GuiElementDropDown))
                {
                    continue;
                }

                ElementBounds bounds = element.Bounds;
                if (bounds.fixedX < left - 2 ||
                    bounds.fixedX > left + 210 ||
                    bounds.fixedY <= eyeBottom + 1 ||
                    bounds.fixedY >= 430)
                {
                    continue;
                }
                bounds.fixedY += offset;
            }
        }

        private static IEnumerable<GuiElement> EnumerateComposerElements(
            GuiComposer composer)
        {
            foreach (string fieldName in new[]
            {
                "staticElements", "interactiveElements"
            })
            {
                object? collection = AccessTools.Field(
                    composer.GetType(),
                    fieldName
                )?.GetValue(composer);
                if (collection is not IEnumerable enumerable) continue;

                foreach (object? item in enumerable)
                {
                    if (item is GuiElement element)
                    {
                        yield return element;
                        continue;
                    }
                    object? value = item == null
                        ? null
                        : AccessTools.Property(item.GetType(), "Value")
                            ?.GetValue(item);
                    if (value is GuiElement keyedElement)
                    {
                        yield return keyedElement;
                    }
                }
            }
        }

        private static void AddColorTooltips(
            GuiComposer composer,
            string key,
            SkinnablePartVariant[] variants)
        {
            for (int index = 0; index < variants.Length; index++)
            {
                GuiElementColorListPicker picker = composer.GetColorListPicker(
                    key + "-" + index
                );
                picker.ShowToolTip = true;
                picker.TooltipText = Lang.Get("color-" + variants[index].Code);
            }
        }

        private static void OnCustomSkinColorChanged(
            EntityBehaviorExtraSkinnable skin,
            string partCode,
            SkinnablePartVariant[] variants,
            int index)
        {
            if (index < 0 || index >= variants.Length) return;
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, variants[index].Code, true, false }
            );
        }

        private static bool TryGetPickerAnchor(
            GuiComposer composer,
            string key,
            int count,
            out double left,
            out double bottom)
        {
            left = 0;
            bottom = 0;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                GuiElementColorListPicker? picker = composer.GetColorListPicker(
                    key + "-" + index
                );
                if (picker == null) continue;
                if (!found)
                {
                    left = picker.Bounds.fixedX;
                    found = true;
                }
                bottom = Math.Max(
                    bottom,
                    picker.Bounds.fixedY + picker.Bounds.fixedHeight
                );
            }
            return found;
        }

        private static void OnHornColorPickerChanged(int index)
        {
            string[] codes = GetHornColorCodes();
            if (index < 0 || index >= codes.Length) return;
            OnSkinHornColorChanged(codes[index]);
        }

        private static GuiComposer? GetCharacterComposer(object dialog)
        {
            if (dialog is GuiDialog guiDialog)
            {
                if (guiDialog.Composers.ContainsKey("createcharacter"))
                {
                    return guiDialog.Composers["createcharacter"];
                }

                GuiComposer? matchingComposer = guiDialog.Composers.Values.FirstOrDefault(
                    composer => composer.GetHorizontalTabs("tabs") != null ||
                        composer.GetRichtext("characterDesc") != null
                );
                if (matchingComposer != null)
                {
                    return matchingComposer;
                }
            }

            return AccessTools.Property(dialog.GetType(), "SingleComposer")
                ?.GetValue(dialog) as GuiComposer;
        }

        private static void FixActiveTab(object dialog)
        {
            if (GetCharacterComposer(dialog) is not GuiComposer composer ||
                composer.GetHorizontalTabs("tabs") is not GuiElementHorizontalTabs tabsElement)
            {
                return;
            }

            int tabId = GetDialogTab(dialog);
            int index = Array.FindIndex(tabsElement.tabs, tab => tab.DataInt == tabId);
            if (index >= 0)
            {
                // activeElement is an array index, while curTab stores GuiTab.DataInt.
                // Reordering Race before Skin & Voice means those values are no
                // longer interchangeable.
                tabsElement.SetValue(index, false);
            }
        }

        private static void RefreshOptionsDialog(object dialog)
        {
            ICoreClientAPI? capi = clientApi;
            EntityPlayer? player = capi?.World.Player?.Entity;
            if (capi == null || player == null || GetDialogTab(dialog) != 1)
            {
                HideOptionsDialog();
                return;
            }

            RaceProfile profile = GetProfile(player);
            FieldInfo? heightField = AccessTools.Field(dialog.GetType(), "dlgHeight");
            int dialogHeight = heightField?.GetValue(dialog) is int value ? value : 500;
            double traitBottom = GetTraitBottom(dialog);

            if (optionsDialog == null)
            {
                optionsDialog = new RaceOptionsDialog(
                    capi,
                    dialogHeight,
                    profile,
                    traitBottom,
                    OnBodyOptionsChanged
                );
            }
            else
            {
                optionsDialog.Refresh(profile, traitBottom);
            }

            if (!optionsDialog.IsOpened())
            {
                optionsDialog.TryOpen();
            }
        }

        private static double GetTraitBottom(object dialog)
        {
            if (GetCharacterComposer(dialog)?.GetRichtext("characterDesc")
                    is not GuiElementRichtext richtext)
            {
                return 190;
            }

            double height = Math.Max(
                richtext.Bounds.fixedHeight,
                richtext.TotalHeight
            );
            return richtext.Bounds.fixedY + height;
        }

        private static void HideOptionsDialog()
        {
            if (optionsDialog?.IsOpened() == true)
            {
                optionsDialog.TryClose();
            }
        }

        private static void DestroyOptionsDialog()
        {
            if (optionsDialog != null)
            {
                if (optionsDialog.IsOpened()) optionsDialog.TryClose();
                ForceReleaseCharacterDialog(optionsDialog);
            }
            optionsDialog = null;
        }

        private static void OnSkinHornColorChanged(string hornColor)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null) return;

            RaceProfile profile = GetProfile(player);
            SaveBodyChoices(
                player,
                profile,
                GetHeightChoice(player),
                GetThicknessChoice(player),
                GetHornChoice(player, profile),
                GetTeethChoice(player, profile),
                GetSubclassChoice(player, profile),
                GetProfessionChoice(player),
                hornColor
            );
            player.WatchedAttributes.MarkAllDirty();
            ApplyRaceAppearance(player, GetClassCode(player));
        }

        private static void OnBodyOptionsChanged(
            float height,
            float thickness,
            string horns,
            string teeth,
            string subclass,
            string profession)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null) return;

            RaceProfile profile = GetProfile(player);
            SaveBodyChoices(
                player,
                profile,
                height,
                thickness,
                horns,
                teeth,
                subclass,
                profession,
                GetHornColorChoice(player)
            );
            player.WatchedAttributes.MarkAllDirty();
            ApplyRaceAppearance(player, GetClassCode(player));
            ApplyBodyStats(player, profile);
            if (activeCharacterDialog != null)
            {
                ConfirmedRaceDialogs.Remove(activeCharacterDialog);
                ResetDialogZoom(activeCharacterDialog);
                UpdateBodyTrait(activeCharacterDialog);
                RefreshOptionsDialog(activeCharacterDialog);
            }
        }

        private static bool BeforeGuiComposerMouseWheel(MouseWheelEventArgs __0)
        {
            object? dialog = activeCharacterDialog;
            ICoreClientAPI? capi = clientApi;
            if (dialog == null || capi == null) return true;

            FieldInfo? boundsField = AccessTools.Field(
                dialog.GetType(),
                "insetSlotBounds"
            );
            if (boundsField?.GetValue(dialog) is not ElementBounds bounds ||
                !bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            {
                return true;
            }

            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            EntityPlayer? player = capi.World.Player?.Entity;
            if (zoomField?.FieldType != typeof(float) || player == null) return true;

            RaceProfile profile = GetProfile(player);
            float minZoom = GetMinimumPreviewZoom(profile, player);
            float maxZoom = GetMaximumPreviewZoom(profile, player);
            float current = (float)zoomField.GetValue(dialog)!;
            zoomField.SetValue(
                dialog,
                Math.Clamp(current + __0.deltaPrecise / 8f, minZoom, maxZoom)
            );
            __0.SetHandled(true);
            return false;
        }

        private static void BeforeRenderEntityToGui(
            Entity __1,
            ref double __2,
            ref double __3,
            ref float __6)
        {
            object? dialog = activeCharacterDialog;
            if (dialog == null || clientApi?.World.Player?.Entity != __1) return;

            RaceProfile profile = GetProfile(__1);
            FieldInfo? tabField = AccessTools.Field(dialog.GetType(), "curTab");
            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            if (tabField?.FieldType != typeof(int) || zoomField?.FieldType != typeof(float))
                return;

            int tab = (int)tabField.GetValue(dialog)!;
            float zoom = (float)zoomField.GetValue(dialog)!;
            float originalSize = __6;
            float cameraBasis = tab == 1
                ? originalSize
                : originalSize / Math.Max(zoom, 0.01f) * 0.62f;

            if (tab == 1)
            {
                __6 *= zoom;
                __2 += originalSize - __6;
            }

            float minZoom = GetMinimumPreviewZoom(profile, __1);
            float maxZoom = GetMaximumPreviewZoom(profile, __1);
            float progress = Math.Clamp(
                (zoom - minZoom) / Math.Max(maxZoom - minZoom, 0.01f),
                0f,
                1f
            );
            __2 += cameraBasis * profile.PreviewXOffset;
            __3 += cameraBasis * (
                profile.PreviewYOffset + progress * profile.PreviewFacePan
            );
        }

    }
}
