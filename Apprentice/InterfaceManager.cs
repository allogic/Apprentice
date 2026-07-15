using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Apprentice
{
    internal sealed class SkillTreeDialog :
        GuiDialog
    {
        // A fixed 1280 x 780 content area preserves the 1120 x 682 design
        // aspect ratio while making the complete interface visibly larger.
        // These dimensions are Apprentice-dialog units; they are not derived
        // from the game window or render-frame size.
        private const double DialogContentWidth = 1280;
        private const double DialogContentHeight = 780;

        private readonly GuiElementSkillTreeCanvas
            canvas;

        public override string?
            ToggleKeyCombinationCode =>
                null;

        public SkillTreeDialog(
            ICoreClientAPI api,
            ClassConfig classConfig,
            SkillTreeConfig skillConfig,
            IClientNetworkChannel channel)
            : base(api)
        {
            _ =
                classConfig ??
                throw new ArgumentNullException(
                    nameof(classConfig)
                );

            // Use the exact root/background sizing pattern proven stable
            // by the working Compact GUI 2.0.4.
            ElementBounds dialogBounds =
                ElementStdBounds
                    .AutosizedMainDialog
                    .WithAlignment(
                        EnumDialogArea.CenterMiddle
                    );

            // Keep the Apprentice dialog independent of the game-window size.
            // This bounds object sizes the complete usable content area and is
            // deliberately not shared with the canvas element. The larger
            // fixed bounds make the entire UI grow together; they do not turn
            // the tabs into a full-width tab strip.
            ElementBounds contentBounds =
                ElementBounds.Fixed(
                    0,
                    40,
                    DialogContentWidth,
                    DialogContentHeight
                );

            // The canvas consumes the complete Apprentice content area while
            // the title bar remains owned by the dialog shell.
            ElementBounds canvasBounds =
                ElementBounds.Fixed(
                    0,
                    40,
                    DialogContentWidth,
                    DialogContentHeight
                );

            // The shaded background already paints its own frame. Additional
            // composer padding produced the broad brown strip visible at the
            // right and bottom of the enlarged dialog, so let the Apprentice
            // canvas consume the complete framed content area.
            ElementBounds backgroundBounds =
                ElementBounds.Fill
                    .WithFixedPadding(0);

            backgroundBounds.BothSizing =
                ElementSizing.FitToChildren;

            backgroundBounds.WithChildren(
                contentBounds
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "creating canvas object."
            );

            canvas =
                new GuiElementSkillTreeCanvas(
                    api,
                    canvasBounds,
                    skillConfig,
                    channel
                );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "canvas object ready."
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "creating composer."
            );

            GuiComposer composer =
                api.Gui.CreateCompo(
                    "apprentice-interactive-skilltree",
                    dialogBounds
                );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "composer created."
            );

            composer.AddShadedDialogBG(
                backgroundBounds
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "background added."
            );

            composer.AddDialogTitleBar(
                "Apprentice Mastery and Skill Trees",
                OnClose
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "title bar added."
            );

            composer.AddInteractiveElement(
                canvas,
                "apprentice-skilltree-canvas"
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "canvas element added."
            );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "composer Compose begin."
            );

            SingleComposer =
                composer.Compose(
                    focusFirstElement: false
                );

            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "composer Compose complete."
            );
        }

        private void OnClose()
        {
            TryClose();
        }

        public void Refresh(
            string? message = null)
        {
            canvas.Refresh(
                message
            );
        }

        public void ApplyPurchaseResult(
            SkillPurchaseResultPacket packet)
        {
            canvas.ApplyPurchaseResult(packet);
        }
    }

    internal sealed class InterfaceManager :
        IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly ClassConfig classConfig;
        private readonly SkillTreeConfig skillConfig;
        private readonly IClientNetworkChannel channel;

        private SkillTreeDialog? dialog;

        public InterfaceManager(
            ICoreClientAPI api,
            ClassConfig classConfig,
            SkillTreeConfig skillConfig,
            IClientNetworkChannel channel)
        {
            this.api =
                api ??
                throw new ArgumentNullException(
                    nameof(api)
                );

            this.classConfig =
                classConfig ??
                throw new ArgumentNullException(
                    nameof(classConfig)
                );

            this.skillConfig =
                skillConfig ??
                throw new ArgumentNullException(
                    nameof(skillConfig)
                );

            this.channel =
                channel ??
                throw new ArgumentNullException(
                    nameof(channel)
                );

            api.Input.RegisterHotKey(
                ApprenticeConstants
                    .ExperienceDialogHotKey,
                "Apprentice mastery and skill trees",
                GlKeys.U,
                HotkeyType.GUIOrOtherControls
            );

            api.Input.SetHotKeyHandler(
                ApprenticeConstants
                    .ExperienceDialogHotKey,
                OnToggle
            );
        }

        public void RefreshIfOpen(
            string? message = null)
        {
            if (dialog?.IsOpened() ==
                true)
            {
                dialog.Refresh(
                    message
                );
            }
        }

        public void ApplyPurchaseResult(
            SkillPurchaseResultPacket packet)
        {
            dialog?.ApplyPurchaseResult(packet);
        }

        private bool OnToggle(
            KeyCombination combination)
        {
            api.Logger.Notification(
                "[Apprentice] Interactive skill-tree GUI: " +
                "U hotkey received."
            );

            try
            {
                if (dialog == null)
                {
                    api.Logger.Notification(
                        "[Apprentice] Interactive skill-tree GUI: " +
                        "creating dialog."
                    );

                    dialog =
                        new SkillTreeDialog(
                            api,
                            classConfig,
                            skillConfig,
                            channel
                        );
                }

                if (dialog.IsOpened())
                {
                    api.Logger.Notification(
                        "[Apprentice] Interactive skill-tree GUI: " +
                        "closing dialog."
                    );

                    dialog.TryClose();
                }
                else
                {
                    api.Logger.Notification(
                        "[Apprentice] Interactive skill-tree GUI: " +
                        "opening dialog."
                    );

                    bool opened =
                        dialog.TryOpen();

                    api.Logger.Notification(
                        "[Apprentice] Interactive skill-tree GUI: " +
                        $"TryOpen returned {opened}."
                    );
                }
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    "[Apprentice] Interactive skill-tree GUI " +
                    "failed with a managed exception."
                );

                api.Logger.Error(
                    exception
                );

                api.ShowChatMessage(
                    "Apprentice: interactive skill-tree GUI failed. " +
                    "Check client-main.log."
                );
            }

            return true;
        }

        public void Dispose()
        {
            dialog?.TryClose();
            dialog?.Dispose();
            dialog = null;
        }
    }
}
