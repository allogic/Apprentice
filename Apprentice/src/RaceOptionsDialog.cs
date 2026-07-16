using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Apprentice
{
    /// <summary>
    /// Transparent companion controls for the game's race-selection page.
    /// The stock dialog still owns the preview and race list; this dialog only
    /// adds the Apprentice-specific body and facial options.
    /// </summary>
    internal sealed class RaceOptionsDialog : GuiDialog
    {
        private const string HeightKey = "apprentice-race-height";
        private const string ThicknessKey = "apprentice-race-thickness";
        private const string SubclassKey = "apprentice-race-subclass";
        private const string ProfessionKey = "apprentice-race-profession";
        private const string HornsKey = "apprentice-race-horns";
        private const string TeethKey = "apprentice-race-teeth";
        private const string HeightValueKey = "apprentice-race-height-value";
        private const string ThicknessValueKey = "apprentice-race-thickness-value";

        private readonly Action<float, float, string, string, string, string> changed;
        private readonly int dialogHeight;
        private RaceAppearanceSystem.RaceProfile profile;
        private bool composing;
        private float currentHeight;
        private float currentThickness;
        private string currentSubclass = "select";
        private string currentProfession = "select";
        private string currentHorns = "none";
        private string currentTeeth = "none";

        public override string? ToggleKeyCombinationCode => null;
        public override EnumDialogType DialogType => EnumDialogType.HUD;
        public override bool Focusable => false;
        public override bool PrefersUngrabbedMouse => true;
        public override bool DisableMouseGrab => false;
        public override double InputOrder => -10;
        public override double DrawOrder => 1.15;

        public RaceOptionsDialog(
            ICoreClientAPI api,
            int dialogHeight,
            RaceAppearanceSystem.RaceProfile profile,
            Action<float, float, string, string, string, string> changed)
            : base(api)
        {
            this.dialogHeight = dialogHeight;
            this.profile = profile;
            this.changed = changed;
            ComposeControls();
        }

        public override bool ShouldReceiveMouseEvents() => true;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool CaptureAllInputs() => false;
        public override bool CaptureRawMouse() => false;

        public void Refresh(RaceAppearanceSystem.RaceProfile nextProfile)
        {
            if (ReferenceEquals(profile, nextProfile)) return;
            profile = nextProfile;
            ComposeControls();
        }

        private void ComposeControls()
        {
            composing = true;

            currentHeight = RaceAppearanceSystem.GetHeightChoice(capi.World.Player.Entity);
            currentThickness = RaceAppearanceSystem.GetThicknessChoice(capi.World.Player.Entity);
            currentSubclass = RaceAppearanceSystem.GetSubclassChoice(
                capi.World.Player.Entity,
                profile
            );
            currentProfession = RaceAppearanceSystem.GetProfessionChoice(
                capi.World.Player.Entity
            );
            currentHorns = RaceAppearanceSystem.GetHornChoice(capi.World.Player.Entity, profile);
            currentTeeth = RaceAppearanceSystem.GetTeethChoice(capi.World.Player.Entity, profile);

            bool hasSubclass = RaceAppearanceSystem.HasSubclasses(profile);
            bool showHorns = profile.HornCodes.Any(
                code => code != "none"
            );
            bool showTeeth = profile.TeethCodes.Any(
                code => code != "none"
            );
            string[] subclassCodes = RaceAppearanceSystem.GetSubclassCodes(profile);
            string[] subclassNames = RaceAppearanceSystem.GetSubclassNames(profile);
            string[] professionCodes = RaceAppearanceSystem.GetProfessionCodes();
            string[] professionNames = RaceAppearanceSystem.GetProfessionNames();
            int subclassIndex = Math.Max(0, Array.IndexOf(subclassCodes, currentSubclass));
            int professionIndex = Math.Max(
                0,
                Array.IndexOf(professionCodes, currentProfession)
            );
            int hornIndex = Math.Max(0, Array.IndexOf(profile.HornCodes, currentHorns));
            int teethIndex = Math.Max(0, Array.IndexOf(profile.TeethCodes, currentTeeth));

            // Keep the companion dialog tightly wrapped around its own controls.
            // A full-size transparent HUD receives mouse events across the whole
            // character dialog and prevents the stock race arrows, tabs and
            // confirmation button from receiving clicks.
            const int fullDialogWidth = 757;
            const int optionsLeft = 240;
            // Keep the entire options block clear of the stock Confirm Race
            // button, including races that show subclass, horns and teeth.
            // The trait panel is rendered with the same compact font for every
            // race, so this fixed top edge is now safe for every profile.
            const int optionsTop = 260;
            const int optionsWidth = 475;
            int professionTop = hasSubclass ? 58 : 23;
            int bodyTop = hasSubclass ? 95 : 60;
            int optionsHeight = bodyTop + (showHorns || showTeeth ? 100 : 65);
            double xOffset = optionsLeft + optionsWidth / 2d - fullDialogWidth / 2d;
            double yOffset = optionsTop + optionsHeight / 2d - (dialogHeight + 40) / 2d;

            ElementBounds dialogBounds = ElementBounds.Fixed(
                EnumDialogArea.CenterMiddle,
                xOffset,
                yOffset,
                optionsWidth,
                optionsHeight
            );

            CairoFont labelFont = CairoFont.WhiteSmallText();
            CairoFont valueFont = CairoFont.WhiteSmallText()
                .WithColor(GuiStyle.DialogDefaultTextColor);

            SingleComposer?.Dispose();
            GuiComposer composer = capi.Gui
                .CreateCompo("apprentice-race-options", dialogBounds)
                .AddStaticText(
                    Lang.Get("apprentice:race-body-options"),
                    CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                    ElementBounds.Fixed(10, 0, 430, 25)
                );

            if (hasSubclass)
            {
                composer
                    .AddStaticText(
                        Lang.Get("apprentice:race-subclass"),
                        labelFont,
                        ElementBounds.Fixed(10, 28, 92, 25)
                    )
                    .AddDropDown(
                        subclassCodes,
                        subclassNames,
                        subclassIndex,
                        OnSubclassChanged,
                        ElementBounds.Fixed(105, 23, 300, 30),
                        SubclassKey
                    );
            }

            composer
                .AddStaticText(
                    Lang.Get("apprentice:race-profession"),
                    labelFont,
                    ElementBounds.Fixed(10, professionTop + 5, 92, 25)
                )
                .AddDropDown(
                    professionCodes,
                    professionNames,
                    professionIndex,
                    OnProfessionChanged,
                    ElementBounds.Fixed(105, professionTop, 300, 30),
                    ProfessionKey
                );

            composer
                .AddStaticText(
                    Lang.Get("apprentice:race-height"),
                    labelFont,
                    ElementBounds.Fixed(10, bodyTop + 3, 92, 25)
                )
                .AddSlider(
                    OnHeightChanged,
                    ElementBounds.Fixed(105, bodyTop, 245, 25),
                    HeightKey
                )
                .AddDynamicText(
                    PercentLabel(currentHeight),
                    valueFont,
                    ElementBounds.Fixed(360, bodyTop + 3, 100, 25),
                    HeightValueKey
                )
                .AddStaticText(
                    Lang.Get("apprentice:race-thickness"),
                    labelFont,
                    ElementBounds.Fixed(10, bodyTop + 33, 92, 25)
                )
                .AddSlider(
                    OnThicknessChanged,
                    ElementBounds.Fixed(105, bodyTop + 30, 245, 25),
                    ThicknessKey
                )
                .AddDynamicText(
                    PercentLabel(currentThickness),
                    valueFont,
                    ElementBounds.Fixed(360, bodyTop + 33, 100, 25),
                    ThicknessValueKey
                );

            if (showHorns && showTeeth)
            {
                composer
                    .AddStaticText(
                        Lang.Get("apprentice:race-horns"),
                        labelFont,
                        ElementBounds.Fixed(10, bodyTop + 73, 50, 25)
                    )
                    .AddDropDown(
                        profile.HornCodes,
                        profile.HornNames,
                        hornIndex,
                        OnHornsChanged,
                        ElementBounds.Fixed(65, bodyTop + 68, 170, 30),
                        HornsKey
                    )
                    .AddStaticText(
                        Lang.Get("apprentice:race-teeth"),
                        labelFont,
                        ElementBounds.Fixed(245, bodyTop + 73, 50, 25)
                    )
                    .AddDropDown(
                        profile.TeethCodes,
                        profile.TeethNames,
                        teethIndex,
                        OnTeethChanged,
                        ElementBounds.Fixed(295, bodyTop + 68, 170, 30),
                        TeethKey
                    );
            }
            else if (showHorns)
            {
                composer
                    .AddStaticText(
                        Lang.Get("apprentice:race-horns"),
                        labelFont,
                        ElementBounds.Fixed(10, bodyTop + 73, 92, 25)
                    )
                    .AddDropDown(
                        profile.HornCodes,
                        profile.HornNames,
                        hornIndex,
                        OnHornsChanged,
                        ElementBounds.Fixed(105, bodyTop + 68, 300, 30),
                        HornsKey
                    );
            }
            else if (showTeeth)
            {
                composer
                    .AddStaticText(
                        Lang.Get("apprentice:race-teeth"),
                        labelFont,
                        ElementBounds.Fixed(10, bodyTop + 73, 92, 25)
                    )
                    .AddDropDown(
                        profile.TeethCodes,
                        profile.TeethNames,
                        teethIndex,
                        OnTeethChanged,
                        ElementBounds.Fixed(105, bodyTop + 68, 300, 30),
                        TeethKey
                    );
            }

            SingleComposer = composer.Compose();

            SingleComposer.GetSlider(HeightKey).SetValues(
                (int)Math.Round(currentHeight * 100),
                0,
                100,
                1,
                "%"
            );
            SingleComposer.GetSlider(ThicknessKey).SetValues(
                (int)Math.Round(currentThickness * 100),
                0,
                100,
                1,
                "%"
            );

            composing = false;
        }

        private bool OnHeightChanged(int value)
        {
            if (composing) return true;
            currentHeight = value / 100f;
            Emit();
            return true;
        }

        private bool OnThicknessChanged(int value)
        {
            if (composing) return true;
            currentThickness = value / 100f;
            Emit();
            return true;
        }

        private void OnHornsChanged(string code, bool selected)
        {
            if (!selected || composing) return;
            currentHorns = code;
            Emit();
        }

        private void OnSubclassChanged(string code, bool selected)
        {
            if (!selected || composing) return;
            currentSubclass = code;
            Emit();
        }

        private void OnProfessionChanged(string code, bool selected)
        {
            if (!selected || composing) return;
            currentProfession = code;
            Emit();
        }

        private void OnTeethChanged(string code, bool selected)
        {
            if (!selected || composing) return;
            currentTeeth = code;
            Emit();
        }

        private void Emit()
        {
            changed(
                currentHeight,
                currentThickness,
                currentHorns,
                currentTeeth,
                currentSubclass,
                currentProfession
            );
            SingleComposer.GetDynamicText(HeightValueKey)
                .SetNewText(PercentLabel(currentHeight));
            SingleComposer.GetDynamicText(ThicknessValueKey)
                .SetNewText(PercentLabel(currentThickness));
        }

        private static string PercentLabel(float value) =>
            $"{Math.Round(value * 100):0}%";
    }
}
