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
	/// Native entity-death adapter for player-attributed kills.
	/// </summary>
	internal sealed class EntityDeathInteractionAdapter
		: IInteractionEventAdapter
	{
		private readonly ICoreServerAPI serverApi;
		private readonly ExperienceManager experienceManager;

		private bool disposed;

		public EntityDeathInteractionAdapter(
			ICoreServerAPI serverApi,
			ExperienceManager experienceManager)
		{
			this.serverApi = serverApi
				?? throw new ArgumentNullException(
					nameof(serverApi)
				);

			this.experienceManager = experienceManager
				?? throw new ArgumentNullException(
					nameof(experienceManager)
				);

			serverApi.Event.OnEntityDeath +=
				OnEntityDeath;
		}

		private void OnEntityDeath(
			Entity deadEntity,
			DamageSource damageSource)
		{
			try
			{
				if (deadEntity is EntityPlayer deadPlayerEntity &&
					deadPlayerEntity.Player is
					IServerPlayer deadPlayer)
				{
					experienceManager
						.LoseCurrentLevelProgress(deadPlayer);
				}

				if (deadEntity?.Code == null ||
					damageSource == null)
				{
					return;
				}

				Entity? causeEntity =
					damageSource.GetCauseEntity();

				if (causeEntity is not
					EntityPlayer killerEntity)
				{
					return;
				}

				if (killerEntity.EntityId ==
					deadEntity.EntityId)
				{
					return;
				}

				if (killerEntity.Player is not
					IServerPlayer killerPlayer)
				{
					return;
				}

				string interaction =
					deadEntity is EntityPlayer
						? InteractionNames.KillPvP
						: InteractionNames.KillPvE;

				// Victim-based reward remains available for skills
				// such as Hunter.
				experienceManager.HandleInteraction(
					new InteractionContext(
						killerPlayer,
						interaction,
						deadEntity.Code
					)
				);

				AssetLocation? weaponCode =
					CompletionInteractionPatches
						.ConsumeWeaponForDeath(
							deadEntity,
							damageSource,
							killerEntity
						);

				if (weaponCode != null)
				{
					string weaponInteraction =
						deadEntity is EntityPlayer
							? InteractionNames
								.WeaponKillPvP
							: InteractionNames
								.WeaponKillPvE;

					experienceManager.HandleInteraction(
						new InteractionContext(
							killerPlayer,
							weaponInteraction,
							weaponCode
						)
					);
				}
			}
			catch (Exception exception)
			{
				serverApi.Logger.Error(
					"[Apprentice] Failed to process an " +
					"entity-death interaction."
				);

				serverApi.Logger.Error(exception);
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			serverApi.Event.OnEntityDeath -=
				OnEntityDeath;
		}
	}
}
