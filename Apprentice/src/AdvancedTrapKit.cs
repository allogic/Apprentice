using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    public sealed class ItemAdvancedTrapKit : Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (blockSel == null || byEntity?.World == null) return;

            BlockPos position = blockSel.Position.AddCopy(blockSel.Face);
            Block? trap = byEntity.World.GetBlock(
                new AssetLocation("apprentice", "advancedtrap-armed")
            );
            Block existing = byEntity.World.BlockAccessor.GetBlock(position);
            if (trap == null || trap.Id == 0 || existing.Replaceable < 6000)
            {
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server) return;

            byEntity.World.BlockAccessor.SetBlock(trap.Id, position);
            if (byEntity is EntityPlayer player &&
                player.Player?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
        }
    }

    /// <summary>
    /// A zero-collision, server-authoritative foothold trap.  The block's two
    /// variants are deliberately used as the animation endpoints so clients
    /// never invent trap state locally.
    /// </summary>
    public sealed class BlockAdvancedTrap : Block
    {
        private static readonly Cuboidf[] NoCollision = System.Array.Empty<Cuboidf>();
        private string State => Variant?["state"] ?? "armed";
        private bool IsArmed => State == "armed";

        public override Cuboidf[] GetCollisionBoxes(
            IBlockAccessor blockAccessor,
            BlockPos pos) => NoCollision;

        public override void OnEntityInside(
            IWorldAccessor world,
            Entity entity,
            BlockPos pos)
        {
            base.OnEntityInside(world, entity, pos);
            if (world.Side != EnumAppSide.Server || !IsArmed ||
                entity is not EntityAgent || !entity.Alive)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(pos) is
                BlockEntityAdvancedTrap trap)
            {
                trap.Trigger(entity);
            }
        }

        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (IsArmed || blockSel == null) return false;
            if (world.Side == EnumAppSide.Server &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityAdvancedTrap trap)
            {
                trap.BeginRearming(byPlayer?.Entity);
            }
            return true;
        }

        public override bool OnBlockInteractStep(
            float secondsUsed,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (IsArmed || blockSel == null) return false;

            if (world.Side == EnumAppSide.Server &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityAdvancedTrap trap)
            {
                trap.SetRearmingProgress(secondsUsed);
            }
            if (secondsUsed < 5f) return true;

            if (world.Side == EnumAppSide.Server &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is
                    BlockEntityAdvancedTrap completingTrap)
            {
                completingTrap.CompleteRearming(byPlayer?.Entity);
            }

            return false;
        }

        public override bool OnBlockInteractCancel(
            float secondsUsed,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            EnumItemUseCancelReason cancelReason)
        {
            if (world.Side == EnumAppSide.Server && blockSel != null &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityAdvancedTrap trap)
            {
                trap.CancelRearming();
            }
            return base.OnBlockInteractCancel(
                secondsUsed,
                world,
                byPlayer,
                blockSel,
                cancelReason
            );
        }
    }

    public sealed class BlockEntityAdvancedTrap : BlockEntity
    {
        private const float TriggerDamage = 1.5f;
        private long capturedEntityId;
        private long armingEntityId;
        private long armingGraceUntilMs;
        private long pinListenerId;
        private double pinY;
        private float rearmingProgress;

        public bool Triggered =>
            (Block?.Variant?["state"] ?? "armed") != "armed";

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            // Run on both sides.  The server remains authoritative, while the
            // client-side pass suppresses local input prediction so a trapped
            // player cannot keep walking visually between server corrections.
            pinListenerId = RegisterGameTickListener(UpdateTrap, 50);
        }

        public void Trigger(Entity entity)
        {
            if (Api.Side != EnumAppSide.Server || Triggered ||
                capturedEntityId != 0 || !entity.Alive ||
                (entity.EntityId == armingEntityId &&
                 Api.World.ElapsedMilliseconds < armingGraceUntilMs))
            {
                return;
            }

            capturedEntityId = entity.EntityId;
            pinY = entity.Pos.Y;
            entity.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Block,
                    SourceBlock = Block,
                    SourcePos = Pos.ToVec3d(),
                    Type = EnumDamageType.PiercingAttack,
                    DamageTier = 0
                },
                TriggerDamage
            );

            ExchangeState("triggered");
            UpdateTrap(0);
        }

        public void BeginRearming(Entity? armingEntity)
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;

            armingEntityId = armingEntity?.EntityId ?? 0;
            rearmingProgress = 0;
            MarkDirty(true);
        }

        public void SetRearmingProgress(float secondsUsed)
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;
            // Never exchange the block during a held interaction. Exchanging
            // it destroys the interaction context, which made the five-second
            // action reset itself. The block entity is the persistent owner
            // of progress; clients receive it through MarkDirty.
            rearmingProgress = GameMath.Clamp(secondsUsed / 5f, 0, 1);
            MarkDirty(true);
        }

        public void CancelRearming()
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;
            armingEntityId = 0;
            rearmingProgress = 0;
            MarkDirty(true);
        }

        public void CompleteRearming(Entity? armingEntity)
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;
            capturedEntityId = 0;
            armingEntityId = armingEntity?.EntityId ?? 0;
            armingGraceUntilMs = Api.World.ElapsedMilliseconds + 2000;
            pinY = 0;
            rearmingProgress = 1;
            ExchangeState("armed");
        }

        private void ExchangeState(string state)
        {
            Block? next = Api.World.GetBlock(
                new AssetLocation("apprentice", "advancedtrap-" + state)
            );
            if (next == null || next.Id == 0) return;

            // ExchangeBlock may construct a new block entity.  Copy the
            // authoritative state into that instance; otherwise every visual
            // animation step silently loses the captured/arming entity.
            long captured = capturedEntityId;
            long armer = armingEntityId;
            long grace = armingGraceUntilMs;
            double y = pinY;
            float progress = rearmingProgress;
            Api.World.BlockAccessor.ExchangeBlock(next.Id, Pos);
            if (Api.World.BlockAccessor.GetBlockEntity(Pos) is
                BlockEntityAdvancedTrap replacement)
            {
                replacement.capturedEntityId = captured;
                replacement.armingEntityId = armer;
                replacement.armingGraceUntilMs = grace;
                replacement.pinY = y;
                replacement.rearmingProgress = progress;
                replacement.MarkDirty(true);
            }
        }

        private void UpdateTrap(float deltaTime)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                ICoreClientAPI? capi = Api as ICoreClientAPI;
                EntityPlayer? localPlayer = capi?.World?.Player?.Entity;
                if (capturedEntityId != 0 &&
                    localPlayer?.EntityId == capturedEntityId)
                {
                    SuppressMovement(localPlayer.Controls);
                    PinPosition(localPlayer, Pos, pinY, false);
                }
                return;
            }

            // A walkable block has no collision callback to rely on. Detect
            // feet in the block volume on the authoritative server instead.
            if (!Triggered && capturedEntityId == 0)
            {
                Entity[] candidates = Api.World.GetEntitiesAround(
                    new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5),
                    0.8f,
                    1.2f,
                    entity => entity.Alive && entity is EntityAgent
                );
                foreach (Entity candidate in candidates)
                {
                    if (candidate.EntityId == armingEntityId &&
                        Api.World.ElapsedMilliseconds < armingGraceUntilMs)
                    {
                        continue;
                    }
                    double x = candidate.Pos.X;
                    double y = candidate.Pos.Y;
                    double z = candidate.Pos.Z;
                    if (x >= Pos.X - 0.2 && x < Pos.X + 1.2 &&
                        z >= Pos.Z - 0.2 && z < Pos.Z + 1.2 &&
                        y >= Pos.Y - 0.25 && y < Pos.Y + 1.25)
                    {
                        Trigger(candidate);
                        break;
                    }
                }
            }

            if (capturedEntityId == 0) return;

            Entity? entity = Api.World.GetEntityById(capturedEntityId);
            if (entity == null || !entity.Alive)
            {
                capturedEntityId = 0;
                MarkDirty(true);
                return;
            }

            // Pin the entity position to the centre of the jaws. Pos is the
            // authoritative position on the server in the current API.
            // Reapplying this every server tick defeats player and AI controls
            // without adding an invisible collision cube.
            if (entity is EntityAgent agent)
            {
                SuppressMovement(agent.Controls);
                SuppressMovement(agent.ServerControls);
            }
            PinPosition(entity, Pos, pinY, true);
        }

        private static void PinPosition(
            Entity entity,
            BlockPos trapPos,
            double y,
            bool includeServerPosition)
        {
            double x = trapPos.X + 0.5;
            double z = trapPos.Z + 0.5;

            entity.Pos.Motion.Set(0, 0, 0);
            entity.Pos.X = x;
            entity.Pos.Y = y;
            entity.Pos.Z = z;

            // Player movement packets update ServerPos independently of Pos.
            // Clearing only Pos lets the next packet immediately move the
            // player again even though ordinary EntityAgent controls are off.
            // Pin both authoritative coordinates on the server; the client
            // does not own or mutate ServerPos.
            if (!includeServerPosition) return;

            entity.ServerPos.Motion.Set(0, 0, 0);
            entity.ServerPos.X = x;
            entity.ServerPos.Y = y;
            entity.ServerPos.Z = z;
        }

        private static void SuppressMovement(EntityControls? controls)
        {
            if (controls == null) return;

            // Use the engine's canonical reset so every movement action is
            // cleared, including glide/sneak states that are not covered by
            // the eight directional properties alone.  Keep the calculated
            // vectors at zero as they may have been produced earlier in the
            // same tick.
            controls.StopAllMovement();
            controls.WalkVector.Set(0, 0, 0);
            controls.FlyVector.Set(0, 0, 0);
            controls.Dirty = true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("capturedEntityId", capturedEntityId);
            tree.SetLong("armingEntityId", armingEntityId);
            tree.SetLong("armingGraceUntilMs", armingGraceUntilMs);
            tree.SetDouble("pinY", pinY);
            tree.SetFloat("rearmingProgress", rearmingProgress);
        }

        public override void FromTreeAttributes(
            ITreeAttribute tree,
            IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            capturedEntityId = tree.GetLong("capturedEntityId", 0);
            armingEntityId = tree.GetLong("armingEntityId", 0);
            armingGraceUntilMs = tree.GetLong("armingGraceUntilMs", 0);
            pinY = tree.GetDouble("pinY", 0);
            rearmingProgress = tree.GetFloat("rearmingProgress", 0);
        }
    }
}
