using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Apprentice.AnimationReference;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal sealed class ApprenticeAnimationSystem : IDisposable
    {
        private const int TickMilliseconds = 20;

        private readonly ICoreClientAPI api;
        private readonly IClientNetworkChannel channel;
        private readonly ApprenticeAnimationDefinition definition;
        private readonly WarScytheGeometryProbe geometryProbe;
        private readonly WarScytheAnimationEditor editor;
        private readonly Dictionary<long, WarScythePoseBehavior>
            thirdPersonBehaviors = new();
        private readonly Dictionary<long, ActiveRuntimeState>
            activeStates = new();
        private readonly Dictionary<long, int> localSequences = new();
        private readonly Dictionary<long, int> lastAcceptedSequences = new();
        private readonly Queue<string> callbackTrace = new();
        private readonly long tickListenerId;

        private WarScythePoseBehavior? firstPersonBehavior;
        private bool disposed;
        private bool hookReached;
        private bool hookReachLogged;
        private long appliedElementCount;
        private string lastGeometry = "none";

        public ApprenticeAnimationSystem(
            ICoreClientAPI api,
            IClientNetworkChannel channel,
            ApprenticeAnimationDefinition definition)
        {
            this.api = api;
            this.channel = channel;
            this.definition = definition;
            geometryProbe = new WarScytheGeometryProbe(api);

            channel.SetMessageHandler<WarScytheAnimationPacket>(
                OnPacket
            );
            editor = new WarScytheAnimationEditor(
                api,
                this,
                definition,
                geometryProbe
            );
            ApprenticeAnimationHook.Install(api, this);
            tickListenerId = api.Event.RegisterGameTickListener(
                OnTick,
                TickMilliseconds
            );
            RegisterStatusCommand();
        }

        public bool EditorPreviewActive => editor.PreviewActive;

        public static void RegisterServerHandler(
            ICoreServerAPI api,
            IServerNetworkChannel channel,
            ApprenticeAnimationDefinition definition)
        {
            Dictionary<long, int> lastAcceptedSequences = new();
            channel.SetMessageHandler<WarScytheAnimationPacket>(
                (player, packet) =>
                {
                    EntityAgent entity = player.Entity;
                    ItemStack? stack =
                        entity.RightHandItemSlot?.Itemstack;
                    if (!entity.Alive || stack?.Item == null ||
                        stack.Item.Code?.ToString() !=
                            definition.HeldItemCode ||
                        packet.Sequence <= 0)
                    {
                        return;
                    }

                    long entityId = entity.EntityId;
                    if (packet.Stop)
                    {
                        if (!lastAcceptedSequences.TryGetValue(
                                entityId,
                                out int activeSequence) ||
                            packet.Sequence != activeSequence)
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (lastAcceptedSequences.TryGetValue(
                                entityId,
                                out int latestSequence) &&
                            packet.Sequence <= latestSequence)
                        {
                            return;
                        }
                        lastAcceptedSequences[entityId] =
                            packet.Sequence;
                    }

                    WarScytheAnimationPacket sanitized = new()
                    {
                        EntityId = entityId,
                        ItemId = stack.Item.Id,
                        Sequence = packet.Sequence,
                        AnimationCode = definition.Code,
                        Category = definition.Category,
                        Speed = 1f,
                        EaseInMilliseconds = ToMilliseconds(
                            definition.EaseInSeconds
                        ),
                        EaseOutMilliseconds = ToMilliseconds(
                            definition.EaseOutSeconds
                        ),
                        Stop = packet.Stop
                    };
                    channel.BroadcastPacket(sanitized, player);
                }
            );

            api.Logger.Notification(
                "[Apprentice] War Scythe reference-animation relay registered with held-item and sequence validation."
            );
        }

        public void StartLocal(EntityAgent entity)
        {
            if (disposed || editor.PreviewActive ||
                entity is not EntityPlayer player ||
                !TryGetHeldWarScythe(player, out ItemStack stack))
            {
                return;
            }

            int sequence = localSequences.TryGetValue(
                entity.EntityId,
                out int previous)
                    ? previous + 1
                    : 1;
            if (sequence <= 0) sequence = 1;
            localSequences[entity.EntityId] = sequence;

            EnsureLocalBehaviors(player);
            AnimationRequest firstPersonRequest =
                CreateRequest(
                    callback => Trace(
                        entity.EntityId,
                        sequence,
                        callback
                    )
                );
            firstPersonBehavior!.Play(firstPersonRequest, stack.Item.Id);
            GetThirdPersonBehavior(player).Play(
                CreateRequest(callbackHandler: null),
                stack.Item.Id
            );

            activeStates[entity.EntityId] = new ActiveRuntimeState(
                stack.Item.Id,
                sequence,
                api.World.ElapsedMilliseconds,
                geometryProbe.Acceptance
            );
            lastAcceptedSequences[entity.EntityId] = sequence;
            Trace(entity.EntityId, sequence, "start");
            channel.SendPacket(CreatePacket(
                entity.EntityId,
                stack.Item.Id,
                sequence,
                stop: false
            ));
        }

        public void StopLocal(EntityAgent entity)
        {
            if (disposed ||
                !activeStates.TryGetValue(
                    entity.EntityId,
                    out ActiveRuntimeState? state))
            {
                return;
            }

            StopBehaviors(entity.EntityId);
            activeStates.Remove(entity.EntityId);
            channel.SendPacket(CreatePacket(
                entity.EntityId,
                state.ItemId,
                state.Sequence,
                stop: true
            ));
            Trace(entity.EntityId, state.Sequence, "stop");
        }

        public void EnterEditorMode()
        {
            EntityPlayer? player = api.World.Player?.Entity;
            if (player == null) return;

            if (activeStates.TryGetValue(
                player.EntityId,
                out ActiveRuntimeState? state))
            {
                StopBehaviors(player.EntityId);
                activeStates.Remove(player.EntityId);
                channel.SendPacket(CreatePacket(
                    player.EntityId,
                    state.ItemId,
                    state.Sequence,
                    stop: true
                ));
                Trace(
                    player.EntityId,
                    state.Sequence,
                    "editor-stop"
                );
            }
            EnsureLocalBehaviors(player);
        }

        internal void SetEditorFrameOverride(
            PlayerItemFrame? frame)
        {
            EntityPlayer? player = api.World.Player?.Entity;
            if (player == null) return;
            EnsureLocalBehaviors(player);
            firstPersonBehavior!.FrameOverride = frame;
            GetThirdPersonBehavior(player).FrameOverride = frame;
        }

        public void NoteLocalLifecycle(
            EntityAgent entity,
            string eventCode)
        {
            if (disposed ||
                string.IsNullOrWhiteSpace(eventCode) ||
                !localSequences.TryGetValue(
                    entity.EntityId,
                    out int sequence))
            {
                return;
            }
            Trace(entity.EntityId, sequence, eventCode);
        }

        internal void OnBeforeReferenceFrame(
            Entity entity,
            float deltaTime)
        {
            if (disposed || entity is not EntityPlayer player) return;

            WarScythePoseBehavior third =
                GetThirdPersonBehavior(player);
            if (IsLocalPlayer(player))
            {
                EnsureLocalBehaviors(player);
                firstPersonBehavior!.Advance(deltaTime);
            }
            third.Advance(deltaTime);
        }

        internal void OnReferenceFrame(
            EntityPlayer player,
            ElementPose pose,
            ClientAnimator animator)
        {
            if (disposed) return;

            bool applied = false;
            if (thirdPersonBehaviors.TryGetValue(
                player.EntityId,
                out WarScythePoseBehavior? third))
            {
                applied |= third.OnFrame(pose);
            }
            if (IsLocalPlayer(player))
            {
                EnsureLocalBehaviors(player);
                applied |= firstPersonBehavior!.OnFrame(pose);
            }

            if (!applied) return;
            hookReached = true;
            appliedElementCount++;
            string elementName =
                pose.ForElement?.Name ?? "unknown";
            editor.NoteHookElement(elementName);
            if (!hookReachLogged)
            {
                hookReachLogged = true;
                api.Logger.Notification(
                    "[Apprentice] War Scythe reference pipeline reached the animator-owned ElementPose traversal for entity {0}.",
                    player.EntityId
                );
            }
        }

        public string BuildStatus()
        {
            string callbacks = callbackTrace.Count == 0
                ? "none"
                : string.Join(" | ", callbackTrace);
            return string.Format(
                CultureInfo.InvariantCulture,
                "War Scythe animation: pipeline=OverhaulLib-reference AnimationJson->Animation->Animator->Composer->OnFrameInvoke->ElementPose; hookEnabled={0}; insertionPoints={1}; hookReached={2}; FPowner={3}; TPowners={4}; activeEntities={5}; editorPreview={6}; category={7}; duration={8:0.###}s; appliedElements={9}; callbacks=[{10}]; lastGeometry=[{11}]",
                ApprenticeAnimationHook.Enabled,
                ApprenticeAnimationHook.InjectionPointCount,
                hookReached,
                firstPersonBehavior != null,
                thirdPersonBehaviors.Count,
                activeStates.Count,
                editor.PreviewActive,
                definition.Category,
                definition.TotalActionSeconds,
                appliedElementCount,
                callbacks,
                lastGeometry
            );
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            api.Event.UnregisterGameTickListener(tickListenerId);
            editor.Dispose();
            firstPersonBehavior?.StopAll();
            firstPersonBehavior = null;
            foreach (WarScythePoseBehavior behavior in
                thirdPersonBehaviors.Values)
            {
                behavior.StopAll();
            }
            thirdPersonBehaviors.Clear();
            activeStates.Clear();
            localSequences.Clear();
            lastAcceptedSequences.Clear();
            callbackTrace.Clear();
            ApprenticeAnimationHook.Uninstall(this);
        }

        private void OnPacket(WarScytheAnimationPacket packet)
        {
            if (disposed ||
                packet.AnimationCode != definition.Code ||
                packet.Category != definition.Category ||
                packet.Speed != 1f ||
                packet.Sequence <= 0)
            {
                return;
            }

            if (api.World.GetEntityById(packet.EntityId) is not
                    EntityPlayer player ||
                !TryGetHeldWarScythe(
                    player,
                    out ItemStack stack) ||
                stack.Item.Id != packet.ItemId)
            {
                return;
            }

            if (packet.Stop)
            {
                if (lastAcceptedSequences.TryGetValue(
                        packet.EntityId,
                        out int activeSequence) &&
                    activeSequence == packet.Sequence)
                {
                    GetThirdPersonBehavior(player).Stop(
                        definition.Category
                    );
                    activeStates.Remove(packet.EntityId);
                    Trace(
                        packet.EntityId,
                        packet.Sequence,
                        "remote-stop"
                    );
                }
                return;
            }

            if (lastAcceptedSequences.TryGetValue(
                    packet.EntityId,
                    out int latestSequence) &&
                packet.Sequence <= latestSequence)
            {
                return;
            }

            lastAcceptedSequences[packet.EntityId] =
                packet.Sequence;
            GetThirdPersonBehavior(player).Play(
                CreateRequest(callbackHandler: null),
                stack.Item.Id
            );
            activeStates[packet.EntityId] = new ActiveRuntimeState(
                stack.Item.Id,
                packet.Sequence,
                api.World.ElapsedMilliseconds,
                geometryProbe.Acceptance
            );
            Trace(
                packet.EntityId,
                packet.Sequence,
                "remote-start"
            );
        }

        private void OnTick(float deltaTime)
        {
            if (disposed) return;
            editor.Tick(deltaTime);

            long now = api.World.ElapsedMilliseconds;
            foreach ((long entityId, ActiveRuntimeState state) in
                activeStates.ToArray())
            {
                if (api.World.GetEntityById(entityId) is not
                        EntityPlayer player ||
                    !player.Alive ||
                    !TryGetHeldWarScythe(
                        player,
                        out ItemStack stack) ||
                    stack.Item.Id != state.ItemId)
                {
                    StopBehaviors(entityId);
                    activeStates.Remove(entityId);
                    Trace(
                        entityId,
                        state.Sequence,
                        "stale-item-stop"
                    );
                    continue;
                }

                bool local =
                    api.World.Player?.Entity?.EntityId == entityId;
                float actionTime = Math.Clamp(
                    Math.Max(0, now - state.StartedMs) / 1000f -
                        definition.EaseInSeconds,
                    0,
                    definition.DurationSeconds
                );
                if (local && !editor.PreviewActive)
                {
                    try
                    {
                        if (geometryProbe.TrySample(
                            player,
                            stack,
                            out WarScytheGeometrySample geometry))
                        {
                            state.Geometry.Record(
                                geometry,
                                actionTime,
                                definition.Callbacks[0].TimeSeconds,
                                definition.Callbacks[2].TimeSeconds
                            );
                        }
                    }
                    catch (Exception exception)
                    {
                        api.Logger.Warning(
                            "[Apprentice] War Scythe geometry diagnostics stopped for sequence {0}: {1}",
                            state.Sequence,
                            exception.Message
                        );
                    }
                }

                if (now - state.StartedMs <
                    definition.TotalActionSeconds * 1000f)
                {
                    continue;
                }

                if (local)
                {
                    lastGeometry =
                        state.Geometry.BuildStatus(state.Sequence);
                    api.Logger.Notification(
                        "[Apprentice] WARSCYTHE GEOMETRY {0}",
                        lastGeometry
                    );
                }
                activeStates.Remove(entityId);
                Trace(entityId, state.Sequence, "finish");
            }
        }

        private AnimationRequest CreateRequest(
            Action<string>? callbackHandler) =>
            new(
                definition.Animation,
                animationSpeed: 1f,
                weight: 1f,
                category: definition.Category,
                easeOutDuration: TimeSpan.FromSeconds(
                    definition.EaseOutSeconds
                ),
                easeInDuration: TimeSpan.FromSeconds(
                    definition.EaseInSeconds
                ),
                easeOut: true,
                finishCallback: null,
                callbackHandler: callbackHandler
            );

        private void EnsureLocalBehaviors(EntityPlayer player)
        {
            if (firstPersonBehavior == null ||
                firstPersonBehavior.Player.EntityId !=
                    player.EntityId)
            {
                firstPersonBehavior = new WarScythePoseBehavior(
                    api,
                    player,
                    firstPerson: true,
                    definition
                );
            }
            _ = GetThirdPersonBehavior(player);
        }

        private WarScythePoseBehavior GetThirdPersonBehavior(
            EntityPlayer player)
        {
            if (!thirdPersonBehaviors.TryGetValue(
                player.EntityId,
                out WarScythePoseBehavior? behavior))
            {
                behavior = new WarScythePoseBehavior(
                    api,
                    player,
                    firstPerson: false,
                    definition
                );
                thirdPersonBehaviors[player.EntityId] = behavior;
            }
            return behavior;
        }

        private void StopBehaviors(long entityId)
        {
            if (firstPersonBehavior?.Player.EntityId == entityId)
            {
                firstPersonBehavior.Stop(definition.Category);
            }
            if (thirdPersonBehaviors.TryGetValue(
                entityId,
                out WarScythePoseBehavior? third))
            {
                third.Stop(definition.Category);
            }
        }

        private bool TryGetHeldWarScythe(
            EntityAgent entity,
            out ItemStack stack)
        {
            ItemStack? current =
                entity.RightHandItemSlot?.Itemstack;
            if (current?.Item?.Code?.ToString() ==
                definition.HeldItemCode)
            {
                stack = current;
                return true;
            }

            stack = null!;
            return false;
        }

        private bool IsLocalPlayer(EntityPlayer player) =>
            api.World.Player?.Entity?.EntityId == player.EntityId;

        private WarScytheAnimationPacket CreatePacket(
            long entityId,
            int itemId,
            int sequence,
            bool stop) => new()
        {
            EntityId = entityId,
            ItemId = itemId,
            Sequence = sequence,
            AnimationCode = definition.Code,
            Category = definition.Category,
            Speed = 1f,
            EaseInMilliseconds = ToMilliseconds(
                definition.EaseInSeconds
            ),
            EaseOutMilliseconds = ToMilliseconds(
                definition.EaseOutSeconds
            ),
            Stop = stop
        };

        private void RegisterStatusCommand()
        {
            api.ChatCommands.Create("apprenticeanimstatus")
                .WithDescription(
                    "Show the Apprentice OverhaulLib-reference animation pipeline state"
                )
                .HandleWith(_ =>
                    TextCommandResult.Success(BuildStatus()));
        }

        private void Trace(
            long entityId,
            int sequence,
            string callback)
        {
            string entry = string.Format(
                CultureInfo.InvariantCulture,
                "entity={0},seq={1},{2}",
                entityId,
                sequence,
                callback
            );
            callbackTrace.Enqueue(entry);
            while (callbackTrace.Count > 12)
            {
                callbackTrace.Dequeue();
            }

            if (api.World.Player?.Entity?.EntityId == entityId)
            {
                api.Logger.Notification(
                    "[Apprentice] WARSCYTHE CALLBACK {0}",
                    entry
                );
            }
        }

        private static int ToMilliseconds(float seconds) =>
            Math.Max(1, (int)Math.Round(seconds * 1000f));

        private sealed class ActiveRuntimeState
        {
            public ActiveRuntimeState(
                int itemId,
                int sequence,
                long startedMs,
                WarScytheAcceptanceDefinition acceptance)
            {
                ItemId = itemId;
                Sequence = sequence;
                StartedMs = startedMs;
                Geometry = new WarScytheGeometryTrace(acceptance);
            }

            public int ItemId { get; }
            public int Sequence { get; }
            public long StartedMs { get; }
            public WarScytheGeometryTrace Geometry { get; }
        }

        private sealed class WarScythePoseBehavior
        {
            private readonly ICoreClientAPI api;
            private readonly bool firstPerson;
            private readonly ApprenticeAnimationDefinition definition;
            private readonly Composer composer;
            private PlayerItemFrame currentFrame =
                PlayerItemFrame.Empty;
            private int activeItemId;

            public WarScythePoseBehavior(
                ICoreClientAPI api,
                EntityPlayer player,
                bool firstPerson,
                ApprenticeAnimationDefinition definition)
            {
                this.api = api;
                Player = player;
                this.firstPerson = firstPerson;
                this.definition = definition;
                composer = new Composer(
                    soundsManager: null,
                    particleEffectsManager: null,
                    player: player
                );
            }

            public EntityPlayer Player { get; }
            public PlayerItemFrame? FrameOverride { get; set; }

            public void Play(
                AnimationRequest request,
                int itemId)
            {
                activeItemId = itemId;
                composer.Play(request);
            }

            public void Stop(string category)
            {
                composer.Stop(category);
                activeItemId = 0;
                currentFrame = PlayerItemFrame.Empty;
            }

            public void StopAll()
            {
                composer.StopAll();
                activeItemId = 0;
                currentFrame = PlayerItemFrame.Empty;
                FrameOverride = null;
            }

            public void Advance(float deltaTime)
            {
                if (FrameOverride != null) return;

                Item? heldItem =
                    Player.RightHandItemSlot?.Itemstack?.Item;
                if (activeItemId != 0 &&
                    (!Player.Alive ||
                    heldItem?.Id != activeItemId ||
                    heldItem.Code?.ToString() !=
                        definition.HeldItemCode))
                {
                    StopAll();
                    return;
                }

                currentFrame = composer.Compose(
                    TimeSpan.FromSeconds(
                        Math.Max(0, deltaTime)
                    )
                );
                if (!composer.AnyActiveAnimations())
                {
                    activeItemId = 0;
                }
            }

            public bool OnFrame(ElementPose pose)
            {
                IClientPlayer? localPlayer = api.World.Player;
                bool local = localPlayer?.Entity?.EntityId ==
                    Player.EntityId;
                bool localFirstPerson = local &&
                    localPlayer!.CameraMode ==
                        EnumCameraMode.FirstPerson;
                if (firstPerson != localFirstPerson)
                {
                    return false;
                }

                PlayerItemFrame? selected =
                    FrameOverride ??
                    (composer.AnyActiveAnimations()
                        ? currentFrame
                        : null);
                if (selected == null ||
                    pose.ForElement?.Name is not string name ||
                    !Enum.TryParse(
                        name,
                        ignoreCase: false,
                        out EnumAnimatedElement element) ||
                    element == EnumAnimatedElement.Unknown)
                {
                    return false;
                }

                bool controlled = element is
                    EnumAnimatedElement.ItemAnchor or
                    EnumAnimatedElement.ItemAnchorL or
                    EnumAnimatedElement.UpperArmR or
                    EnumAnimatedElement.LowerArmR or
                    EnumAnimatedElement.UpperArmL or
                    EnumAnimatedElement.LowerArmL;
                if (!controlled) return false;

                Vector3 eyePosition = new(
                    (float)Player.LocalEyePos.X,
                    (float)Player.LocalEyePos.Y,
                    (float)Player.LocalEyePos.Z
                );
                selected.Value.Apply(
                    pose,
                    element,
                    eyePosition,
                    (float)Player.Properties.EyeHeight,
                    Player.Pos.HeadPitch,
                    applyCameraPitch:
                        !firstPerson &&
                        composer.AnyActiveAnimations()
                );
                return true;
            }
        }
    }
}
