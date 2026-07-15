using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Apprentice
{
	/// <summary>
	/// Phase-one XP popup.
	///
	/// This HUD intentionally contains only two dynamic text elements.
	/// It has:
	///
	/// - no stat bar;
	/// - no custom OpenGL renderer;
	/// - no OnRenderGUI override;
	/// - no hotkey;
	/// - no construction during client connection.
	///
	/// Packets are queued and displayed one at a time.
	/// </summary>
	internal sealed class ExperienceTextPopupHud : HudElement
	{
		private const string TitleKey =
			"apprentice-popup-title";

		private const string GainKey =
			"apprentice-popup-gain";

		private readonly Queue<ExperienceNotificationPacket> queue =
			new();

		private readonly int displayDurationMs;

		private ExperienceNotificationPacket? activePacket;
		private long activeSinceMs;
		private long tickListenerId;

		public override bool Focusable => false;

		public ExperienceTextPopupHud(
			ICoreClientAPI api,
			BaseConfig config)
			: base(api)
		{
			ArgumentNullException.ThrowIfNull(config);

			// Reuse the existing timing options. During this text-only
			// phase, fill time plus hold time is the total visibility time.
			displayDurationMs = Math.Max(
				500,
				config.ExperienceNotificationFillDurationMs +
				config.ExperienceNotificationHoldDurationMs
			);

			ComposePopup();

			tickListenerId =
				capi.Event.RegisterGameTickListener(
					OnClientTick,
					OnTickException,
					50
				);
		}

		public void Enqueue(
			ExperienceNotificationPacket packet)
		{
			ArgumentNullException.ThrowIfNull(packet);

			queue.Enqueue(packet);

			if (activePacket == null)
			{
				StartNext();
			}
		}

		public override bool ShouldReceiveMouseEvents()
		{
			return false;
		}

		public override bool ShouldReceiveKeyboardEvents()
		{
			return false;
		}

		public override void Dispose()
		{
			if (tickListenerId != 0)
			{
				capi.Event.UnregisterGameTickListener(
					tickListenerId
				);

				tickListenerId = 0;
			}

			queue.Clear();
			activePacket = null;

			base.Dispose();
		}

		private void ComposePopup()
		{
			ElementBounds dialogBounds =
				ElementBounds.FixedOffseted(
					EnumDialogArea.CenterTop,
					0,
					80,
					420,
					78
				);

			ElementBounds backgroundBounds =
				ElementBounds.Fill.WithFixedPadding(8);

			ElementBounds titleBounds =
				ElementBounds.Fixed(
					12,
					10,
					380,
					24
				);

			ElementBounds gainBounds =
				ElementBounds.Fixed(
					12,
					39,
					380,
					22
				);

			SingleComposer =
				capi.Gui.CreateCompo(
					"apprentice-text-xp-popup",
					dialogBounds
				)
				.AddShadedDialogBG(
					backgroundBounds,
					withTitleBar: false,
					strokeWidth: 3,
					alpha: 0.82f
				)
				.AddDynamicText(
					string.Empty,
					CairoFont.WhiteSmallText(),
					titleBounds,
					TitleKey
				)
				.AddDynamicText(
					string.Empty,
					CairoFont.WhiteDetailText(),
					gainBounds,
					GainKey
				)
				.Compose();
		}

		private void StartNext()
		{
			if (queue.Count == 0)
			{
				activePacket = null;

				if (IsOpened())
				{
					TryClose();
				}

				return;
			}

			activePacket = queue.Dequeue();
			activeSinceMs = capi.ElapsedMilliseconds;

			UpdateText(activePacket);

			if (!IsOpened())
			{
				TryOpen(withFocus: false);
			}
		}

		private void OnClientTick(float deltaTime)
		{
			if (activePacket == null)
			{
				return;
			}

			long elapsed =
				capi.ElapsedMilliseconds -
				activeSinceMs;

			if (elapsed >= displayDurationMs)
			{
				StartNext();
			}
		}

		private void OnTickException(Exception exception)
		{
			// The engine invokes this error handler if the game-tick
			// callback throws. Logging here prevents a notification timer
			// failure from becoming an unhandled client exception.
			capi.Logger.Error(
				"[Apprentice] Text popup tick failed."
			);

			capi.Logger.Error(exception);

			queue.Clear();
			activePacket = null;

			if (IsOpened())
			{
				TryClose();
			}
		}

		private void UpdateText(
			ExperienceNotificationPacket packet)
		{
			string displayName =
				string.IsNullOrWhiteSpace(
					packet.ClassDisplayName)
					? packet.ClassId
					: packet.ClassDisplayName;

			string title =
				packet.NewLevel > packet.PreviousLevel
					? $"{displayName} — LEVEL UP! " +
					  $"{packet.PreviousLevel} → {packet.NewLevel}"
					: $"{displayName} — Level {packet.NewLevel}";

			string gain =
				$"+{packet.GainedExperience:0.###} XP " +
				$"({packet.NewTotalExperience:0.###} total)";

			SingleComposer
				.GetDynamicText(TitleKey)
				.SetNewText(
					title,
					forceRedraw: true
				);

			SingleComposer
				.GetDynamicText(GainKey)
				.SetNewText(
					gain,
					forceRedraw: true
				);
		}
	}
}
