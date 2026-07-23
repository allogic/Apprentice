using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Apprentice
{
	internal sealed class WarScytheCalibrationSystem : IDisposable
	{
		private const string ScytheCode = "apprentice:warscythe";
		private const float MaximumTranslation = 8f;
		private const float MaximumOrigin = 4f;
		private const float MinimumScale = 0.05f;
		private const float MaximumScale = 5f;
		private const float MaximumRotation = 3600f;
		private static readonly string[] Components =
		{
			"tx", "ty", "tz", "rx", "ry", "rz", "ox", "oy", "oz", "scale"
		};

		private readonly ICoreClientAPI api;
		private readonly ModelTransform baseline;
		private ModelTransform current;
		private static WarScytheCalibrationSystem? active;

		public WarScytheCalibrationSystem(ICoreClientAPI api)
		{
			this.api = api;
			baseline = Scythe().TpHandTransform?.Clone()
				?? throw new InvalidOperationException(
					"War Scythe has no third-person transform."
				);
			baseline.EnsureDefaultValues();
			current = baseline.Clone();
			active = this;

			CommandArgumentParsers parsers = api.ChatCommands.Parsers;
			api.ChatCommands.Create("scythecal")
				.WithDescription("Live War Scythe third-person pose calibration")
				.WithAdditionalInformation(
					"Components: tx ty tz rx ry rz ox oy oz scale. " +
					"Use .scythecal <component> <amount> for a direct adjustment, " +
					"Use .scythecal add <component> <amount>, .scythecal set <component> <value>, " +
					".scythecal show, .scythecal reset, or .scythecal export."
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
				.BeginSubCommand("tx").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("tx", args)).EndSubCommand()
				.BeginSubCommand("ty").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("ty", args)).EndSubCommand()
				.BeginSubCommand("tz").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("tz", args)).EndSubCommand()
				.BeginSubCommand("rx").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("rx", args)).EndSubCommand()
				.BeginSubCommand("ry").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("ry", args)).EndSubCommand()
				.BeginSubCommand("rz").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("rz", args)).EndSubCommand()
				.BeginSubCommand("ox").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("ox", args)).EndSubCommand()
				.BeginSubCommand("oy").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("oy", args)).EndSubCommand()
				.BeginSubCommand("oz").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("oz", args)).EndSubCommand()
				.BeginSubCommand("scale").WithArgs(parsers.Float("amount")).HandleWith(args => AddDirect("scale", args)).EndSubCommand()
				.BeginSubCommand("reset").HandleWith(Reset).EndSubCommand()
				.BeginSubCommand("export").HandleWith(Export).EndSubCommand();

			api.Logger.Notification("[Apprentice] War Scythe live calibration enabled. Use .scythecal show.");
		}

		private TextCommandResult Show(TextCommandCallingArgs args) =>
			TextCommandResult.Success(FormatPose(current));

		private TextCommandResult Add(TextCommandCallingArgs args)
		{
			string component = (string)args[0];
			float amount = Convert.ToSingle(args[1], CultureInfo.InvariantCulture);
			return Change(component, amount, relative: true);
		}

		private TextCommandResult Set(TextCommandCallingArgs args)
		{
			string component = (string)args[0];
			float value = Convert.ToSingle(args[1], CultureInfo.InvariantCulture);
			return Change(component, value, relative: false);
		}

		private TextCommandResult AddDirect(
			string component,
			TextCommandCallingArgs args)
		{
			float amount = Convert.ToSingle(args[0], CultureInfo.InvariantCulture);
			return Change(component, amount, relative: true);
		}

		private TextCommandResult Change(
			string component,
			float value,
			bool relative)
		{
			ModelTransform next = current.Clone();
			float requested = relative
				? GetComponent(next, component) + value
				: value;

			string? validationError = Validate(component, requested);
			if (validationError != null)
			{
				return TextCommandResult.Success(
					"Scythe calibration unchanged: " + validationError + " " +
					FormatPose(current)
				);
			}

			SetComponent(next, component, requested);
			current = next;
			return TextCommandResult.Success(FormatPose(current));
		}

		private TextCommandResult Reset(TextCommandCallingArgs args)
		{
			current = baseline.Clone();
			return TextCommandResult.Success(
				"Scythe calibration reset. " + FormatPose(current)
			);
		}

		private TextCommandResult Export(TextCommandCallingArgs args)
		{
			string json = FormatJson(current);
			api.Logger.Notification("[Apprentice] SCYTHECAL FINAL {0}", json);
			return TextCommandResult.Success(
				"SCYTHECAL FINAL written to client-main.log. " +
				FormatPose(current)
			);
		}

		internal static ModelTransform? GetThirdPersonPreview() =>
			active?.current;

		public void Dispose()
		{
			if (ReferenceEquals(active, this)) active = null;
		}

		private Item Scythe() => api.World.GetItem(new AssetLocation(ScytheCode))
			?? throw new InvalidOperationException("War Scythe is missing.");

		private static string? Validate(string component, float value)
		{
			if (!float.IsFinite(value))
			{
				return component + " must be a finite number.";
			}

			if ((component == "tx" || component == "ty" || component == "tz") &&
				Math.Abs(value) > MaximumTranslation)
			{
				return component + " must stay between -8 and 8. " +
					"Use small translation steps such as 0.05.";
			}

			if ((component == "ox" || component == "oy" || component == "oz") &&
				Math.Abs(value) > MaximumOrigin)
			{
				return component + " must stay between -4 and 4. " +
					"Use small origin steps such as 0.05.";
			}

			if (component == "scale" &&
				(value < MinimumScale || value > MaximumScale))
			{
				return "scale must stay between 0.05 and 5.";
			}

			if ((component == "rx" || component == "ry" || component == "rz") &&
				Math.Abs(value) > MaximumRotation)
			{
				return component + " must stay between -3600 and 3600 degrees.";
			}

			return null;
		}

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
			"Scythe: T({0:0.###}, {1:0.###}, {2:0.###}) R({3:0.###}, {4:0.###}, {5:0.###}) O({6:0.###}, {7:0.###}, {8:0.###}) S({9:0.###})",
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
