using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;


namespace Apprentice
{
	/// <summary>
	/// Lifetime owner for independent server interaction adapters.
	/// </summary>
	internal sealed class InteractionEventBridge : IDisposable
	{
		private readonly List<IInteractionEventAdapter> adapters =
			new();

		private bool disposed;

		public InteractionEventBridge(
			ICoreServerAPI serverApi,
			ExperienceManager experienceManager)
		{
			ArgumentNullException.ThrowIfNull(serverApi);
			ArgumentNullException.ThrowIfNull(
				experienceManager
			);

			try
			{
				adapters.Add(
					new BlockInteractionAdapter(
						serverApi,
						experienceManager
					)
				);

				adapters.Add(
					new EntityDeathInteractionAdapter(
						serverApi,
						experienceManager
					)
				);

				adapters.Add(
					new CompletionInteractionAdapter(
						serverApi,
						experienceManager
					)
				);
			}
			catch
			{
				Dispose();
				throw;
			}

			serverApi.Logger.Notification(
				"[Apprentice] Interaction adapters ready: " +
				"DestroyBlock, PlaceBlock, Plant, Harvest, " +
				"KillPvE, KillPvP, weapon kills, ShieldBlock, " +
				"DamageTaken, Craft, ClayForm, Repair, Smith, " +
				"Prospect, Pan, Fish, Breed, Milk, Shear, " +
				"Harvest, Smelt, Cook and Process."
			);
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			for (int index = adapters.Count - 1;
				 index >= 0;
				 index--)
			{
				adapters[index].Dispose();
			}

			adapters.Clear();
		}
	}
}
