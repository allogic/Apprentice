using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Apprentice
{
	internal sealed class ManaburnSwordCalibrationSystem
	{
		private const string SwordCode = "apprentice:manaburnsword";
		private static readonly string[] Components =
		{
			"tx", "ty", "tz", "rx", "ry", "rz", "ox", "oy", "oz", "scale"
		};

		private readonly ICoreClientAPI api;
		private readonly ModelTransform baseline;

		public ManaburnSwordCalibrationSystem(ICoreClientAPI api)
		{
			this.api = api;
			baseline = Current().Clone();

			CommandArgumentParsers parsers = api.ChatCommands.Parsers;
			api.ChatCommands.Create("swordcal")
				.WithDescription("Live Manaburn Sword third-person pose calibration")
				.WithAdditionalInformation(
					"Components: tx ty tz rx ry rz ox oy oz scale. " +
					"Use .swordcal add <component> <amount>, .swordcal set <component> <value>, " +
					".swordcal show, .swordcal reset, or .swordcal export."
				)
				.BeginSubCommand("show").HandleWith(Show).EndSubCommand()
				.BeginSubCommand("add")
					.WithArgs(parsers.WordRange("component", Components), parsers.Float("amount"))
					.HandleWith(Add)
				.EndSubCommand()
				.BeginSubCommand("set")
					.WithArgs(parsers.WordRange("component", Components), parsers.Float("value"))
					.HandleWith(Set)
				.EndSubCommand()
				.BeginSubCommand("reset").HandleWith(Reset).EndSubCommand()
				.BeginSubCommand("export").HandleWith(Export).EndSubCommand();

			api.Logger.Notification("[Apprentice] Manaburn Sword live calibration enabled. Use .swordcal show.");
		}

		private TextCommandResult Show(TextCommandCallingArgs args) =>
			TextCommandResult.Success(FormatPose(Current()));

		private TextCommandResult Add(TextCommandCallingArgs args)
		{
			string component = (string)args[0];
			float amount = Convert.ToSingle(args[1], CultureInfo.InvariantCulture);
			ModelTransform pose = Current();
			SetComponent(pose, component, GetComponent(pose, component) + amount);
			RefreshHeldItem();
			return TextCommandResult.Success(FormatPose(pose));
		}

		private TextCommandResult Set(TextCommandCallingArgs args)
		{
			string component = (string)args[0];
			float value = Convert.ToSingle(args[1], CultureInfo.InvariantCulture);
			ModelTransform pose = Current();
			SetComponent(pose, component, value);
			RefreshHeldItem();
			return TextCommandResult.Success(FormatPose(pose));
		}

		private TextCommandResult Reset(TextCommandCallingArgs args)
		{
			Sword().TpHandTransform = baseline.Clone();
			RefreshHeldItem();
			return TextCommandResult.Success("Sword calibration reset. " + FormatPose(Current()));
		}

		private TextCommandResult Export(TextCommandCallingArgs args)
		{
			string json = FormatJson(Current());
			api.Logger.Notification("[Apprentice] SWORDCAL FINAL {0}", json);
			return TextCommandResult.Success("SWORDCAL FINAL written to client-main.log. " + FormatPose(Current()));
		}

		private Item Sword() => api.World.GetItem(new AssetLocation(SwordCode))
			?? throw new InvalidOperationException("Manaburn Sword is missing.");

		private ModelTransform Current() => Sword().TpHandTransform
			?? throw new InvalidOperationException("Manaburn Sword has no third-person transform.");

		private void RefreshHeldItem() => api.World.Player.InventoryManager.ActiveHotbarSlot?.MarkDirty();

		private static float GetComponent(ModelTransform pose, string component) => component switch
		{
			"tx" => pose.Translation.X, "ty" => pose.Translation.Y, "tz" => pose.Translation.Z,
			"rx" => pose.Rotation.X, "ry" => pose.Rotation.Y, "rz" => pose.Rotation.Z,
			"ox" => pose.Origin.X, "oy" => pose.Origin.Y, "oz" => pose.Origin.Z,
			"scale" => pose.ScaleXYZ.X,
			_ => throw new ArgumentOutOfRangeException(nameof(component))
		};

		private static void SetComponent(ModelTransform pose, string component, float value)
		{
			switch (component)
			{
				case "tx": pose.Translation.X = value; break;
				case "ty": pose.Translation.Y = value; break;
				case "tz": pose.Translation.Z = value; break;
				case "rx": pose.Rotation.X = value; break;
				case "ry": pose.Rotation.Y = value; break;
				case "rz": pose.Rotation.Z = value; break;
				case "ox": pose.Origin.X = value; break;
				case "oy": pose.Origin.Y = value; break;
				case "oz": pose.Origin.Z = value; break;
				case "scale": pose.ScaleXYZ.Set(value, value, value); break;
				default: throw new ArgumentOutOfRangeException(nameof(component));
			}
		}

		private static string FormatPose(ModelTransform pose) => string.Format(
			CultureInfo.InvariantCulture,
			"Sword: T({0:0.###}, {1:0.###}, {2:0.###}) R({3:0.###}, {4:0.###}, {5:0.###}) O({6:0.###}, {7:0.###}, {8:0.###}) S({9:0.###})",
			pose.Translation.X, pose.Translation.Y, pose.Translation.Z,
			pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z,
			pose.Origin.X, pose.Origin.Y, pose.Origin.Z, pose.ScaleXYZ.X
		);

		private static string FormatJson(ModelTransform pose) => string.Format(
			CultureInfo.InvariantCulture,
			"{{\"translation\":{{\"x\":{0:0.#####},\"y\":{1:0.#####},\"z\":{2:0.#####}}},\"rotation\":{{\"x\":{3:0.#####},\"y\":{4:0.#####},\"z\":{5:0.#####}}},\"origin\":{{\"x\":{6:0.#####},\"y\":{7:0.#####},\"z\":{8:0.#####}}},\"scale\":{9:0.#####}}}",
			pose.Translation.X, pose.Translation.Y, pose.Translation.Z,
			pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z,
			pose.Origin.X, pose.Origin.Y, pose.Origin.Z, pose.ScaleXYZ.X
		);
	}
}
