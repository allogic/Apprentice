using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Apprentice
{
    /// <summary>
    /// Client-only live calibration for the Master Fishing Rod. It changes the
    /// actual held transform used by the renderer and reports exact values, so
    /// the final pose can be captured without rebuild-and-screenshot guessing.
    /// </summary>
    internal sealed class FishingRodCalibrationSystem
    {
        private const string RodCode = "apprentice:masterfishingrod";
        private readonly ICoreClientAPI api;
        private readonly ModelTransform baseline;

        public FishingRodCalibrationSystem(ICoreClientAPI api)
        {
            this.api = api;
            Item rod = api.World.GetItem(new AssetLocation(RodCode))
                ?? throw new InvalidOperationException("Master Fishing Rod is missing.");
            baseline = (rod.TpHandTransform
                ?? throw new InvalidOperationException("Master Fishing Rod has no third-person transform."))
                .Clone();

            CommandArgumentParsers parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("rodcal")
                .WithDescription("Live Master Fishing Rod third-person pose calibration")
                .WithAdditionalInformation(
                    "Components: tx ty tz rx ry rz ox oy oz scale. " +
                    "Use .rodcal add <component> <amount>, .rodcal set <component> <value>, " +
                    ".rodcal show, or .rodcal reset. Changes are client-side and live."
                )
                .BeginSubCommand("show")
                    .HandleWith(Show)
                .EndSubCommand()
                .BeginSubCommand("add")
                    .WithArgs(
                        parsers.WordRange("component", Components),
                        parsers.Float("amount")
                    )
                    .HandleWith(Add)
                .EndSubCommand()
                .BeginSubCommand("set")
                    .WithArgs(
                        parsers.WordRange("component", Components),
                        parsers.Float("value")
                    )
                    .HandleWith(Set)
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .HandleWith(Reset)
                .EndSubCommand()
                .BeginSubCommand("export")
                    .HandleWith(Export)
                .EndSubCommand();

            api.Logger.Notification(
                "[Apprentice] Fishing Rod live calibration enabled. Use .rodcal show."
            );
        }

        private static readonly string[] Components =
        {
            "tx", "ty", "tz", "rx", "ry", "rz", "ox", "oy", "oz", "scale"
        };

        private TextCommandResult Show(TextCommandCallingArgs args)
        {
            return TextCommandResult.Success(FormatPose(Current()));
        }

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
            Rod().TpHandTransform = baseline.Clone();
            RefreshHeldItem();
            return TextCommandResult.Success("Rod calibration reset. " + FormatPose(Current()));
        }

        private TextCommandResult Export(TextCommandCallingArgs args)
        {
            string json = FormatJson(Current());
            api.Logger.Notification("[Apprentice] RODCAL FINAL {0}", json);
            return TextCommandResult.Success("RODCAL FINAL (also written to client-main.log): " + json);
        }

        private Item Rod()
        {
            return api.World.GetItem(new AssetLocation(RodCode))
                ?? throw new InvalidOperationException("Master Fishing Rod is missing.");
        }

        private ModelTransform Current()
        {
            return Rod().TpHandTransform
                ?? throw new InvalidOperationException("Master Fishing Rod has no third-person transform.");
        }

        private void RefreshHeldItem()
        {
            api.World.Player.InventoryManager.ActiveHotbarSlot?.MarkDirty();
        }

        private static float GetComponent(ModelTransform pose, string component)
        {
            return component switch
            {
                "tx" => pose.Translation.X, "ty" => pose.Translation.Y, "tz" => pose.Translation.Z,
                "rx" => pose.Rotation.X, "ry" => pose.Rotation.Y, "rz" => pose.Rotation.Z,
                "ox" => pose.Origin.X, "oy" => pose.Origin.Y, "oz" => pose.Origin.Z,
                "scale" => pose.ScaleXYZ.X,
                _ => throw new ArgumentOutOfRangeException(nameof(component))
            };
        }

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

        private static string FormatPose(ModelTransform pose)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Rod: T({0:0.###}, {1:0.###}, {2:0.###}) R({3:0.###}, {4:0.###}, {5:0.###}) O({6:0.###}, {7:0.###}, {8:0.###}) S({9:0.###})",
                pose.Translation.X, pose.Translation.Y, pose.Translation.Z,
                pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z,
                pose.Origin.X, pose.Origin.Y, pose.Origin.Z, pose.ScaleXYZ.X
            );
        }

        private static string FormatJson(ModelTransform pose)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{{\"translation\":{{\"x\":{0:0.#####},\"y\":{1:0.#####},\"z\":{2:0.#####}}},\"rotation\":{{\"x\":{3:0.#####},\"y\":{4:0.#####},\"z\":{5:0.#####}}},\"origin\":{{\"x\":{6:0.#####},\"y\":{7:0.#####},\"z\":{8:0.#####}}},\"scale\":{9:0.#####}}}",
                pose.Translation.X, pose.Translation.Y, pose.Translation.Z,
                pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z,
                pose.Origin.X, pose.Origin.Y, pose.Origin.Z, pose.ScaleXYZ.X
            );
        }
    }
}
