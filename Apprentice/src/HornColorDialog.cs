using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

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
        private double optionsLeft;
        private double optionsTop;

        private static readonly int[] SwatchColors =
        {
            ColorUtil.ToRgba(255, 60, 65, 70),
            ColorUtil.ToRgba(255, 167, 173, 178),
            ColorUtil.ToRgba(255, 231, 230, 224),
            ColorUtil.ToRgba(255, 231, 177, 188),
            ColorUtil.ToRgba(255, 227, 215, 172)
        };

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
            double optionsLeft,
            double optionsTop,
            Action<string> changed)
            : base(api)
        {
            this.dialogHeight = dialogHeight;
            this.optionsLeft = optionsLeft;
            this.optionsTop = optionsTop;
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

        public void Refresh(double nextLeft, double nextTop)
        {
            string selected = RaceAppearanceSystem.GetHornColorChoice(
                capi.World.Player.Entity
            );
            if (selected == currentHornColor &&
                Math.Abs(optionsLeft - nextLeft) < 0.5 &&
                Math.Abs(optionsTop - nextTop) < 0.5)
            {
                return;
            }

            currentHornColor = selected;
            optionsLeft = nextLeft;
            optionsTop = nextTop;
            ComposeControls();
        }

        private void ComposeControls()
        {
            composing = true;

            string[] colorCodes = RaceAppearanceSystem.GetHornColorCodes();
            string[] colorNames = RaceAppearanceSystem.GetHornColorNames();
            int colorIndex = Math.Max(0, Array.IndexOf(colorCodes, currentHornColor));

            const int fullDialogWidth = 757;
            const int optionsWidth = 215;
            const int optionsHeight = 60;
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
            GuiComposer composer = capi.Gui
                .CreateCompo("apprentice-skin-horn-color", dialogBounds)
                .AddStaticText(
                    Lang.Get("apprentice:race-horn-color"),
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(0, 0, optionsWidth, 25)
                )
                .AddColorListPicker(
                    SwatchColors,
                    OnHornColorChanged,
                    ElementBounds.Fixed(0, 27, 24, 24),
                    180,
                    HornColorKey
                );

            SingleComposer = composer.Compose();
            for (int index = 0; index < colorCodes.Length; index++)
            {
                GuiElementColorListPicker picker = SingleComposer.GetColorListPicker(
                    HornColorKey + "-" + index
                );
                picker.ShowToolTip = true;
                picker.TooltipText = colorNames[index];
            }
            SingleComposer.ColorListPickerSetValue(HornColorKey, colorIndex);

            composing = false;
        }

        private void OnHornColorChanged(int index)
        {
            string[] colorCodes = RaceAppearanceSystem.GetHornColorCodes();
            if (composing || index < 0 || index >= colorCodes.Length) return;
            currentHornColor = colorCodes[index];
            changed(currentHornColor);
        }
    }
}
