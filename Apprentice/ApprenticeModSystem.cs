using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{ 
	public class ApprenticeModSystem : ModSystem
	{
		private ICoreServerAPI? serverApi = null;
		private ICoreClientAPI? clientApi = null;

		private ExperienceManager? experienceManager = null;
		private InterfaceManager? interfaceManager = null;
		private OverlayManager? overlayManager = null;

		public override void Start(ICoreAPI api)
		{
			api.ChatCommands.Create(api).RequiresPrivilege(Privilege.cha)
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			serverApi = api;

			experienceManager = new ExperienceManager(api);

			// TODO: move these into xp-mgr directly, since we push callbacks..
			api.Event.PlayerJoin += OnPlayerJoin;
			api.Event.PlayerLeave += OnPlayerLeave;
			api.Event.PlayerDeath += OnPlayerDeath;
			api.Event.PlayerRespawn += OnPlayerRespawn;
			api.Event.SaveGameLoaded += OnSaveGameLoaded;
			api.Event.GameWorldSave += OnGameWorldSave;
			api.Event.DidBreakBlock += OnDidBreakBlock;
			api.Event.HandInteract += OnHandInteract;
			api.Event.EntityMounted += OnEntityMounted;
			api.Event.EntityUnmounted += OnEntityUnmounted;
			api.Event.MatchesRecipe += OnMatchesRecipe;
			api.Event.MatchesGridRecipe += OnMatchGridRecipe;
		}
		public override void StartClientSide(ICoreClientAPI api)
		{
			clientApi = api;

			interfaceManager = new InterfaceManager(api);
			overlayManager = new OverlayManager(api);
		}

		private void OnPlayerJoin(IServerPlayer player)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnPlayerLeave(IServerPlayer player)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnPlayerRespawn(IServerPlayer player)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnSaveGameLoaded()
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnGameWorldSave()
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnDidBreakBlock(IServerPlayer player, int oldblockId, BlockSelection blockSel)
		{
			if (serverApi == null) return;
			if (experienceManager == null) return;

			Block block = serverApi.World.GetBlock(oldblockId);

			experienceManager.UpdatePlayerExperienceByBreakingBlocks(player, block);
		}
		private void OnHandInteract(IServerPlayer player, EnumHandInteractNw enumHandInteract, float secondsPassed, ref EnumHandling handling)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnEntityMounted(EntityAgent mountingEntity, IMountableSeat mountedSeat)
		{
			if (serverApi == null) return;

			// TODO
		}
		private void OnEntityUnmounted(EntityAgent mountingEntity, IMountableSeat mountedSeat)
		{
			if (serverApi == null) return;

			// TODO
		}
		private bool OnMatchesRecipe(IPlayer player, IRecipeBase recipe, ItemSlot[] ingredients)
		{
			return false;
		}
		private bool OnMatchGridRecipe(IPlayer player, GridRecipe recipe, ItemSlot[] ingredients, int gridWidth)
		{
			return false;
		}
	}
}
