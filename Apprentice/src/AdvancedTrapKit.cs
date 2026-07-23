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
    /// A zero-collision, server-authoritative foothold trap. Its block variants
    /// are the replicated animation frames, so every client sees the same
    /// triggered-to-armed transition without inventing trap state locally.
    /// </summary>
    public class BlockAdvancedTrap : Block
    {
        private static readonly Cuboidf[] NoCollision = System.Array.Empty<Cuboidf>();
        protected virtual string State => Variant?["state"] ?? "armed";
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

        public override float OnGettingBroken(
            IPlayer player,
            BlockSelection blockSel,
            ItemSlot itemslot,
            float remainingResistance,
            float dt,
            int counter)
        {
            if (!IsArmed && blockSel != null)
            {
                if (api.Side == EnumAppSide.Server &&
                    api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is
                        BlockEntityAdvancedTrap trap)
                {
                    trap.AdvanceLeftClickRearming(player?.Entity, dt);
                }

                // A triggered trap is rearmed by holding the attack button;
                // it must not also accumulate ordinary block-breaking damage.
                return remainingResistance;
            }

            return base.OnGettingBroken(
                player,
                blockSel,
                itemslot,
                remainingResistance,
                dt,
                counter
            );
        }

        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            // Right-click is the authoritative opening lifecycle. Keep the
            // left-click path above as a convenience, but do not depend on
            // block-breaking packets for the trap's primary interaction.
            return !IsArmed && blockSel != null;
        }

        public override bool OnBlockInteractStep(
            float secondsUsed,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (IsArmed || blockSel == null) return false;

            if (world.Side == EnumAppSide.Server &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is
                    BlockEntityAdvancedTrap trap)
            {
                trap.SetTimedRearming(byPlayer?.Entity, secondsUsed);
            }

            return secondsUsed < 5f;
        }

        public override void OnBlockInteractStop(
            float secondsUsed,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (secondsUsed >= 5f || world.Side != EnumAppSide.Server ||
                blockSel == null) return;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is
                BlockEntityAdvancedTrap trap)
            {
                trap.CancelRearming();
            }
        }

        public override bool OnBlockInteractCancel(
            float secondsUsed,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            EnumItemUseCancelReason cancelReason)
        {
            if (world.Side == EnumAppSide.Server && blockSel != null &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is
                    BlockEntityAdvancedTrap trap)
            {
                trap.CancelRearming();
            }
            return true;
        }
    }

    /// <summary>
    /// Compatibility block for traps saved before the state variants were
    /// introduced. Those saves contain exactly apprentice:advancedtrap, so
    /// the code must remain registered or the engine replaces the trap with
    /// an unknown block before interaction callbacks can run. Treating the
    /// legacy block as triggered is safe: the player can hold right-click to
    /// rearm it, and the first opening frame migrates it to the current block
    /// variants permanently.
    /// </summary>
    public sealed class BlockLegacyAdvancedTrap : BlockAdvancedTrap
    {
        protected override string State => "triggered";
    }

    public sealed class BlockEntityAdvancedTrap : BlockEntity
    {
        private const float TriggerDamage = 1.5f;
        private const float RearmDurationSeconds = 5f;
        private const int RearmCancelGraceMs = 350;
        private long capturedEntityId;
        private long armingEntityId;
        private long armingGraceUntilMs;
        private long lastRearmInputMs;
        private long pinListenerId;
        private double pinY;
        private float rearmingProgress;

        private string State => Block is BlockLegacyAdvancedTrap
            ? "triggered"
            : Block?.Variant?["state"] ?? "armed";

        public bool Triggered => State != "armed";

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

        public void AdvanceLeftClickRearming(Entity? armingEntity, float dt)
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;

            long now = Api.World.ElapsedMilliseconds;
            if (lastRearmInputMs == 0 ||
                now - lastRearmInputMs > RearmCancelGraceMs)
            {
                rearmingProgress = 0;
                if (State != "triggered")
                {
                    ExchangeState("triggered");
                }
            }

            lastRearmInputMs = now;
            armingEntityId = armingEntity?.EntityId ?? 0;
            rearmingProgress = GameMath.Clamp(
                rearmingProgress + System.Math.Max(0, dt) / RearmDurationSeconds,
                0,
                1
            );

            if (rearmingProgress >= 1f)
            {
                capturedEntityId = 0;
                armingGraceUntilMs = now + 2000;
                pinY = 0;
                lastRearmInputMs = 0;
                rearmingProgress = 0;
                ExchangeState("armed");
                return;
            }

            ApplyRearmingVisualState();
        }

        public void SetTimedRearming(Entity? armingEntity, float secondsUsed)
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;

            long now = Api.World.ElapsedMilliseconds;
            lastRearmInputMs = now;
            armingEntityId = armingEntity?.EntityId ?? 0;
            rearmingProgress = GameMath.Clamp(
                System.Math.Max(0, secondsUsed) / RearmDurationSeconds,
                0,
                1
            );

            if (rearmingProgress >= 1f)
            {
                capturedEntityId = 0;
                armingGraceUntilMs = now + 2000;
                pinY = 0;
                lastRearmInputMs = 0;
                rearmingProgress = 0;
                ExchangeState("armed");
            }
            else
            {
                ApplyRearmingVisualState();
            }
        }

        private void ApplyRearmingVisualState()
        {
            string desired = rearmingProgress >= 0.8f
                ? "opening4"
                : rearmingProgress >= 0.6f
                    ? "opening3"
                    : rearmingProgress >= 0.4f
                        ? "opening2"
                        : rearmingProgress >= 0.2f
                            ? "opening1"
                            : "triggered";

            if (State != desired)
            {
                ExchangeState(desired);
            }
            else
            {
                MarkDirty(true);
            }
        }

        public void CancelRearming()
        {
            if (Api.Side != EnumAppSide.Server || !Triggered) return;
            lastRearmInputMs = 0;
            armingEntityId = 0;
            rearmingProgress = 0;
            if (State != "triggered")
            {
                ExchangeState("triggered");
            }
            else
            {
                MarkDirty(true);
            }
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
            long lastInput = lastRearmInputMs;
            double y = pinY;
            float progress = rearmingProgress;
            Api.World.BlockAccessor.ExchangeBlock(next.Id, Pos);
            if (Api.World.BlockAccessor.GetBlockEntity(Pos) is
                BlockEntityAdvancedTrap replacement)
            {
                replacement.capturedEntityId = captured;
                replacement.armingEntityId = armer;
                replacement.armingGraceUntilMs = grace;
                replacement.lastRearmInputMs = lastInput;
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
                    PinPosition(localPlayer, Pos, pinY);
                }
                return;
            }

            string state = State;
            bool interruptedOpening = state != "armed" &&
                state != "triggered" && lastRearmInputMs == 0;
            bool staleInput = state != "armed" &&
                rearmingProgress > 0 && lastRearmInputMs > 0 &&
                Api.World.ElapsedMilliseconds - lastRearmInputMs >
                    RearmCancelGraceMs;
            if (interruptedOpening || staleInput)
            {
                lastRearmInputMs = 0;
                armingEntityId = 0;
                rearmingProgress = 0;
                if (state != "triggered")
                {
                    ExchangeState("triggered");
                    return;
                }
                MarkDirty(true);
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
            PinPosition(entity, Pos, pinY);
        }

        private static void PinPosition(
            Entity entity,
            BlockPos trapPos,
            double y)
        {
            double x = trapPos.X + 0.5;
            double z = trapPos.Z + 0.5;

            entity.Pos.Motion.Set(0, 0, 0);
            entity.Pos.X = x;
            entity.Pos.Y = y;
            entity.Pos.Z = z;
        }

        private static void SuppressMovement(EntityControls? controls)
        {
            if (controls == null) return;

            // Clear locomotion explicitly. StopAllMovement() also clears the
            // mouse-button flags, which cancels the five-second left-click
            // rearm action every time this pinning tick runs.
            controls.Forward = false;
            controls.Backward = false;
            controls.Left = false;
            controls.Right = false;
            controls.Jump = false;
            controls.Sneak = false;
            controls.Sprint = false;
            controls.Up = false;
            controls.Down = false;
            controls.Gliding = false;
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
            tree.SetLong("lastRearmInputMs", lastRearmInputMs);
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
            lastRearmInputMs = tree.GetLong("lastRearmInputMs", 0);
            pinY = tree.GetDouble("pinY", 0);
            rearmingProgress = tree.GetFloat("rearmingProgress", 0);
        }
    }
}
