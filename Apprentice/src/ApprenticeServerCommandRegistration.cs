using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
	internal static class ApprenticeServerCommandRegistration
	{
		private const BindingFlags StaticMethodFlags =
			BindingFlags.Static |
			BindingFlags.Public |
			BindingFlags.NonPublic;

		internal static void Register(
			ICoreServerAPI api,
			IServerNetworkChannel channel,
			EcologyWorldgenSystem ecologyWorldgenSystem,
			ConcentricRealmWorldgenSystem realmWorldgenSystem)
		{
			MethodInfo? combinedRegistration = FindRegistration(
				typeof(ICoreServerAPI),
				typeof(IServerNetworkChannel),
				typeof(EcologyWorldgenSystem)
			);
			if (combinedRegistration != null)
			{
				Invoke(
					combinedRegistration,
					api,
					channel,
					ecologyWorldgenSystem
				);
			}
			else
			{
				MethodInfo? coreRegistration = FindRegistration(
					typeof(ICoreServerAPI),
					typeof(IServerNetworkChannel)
				);
				if (coreRegistration == null)
				{
					throw new MissingMethodException(
						typeof(ItemCalibrationSystem).FullName,
						"RegisterServerCommand"
					);
				}

				Invoke(coreRegistration, api, channel);
				RegisterEcologyProbe(api, ecologyWorldgenSystem);
			}

			RegisterFrozenExpanseProbe(api, realmWorldgenSystem);
			RegisterPoisonMireProbe(api, realmWorldgenSystem);
			RegisterShatteredHighlandsProbe(api, realmWorldgenSystem);

			api.Logger.Notification(
				"[Apprentice] Chat-command runtime contract active: " +
				"every registered root, branch and executable leaf " +
				"declares its own privilege."
			);
		}

		private static MethodInfo? FindRegistration(params Type[] parameterTypes)
		{
			return typeof(ItemCalibrationSystem).GetMethod(
				"RegisterServerCommand",
				StaticMethodFlags,
				binder: null,
				types: parameterTypes,
				modifiers: null
			);
		}

		private static void Invoke(MethodInfo method, params object[] arguments)
		{
			try
			{
				method.Invoke(null, arguments);
			}
			catch (TargetInvocationException exception)
				when (exception.InnerException != null)
			{
				ExceptionDispatchInfo
					.Capture(exception.InnerException)
					.Throw();
			}
		}

		private static void RegisterEcologyProbe(
			ICoreServerAPI api,
			EcologyWorldgenSystem ecologyWorldgenSystem)
		{
			api.ChatCommands
				.GetOrCreate("apprentice")
				.RequiresPrivilege(Privilege.chat)
				.BeginSubCommand("ecology")
					.WithDescription(
						"Inspect Apprentice ecology world generation"
					)
					.RequiresPrivilege(Privilege.controlserver)
					.BeginSubCommand("probe")
						.WithDescription(
							"Generate and scan deterministic scratch chunks for Apprentice plants"
						)
						.WithAdditionalInformation(
							"Runs the real worldgen pipeline through PeekChunkColumn. " +
							"No saved or loaded chunks are modified."
						)
						.WithExamples(new[]
						{
							"/apprentice ecology probe"
						})
						.RequiresPrivilege(Privilege.controlserver)
						.HandleWith(
							ecologyWorldgenSystem.StartWorldgenProbe
						)
					.EndSubCommand()
				.EndSubCommand();
		}

		private static void RegisterFrozenExpanseProbe(
			ICoreServerAPI api,
			ConcentricRealmWorldgenSystem realmWorldgenSystem)
		{
			api.ChatCommands
				.GetOrCreate("apprentice")
				.RequiresPrivilege(Privilege.chat)
				.BeginSubCommand("frozen")
					.WithDescription(
						"Inspect Frozen Expanse world generation"
					)
					.RequiresPrivilege(Privilege.controlserver)
					.BeginSubCommand("probe")
						.WithDescription(
							"Generate and scan temporary Level 5 chunks"
						)
						.WithAdditionalInformation(
							"Reports glacier coverage, trees, open water, " +
							"terrain range and cave columns. No saved or " +
							"loaded chunks are modified."
						)
						.WithExamples(new[]
						{
							"/apprentice frozen probe"
						})
						.RequiresPrivilege(Privilege.controlserver)
						.HandleWith(
							realmWorldgenSystem.StartFrozenExpanseProbe
						)
					.EndSubCommand()
					.BeginSubCommand("spikes")
						.WithDescription(
							"Locate and validate Level 5 Ice-Spike Fields"
						)
						.RequiresPrivilege(Privilege.controlserver)
						.BeginSubCommand("locate")
							.WithDescription(
								"Report the nearest deterministic Ice-Spike Field"
							)
							.WithAdditionalInformation(
								"Searches from your position when you are inside " +
								"Level 5, otherwise from the nearest direction " +
								"through the middle of the Frozen Expanse."
							)
							.WithExamples(new[]
							{
								"/apprentice frozen spikes locate"
							})
							.RequiresPrivilege(Privilege.controlserver)
							.HandleWith(
								realmWorldgenSystem
									.LocateNearestIceSpikeField
							)
						.EndSubCommand()
						.BeginSubCommand("probe")
							.WithDescription(
								"Generate and validate a chunk-crossing ice-spike cluster"
							)
							.WithAdditionalInformation(
								"Reports field counts, spike height, glacier " +
								"blocks, open ground, chunk-border continuity " +
								"and generation time. No saved or loaded chunks " +
								"are modified."
							)
							.WithExamples(new[]
							{
								"/apprentice frozen spikes probe"
							})
							.RequiresPrivilege(Privilege.controlserver)
							.HandleWith(
								realmWorldgenSystem.StartIceSpikeProbe
							)
						.EndSubCommand()
					.EndSubCommand()
				.EndSubCommand();
		}

		private static void RegisterPoisonMireProbe(
			ICoreServerAPI api,
			ConcentricRealmWorldgenSystem realmWorldgenSystem)
		{
			api.ChatCommands
				.GetOrCreate("apprentice")
				.RequiresPrivilege(Privilege.chat)
				.BeginSubCommand("mire")
					.WithDescription(
						"Inspect Poison Mire world generation"
					)
					.RequiresPrivilege(Privilege.controlserver)
					.BeginSubCommand("probe")
						.WithDescription(
							"Generate and scan temporary Level 6 chunks"
						)
						.WithAdditionalInformation(
							"Reports dry bog islands, gentle routes, shallow " +
							"fresh water, deep water, salt water, trees and " +
							"terrain range. No saved or loaded chunks are modified."
						)
						.WithExamples(new[]
						{
							"/apprentice mire probe"
						})
						.RequiresPrivilege(Privilege.controlserver)
						.HandleWith(
							realmWorldgenSystem.StartPoisonMireProbe
						)
					.EndSubCommand()
				.EndSubCommand();
		}

		private static void RegisterShatteredHighlandsProbe(
			ICoreServerAPI api,
			ConcentricRealmWorldgenSystem realmWorldgenSystem)
		{
			api.ChatCommands
				.GetOrCreate("apprentice")
				.RequiresPrivilege(Privilege.chat)
				.BeginSubCommand("highlands")
					.WithDescription(
						"Inspect Shattered Highlands world generation"
					)
					.RequiresPrivilege(Privilege.controlserver)
					.BeginSubCommand("probe")
						.WithDescription(
							"Generate and scan temporary Level 7 chunks"
						)
						.WithAdditionalInformation(
							"Reports terrain, routes and a deterministic ruined-city " +
							"generation proof. No saved or loaded chunks are modified."
						)
						.WithExamples(new[]
						{
							"/apprentice highlands probe"
						})
						.RequiresPrivilege(Privilege.controlserver)
						.HandleWith(
							realmWorldgenSystem
								.StartShatteredHighlandsProbe
						)
					.EndSubCommand()
					.BeginSubCommand("ruins")
						.WithDescription(
							"Inspect Level 7 ruined valley cities"
						)
						.RequiresPrivilege(Privilege.controlserver)
						.BeginSubCommand("locate")
							.WithDescription(
								"Locate the nearest deterministic Level 7 ruined city"
							)
							.WithAdditionalInformation(
								"Reports map coordinates, distance, culture, layout " +
								"signature and whether a saved landmark anchor exists."
							)
							.WithExamples(new[]
							{
								"/apprentice highlands ruins locate"
							})
							.RequiresPrivilege(Privilege.controlserver)
							.HandleWith(
								realmWorldgenSystem
									.LocateNearestShatteredHighlandsRuins
							)
						.EndSubCommand()
						.BeginSubCommand("probe")
							.WithDescription(
								"Prove Level 7 ruined-city generation in scratch chunks"
							)
							.WithAdditionalInformation(
								"Runs the real worldgen pipeline at a deterministic city " +
								"and requires a landmark plus at least eight modules. " +
								"No saved or loaded chunks are modified."
							)
							.WithExamples(new[]
							{
								"/apprentice highlands ruins probe"
							})
							.RequiresPrivilege(Privilege.controlserver)
							.HandleWith(
								realmWorldgenSystem
									.StartShatteredHighlandsRuinsProbe
							)
						.EndSubCommand()
					.EndSubCommand()
				.EndSubCommand();
		}
	}
}
