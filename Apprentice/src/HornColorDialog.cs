using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Apprentice
{
    /// <summary>
    /// Small companion control for horn-bearing races on Skin & Voice.
    /// Its bounds cover only the new selector so the stock customization
    /// controls, preview, tabs, and confirmation button keep receiving input.
    /// </summary>
    internal sealed class HornColorDialog : GuiDialog
    {
        private const string HornColorKey = "apprentice-skin-horn-color";

        private readonly Action<string> changed;
        private readonly int dialogHeight;
        private bool composing;
        private string currentHornColor;

        public override string? ToggleKeyCombinationCode => null;
        public override EnumDialogType DialogType => EnumDialogType.HUD;
        public override bool Focusable => false;
        public override bool PrefersUngrabbedMouse => true;
        public override bool DisableMouseGrab => false;
        public override double InputOrder => -10;
        public override double DrawOrder => 1.16;

        public HornColorDialog(
            ICoreClientAPI api,
            int dialogHeight,
            Action<string> changed)
            : base(api)
        {
            this.dialogHeight = dialogHeight;
            this.changed = changed;
            currentHornColor = RaceAppearanceSystem.GetHornColorChoice(
                capi.World.Player.Entity
            );
            ComposeControls();
        }

        public override bool ShouldReceiveMouseEvents() => true;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool CaptureAllInputs() => false;
        public override bool CaptureRawMouse() => false;

        public void Refresh()
        {
            string selected = RaceAppearanceSystem.GetHornColorChoice(
                capi.World.Player.Entity
            );
            if (selected == currentHornColor) return;

            currentHornColor = selected;
            ComposeControls();
        }

        private void ComposeControls()
        {
            composing = true;

            string[] colorCodes = RaceAppearanceSystem.GetHornColorCodes();
            string[] colorNames = RaceAppearanceSystem.GetHornColorNames();
            int colorIndex = Math.Max(0, Array.IndexOf(colorCodes, currentHornColor));

            const int fullDialogWidth = 757;
            const int optionsLeft = 500;
            const int optionsTop = 390;
            const int optionsWidth = 215;
            const int optionsHeight = 65;
            double xOffset = optionsLeft + optionsWidth / 2d - fullDialogWidth / 2d;
            double yOffset = optionsTop + optionsHeight / 2d - (dialogHeight + 40) / 2d;

            ElementBounds dialogBounds = ElementBounds.Fixed(
                EnumDialogArea.CenterMiddle,
                xOffset,
                yOffset,
                optionsWidth,
                optionsHeight
            );

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("apprentice-skin-horn-color", dialogBounds)
                .AddStaticText(
                    Lang.Get("apprentice:race-horn-color"),
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(0, 0, optionsWidth, 25)
                )
                .AddDropDown(
                    colorCodes,
                    colorNames,
                    colorIndex,
                    OnHornColorChanged,
                    ElementBounds.Fixed(0, 25, optionsWidth, 30),
                    HornColorKey
                )
                .Compose();

            composing = false;
        }

        private void OnHornColorChanged(string code, bool selected)
        {
            if (!selected || composing) return;
            currentHornColor = code;
            changed(code);
        }
    }
}
