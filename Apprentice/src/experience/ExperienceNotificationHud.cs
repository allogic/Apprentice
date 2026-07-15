using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Apprentice
{
	/// <summary>
	/// Shows every XP increase as a queued, temporary HUD element.
	///
	/// The bar interpolates from the old total XP to the new total XP.
	/// ExpMath is evaluated throughout the interpolation, so the bar
	/// fills, resets and changes the displayed level when thresholds
	/// are crossed. Level-up text blinks during the animation.
	/// </summary>
	internal sealed class ExperienceNotificationHud : HudElement
	{
		private const string TitleKey = "skill-title";
		private const string GainKey = "xp-gain";
		private const string ProgressKey = "xp-progress";

		private readonly BaseConfig config;
		private readonly Queue<ExperienceNotificationPacket> queue =
			new();

		private ExperienceNotificationPacket? activePacket;
		private long activeSinceMs;
		private int activeFillDurationMs;

		public override bool Focusable => false;

		public ExperienceNotificationHud(
			ICoreClientAPI api,
			BaseConfig config)
			: base(api)
		{
			this.config = config
				?? throw new ArgumentNullException(
					nameof(config)
				);

			ComposeHud();
		}

		public void Enqueue(
			ExperienceNotificationPacket packet)
		{
			ArgumentNullException.ThrowIfNull(packet);

			if (!config.EnableExperienceNotifications)
			{
				return;
			}

			queue.Enqueue(packet);

			if (activePacket == null)
			{
				StartNextNotification();
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

		public override void OnRenderGUI(float deltaTime)
		{
			UpdateAnimation();
			base.OnRenderGUI(deltaTime);
		}

		private void ComposeHud()
		{
			ElementBounds dialogBounds =
				ElementBounds.FixedOffseted(
					EnumDialogArea.CenterTop,
					0,
					80,
					430,
					92
				);

			ElementBounds backgroundBounds =
				ElementBounds.Fill.WithFixedPadding(10);

			ElementBounds titleBounds =
				ElementBounds.Fixed(
					8,
					5,
					394,
					24
				);

			ElementBounds gainBounds =
				ElementBounds.Fixed(
					8,
					31,
					394,
					20
				);

			ElementBounds progressBounds =
				ElementBounds.Fixed(
					8,
					58,
					394,
					12
				);

			SingleComposer =
				capi.Gui.CreateCompo(
					"apprentice-experience-notification",
					dialogBounds
				)
				.AddShadedDialogBG(
					backgroundBounds,
					withTitleBar: false,
					strokeWidth: 3,
					alpha: 0.82f
				)
				.BeginChildElements(
					backgroundBounds
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
				.AddStatbar(
					progressBounds,
					GuiStyle.FoodBarColor,
					hideable: false,
					key: ProgressKey
				)
				.EndChildElements()
				.Compose();

			SingleComposer
				.GetStatbar(ProgressKey)
				.ShowValueOnHover = false;
		}

		private void StartNextNotification()
		{
			if (queue.Count == 0)
			{
				activePacket = null;
				TryClose();
				return;
			}

			activePacket = queue.Dequeue();
			activeSinceMs = capi.ElapsedMilliseconds;

			int gainedLevels = Math.Max(
				0,
				activePacket.NewLevel -
				activePacket.PreviousLevel
			);

			// Extra duration is capped at five levels so the HUD remains
			// responsive even when the supplied equation jumps far.
			activeFillDurationMs =
				config.ExperienceNotificationFillDurationMs +
				Math.Min(gainedLevels, 5) *
				config.ExperienceNotificationLevelUpExtraDurationMs;

			UpdateVisuals(
				activePacket.PreviousTotalExperience,
				blinkLevelUp: false
			);

			if (!IsOpened())
			{
				TryOpen(withFocus: false);
			}
		}

		private void UpdateAnimation()
		{
			if (activePacket == null)
			{
				return;
			}

			long elapsed =
				capi.ElapsedMilliseconds -
				activeSinceMs;

			if (elapsed < activeFillDurationMs)
			{
				double linearProgress =
					activeFillDurationMs <= 0
					? 1
					: elapsed /
					  (double)activeFillDurationMs;

				double easedProgress =
					SmoothStep(
						Math.Clamp(
							linearProgress,
							0,
							1
						)
					);

				double displayedTotal =
					Lerp(
						activePacket
							.PreviousTotalExperience,
						activePacket
							.NewTotalExperience,
						easedProgress
					);

				bool blink =
					activePacket.NewLevel >
					activePacket.PreviousLevel &&
					(
						elapsed /
						config.LevelUpBlinkIntervalMs
					) % 2 == 0;

				UpdateVisuals(
					displayedTotal,
					blink
				);

				return;
			}

			if (elapsed <
				activeFillDurationMs +
				config.ExperienceNotificationHoldDurationMs)
			{
				bool blink =
					activePacket.NewLevel >
					activePacket.PreviousLevel &&
					(
						elapsed /
						config.LevelUpBlinkIntervalMs
					) % 2 == 0;

				UpdateVisuals(
					activePacket.NewTotalExperience,
					blink
				);

				return;
			}

			StartNextNotification();
		}

		private void UpdateVisuals(
			double displayedTotal,
			bool blinkLevelUp)
		{
			if (activePacket == null)
			{
				return;
			}

			int displayedLevel =
				ExpMath.GetLevel(displayedTotal);

			double levelProgress =
				ExpMath.GetProgress(displayedTotal);

			string displayName =
				string.IsNullOrWhiteSpace(
					activePacket.ClassDisplayName)
				? activePacket.ClassId
				: activePacket.ClassDisplayName;

			string title = blinkLevelUp
				? $"{displayName} — LEVEL {displayedLevel}!"
				: $"{displayName} — Level {displayedLevel}";

			string gain =
				$"+{activePacket.GainedExperience:0.###} XP";

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

			SingleComposer
				.GetStatbar(ProgressKey)
				.SetValues(
					(float)(levelProgress * 100),
					0,
					100
				);
		}

		private static double Lerp(
			double start,
			double end,
			double amount)
		{
			return start +
				(end - start) * amount;
		}

		private static double SmoothStep(double value)
		{
			return value * value *
				(3 - 2 * value);
		}
	}
}
