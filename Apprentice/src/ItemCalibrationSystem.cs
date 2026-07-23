using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
	[ProtoContract]
	public sealed class ItemCalibrationCommandPacket
	{
		[ProtoMember(1)]
		public string Arguments { get; set; } = string.Empty;
	}

	/// <summary>
	/// Client-owned live third-person calibration for every loaded item. Slash
	/// commands are relayed by the server only to the player who issued them;
	/// no collectible or inventory state is changed on the server.
	/// </summary>
	internal sealed class ItemCalibrationSystem : IDisposable
	{
		private const string RenderHarmonyId =
			"apprentice.item-calibration-render";
		private const string WarScytheCode = "apprentice:warscythe";
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
		private readonly Dictionary<string, ModelTransform> baselines =
			new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, ModelTransform> previews =
			new(StringComparer.OrdinalIgnoreCase);
		private Harmony? renderHarmony;
		private static ItemCalibrationSystem? active;

		public ItemCalibrationSystem(
			ICoreClientAPI api,
			IClientNetworkChannel channel)
		{
			this.api = api;
			active = this;
			channel.SetMessageHandler<ItemCalibrationCommandPacket>(
				OnCommandPacket
			);

			InstallRenderPatches();
			RegisterLegacyScytheCommand();

			api.Logger.Notification(
				"[Apprentice] Generic item calibration enabled. " +
				"Use /apprentice calibrate <itemname> <show|set|add|reset|export>."
			);
		}

		internal static void RegisterServerCommand(
			ICoreServerAPI api,
			IServerNetworkChannel channel)
		{
			CommandArgumentParsers parsers = api.ChatCommands.Parsers;
			api.ChatCommands.Create("apprentice")
				.WithDescription("Apprentice administration and developer tools")
				.RequiresPlayer()
				.BeginSubCommand("calibrate")
					.WithDescription(
						"Live third-person held-item transform calibration"
					)
					.WithAdditionalInformation(
						"Components: tx ty tz rx ry rz ox oy oz scale. " +
						"Use an exact item code (game:axe-copper) or a unique short path."
					)
					.WithExamples(new[]
					{
						"/apprentice calibrate apprentice:warscythe show",
						"/apprentice calibrate game:axe-copper add rx 5",
						"/apprentice calibrate game:axe-copper set scale 0.8",
						"/apprentice calibrate apprentice:warscythe export"
					})
					.WithArgs(parsers.All("item and calibration operation"))
					.HandleWith(args => RelayToIssuingClient(args, channel))
				.EndSubCommand();
		}

		private static TextCommandResult RelayToIssuingClient(
			TextCommandCallingArgs args,
			IServerNetworkChannel channel)
		{
			string arguments = args.ArgCount > 0
				? Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? string.Empty
				: string.Empty;

			if (args.Caller.Player is not IServerPlayer player)
			{
				return TextCommandResult.Error(
					"This command must be used by a player.",
					"apprentice-calibration-player-required"
				);
			}

			if (string.IsNullOrWhiteSpace(arguments))
			{
				return TextCommandResult.Error(
					Usage,
					"apprentice-calibration-usage"
				);
			}

			channel.SendPacket(
				new ItemCalibrationCommandPacket { Arguments = arguments },
				player
			);
			return TextCommandResult.Success(
				"Calibration request sent to your client."
			);
		}

		private void OnCommandPacket(ItemCalibrationCommandPacket packet)
		{
			api.Event.EnqueueMainThreadTask(
				() =>
				{
					if (!ReferenceEquals(active, this)) return;
					api.ShowChatMessage(
						"[Apprentice] " + Execute(packet.Arguments)
					);
				},
				"apprentice-item-calibration"
			);
		}

		private string Execute(string rawArguments)
		{
			string[] tokens = rawArguments.Split(
				new[] { ' ' },
				StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries
			);
			if (tokens.Length < 2)
			{
				return Usage;
			}

			if (!TryResolveItem(tokens[0], out Item? item, out string error))
			{
				return error;
			}

			string operation = tokens[1].ToLowerInvariant();
			switch (operation)
			{
				case "show":
					return tokens.Length == 2
						? FormatPose(item!, GetCurrent(item!))
						: Usage;

				case "reset":
					if (tokens.Length != 2) return Usage;
					return Reset(item!);

				case "export":
					if (tokens.Length != 2) return Usage;
					return Export(item!);

				case "add":
				case "set":
					if (tokens.Length != 4) return Usage;
					if (!Components.Contains(
						tokens[2],
						StringComparer.OrdinalIgnoreCase
					))
					{
						return "Unknown component '" + tokens[2] +
							"'. Components: " + string.Join(" ", Components) + ".";
					}
					if (!float.TryParse(
						tokens[3],
						NumberStyles.Float,
						CultureInfo.InvariantCulture,
						out float value))
					{
						return "'" + tokens[3] +
							"' is not a valid number. Use a decimal point.";
					}
					return Change(
						item!,
						tokens[2].ToLowerInvariant(),
						value,
						relative: operation == "add"
					);

				default:
					return "Unknown operation '" + tokens[1] + "'. " + Usage;
			}
		}

		private bool TryResolveItem(
			string requested,
			out Item? resolved,
			out string error)
		{
			resolved = null;
			error = string.Empty;
			string query = requested.Trim();
			if (query.Length == 0)
			{
				error = Usage;
				return false;
			}

			Item[] loaded = api.World.Items
				.Where(item => item?.Code != null && !item.IsMissing)
				.ToArray();
			bool hasDomain = query.Contains(':');
			Item[] exact = loaded
				.Where(item =>
					item.Code.ToString().Equals(
						query,
						StringComparison.OrdinalIgnoreCase
					) ||
					(!hasDomain && item.Code.Path.Equals(
						query,
						StringComparison.OrdinalIgnoreCase
					)))
				.ToArray();

			if (exact.Length == 1)
			{
				resolved = exact[0];
				return true;
			}

			if (exact.Length > 1)
			{
				error = FormatAmbiguous(query, exact);
				return false;
			}

			Item[] partial = loaded
				.Where(item =>
					item.Code.ToString().Contains(
						query,
						StringComparison.OrdinalIgnoreCase
					))
				.Take(9)
				.ToArray();
			if (partial.Length == 1)
			{
				resolved = partial[0];
				return true;
			}

			if (partial.Length > 1)
			{
				error = FormatAmbiguous(query, partial);
				return false;
			}

			error = "No loaded item matches '" + query + "'. " +
				"Use its full code, for example game:axe-copper.";
			return false;
		}

		private static string FormatAmbiguous(string query, Item[] matches)
		{
			string codes = string.Join(
				", ",
				matches.Take(8).Select(item => item.Code.ToString())
			);
			string suffix = matches.Length > 8 ? ", ..." : string.Empty;
			return "Item name '" + query + "' is ambiguous. Use one of: " +
				codes + suffix + ".";
		}

		private string Change(
			Item item,
			string component,
			float value,
			bool relative)
		{
			ModelTransform next = GetCurrent(item).Clone();
			float requested = relative
				? GetComponent(next, component) + value
				: value;

			string? validationError = Validate(component, requested);
			if (validationError != null)
			{
				return "Calibration unchanged: " + validationError + " " +
					FormatPose(item, GetCurrent(item));
			}

			SetComponent(next, component, requested);
			previews[CodeOf(item)] = next;
			return FormatPose(item, next);
		}

		private string Reset(Item item)
		{
			previews.Remove(CodeOf(item));
			return "Calibration reset. " + FormatPose(item, GetBaseline(item));
		}

		private string Export(Item item)
		{
			ModelTransform pose = GetCurrent(item);
			string code = CodeOf(item);
			string json = FormatJson(pose);
			api.Logger.Notification(
				"[Apprentice] ITEMCAL FINAL code={0} {1}",
				code,
				json
			);
			return "ITEMCAL FINAL for " + code +
				" written to client-main.log. " + FormatPose(item, pose);
		}

		private ModelTransform GetCurrent(Item item)
		{
			string code = CodeOf(item);
			return previews.TryGetValue(code, out ModelTransform? preview)
				? preview
				: GetBaseline(item);
		}

		private ModelTransform GetBaseline(Item item)
		{
			string code = CodeOf(item);
			if (baselines.TryGetValue(code, out ModelTransform? baseline))
			{
				return baseline;
			}

			baseline = item.TpHandTransform?.Clone() ??
				ModelTransform.ItemDefaultTp();
			baseline.EnsureDefaultValues();
			baselines[code] = baseline;
			return baseline;
		}

		internal static ModelTransform? GetThirdPersonPreview(
			AssetLocation? code)
		{
			ItemCalibrationSystem? system = active;
			if (system == null || code == null) return null;

			return system.previews.TryGetValue(
				code.ToString(),
				out ModelTransform? preview
			) ? preview : null;
		}

		private void InstallRenderPatches()
		{
			try
			{
				renderHarmony = new Harmony(RenderHarmonyId);
				Type[] signature =
				{
					typeof(ICoreClientAPI),
					typeof(ItemStack),
					typeof(EnumItemRenderTarget),
					typeof(ItemRenderInfo).MakeByRefType()
				};
				MethodInfo postfix = typeof(ItemCalibrationRenderPatch)
					.GetMethod(
						nameof(ItemCalibrationRenderPatch.Postfix),
						BindingFlags.Static | BindingFlags.Public
					) ?? throw new MissingMethodException(
						nameof(ItemCalibrationRenderPatch.Postfix)
					);

				MethodInfo[] renderMethods = api.World.Items
					.Where(item => item != null)
					.Select(item => item.GetType().GetMethod(
						nameof(CollectibleObject.OnBeforeRender),
						BindingFlags.Instance | BindingFlags.Public,
						binder: null,
						types: signature,
						modifiers: null
					))
					.Where(method => method != null)
					.Cast<MethodInfo>()
					.Distinct()
					.ToArray();

				int patched = 0;
				foreach (MethodInfo renderMethod in renderMethods)
				{
					try
					{
						renderHarmony.Patch(
							renderMethod,
							postfix: new HarmonyMethod(postfix)
						);
						patched++;
					}
					catch (Exception exception)
					{
						api.Logger.Warning(
							"[Apprentice] Could not attach item calibration to {0}: {1}",
							renderMethod.DeclaringType?.FullName ?? "unknown item type",
							exception.Message
						);
					}
				}

				api.Logger.Notification(
					"[Apprentice] Generic item calibration attached to {0} held-item render methods.",
					patched
				);
			}
			catch (Exception exception)
			{
				api.Logger.Error(
					"[Apprentice] Generic item calibration render integration failed: {0}",
					exception.Message
				);
			}
		}

		private void RegisterLegacyScytheCommand()
		{
			api.ChatCommands.Create("scythecal")
				.WithDescription(
					"Compatibility alias for War Scythe calibration"
				)
				.WithAdditionalInformation(
					"Prefer /apprentice calibrate apprentice:warscythe ..."
				)
				.WithArgs(api.ChatCommands.Parsers.All("calibration operation"))
				.HandleWith(args =>
				{
					string legacy = Convert.ToString(
						args[0],
						CultureInfo.InvariantCulture
					) ?? string.Empty;
					string[] tokens = legacy.Split(
						new[] { ' ' },
						StringSplitOptions.RemoveEmptyEntries |
						StringSplitOptions.TrimEntries
					);
					if (tokens.Length == 2 && Components.Contains(
						tokens[0],
						StringComparer.OrdinalIgnoreCase
					))
					{
						legacy = "add " + legacy;
					}

					return TextCommandResult.Success(
						Execute(WarScytheCode + " " + legacy)
					);
				});
		}

		public void Dispose()
		{
			renderHarmony?.UnpatchAll(RenderHarmonyId);
			renderHarmony = null;
			previews.Clear();
			baselines.Clear();
			if (ReferenceEquals(active, this)) active = null;
		}

		private static string CodeOf(Item item) => item.Code.ToString();

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

		private static float GetComponent(
			ModelTransform pose,
			string component) => component switch
		{
			"tx" => pose.Translation.X,
			"ty" => pose.Translation.Y,
			"tz" => pose.Translation.Z,
			"rx" => pose.Rotation.X,
			"ry" => pose.Rotation.Y,
			"rz" => pose.Rotation.Z,
			"ox" => pose.Origin.X,
			"oy" => pose.Origin.Y,
			"oz" => pose.Origin.Z,
			"scale" => pose.ScaleXYZ.X,
			_ => throw new ArgumentOutOfRangeException(nameof(component))
		};

		private static void SetComponent(
			ModelTransform pose,
			string component,
			float value)
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

		private static string FormatPose(Item item, ModelTransform pose) =>
			string.Format(
				CultureInfo.InvariantCulture,
				"{0}: T({1:0.###}, {2:0.###}, {3:0.###}) R({4:0.###}, {5:0.###}, {6:0.###}) O({7:0.###}, {8:0.###}, {9:0.###}) S({10:0.###})",
				CodeOf(item),
				pose.Translation.X,
				pose.Translation.Y,
				pose.Translation.Z,
				pose.Rotation.X,
				pose.Rotation.Y,
				pose.Rotation.Z,
				pose.Origin.X,
				pose.Origin.Y,
				pose.Origin.Z,
				pose.ScaleXYZ.X
			);

		private static string FormatJson(ModelTransform pose) => string.Format(
			CultureInfo.InvariantCulture,
			"{{\"translation\":{{\"x\":{0:0.#####},\"y\":{1:0.#####},\"z\":{2:0.#####}}},\"rotation\":{{\"x\":{3:0.#####},\"y\":{4:0.#####},\"z\":{5:0.#####}}},\"origin\":{{\"x\":{6:0.#####},\"y\":{7:0.#####},\"z\":{8:0.#####}}},\"scale\":{9:0.#####}}}",
			pose.Translation.X,
			pose.Translation.Y,
			pose.Translation.Z,
			pose.Rotation.X,
			pose.Rotation.Y,
			pose.Rotation.Z,
			pose.Origin.X,
			pose.Origin.Y,
			pose.Origin.Z,
			pose.ScaleXYZ.X
		);

		private const string Usage =
			"Usage: /apprentice calibrate <itemname> " +
			"<show|reset|export|add component amount|set component value>.";
	}

	internal static class ItemCalibrationRenderPatch
	{
		public static void Postfix(
			CollectibleObject __instance,
			EnumItemRenderTarget target,
			ref ItemRenderInfo renderinfo)
		{
			if (target != EnumItemRenderTarget.HandTp) return;

			ModelTransform? preview =
				ItemCalibrationSystem.GetThirdPersonPreview(__instance.Code);
			if (preview != null)
			{
				renderinfo.Transform = preview;
			}
		}
	}
}
