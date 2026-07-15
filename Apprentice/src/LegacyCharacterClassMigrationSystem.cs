using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
	/// <summary>
	/// Repairs saves whose selected character class was removed when the
	/// fantasy-race roster replaced the vanilla/modded class roster.
	///
	/// CharacterSystem opens the selection dialog by first re-applying the
	/// saved class code. Vintage Story throws when that code is no longer in
	/// the enabled class list, so the repair has to happen on the server before
	/// CharacterSystem handles PlayerJoin.
	/// </summary>
	public sealed class LegacyCharacterClassMigrationSystem : ModSystem
	{
		private static readonly HashSet<string> ValidRaceCodes =
			new HashSet<string>(StringComparer.Ordinal)
			{
				"apprentice-race-dragonborn",
				"apprentice-race-dwarf",
				"apprentice-race-elf",
				"apprentice-race-gnome",
				"apprentice-race-goliath",
				"apprentice-race-halfling",
				"apprentice-race-human",
				"apprentice-race-orc",
				"apprentice-race-tiefling"
			};

		private ICoreServerAPI? serverApi;
		private ICoreClientAPI? clientApi;

		public override double ExecuteOrder()
		{
			// CharacterSystem uses the default order. Register our PlayerJoin
			// handlers first so legacy class codes are repaired before its
			// character-selection dialog reads them.
			return 0.01;
		}

		public override void StartClientSide(ICoreClientAPI api)
		{
			clientApi = api;
			api.Event.PlayerJoin += OnClientPlayerJoin;
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			serverApi = api;
			api.Event.PlayerJoin += OnPlayerJoin;
		}

		private void OnPlayerJoin(IServerPlayer player)
		{
			string? classCode =
				player.Entity.WatchedAttributes.GetString(
					"characterClass",
					null
				);

			if (classCode != null && ValidRaceCodes.Contains(classCode))
			{
				return;
			}

			// A brand-new player has neither a saved selection state nor a
			// class code. Leave that normal first-join flow untouched.
			if (classCode == null &&
				player.GetModdata("createCharacter") == null)
			{
				return;
			}

			player.RemoveModdata("createCharacter");
			player.Entity.WatchedAttributes.RemoveAttribute(
				"allowcharselonce"
			);
			player.Entity.WatchedAttributes.RemoveAttribute(
				"characterClass"
			);
			player.Entity.WatchedAttributes.RemoveAttribute(
				"extraTraits"
			);
			player.Entity.WatchedAttributes.MarkAllDirty();

			serverApi?.Logger.Notification(
				"[Apprentice] Reset invalid legacy character class " +
				$"'{classCode ?? "<missing>"}' for player " +
				$"'{player.PlayerName}'. Race selection will open on join."
			);
		}

		private void OnClientPlayerJoin(IClientPlayer player)
		{
			if (clientApi == null ||
				player.PlayerUID != clientApi.World.Player.PlayerUID)
			{
				return;
			}

			string? classCode =
				player.Entity.WatchedAttributes.GetString(
					"characterClass",
					null
				);

			if (classCode != null && ValidRaceCodes.Contains(classCode))
			{
				return;
			}

			// The server migration can arrive after the client's PlayerJoin
			// event. CharacterSystem immediately opens its dialog and throws
			// if the watched class code is no longer present. Give that dialog
			// a valid temporary race; the player's actual choice is still
			// sent to and saved by the server normally.
			player.Entity.WatchedAttributes.SetString(
				"characterClass",
				"apprentice-race-human"
			);
			player.Entity.WatchedAttributes.RemoveAttribute("extraTraits");

			clientApi.Logger.Notification(
				"[Apprentice] Protected character selection from invalid " +
				$"legacy class '{classCode ?? "<missing>"}'."
			);
		}

		public override void Dispose()
		{
			if (serverApi != null)
			{
				serverApi.Event.PlayerJoin -= OnPlayerJoin;
			}

			if (clientApi != null)
			{
				clientApi.Event.PlayerJoin -= OnClientPlayerJoin;
			}

			serverApi = null;
			clientApi = null;
			base.Dispose();
		}
	}
}
