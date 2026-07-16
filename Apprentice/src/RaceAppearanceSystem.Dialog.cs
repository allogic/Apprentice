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
            GuiComposer composer,
            string text,
            ref GuiComposer __result)
        {
            if (activeCharacterDialog == null ||
                !ReferenceEquals(GetCharacterComposer(activeCharacterDialog), composer))
            {
                return true;
            }

            bool unnecessarySkinAction =
                text == Lang.Get("Randomize") ||
                text == Lang.Get("Last selection");
            if (!unnecessarySkinAction) return true;

            // Keep the fluent composer chain intact while omitting both buttons.
            __result = composer;
            return false;
        }

        private static void AfterDialogOpened(object __instance)
        {
            activeCharacterDialog = __instance;
            ConfirmedRaceDialogs.Remove(__instance);
            SetDialogTab(__instance, 1);
            ResetDialogZoom(__instance);
            AccessTools.Method(__instance.GetType(), "ComposeGuis")?.Invoke(__instance, null);
            FixActiveTab(__instance);
        }

        private static void AfterDialogClosed(object __instance)
        {
            RestoreNaturalPalette();
            ConfirmedRaceDialogs.Remove(__instance);
            if (ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                pendingSkinConfirmDialog = null;
            }
            if (ReferenceEquals(activeCharacterDialog, __instance))
            {
                activeCharacterDialog = null;
            }
            DestroyOptionsDialog();
            DestroyHornColorDialog();
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
            FixActiveTab(__instance);
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

            // The stock Skin button calls OnNext() from inside the dialog's
            // OnMouseDown handler. Closing synchronously here disposes its GUI
            // composer while that handler is still using it. Finish on the next
            // client frame, after the mouse event has returned.
            if (!ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                EntityPlayer? player = clientApi?.World.Player?.Entity;
                if (player != null)
                {
                    SendBodyPacket(player);
                }

                object dialog = __instance;
                pendingSkinConfirmDialog = dialog;
                clientApi?.Event.EnqueueMainThreadTask(
                    () =>
                    {
                        if (ReferenceEquals(pendingSkinConfirmDialog, dialog))
                        {
                            pendingSkinConfirmDialog = null;
                        }
                        if (!ReferenceEquals(activeCharacterDialog, dialog) ||
                            GetDialogTab(dialog) != 0 ||
                            !ConfirmedRaceDialogs.Contains(dialog))
                        {
                            return;
                        }
                        AccessTools.Method(dialog.GetType(), "OnConfirm")
                            ?.Invoke(dialog, null);
                    },
                    "apprentice-confirm-skin"
                );
            }
            __result = true;
            return false;
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

        private static void BeforeComposeGuis(object __instance)
        {
            activeCharacterDialog = __instance;
            if (GetDialogTab(__instance) == 0)
            {
                ApplyNaturalPalette();
            }
            else
            {
                RestoreNaturalPalette();
            }
        }

        private static void AfterComposeGuis(object __instance)
        {
            FixActiveTab(__instance);
            UpdateBodyTrait(__instance);
            RefreshOptionsDialog(__instance);
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
            RefreshHornColorDialog(dialog);

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

            if (optionsDialog == null)
            {
                optionsDialog = new RaceOptionsDialog(
                    capi,
                    dialogHeight,
                    profile,
                    OnBodyOptionsChanged
                );
            }
            else
            {
                optionsDialog.Refresh(profile);
            }

            if (!optionsDialog.IsOpened())
            {
                optionsDialog.TryOpen();
            }
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
            HideOptionsDialog();
            optionsDialog?.Dispose();
            optionsDialog = null;
        }

        private static void RefreshHornColorDialog(object dialog)
        {
            ICoreClientAPI? capi = clientApi;
            EntityPlayer? player = capi?.World.Player?.Entity;
            RaceProfile? profile = player == null ? null : GetProfile(player);
            bool hasHorns = profile?.HornCodes.Any(code => code != "none") == true;
            if (capi == null ||
                player == null ||
                GetDialogTab(dialog) != 0 ||
                !ConfirmedRaceDialogs.Contains(dialog) ||
                !hasHorns)
            {
                HideHornColorDialog();
                return;
            }

            FieldInfo? heightField = AccessTools.Field(dialog.GetType(), "dlgHeight");
            int dialogHeight = heightField?.GetValue(dialog) is int value ? value : 500;

            if (hornColorDialog == null)
            {
                hornColorDialog = new HornColorDialog(
                    capi,
                    dialogHeight,
                    OnSkinHornColorChanged
                );
            }
            else
            {
                hornColorDialog.Refresh();
            }

            if (!hornColorDialog.IsOpened())
            {
                hornColorDialog.TryOpen();
            }
        }

        private static void HideHornColorDialog()
        {
            if (hornColorDialog?.IsOpened() == true)
            {
                hornColorDialog.TryClose();
            }
        }

        private static void DestroyHornColorDialog()
        {
            HideHornColorDialog();
            hornColorDialog?.Dispose();
            hornColorDialog = null;
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
            ApplyRaceAppearance(player, GetClassCode(player));
            SendBodyPacket(player);
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
            ApplyRaceAppearance(player, GetClassCode(player));
            ApplyBodyStats(player, profile);
            if (activeCharacterDialog != null)
            {
                ConfirmedRaceDialogs.Remove(activeCharacterDialog);
                ResetDialogZoom(activeCharacterDialog);
                UpdateBodyTrait(activeCharacterDialog);
            }
            SendBodyPacket(player);
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
