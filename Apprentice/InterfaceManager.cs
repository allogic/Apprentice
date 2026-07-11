using Vintagestory.API.Client;

namespace Apprentice
{
	internal class ExperienceDialog : GuiDialog
	{
		public override string ToggleKeyCombinationCode => "experiencedialog";

		public ExperienceDialog(ICoreClientAPI api) : base(api)
		{
			// Auto-sized dialog at the center of the screen
			ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

			// Just a simple 300x300 pixel box
			ElementBounds textBounds = ElementBounds.Fixed(0, 40, 300, 100);

			// Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
			ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
			bgBounds.BothSizing = ElementSizing.FitToChildren;
			bgBounds.WithChildren(textBounds);

			// Create the dialog
			SingleComposer = capi.Gui.CreateCompo("Experience Dialog", dialogBounds)
				.AddShadedDialogBG(bgBounds)
				.AddDialogTitleBar("Heck yeah!", OnTitleBarCloseClicked)
				.AddStaticText("This is a piece of text at the center of your screen - Enjoy!", CairoFont.WhiteDetailText(), textBounds)
				.Compose();
		}

		private void OnTitleBarCloseClicked()
		{
			TryClose();
		}
	}

	internal class InterfaceManager
	{
		private readonly ICoreClientAPI? api = null;
		private readonly GuiDialog? dialog = null;

		public InterfaceManager(ICoreClientAPI api)
		{
			this.api = api;

			// Setup a basic dialog
			dialog = new ExperienceDialog(api);

			// Register hotkey's
			api.Input.RegisterHotKey("experiencedialog", "Some stats", GlKeys.U, HotkeyType.GUIOrOtherControls);
			api.Input.SetHotKeyHandler("experiencedialog", OnToggleExperienceDialog);
		}

		private bool OnToggleExperienceDialog(KeyCombination comb)
		{
			if (dialog == null) return true;
			if (dialog.IsOpened()) dialog.TryClose();
			else dialog.TryOpen();
			return true;
		}
	}
}
