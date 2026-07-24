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
            firstPersonBehavior!.PlayAttack(
                stack.Item.Id,
                callback => Trace(
                    entity.EntityId,
                    sequence,
                    callback
                )
            );
            GetThirdPersonBehavior(player).PlayAttack(
                stack.Item.Id,
                callbackHandler: null
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

        public void CompleteLocal(EntityAgent entity)
        {
            if (disposed)
            {
                return;
            }

            int itemId;
            int sequence;
            if (activeStates.TryGetValue(
                entity.EntityId,
                out ActiveRuntimeState? state))
            {
                itemId = state.ItemId;
                sequence = state.Sequence;
                activeStates.Remove(entity.EntityId);
            }
            else if (entity is EntityPlayer player &&
                localSequences.TryGetValue(
                    entity.EntityId,
                    out sequence) &&
                TryGetHeldWarScythe(player, out ItemStack stack))
            {
                itemId = stack.Item.Id;
            }
            else
            {
                return;
            }

            firstPersonBehavior?.CompleteAction();
            if (thirdPersonBehaviors.TryGetValue(
                entity.EntityId,
                out WarScythePoseBehavior? third))
            {
                third.CompleteAction();
            }

            channel.SendPacket(CreatePacket(
                entity.EntityId,
                itemId,
                sequence,
                stop: true
            ));
            Trace(entity.EntityId, sequence, "action-complete");
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
                firstPersonBehavior.BeginBasePoseCapture();
            }
            third.Advance(deltaTime);
            third.BeginBasePoseCapture();
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
            string firstPersonState =
                firstPersonBehavior?.StateName ?? "uninitialized";
            string thirdPersonStates =
                thirdPersonBehaviors.Count == 0
                    ? "none"
                    : string.Join(
                        ",",
                        thirdPersonBehaviors
                            .OrderBy(entry => entry.Key)
                            .Select(entry =>
                                $"{entry.Key}:{entry.Value.StateName}")
                    );
            return string.Format(
                CultureInfo.InvariantCulture,
                "War Scythe animation: pipeline=OverhaulLib-reference AnimationJson->Animation->Animator->Composer->OnFrameInvoke->ElementPose; hookEnabled={0}; insertionPoints={1}; hookReached={2}; FPowner={3}; TPowners={4}; FPstate={5}; TPstates=[{6}]; activeEntities={7}; editorPreview={8}; category={9}; duration={10:0.###}s; appliedElements={11}; callbacks=[{12}]; lastGeometry=[{13}]",
                ApprenticeAnimationHook.Enabled,
                ApprenticeAnimationHook.InjectionPointCount,
                hookReached,
                firstPersonBehavior != null,
                thirdPersonBehaviors.Count,
                firstPersonState,
                thirdPersonStates,
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
                    GetThirdPersonBehavior(player).CompleteAction();
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
            GetThirdPersonBehavior(player).PlayAttack(
                stack.Item.Id,
                callbackHandler: null
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

            if (editor.PreviewActive) return;

            foreach (WarScythePoseBehavior behavior in
                thirdPersonBehaviors.Values)
            {
                RequestNativeRestWhenReady(behavior, now);
            }
            if (firstPersonBehavior != null)
            {
                RequestNativeRestWhenReady(firstPersonBehavior, now);
            }
        }

        private void RequestNativeRestWhenReady(
            WarScythePoseBehavior behavior,
            long now)
        {
            EntityPlayer player = behavior.Player;
            if (activeStates.ContainsKey(player.EntityId) ||
                !player.Alive ||
                !TryGetHeldWarScythe(player, out _))
            {
                return;
            }

            bool local = IsLocalPlayer(player);
            bool firstPersonView = local &&
                api.World.Player?.CameraMode ==
                    EnumCameraMode.FirstPerson;
            if (behavior.IsFirstPerson != firstPersonView)
            {
                return;
            }

            if (behavior.IsRest)
            {
                behavior.EnsureNativeRest();
                return;
            }
            if (!behavior.IsReadyForRest(now))
            {
                return;
            }

            behavior.RequestReturnToRest();
        }

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
            private const int CompleteCaptureMask = 0b11_1111;
            private const int RestCaptureFrameCount = 2;
            private const int NativeRestHandoffFrameCount = 3;

            private static readonly float[] UpperTorsoXCurve =
            {
                -2.5f, -4.5f, -6.5f, -5f, -4.5f, -2.5f
            };
            private static readonly float[] UpperTorsoYCurve =
            {
                -5f, -10f, -6.5f, 2.5f, 13f, 6.5f
            };
            private static readonly float[] UpperTorsoZCurve =
            {
                2.5f, 4.5f, 5f, 4.5f, 4f, 2.5f
            };
            private static readonly float[] LowerTorsoYCurve =
            {
                -13f, -23f, -16f, 6.5f, 28f, 13f
            };
            private static readonly float[] LowerTorsoZCurve =
            {
                5f, 6.5f, 6.5f, 6.5f, 6.5f, 5f
            };

            private readonly ICoreClientAPI api;
            private readonly bool firstPerson;
            private readonly ApprenticeAnimationDefinition definition;
            private readonly Composer composer;
            private readonly Apprentice.AnimationReference.Animation
                swingAnimation;
            private readonly PlayerItemFrame readyFrame;
            private readonly Apprentice.AnimationReference.Animation
                readyIdleAnimation;
            private PlayerItemFrame currentFrame =
                PlayerItemFrame.Empty;
            private AnimationElement capturedItemAnchor;
            private AnimationElement capturedItemAnchorL;
            private AnimationElement capturedUpperArmR;
            private AnimationElement capturedLowerArmR;
            private AnimationElement capturedUpperArmL;
            private AnimationElement capturedLowerArmL;
            private Action<string>? swingCallbackHandler;
            private WarScythePoseState state =
                WarScythePoseState.Rest;
            private int captureMask;
            private int activeItemId;
            private long readySinceMs;
            private int restCaptureFramesRemaining;
            private int nativeRestHandoffFramesRemaining;
            private bool restRequested;
            private bool missingBasePoseLogged;

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
                swingAnimation = CreateBodyWeightedSwingAnimation(
                    definition.Animation
                );
                readyFrame = swingAnimation.StillFrame(0);
                readyIdleAnimation = CreateReadyIdleAnimation(
                    readyFrame
                );
            }

            public EntityPlayer Player { get; }
            public PlayerItemFrame? FrameOverride { get; set; }
            public string StateName => state.ToString();
            public bool IsFirstPerson => firstPerson;
            public bool IsRest =>
                state == WarScythePoseState.Rest;
            private string NativeRestAnimation =>
                firstPerson
                    ? "scytheIdle-fp"
                    : "scytheIdle";
            private string OtherNativeRestAnimation =>
                firstPerson
                    ? "scytheIdle"
                    : "scytheIdle-fp";

            public void PlayAttack(
                int itemId,
                Action<string>? callbackHandler)
            {
                activeItemId = itemId;
                readySinceMs = 0;
                restCaptureFramesRemaining = 0;
                nativeRestHandoffFramesRemaining = 0;
                restRequested = false;
                swingCallbackHandler = callbackHandler;
                Player.StopAnimation(NativeRestAnimation);

                if (state == WarScythePoseState.ReadyIdle)
                {
                    QueueSwing();
                    return;
                }

                PlayerItemFrame start =
                    ResolveTransitionStart();
                state = WarScythePoseState.ToReadyBeforeSwing;
                composer.Play(CreateRequest(
                    CreateDrawTransition(
                        start,
                        readyFrame,
                        TimeSpan.FromSeconds(
                            definition.EaseInSeconds
                        )
                    ),
                    finishCallback: BeginSwing,
                    callbackHandler: null
                ));
            }

            public void CompleteAction()
            {
                // The reference phase chain owns completion. A normal held
                // action stop must not erase Ready; cancellation still calls
                // Stop/StopAll and releases the category immediately.
            }

            public bool IsReadyForRest(long now) =>
                state == WarScythePoseState.ReadyIdle &&
                readySinceMs > 0 &&
                now - readySinceMs >=
                    definition.ReadyIdleDelaySeconds * 1000f;

            public void RequestReturnToRest()
            {
                if (state == WarScythePoseState.ReadyIdle &&
                    !restRequested)
                {
                    restRequested = true;
                    restCaptureFramesRemaining =
                        RestCaptureFrameCount;
                    Player.StopAnimation(
                        OtherNativeRestAnimation
                    );
                    Player.AnimManager.StartAnimation(
                        NativeRestAnimation
                    );
                }
            }

            public void EnsureNativeRest()
            {
                if (Player.AnimManager.IsAnimationActive(
                        NativeRestAnimation))
                {
                    return;
                }

                Player.StopAnimation(OtherNativeRestAnimation);
                Player.AnimManager.StartAnimation(
                    NativeRestAnimation
                );
            }

            public void BeginBasePoseCapture()
            {
                captureMask = 0;
                if (restRequested &&
                    state == WarScythePoseState.ReadyIdle &&
                    restCaptureFramesRemaining > 0)
                {
                    restCaptureFramesRemaining--;
                }
            }

            public void Stop(string category)
            {
                composer.Stop(category);
                activeItemId = 0;
                currentFrame = PlayerItemFrame.Empty;
                swingCallbackHandler = null;
                readySinceMs = 0;
                restCaptureFramesRemaining = 0;
                nativeRestHandoffFramesRemaining = 0;
                restRequested = false;
                state = WarScythePoseState.Rest;
            }

            public void StopAll()
            {
                composer.StopAll();
                activeItemId = 0;
                currentFrame = PlayerItemFrame.Empty;
                FrameOverride = null;
                swingCallbackHandler = null;
                readySinceMs = 0;
                restCaptureFramesRemaining = 0;
                nativeRestHandoffFramesRemaining = 0;
                restRequested = false;
                state = WarScythePoseState.Rest;
            }

            public void Advance(float deltaTime)
            {
                if (FrameOverride != null) return;

                Item? heldItem =
                    Player.RightHandItemSlot?.Itemstack?.Item;
                if (activeItemId != 0 &&
                    (!Player.Alive ||
                    heldItem?.Id != activeItemId ||
                    heldItem?.Code?.ToString() !=
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
                if (state ==
                        WarScythePoseState.NativeRestHandoff &&
                    nativeRestHandoffFramesRemaining > 0)
                {
                    nativeRestHandoffFramesRemaining--;
                    if (nativeRestHandoffFramesRemaining == 0)
                    {
                        composer.Stop(definition.Category);
                        currentFrame = PlayerItemFrame.Empty;
                        activeItemId = 0;
                        state = WarScythePoseState.Rest;
                    }
                }
                if (!composer.AnyActiveAnimations())
                {
                    activeItemId = 0;
                    if (state == WarScythePoseState.ToRest)
                    {
                        state = WarScythePoseState.Rest;
                    }
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

                if (pose.ForElement?.Name is not string name ||
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
                    EnumAnimatedElement.LowerArmL or
                    EnumAnimatedElement.UpperTorso or
                    EnumAnimatedElement.LowerTorso;
                if (!controlled) return false;

                CaptureBasePose(element, pose);
                if (captureMask == CompleteCaptureMask &&
                    restRequested &&
                    state == WarScythePoseState.ReadyIdle &&
                    restCaptureFramesRemaining == 0)
                {
                    QueueReturnToRest(BuildCapturedBaseFrame());
                }

                PlayerItemFrame? selected =
                    FrameOverride ??
                    (composer.AnyActiveAnimations()
                        ? currentFrame
                        : null);
                if (selected == null) return false;

                Vector3 eyePosition = new(
                    (float)Player.LocalEyePos.X,
                    (float)Player.LocalEyePos.Y,
                    (float)Player.LocalEyePos.Z
                );
                if (element == EnumAnimatedElement.UpperTorso)
                {
                    return ApplyAdditive(
                        selected.Value.Player.UpperTorso,
                        pose
                    );
                }
                if (element == EnumAnimatedElement.LowerTorso)
                {
                    return ApplyAdditive(
                        selected.Value.Player.LowerTorso,
                        pose
                    );
                }

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

            private PlayerItemFrame ResolveTransitionStart()
            {
                if (state != WarScythePoseState.Rest &&
                    HasBothHands(currentFrame))
                {
                    return currentFrame;
                }
                if (captureMask == CompleteCaptureMask)
                {
                    return BuildCapturedBaseFrame();
                }

                if (!missingBasePoseLogged)
                {
                    missingBasePoseLogged = true;
                    api.Logger.Warning(
                        "[Apprentice] War Scythe {0} Rest pose was not captured before attack start; beginning from Ready without inventing pose values.",
                        firstPerson ? "first-person" : "third-person"
                    );
                }
                return readyFrame;
            }

            private bool BeginSwing()
            {
                QueueSwing();
                return true;
            }

            private void QueueSwing()
            {
                state = WarScythePoseState.Swing;
                composer.Play(CreateRequest(
                    swingAnimation,
                    finishCallback: BeginReturnToReady,
                    callbackHandler: swingCallbackHandler
                ));
            }

            private bool BeginReturnToReady()
            {
                state = WarScythePoseState.ToReadyAfterSwing;
                composer.Play(CreateRequest(
                    CreateTransition(
                        swingAnimation.StillFrame(1),
                        readyFrame,
                        TimeSpan.FromSeconds(
                            definition.EaseOutSeconds
                        )
                    ),
                    finishCallback: HoldReady,
                    callbackHandler: null
                ));
                return true;
            }

            private bool HoldReady()
            {
                state = WarScythePoseState.ReadyIdle;
                readySinceMs = api.World.ElapsedMilliseconds;
                restRequested = false;
                Player.StopAnimation(NativeRestAnimation);
                composer.Play(CreateRequest(
                    readyIdleAnimation,
                    finishCallback: RepeatReadyIdle,
                    callbackHandler: null
                ));
                return true;
            }

            private bool RepeatReadyIdle()
            {
                if (state != WarScythePoseState.ReadyIdle)
                {
                    return false;
                }

                composer.Play(CreateRequest(
                    readyIdleAnimation,
                    finishCallback: RepeatReadyIdle,
                    callbackHandler: null
                ));
                return true;
            }

            private void QueueReturnToRest(
                PlayerItemFrame restFrame)
            {
                Player.StopAnimation(NativeRestAnimation);
                restRequested = false;
                restCaptureFramesRemaining = 0;
                readySinceMs = 0;
                state = WarScythePoseState.ToRest;
                PlayerItemFrame transitionStart =
                    HasBothHands(currentFrame)
                        ? currentFrame
                        : readyFrame;
                composer.Play(CreateRequest(
                    CreateReturnToRestTransition(
                        transitionStart,
                        restFrame,
                        TimeSpan.FromSeconds(
                            definition.ReadyToRestSeconds
                        )
                    ),
                    finishCallback: FinishReturnToRest,
                    callbackHandler: null
                ));
            }

            private bool FinishReturnToRest()
            {
                state = WarScythePoseState.NativeRestHandoff;
                swingCallbackHandler = null;
                readySinceMs = 0;
                nativeRestHandoffFramesRemaining =
                    NativeRestHandoffFrameCount;
                Player.StopAnimation(
                    OtherNativeRestAnimation
                );
                Player.AnimManager.StartAnimation(
                    NativeRestAnimation
                );
                return true;
            }

            private AnimationRequest CreateRequest(
                Apprentice.AnimationReference.Animation animation,
                Func<bool>? finishCallback,
                Action<string>? callbackHandler) =>
                new(
                    animation,
                    animationSpeed: 1f,
                    weight: 1f,
                    category: definition.Category,
                    easeOutDuration: TimeSpan.FromMilliseconds(1),
                    easeInDuration: TimeSpan.FromMilliseconds(1),
                    easeOut: false,
                    finishCallback: finishCallback,
                    callbackHandler: callbackHandler
                );

            private static Apprentice.AnimationReference.Animation
                CreateTransition(
                    PlayerItemFrame from,
                    PlayerItemFrame to,
                    TimeSpan duration)
            {
                TimeSpan safeDuration = SafeDuration(duration);
                PlayerFrame target =
                    InterpolatePlayerFrameShortest(
                        from.Player,
                        to.Player,
                        1f
                    );
                TimeSpan midpointTime =
                    TimeSpan.FromTicks(safeDuration.Ticks / 2);
                PlayerFrame midpoint =
                    InterpolatePlayerFrameShortest(
                        from.Player,
                        target,
                        0.5f
                    );
                return new Apprentice.AnimationReference.Animation(
                    new[]
                    {
                        new PLayerKeyFrame(
                            from.Player,
                            TimeSpan.Zero,
                            EasingFunctionType.Linear
                        ),
                        new PLayerKeyFrame(
                            midpoint,
                            midpointTime,
                            EasingFunctionType.EaseInOutSine
                        ),
                        new PLayerKeyFrame(
                            target,
                            safeDuration,
                            EasingFunctionType.EaseInOutSine
                        )
                    }
                );
            }

            private static Apprentice.AnimationReference.Animation
                CreateDrawTransition(
                    PlayerItemFrame from,
                    PlayerItemFrame to,
                    TimeSpan duration)
            {
                TimeSpan safeDuration = SafeDuration(duration);
                PlayerFrame target =
                    InterpolatePlayerFrameShortest(
                        from.Player,
                        to.Player,
                        1f
                    );
                PlayerFrame firstClearance =
                    CreateShoulderSeatedFrame(
                        from.Player,
                        target,
                        pathProgress: 0.24f,
                        socketProgress: 0.10f
                    );
                firstClearance = OffsetBodyRotation(
                    firstClearance,
                    upperY: -2f,
                    upperZ: 1f,
                    lowerY: -4f,
                    lowerZ: 1f
                );
                PlayerFrame secondClearance =
                    CreateShoulderSeatedFrame(
                        from.Player,
                        target,
                        pathProgress: 0.72f,
                        socketProgress: 0.55f
                    );
                secondClearance = OffsetBodyRotation(
                    secondClearance,
                    upperY: -1f,
                    upperZ: 0.5f,
                    lowerY: -2f,
                    lowerZ: 0.5f
                );

                return new Apprentice.AnimationReference.Animation(
                    new[]
                    {
                        new PLayerKeyFrame(
                            from.Player,
                            TimeSpan.Zero,
                            EasingFunctionType.Linear
                        ),
                        new PLayerKeyFrame(
                            firstClearance,
                            AtFraction(safeDuration, 0.32f),
                            EasingFunctionType.EaseInOutSine
                        ),
                        new PLayerKeyFrame(
                            secondClearance,
                            AtFraction(safeDuration, 0.68f),
                            EasingFunctionType.EaseInOutSine
                        ),
                        new PLayerKeyFrame(
                            target,
                            safeDuration,
                            EasingFunctionType.EaseInOutSine
                        )
                    }
                );
            }

            private static Apprentice.AnimationReference.Animation
                CreateReturnToRestTransition(
                    PlayerItemFrame from,
                    PlayerItemFrame to,
                    TimeSpan duration)
            {
                TimeSpan safeDuration = SafeDuration(duration);
                PlayerFrame target =
                    InterpolatePlayerFrameShortest(
                        from.Player,
                        to.Player,
                        1f
                    );
                PlayerFrame firstSocketFrame =
                    CreateShoulderSeatedFrame(
                        from.Player,
                        target,
                        pathProgress: 0.26f,
                        socketProgress: 0.55f
                    );
                PlayerFrame secondSocketFrame =
                    CreateShoulderSeatedFrame(
                        from.Player,
                        target,
                        pathProgress: 0.76f,
                        socketProgress: 0.92f
                    );

                return new Apprentice.AnimationReference.Animation(
                    new[]
                    {
                        new PLayerKeyFrame(
                            from.Player,
                            TimeSpan.Zero,
                            EasingFunctionType.Linear
                        ),
                        new PLayerKeyFrame(
                            firstSocketFrame,
                            AtFraction(safeDuration, 0.32f),
                            EasingFunctionType.EaseInOutSine
                        ),
                        new PLayerKeyFrame(
                            secondSocketFrame,
                            AtFraction(safeDuration, 0.72f),
                            EasingFunctionType.EaseInOutSine
                        ),
                        new PLayerKeyFrame(
                            target,
                            safeDuration,
                            EasingFunctionType.EaseInOutSine
                        )
                    }
                );
            }

            private static Apprentice.AnimationReference.Animation
                CreateBodyWeightedSwingAnimation(
                    Apprentice.AnimationReference.Animation source)
            {
                PLayerKeyFrame[] frames = source.PlayerKeyFrames
                    .Select((frame, index) =>
                    {
                        float phase = source.PlayerKeyFrames.Count <= 1
                            ? 0
                            : (float)index /
                                (source.PlayerKeyFrames.Count - 1);
                        AnimationElement upper = new(
                            0,
                            0,
                            0,
                            SampleCurve(
                                phase,
                                UpperTorsoXCurve
                            ),
                            SampleCurve(
                                phase,
                                UpperTorsoYCurve
                            ),
                            SampleCurve(
                                phase,
                                UpperTorsoZCurve
                            )
                        );
                        AnimationElement lower = new(
                            0,
                            0,
                            0,
                            0,
                            SampleCurve(
                                phase,
                                LowerTorsoYCurve
                            ),
                            SampleCurve(
                                phase,
                                LowerTorsoZCurve
                            )
                        );
                        return new PLayerKeyFrame(
                            WithBodyPose(
                                frame.Frame,
                                upper,
                                lower
                            ),
                            frame.Time,
                            frame.EasingFunction,
                            frame.EasingType,
                            frame.FrameProgressRange
                        );
                    })
                    .ToArray();

                return new Apprentice.AnimationReference.Animation(
                    frames,
                    source.ItemKeyFrames,
                    source.SoundFrames,
                    source.ParticlesFrames,
                    source.CallbackFrames
                )
                {
                    Hold = source.Hold,
                    ItemAnimationStart =
                        source.ItemAnimationStart,
                    ItemAnimationEnd = source.ItemAnimationEnd
                };
            }

            private static Apprentice.AnimationReference.Animation
                CreateReadyIdleAnimation(
                    PlayerItemFrame ready)
            {
                PlayerItemFrame breath =
                    CreateReadyBreathFrame(ready);
                Apprentice.AnimationReference.Animation animation =
                    new(
                        new[]
                        {
                            new PLayerKeyFrame(
                                ready.Player,
                                TimeSpan.Zero,
                                EasingFunctionType.EaseInOutSine
                            ),
                            new PLayerKeyFrame(
                                breath.Player,
                                TimeSpan.FromMilliseconds(900),
                                EasingFunctionType.EaseInOutSine
                            ),
                            new PLayerKeyFrame(
                                ready.Player,
                                TimeSpan.FromMilliseconds(1800),
                                EasingFunctionType.EaseInOutSine
                            )
                        }
                    );
                animation.Hold = false;
                return animation;
            }

            private static PlayerItemFrame CreateReadyBreathFrame(
                PlayerItemFrame ready)
            {
                PlayerFrame source = ready.Player;
                RightHandFrame right =
                    source.RightHand ?? RightHandFrame.Zero;
                LeftHandFrame left =
                    source.LeftHand ?? LeftHandFrame.Zero;

                PlayerFrame player = new(
                    rightHand: new RightHandFrame(
                        OffsetRotation(
                            right.ItemAnchor,
                            rotationZ: 1.15f
                        ),
                        OffsetRotation(
                            right.LowerArmR,
                            rotationX: 0.75f
                        ),
                        OffsetRotation(
                            right.UpperArmR,
                            rotationX: 1.35f
                        )
                    ),
                    leftHand: new LeftHandFrame(
                        left.ItemAnchorL,
                        OffsetRotation(
                            left.LowerArmL,
                            rotationX: 0.75f
                        ),
                        OffsetRotation(
                            left.UpperArmL,
                            rotationX: 1.35f
                        )
                    ),
                    otherParts: source.OtherParts,
                    upperTorso: OffsetRotation(
                        source.UpperTorso ??
                            AnimationElement.Zero,
                        rotationX: 1.05f,
                        rotationZ: 0.6f
                    ),
                    detachedAnchorFrame:
                        source.DetachedAnchorFrame,
                    detachedAnchor: source.DetachedAnchor,
                    switchArms: source.SwitchArms,
                    pitchFollow: source.PitchFollow,
                    fovMultiplier: source.FovMultiplier,
                    bobbingAmplitude: source.BobbingAmplitude,
                    detachedAnchorFollow:
                        source.DetachedAnchorFollow,
                    lowerTorso: OffsetRotation(
                        source.LowerTorso ??
                            AnimationElement.Zero,
                        rotationY: 1.1f,
                        rotationZ: 0.3f
                    )
                );
                return new PlayerItemFrame(player, ready.Item);
            }

            private static PlayerFrame CreateShoulderSeatedFrame(
                PlayerFrame from,
                PlayerFrame to,
                float pathProgress,
                float socketProgress)
            {
                PlayerFrame result =
                    InterpolatePlayerFrameShortest(
                        from,
                        to,
                        pathProgress
                    );
                RightHandFrame? right = result.RightHand;
                if (from.RightHand.HasValue &&
                    to.RightHand.HasValue &&
                    right.HasValue)
                {
                    RightHandFrame current = right.Value;
                    right = new RightHandFrame(
                        current.ItemAnchor,
                        current.LowerArmR,
                        InterpolateElementShortest(
                            from.RightHand.Value.UpperArmR,
                            to.RightHand.Value.UpperArmR,
                            socketProgress,
                            pathProgress
                        )
                    );
                }

                LeftHandFrame? left = result.LeftHand;
                if (from.LeftHand.HasValue &&
                    to.LeftHand.HasValue &&
                    left.HasValue)
                {
                    LeftHandFrame current = left.Value;
                    left = new LeftHandFrame(
                        current.ItemAnchorL,
                        current.LowerArmL,
                        InterpolateElementShortest(
                            from.LeftHand.Value.UpperArmL,
                            to.LeftHand.Value.UpperArmL,
                            socketProgress,
                            pathProgress
                        )
                    );
                }

                return CopyPlayerFrame(
                    result,
                    right,
                    left,
                    result.UpperTorso,
                    result.LowerTorso
                );
            }

            private static PlayerFrame
                InterpolatePlayerFrameShortest(
                    PlayerFrame from,
                    PlayerFrame to,
                    float progress)
            {
                float amount = Math.Clamp(progress, 0, 1);
                PlayerFrame result =
                    PlayerFrame.Interpolate(from, to, amount);

                RightHandFrame? right = result.RightHand;
                if (from.RightHand.HasValue &&
                    to.RightHand.HasValue)
                {
                    right = new RightHandFrame(
                        InterpolateElementShortest(
                            from.RightHand.Value.ItemAnchor,
                            to.RightHand.Value.ItemAnchor,
                            amount,
                            amount
                        ),
                        InterpolateElementShortest(
                            from.RightHand.Value.LowerArmR,
                            to.RightHand.Value.LowerArmR,
                            amount,
                            amount
                        ),
                        InterpolateElementShortest(
                            from.RightHand.Value.UpperArmR,
                            to.RightHand.Value.UpperArmR,
                            amount,
                            amount
                        )
                    );
                }

                LeftHandFrame? left = result.LeftHand;
                if (from.LeftHand.HasValue &&
                    to.LeftHand.HasValue)
                {
                    left = new LeftHandFrame(
                        InterpolateElementShortest(
                            from.LeftHand.Value.ItemAnchorL,
                            to.LeftHand.Value.ItemAnchorL,
                            amount,
                            amount
                        ),
                        InterpolateElementShortest(
                            from.LeftHand.Value.LowerArmL,
                            to.LeftHand.Value.LowerArmL,
                            amount,
                            amount
                        ),
                        InterpolateElementShortest(
                            from.LeftHand.Value.UpperArmL,
                            to.LeftHand.Value.UpperArmL,
                            amount,
                            amount
                        )
                    );
                }

                AnimationElement? upper =
                    InterpolateOptionalElementShortest(
                        from.UpperTorso,
                        to.UpperTorso,
                        amount
                    );
                AnimationElement? lower =
                    InterpolateOptionalElementShortest(
                        from.LowerTorso,
                        to.LowerTorso,
                        amount
                    );
                return CopyPlayerFrame(
                    result,
                    right,
                    left,
                    upper,
                    lower
                );
            }

            private static AnimationElement
                InterpolateElementShortest(
                    AnimationElement from,
                    AnimationElement to,
                    float translationProgress,
                    float rotationProgress) =>
                new(
                    InterpolateValue(
                        from.OffsetX,
                        to.OffsetX,
                        translationProgress
                    ),
                    InterpolateValue(
                        from.OffsetY,
                        to.OffsetY,
                        translationProgress
                    ),
                    InterpolateValue(
                        from.OffsetZ,
                        to.OffsetZ,
                        translationProgress
                    ),
                    InterpolateAngle(
                        from.RotationX,
                        to.RotationX,
                        rotationProgress
                    ),
                    InterpolateAngle(
                        from.RotationY,
                        to.RotationY,
                        rotationProgress
                    ),
                    InterpolateAngle(
                        from.RotationZ,
                        to.RotationZ,
                        rotationProgress
                    )
                );

            private static AnimationElement?
                InterpolateOptionalElementShortest(
                    AnimationElement? from,
                    AnimationElement? to,
                    float progress)
            {
                if (!from.HasValue && !to.HasValue)
                {
                    return null;
                }
                return InterpolateElementShortest(
                    from ?? AnimationElement.Zero,
                    to ?? AnimationElement.Zero,
                    progress,
                    progress
                );
            }

            private static float? InterpolateValue(
                float? from,
                float? to,
                float progress)
            {
                if (!from.HasValue && !to.HasValue)
                {
                    return null;
                }
                float amount = Math.Clamp(progress, 0, 1);
                float start = from.GetValueOrDefault();
                return start +
                    (to.GetValueOrDefault() - start) * amount;
            }

            private static float? InterpolateAngle(
                float? from,
                float? to,
                float progress)
            {
                if (!from.HasValue && !to.HasValue)
                {
                    return null;
                }
                float start = from.GetValueOrDefault();
                float end = to.GetValueOrDefault();
                float delta =
                    (end - start + 180f) % 360f;
                if (delta < 0) delta += 360f;
                delta -= 180f;
                return start +
                    delta * Math.Clamp(progress, 0, 1);
            }

            private static PlayerFrame OffsetBodyRotation(
                PlayerFrame source,
                float upperY,
                float upperZ,
                float lowerY,
                float lowerZ) =>
                CopyPlayerFrame(
                    source,
                    source.RightHand,
                    source.LeftHand,
                    OffsetRotation(
                        source.UpperTorso ??
                            AnimationElement.Zero,
                        rotationY: upperY,
                        rotationZ: upperZ
                    ),
                    OffsetRotation(
                        source.LowerTorso ??
                            AnimationElement.Zero,
                        rotationY: lowerY,
                        rotationZ: lowerZ
                    )
                );

            private static PlayerFrame WithBodyPose(
                PlayerFrame source,
                AnimationElement upper,
                AnimationElement lower) =>
                CopyPlayerFrame(
                    source,
                    source.RightHand,
                    source.LeftHand,
                    upper,
                    lower
                );

            private static PlayerFrame CopyPlayerFrame(
                PlayerFrame source,
                RightHandFrame? right,
                LeftHandFrame? left,
                AnimationElement? upper,
                AnimationElement? lower) =>
                new(
                    rightHand: right,
                    leftHand: left,
                    otherParts: source.OtherParts,
                    upperTorso: upper,
                    detachedAnchorFrame:
                        source.DetachedAnchorFrame,
                    detachedAnchor: source.DetachedAnchor,
                    switchArms: source.SwitchArms,
                    pitchFollow: source.PitchFollow,
                    fovMultiplier: source.FovMultiplier,
                    bobbingAmplitude: source.BobbingAmplitude,
                    detachedAnchorFollow:
                        source.DetachedAnchorFollow,
                    lowerTorso: lower
                );

            private static float SampleCurve(
                float progress,
                IReadOnlyList<float> values)
            {
                if (values.Count == 0) return 0;
                if (values.Count == 1) return values[0];

                float position = Math.Clamp(progress, 0, 1) *
                    (values.Count - 1);
                int startIndex = (int)Math.Floor(position);
                int endIndex = Math.Min(
                    values.Count - 1,
                    startIndex + 1
                );
                float amount = position - startIndex;
                return values[startIndex] +
                    (values[endIndex] - values[startIndex]) *
                    amount;
            }

            private static TimeSpan SafeDuration(
                TimeSpan duration) =>
                duration > TimeSpan.FromMilliseconds(1)
                    ? duration
                    : TimeSpan.FromMilliseconds(1);

            private static TimeSpan AtFraction(
                TimeSpan duration,
                float fraction) =>
                TimeSpan.FromTicks(
                    (long)(duration.Ticks *
                        Math.Clamp(fraction, 0, 1))
                );

            private static AnimationElement OffsetRotation(
                AnimationElement element,
                float rotationX = 0,
                float rotationY = 0,
                float rotationZ = 0) =>
                new(
                    element.OffsetX,
                    element.OffsetY,
                    element.OffsetZ,
                    Add(element.RotationX, rotationX),
                    Add(element.RotationY, rotationY),
                    Add(element.RotationZ, rotationZ)
                );

            private static float? Add(
                float? value,
                float offset) =>
                value.HasValue || offset != 0
                    ? value.GetValueOrDefault() + offset
                    : null;

            private static bool ApplyAdditive(
                AnimationElement? element,
                ElementPose pose)
            {
                if (!element.HasValue)
                {
                    return false;
                }

                AnimationElement value = element.Value;
                pose.translateX +=
                    value.OffsetX.GetValueOrDefault() / 16f;
                pose.translateY +=
                    value.OffsetY.GetValueOrDefault() / 16f;
                pose.translateZ +=
                    value.OffsetZ.GetValueOrDefault() / 16f;
                pose.degX += value.RotationX.GetValueOrDefault();
                pose.degY += value.RotationY.GetValueOrDefault();
                pose.degZ += value.RotationZ.GetValueOrDefault();
                return true;
            }

            private void CaptureBasePose(
                EnumAnimatedElement element,
                ElementPose pose)
            {
                AnimationElement captured = new(
                    pose.translateX * 16,
                    pose.translateY * 16,
                    pose.translateZ * 16,
                    pose.degX,
                    pose.degY,
                    pose.degZ
                );
                switch (element)
                {
                    case EnumAnimatedElement.ItemAnchor:
                        capturedItemAnchor = captured;
                        captureMask |= 1 << 0;
                        break;
                    case EnumAnimatedElement.ItemAnchorL:
                        capturedItemAnchorL = captured;
                        captureMask |= 1 << 1;
                        break;
                    case EnumAnimatedElement.UpperArmR:
                        capturedUpperArmR = captured;
                        captureMask |= 1 << 2;
                        break;
                    case EnumAnimatedElement.LowerArmR:
                        capturedLowerArmR = captured;
                        captureMask |= 1 << 3;
                        break;
                    case EnumAnimatedElement.UpperArmL:
                        capturedUpperArmL = captured;
                        captureMask |= 1 << 4;
                        break;
                    case EnumAnimatedElement.LowerArmL:
                        capturedLowerArmL = captured;
                        captureMask |= 1 << 5;
                        break;
                }
            }

            private PlayerItemFrame BuildCapturedBaseFrame() =>
                new(
                    new PlayerFrame(
                        rightHand: new RightHandFrame(
                            capturedItemAnchor,
                            capturedLowerArmR,
                            capturedUpperArmR
                        ),
                        leftHand: new LeftHandFrame(
                            capturedItemAnchorL,
                            capturedLowerArmL,
                            capturedUpperArmL
                        ),
                        upperTorso: AnimationElement.Zero,
                        lowerTorso: AnimationElement.Zero
                    ),
                    item: null
                );

            private static bool HasBothHands(
                PlayerItemFrame frame) =>
                frame.Player.RightHand.HasValue &&
                frame.Player.LeftHand.HasValue;

            private enum WarScythePoseState
            {
                Rest,
                ToReadyBeforeSwing,
                Swing,
                ToReadyAfterSwing,
                ReadyIdle,
                ToRest,
                NativeRestHandoff
            }
        }
    }
}
