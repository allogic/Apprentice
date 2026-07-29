using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed class RealmLevelDefinition
    {
        public int Level { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PageCode { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string TextKey { get; set; } = string.Empty;
        public List<string> RecipeIds { get; set; } = new();
    }

    public sealed class RealmProgressionCatalog
    {
        public int SchemaVersion { get; set; } = 1;
        public List<RealmLevelDefinition> Levels { get; set; } = new();

        internal RealmLevelDefinition? Find(int level) =>
            Levels.FirstOrDefault(entry => entry.Level == level);

        internal static RealmProgressionCatalog Load(ICoreAPI api)
        {
            AssetLocation location = new(
                "apprentice",
                "config/realm-progression.json"
            );
            RealmProgressionCatalog catalog =
                JsonConvert.DeserializeObject<RealmProgressionCatalog>(
                    api.Assets.Get(location).ToText()
                ) ?? throw new InvalidOperationException(
                    $"Failed to parse {location}."
                );

            if (catalog.SchemaVersion < 1 ||
                catalog.Levels.Count == 0)
            {
                throw new InvalidOperationException(
                    "realm-progression.json must define a positive schema and at least one realm."
                );
            }

            HashSet<int> levels = new();
            HashSet<string> pageCodes = new(
                StringComparer.OrdinalIgnoreCase);
            foreach (RealmLevelDefinition realm in catalog.Levels)
            {
                realm.Name = realm.Name.Trim();
                realm.PageCode = realm.PageCode.Trim();
                realm.TitleKey = realm.TitleKey.Trim();
                realm.TextKey = realm.TextKey.Trim();
                realm.RecipeIds = realm.RecipeIds
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (realm.Level < 1 ||
                    realm.Level > 30 ||
                    !levels.Add(realm.Level) ||
                    realm.Name.Length == 0 ||
                    realm.PageCode.Length == 0 ||
                    !pageCodes.Add(realm.PageCode) ||
                    realm.TitleKey.Length == 0 ||
                    realm.TextKey.Length == 0)
                {
                    throw new InvalidOperationException(
                        "realm-progression.json contains an invalid or duplicate realm definition."
                    );
                }
            }

            return catalog;
        }
    }

    internal static class RealmProgressionRuntime
    {
        internal const string RootPath =
            "apprentice:realmDiscoveries";
        private const string MaskKey = "mask";
        internal const int PoisonMireLevel = 6;

        internal static RealmProgressionCatalog Catalog { get; set; } =
            new();

        internal static int GetMask(Entity? entity) =>
            entity?.WatchedAttributes
                .GetTreeAttribute(RootPath)
                ?.GetInt(MaskKey, 0) ?? 0;

        internal static bool IsDiscovered(
            Entity? entity,
            int level) =>
            level <= 0 ||
            (level < 31 &&
             (GetMask(entity) & (1 << level)) != 0);

        internal static int Discover(
            EntityPlayer entity,
            int level)
        {
            int mask = GetMask(entity);
            int next = mask | (1 << level);
            if (next == mask)
            {
                return mask;
            }

            ITreeAttribute root = entity.WatchedAttributes
                .GetOrAddTreeAttribute(RootPath);
            root.SetInt("schema", 1);
            root.SetInt(MaskKey, next);
            entity.WatchedAttributes.MarkPathDirty(
                RootPath + "/schema");
            entity.WatchedAttributes.MarkPathDirty(
                RootPath + "/" + MaskKey);
            return next;
        }
    }

    [ProtoContract]
    public sealed class RealmDiscoveryPacket
    {
        [ProtoMember(1)]
        public int Level { get; set; }

        [ProtoMember(2)]
        public int DiscoveryMask { get; set; }

        [ProtoMember(3)]
        public string RealmName { get; set; } = string.Empty;

        [ProtoMember(4)]
        public string PageCode { get; set; } = string.Empty;

        [ProtoMember(5)]
        public int RecipeCount { get; set; }
    }

    /// <summary>
    /// Owns per-player realm discoveries, Toxicwater contact exposure and the
    /// locked Survival Handbook guide pages. It is intentionally independent
    /// from ApprenticeModSystem so realm progression does not modify shared
    /// weapon, animation or character-registration code.
    /// </summary>
    public sealed class RealmProgressionSystem : ModSystem
    {
        private const string ChannelName =
            "apprentice-realm-progression";
        private IServerNetworkChannel? serverChannel;
        private IClientNetworkChannel? clientChannel;
        private ICoreServerAPI? serverApi;
        private ICoreClientAPI? clientApi;
        private ModSystemSurvivalHandbook? handbookSystem;
        private readonly Dictionary<int, GuiHandbookTextPage>
            handbookPages = new();
        private int clientDiscoveryMask;
        private long tickListenerId;

        public override double ExecuteOrder() => 0.115;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockClass(
                "ApprenticeToxicWater",
                typeof(BlockApprenticeToxicWater)
            );
            api.RegisterBlockClass(
                "ApprenticeToxicWaterFlowing",
                typeof(BlockApprenticeToxicWaterFlowing)
            );
            api.RegisterBlockClass(
                "ApprenticeToxicWaterfall",
                typeof(BlockApprenticeToxicWaterfall)
            );

            if (api.Side == EnumAppSide.Server)
            {
                serverChannel = ((ICoreServerAPI)api).Network
                    .RegisterChannel(ChannelName)
                    .RegisterMessageType<RealmDiscoveryPacket>();
            }
            else
            {
                clientChannel = ((ICoreClientAPI)api).Network
                    .RegisterChannel(ChannelName)
                    .RegisterMessageType<RealmDiscoveryPacket>();
            }
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            RealmProgressionRuntime.Catalog =
                RealmProgressionCatalog.Load(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverApi = api;
            tickListenerId =
                api.Event.RegisterGameTickListener(OnServerTick, 1000);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientApi = api;
            clientChannel?.SetMessageHandler<RealmDiscoveryPacket>(
                OnRealmDiscovered);
            handbookSystem =
                api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
            if (handbookSystem != null)
            {
                handbookSystem.OnInitCustomPages +=
                    OnInitCustomHandbookPages;
            }
        }

        private void OnServerTick(float deltaTime)
        {
            if (serverApi == null)
            {
                return;
            }

            DangerWorldState? state = DangerTierRuntime.WorldState;
            foreach (IServerPlayer player in
                serverApi.World.AllOnlinePlayers)
            {
                EntityPlayer? entity = player.Entity;
                if (entity == null || !entity.Alive)
                {
                    continue;
                }

                ApplyToxicWaterContact(entity);
                if (state == null ||
                    !state.Enabled ||
                    state.RingWidth <= 0)
                {
                    continue;
                }

                int level = WorldZoneLayout.GetLevelAt(
                    state,
                    entity.Pos.X,
                    entity.Pos.Z
                );
                RealmLevelDefinition? realm =
                    RealmProgressionRuntime.Catalog.Find(level);
                if (realm == null ||
                    RealmProgressionRuntime.IsDiscovered(
                        entity,
                        level) ||
                    !WorldZoneLayout.IsInsideLevelCore(
                        state,
                        level,
                        16,
                        entity.Pos.X,
                        entity.Pos.Z))
                {
                    continue;
                }

                int mask = RealmProgressionRuntime.Discover(
                    entity,
                    level
                );
                serverChannel?.SendPacket(
                    new RealmDiscoveryPacket
                    {
                        Level = level,
                        DiscoveryMask = mask,
                        RealmName = realm.Name,
                        PageCode = realm.PageCode,
                        RecipeCount = realm.RecipeIds.Count
                    },
                    player
                );
                serverApi.Logger.Notification(
                    "[Apprentice] {0} discovered realm {1}: {2}; unlocked {3} level recipe(s) and Survival Handbook guide {4}.",
                    player.PlayerName,
                    level,
                    realm.Name,
                    realm.RecipeIds.Count,
                    realm.PageCode
                );
            }
        }

        private void ApplyToxicWaterContact(EntityPlayer entity)
        {
            if (serverApi == null ||
                (!entity.FeetInLiquid && !entity.Swimming))
            {
                return;
            }

            int x = (int)Math.Floor(entity.Pos.X);
            int z = (int)Math.Floor(entity.Pos.Z);
            int feetY = (int)Math.Floor(entity.Pos.Y + 0.1);
            for (int y = feetY; y <= feetY + 1; y++)
            {
                Block fluid = serverApi.World.BlockAccessor.GetBlock(
                    new BlockPos(x, y, z),
                    BlockLayersAccess.Fluid
                );
                if (fluid?.Code?.Domain.Equals(
                        "apprenticemire",
                        StringComparison.OrdinalIgnoreCase) == true &&
                    fluid.Code.Path.StartsWith(
                        "toxicwater-",
                        StringComparison.Ordinal))
                {
                    PoisonRuntime.System?.ApplyPoison(
                        entity,
                        "toxicwater",
                        0,
                        false
                    );
                    return;
                }
            }
        }

        private void OnInitCustomHandbookPages(
            List<GuiHandbookPage> pages)
        {
            if (clientApi == null)
            {
                return;
            }

            handbookPages.Clear();
            clientDiscoveryMask =
                RealmProgressionRuntime.GetMask(
                    clientApi.World.Player?.Entity);
            foreach (RealmLevelDefinition realm in
                RealmProgressionRuntime.Catalog.Levels)
            {
                GuiHandbookTextPage? page = pages
                    .OfType<GuiHandbookTextPage>()
                    .FirstOrDefault(candidate =>
                        string.Equals(
                            candidate.PageCode,
                            realm.PageCode,
                            StringComparison.OrdinalIgnoreCase
                        ));
                if (page == null)
                {
                    clientApi.Logger.Error(
                        "[Apprentice] Survival Handbook guide {0} is missing.",
                        realm.PageCode
                    );
                    continue;
                }

                handbookPages[realm.Level] = page;
                ApplyHandbookLock(realm, page);
            }
        }

        private void OnRealmDiscovered(RealmDiscoveryPacket packet)
        {
            if (clientApi == null)
            {
                return;
            }

            clientDiscoveryMask |= packet.DiscoveryMask;
            RealmLevelDefinition? realm =
                RealmProgressionRuntime.Catalog.Find(packet.Level);
            if (realm != null &&
                handbookPages.TryGetValue(
                    packet.Level,
                    out GuiHandbookTextPage? page))
            {
                ApplyHandbookLock(realm, page);
                RefreshOpenHandbook();
            }

            string message = packet.RecipeCount == 1
                ? Lang.Get(
                    "apprentice:realm-discovered-one-recipe",
                    packet.RealmName)
                : Lang.Get(
                    "apprentice:realm-discovered-many-recipes",
                    packet.RealmName,
                    packet.RecipeCount);
            clientApi.TriggerIngameDiscovery(
                this,
                $"apprentice-realm-{packet.Level}-" +
                    clientApi.ElapsedMilliseconds,
                message
            );
        }

        private void ApplyHandbookLock(
            RealmLevelDefinition realm,
            GuiHandbookTextPage page)
        {
            if (clientApi == null)
            {
                return;
            }

            bool unlocked =
                (clientDiscoveryMask & (1 << realm.Level)) != 0;
            page.Title = realm.TitleKey;
            page.Text = unlocked
                ? realm.TextKey
                : "apprentice:handbook-realm-locked-text";
            page.Visible = unlocked;
            page.Recompose(clientApi);
        }

        private void RefreshOpenHandbook()
        {
            if (handbookSystem == null)
            {
                return;
            }

            try
            {
                FieldInfo? field = typeof(ModSystemSurvivalHandbook)
                    .GetField(
                        "dialog",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic
                    );
                if (field?.GetValue(handbookSystem) is
                    GuiDialogHandbook dialog)
                {
                    dialog.FilterItems();
                    dialog.ReloadPage();
                }
            }
            catch (Exception exception)
            {
                clientApi?.Logger.Warning(
                    "[Apprentice] The newly unlocked handbook guide will appear after reopening the handbook: {0}",
                    exception.Message
                );
            }
        }

        public override void Dispose()
        {
            if (serverApi != null && tickListenerId != 0)
            {
                serverApi.Event.UnregisterGameTickListener(
                    tickListenerId);
            }
            if (handbookSystem != null)
            {
                handbookSystem.OnInitCustomPages -=
                    OnInitCustomHandbookPages;
            }
            handbookPages.Clear();
            serverApi = null;
            clientApi = null;
            serverChannel = null;
            clientChannel = null;
            handbookSystem = null;
            RealmProgressionRuntime.Catalog = new();
            base.Dispose();
        }
    }

    internal static class ToxicWaterArrowCoating
    {
        private const int ArrowBatchSize = 8;

        internal static bool TryHandle(
            IWorldAccessor world,
            IPlayer byPlayer)
        {
            ItemSlot? slot =
                byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStack? stack = slot?.Itemstack;
            string code = stack?.Collectible?.Code?.ToString() ??
                string.Empty;
            if (slot == null ||
                stack == null ||
                stack.StackSize < ArrowBatchSize ||
                !code.StartsWith(
                    "game:arrow-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!RealmProgressionRuntime.IsDiscovered(
                    byPlayer.Entity,
                    RealmProgressionRuntime.PoisonMireLevel))
            {
                if (world.Side == EnumAppSide.Server &&
                    byPlayer is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(
                        GlobalConstants.GeneralChatGroup,
                        Lang.Get(
                            "apprentice:toxicwater-recipe-locked"),
                        EnumChatType.Notification
                    );
                }
                return true;
            }

            if (world.Side != EnumAppSide.Server)
            {
                return true;
            }

            Item? resultItem = world.GetItem(
                new AssetLocation(
                    "apprentice",
                    "arrow-poison-toxicwater"
                ));
            if (resultItem == null)
            {
                world.Logger.Error(
                    "[Apprentice] Cannot coat Toxicwater arrows because apprentice:arrow-poison-toxicwater is missing."
                );
                return true;
            }

            bool creative =
                byPlayer.WorldData?.CurrentGameMode ==
                    EnumGameMode.Creative;
            if (!creative)
            {
                slot.TakeOut(ArrowBatchSize);
                slot.MarkDirty();
            }

            ItemStack result = new(resultItem, ArrowBatchSize);
            if (!byPlayer.InventoryManager.TryGiveItemstack(
                    result,
                    slotNotifyEffect: true))
            {
                world.SpawnItemEntity(
                    result,
                    byPlayer.Entity.Pos.XYZ
                );
            }
            return true;
        }
    }

    public sealed class BlockApprenticeToxicWater : BlockWater
    {
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel) =>
            ToxicWaterArrowCoating.TryHandle(world, byPlayer) ||
            base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public sealed class BlockApprenticeToxicWaterFlowing :
        BlockWaterflowing
    {
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel) =>
            ToxicWaterArrowCoating.TryHandle(world, byPlayer) ||
            base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public sealed class BlockApprenticeToxicWaterfall :
        BlockWaterfall
    {
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel) =>
            ToxicWaterArrowCoating.TryHandle(world, byPlayer) ||
            base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}
