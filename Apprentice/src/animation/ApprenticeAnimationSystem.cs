using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
        private readonly Dictionary<long, ActiveAnimationState> activeStates =
            new();
        private readonly Dictionary<long, int> localSequences = new();
        private readonly Dictionary<long, int> lastAcceptedSequences = new();
        private readonly Dictionary<ElementPose, ElementPose> scratchPoses =
            new();
        private readonly Queue<string> callbackTrace = new();
        private readonly long tickListenerId;

        private bool disposed;
        private bool hookReached;
        private bool hookReachLogged;
        private long temporaryPoseApplications;
        private int finishOwnershipChecks;
        private int finishHitConflicts;
        private bool geometryFailureReported;
        private string lastFinishOwnership = "none";
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

            channel.SetMessageHandler<WarScytheAnimationPacket>(OnPacket);
            ApprenticeAnimationHook.Install(api, this);
            tickListenerId = api.Event.RegisterGameTickListener(
                OnTick,
                TickMilliseconds
            );
            editor = new WarScytheAnimationEditor(
                api,
                this,
                definition,
                geometryProbe
            );
            RegisterStatusCommand();
        }

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
                    ItemStack? stack = entity.RightHandItemSlot?.Itemstack;
                    if (!entity.Alive || stack?.Item == null ||
                        stack.Item.Code?.ToString() != definition.HeldItemCode ||
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

                        lastAcceptedSequences[entityId] = packet.Sequence;
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
                "[Apprentice] War Scythe animation relay registered with held-item validation."
            );
        }

        public void StartLocal(EntityAgent entity)
        {
            if (disposed || editor.PreviewActive ||
                !TryGetHeldWarScythe(
                entity,
                out ItemStack stack))
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

            StartState(entity.EntityId, stack.Item.Id, sequence);
            channel.SendPacket(CreatePacket(
                entity.EntityId,
                stack.Item.Id,
                sequence,
                stop: false
            ));
        }

        public bool EditorPreviewActive => editor.PreviewActive;

        public void EnterEditorMode()
        {
            EntityAgent? entity = api.World.Player?.Entity;
            if (entity == null || !activeStates.TryGetValue(
                    entity.EntityId,
                    out ActiveAnimationState? state))
            {
                return;
            }

            activeStates.Remove(entity.EntityId);
            channel.SendPacket(CreatePacket(
                entity.EntityId,
                state.ItemId,
                state.Sequence,
                stop: true
            ));
            Trace(entity.EntityId, state.Sequence, "editor-stop");
        }

        public void StopLocal(EntityAgent entity)
        {
            if (disposed || !activeStates.TryGetValue(
                entity.EntityId,
                out ActiveAnimationState? state))
            {
                return;
            }

            state.BeginForcedEaseOut(
                api.World.ElapsedMilliseconds,
                definition
            );
            channel.SendPacket(CreatePacket(
                entity.EntityId,
                state.ItemId,
                state.Sequence,
                stop: true
            ));
        }

        public void NoteLocalLifecycle(
            EntityAgent entity,
            string eventCode)
        {
            if (disposed || string.IsNullOrWhiteSpace(eventCode) ||
                !localSequences.TryGetValue(
                    entity.EntityId,
                    out int sequence))
            {
                return;
            }

            Trace(entity.EntityId, sequence, eventCode);
        }

        public ElementPose PrepareFinalPose(
            ClientAnimator animator,
            ElementPose pose)
        {
            if (disposed || !ApprenticeAnimationHook.Enabled ||
                animator.entityForLogging is not EntityAgent entity ||
                pose.ForElement?.Name is not string elementName)
            {
                return pose;
            }

            ApprenticeElementTransform target;
            float weight;
            if (editor.TrySample(entity, elementName, out target))
            {
                weight = 1f;
                editor.NoteHookElement(elementName);
            }
            else
            {
                if (!activeStates.TryGetValue(
                        entity.EntityId,
                        out ActiveAnimationState? state))
                {
                    return pose;
                }

                if (!StateStillMatches(entity, state))
                {
                    activeStates.Remove(entity.EntityId);
                    return pose;
                }

                long now = api.World.ElapsedMilliseconds;
                float actionTime = state.GetActionTimeSeconds(
                    now,
                    definition
                );
                weight = state.GetWeight(now, definition);
                if (weight <= 0 || !definition.TrySample(
                    elementName,
                    actionTime,
                    out target))
                {
                    return pose;
                }
            }

            ElementPose prepared = GetScratchPose(pose);
            CopyPose(pose, prepared);

            prepared.translateX = Lerp(
                prepared.translateX,
                target.OffsetX / 16f,
                weight
            );
            prepared.translateY = Lerp(
                prepared.translateY,
                target.OffsetY / 16f,
                weight
            );
            prepared.translateZ = Lerp(
                prepared.translateZ,
                target.OffsetZ / 16f,
                weight
            );
            prepared.degX = LerpAngle(
                prepared.degX,
                target.RotationX,
                weight
            );
            prepared.degY = LerpAngle(
                prepared.degY,
                target.RotationY,
                weight
            );
            prepared.degZ = LerpAngle(
                prepared.degZ,
                target.RotationZ,
                weight
            );

            hookReached = true;
            temporaryPoseApplications++;
            if (!hookReachLogged)
            {
                hookReachLogged = true;
                api.Logger.Notification(
                    "[Apprentice] War Scythe temporary-pose hook reached for entity {0}; category={1}; animator-owned poses remain unchanged.",
                    entity.EntityId,
                    definition.Category
                );
            }

            return prepared;
        }

        public string BuildStatus()
        {
            string callbacks = callbackTrace.Count == 0
                ? "none"
                : string.Join(" | ", callbackTrace);
            return string.Format(
                CultureInfo.InvariantCulture,
                "War Scythe animation: hookEnabled={0}, injectionPoints={1}, hookReached={2}, activeEntities={3}, editorPreview={4}, definition={5}, category={6}, duration={7:0.###}s, callbacks=[{8}], renderPath=temporary-copy-six-part-composer, controlledElements=ItemAnchor+ItemAnchorL+both-arm-chains, temporaryApplications={9}, scratchPoses={10}, finishOwnershipChecks={11}, finishHitConflicts={12}, lastFinishOwnership=[{13}], lastGeometry=[{14}]",
                ApprenticeAnimationHook.Enabled,
                ApprenticeAnimationHook.InjectionPointCount,
                hookReached,
                activeStates.Count,
                editor.PreviewActive,
                definition.Code,
                definition.Category,
                definition.TotalActionSeconds,
                callbacks,
                temporaryPoseApplications,
                scratchPoses.Count,
                finishOwnershipChecks,
                finishHitConflicts,
                lastFinishOwnership,
                lastGeometry
            );
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            api.Event.UnregisterGameTickListener(tickListenerId);
            editor.Dispose();
            activeStates.Clear();
            localSequences.Clear();
            lastAcceptedSequences.Clear();
            scratchPoses.Clear();
            callbackTrace.Clear();
            ApprenticeAnimationHook.Uninstall(this);
        }

        private void OnPacket(WarScytheAnimationPacket packet)
        {
            if (disposed || packet.AnimationCode != definition.Code ||
                packet.Category != definition.Category ||
                packet.Speed != 1f || packet.Sequence <= 0)
            {
                return;
            }

            if (api.World.GetEntityById(packet.EntityId) is not EntityAgent entity ||
                !TryGetHeldWarScythe(entity, out ItemStack stack) ||
                stack.Item.Id != packet.ItemId)
            {
                return;
            }

            if (packet.Stop)
            {
                if (activeStates.TryGetValue(
                    packet.EntityId,
                    out ActiveAnimationState? state) &&
                    state.Sequence == packet.Sequence)
                {
                    state.BeginForcedEaseOut(
                        api.World.ElapsedMilliseconds,
                        definition
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

            lastAcceptedSequences[packet.EntityId] = packet.Sequence;
            StartState(packet.EntityId, packet.ItemId, packet.Sequence);
        }

        private void StartState(long entityId, int itemId, int sequence)
        {
            if (activeStates.TryGetValue(
                entityId,
                out ActiveAnimationState? existing) &&
                sequence <= existing.Sequence)
            {
                return;
            }

            activeStates[entityId] = new ActiveAnimationState(
                itemId,
                sequence,
                api.World.ElapsedMilliseconds,
                geometryProbe.Acceptance
            );
            lastAcceptedSequences[entityId] = sequence;
            Trace(entityId, sequence, "start");
        }

        private void OnTick(float deltaTime)
        {
            if (disposed) return;
            editor.Tick(deltaTime);

            long now = api.World.ElapsedMilliseconds;
            foreach ((long entityId, ActiveAnimationState state) in
                activeStates.ToArray())
            {
                if (api.World.GetEntityById(entityId) is not EntityAgent entity ||
                    !StateStillMatches(entity, state))
                {
                    activeStates.Remove(entityId);
                    Trace(entityId, state.Sequence, "stale-item-stop");
                    continue;
                }

                float actionTime = state.GetActionTimeSeconds(
                    now,
                    definition
                );
                List<ApprenticeAnimationCallback> crossedCallbacks = new();
                definition.CollectCallbacks(
                    ref state.NextCallbackIndex,
                    actionTime,
                    crossedCallbacks
                );
                foreach (ApprenticeAnimationCallback callback in
                    crossedCallbacks)
                {
                    Trace(entityId, state.Sequence, callback.Code);
                }

                bool isLocalPlayer = api.World.Player?.Entity?.EntityId ==
                    entityId;
                if (isLocalPlayer && !geometryFailureReported)
                {
                    try
                    {
                        if (TryGetHeldWarScythe(
                            entity,
                            out ItemStack heldStack) &&
                            geometryProbe.TrySample(
                                entity,
                                heldStack,
                                out WarScytheGeometrySample geometrySample))
                        {
                            state.Geometry.Record(
                                geometrySample,
                                actionTime,
                                definition.Callbacks[0].TimeSeconds,
                                definition.Callbacks[2].TimeSeconds
                            );
                        }
                    }
                    catch (Exception exception)
                    {
                        lastGeometry = "probe-error";
                        if (!geometryFailureReported)
                        {
                            geometryFailureReported = true;
                            api.Logger.Warning(
                                "[Apprentice] War Scythe geometry diagnostics stopped without affecting animation playback: {0}",
                                exception.Message
                            );
                        }
                    }
                }

                if (state.IsFinished(now, definition))
                {
                    if (isLocalPlayer && !geometryFailureReported)
                    {
                        lastGeometry = state.Geometry.BuildStatus(
                            state.Sequence
                        );
                        api.Logger.Notification(
                            "[Apprentice] WARSCYTHE GEOMETRY {0}",
                            lastGeometry
                        );
                    }
                    InspectFinishOwnership(entity, state.Sequence);
                    activeStates.Remove(entityId);
                    Trace(entityId, state.Sequence, "finish");
                }
            }
        }

        private bool StateStillMatches(
            EntityAgent entity,
            ActiveAnimationState state) =>
            entity.Alive &&
            TryGetHeldWarScythe(entity, out ItemStack stack) &&
            stack.Item.Id == state.ItemId;

        private bool TryGetHeldWarScythe(
            EntityAgent entity,
            out ItemStack stack)
        {
            ItemStack? current = entity.RightHandItemSlot?.Itemstack;
            if (current?.Item?.Code?.ToString() == definition.HeldItemCode)
            {
                stack = current;
                return true;
            }

            stack = null!;
            return false;
        }

        private void InspectFinishOwnership(
            EntityAgent entity,
            int sequence)
        {
            ItemSlot? slot = entity.RightHandItemSlot;
            Item? item = slot?.Itemstack?.Item;
            string? hitCode = item == null || slot == null
                ? null
                : item.GetHeldTpHitAnimation(slot, entity);
            string? idleCode = item == null || slot == null
                ? null
                : item.GetHeldTpIdleAnimation(
                    slot,
                    entity,
                    EnumHand.Right
                );
            bool hitConfigured = !string.IsNullOrWhiteSpace(hitCode);
            bool hitActive = hitConfigured &&
                entity.AnimManager.IsAnimationActive(hitCode);
            bool idleActive = !string.IsNullOrWhiteSpace(idleCode) &&
                entity.AnimManager.IsAnimationActive(idleCode);

            finishOwnershipChecks++;
            if (hitConfigured || hitActive) finishHitConflicts++;
            lastFinishOwnership = string.Format(
                CultureInfo.InvariantCulture,
                "seq={0},hit={1},hitActive={2},idle={3},idleActive={4}",
                sequence,
                hitCode ?? "none",
                hitActive,
                idleCode ?? "none",
                idleActive
            );

            api.Logger.Notification(
                "[Apprentice] WARSCYTHE OWNERSHIP {0}",
                lastFinishOwnership
            );
        }

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
            EaseInMilliseconds = ToMilliseconds(definition.EaseInSeconds),
            EaseOutMilliseconds = ToMilliseconds(definition.EaseOutSeconds),
            Stop = stop
        };

        private void RegisterStatusCommand()
        {
            api.ChatCommands.Create("apprenticeanimstatus")
                .WithDescription(
                    "Show the Apprentice temporary-pose hook, owner and callback state"
                )
                .HandleWith(_ => TextCommandResult.Success(BuildStatus()));
        }

        private void Trace(long entityId, int sequence, string callback)
        {
            string entry = string.Format(
                CultureInfo.InvariantCulture,
                "entity={0},seq={1},{2}",
                entityId,
                sequence,
                callback
            );
            callbackTrace.Enqueue(entry);
            while (callbackTrace.Count > 12) callbackTrace.Dequeue();

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

        private ElementPose GetScratchPose(ElementPose source)
        {
            if (!scratchPoses.TryGetValue(
                source,
                out ElementPose? scratch))
            {
                scratch = new ElementPose();
                scratchPoses.Add(source, scratch);
            }

            return scratch;
        }

        private static void CopyPose(
            ElementPose source,
            ElementPose target)
        {
            target.ForElement = source.ForElement;
            target.AnimModelMatrix = source.AnimModelMatrix;
            target.ChildElementPoses = source.ChildElementPoses;
            target.degOffX = source.degOffX;
            target.degOffY = source.degOffY;
            target.degOffZ = source.degOffZ;
            target.degX = source.degX;
            target.degY = source.degY;
            target.degZ = source.degZ;
            target.scaleX = source.scaleX;
            target.scaleY = source.scaleY;
            target.scaleZ = source.scaleZ;
            target.translateX = source.translateX;
            target.translateY = source.translateY;
            target.translateZ = source.translateZ;
            target.RotShortestDistanceX = source.RotShortestDistanceX;
            target.RotShortestDistanceY = source.RotShortestDistanceY;
            target.RotShortestDistanceZ = source.RotShortestDistanceZ;
        }

        private static float Lerp(float from, float to, float progress) =>
            from + (to - from) * progress;

        private static float LerpAngle(
            float from,
            float to,
            float progress) =>
            from + ApprenticeElementTransform.NormalizeDegrees(to - from) *
                progress;

        private sealed class ActiveAnimationState
        {
            private long? forcedEaseOutStartedMs;
            private float forcedActionTimeSeconds;
            private float forcedStartWeight;

            public ActiveAnimationState(
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
            public int NextCallbackIndex = 0;
            public WarScytheGeometryTrace Geometry { get; }

            public float GetActionTimeSeconds(
                long now,
                ApprenticeAnimationDefinition definition)
            {
                if (forcedEaseOutStartedMs.HasValue)
                {
                    return forcedActionTimeSeconds;
                }

                float elapsed = Math.Max(0, now - StartedMs) / 1000f;
                return Math.Clamp(
                    elapsed - definition.EaseInSeconds,
                    0,
                    definition.DurationSeconds
                );
            }

            public float GetWeight(
                long now,
                ApprenticeAnimationDefinition definition)
            {
                if (forcedEaseOutStartedMs is long stopStarted)
                {
                    float stopProgress = Math.Clamp(
                        (now - stopStarted) /
                            (definition.EaseOutSeconds * 1000f),
                        0,
                        1
                    );
                    return forcedStartWeight * (1f - SmoothStep(stopProgress));
                }

                float elapsed = Math.Max(0, now - StartedMs) / 1000f;
                if (elapsed < definition.EaseInSeconds)
                {
                    return SmoothStep(elapsed / definition.EaseInSeconds);
                }

                float easeOutStart =
                    definition.EaseInSeconds + definition.DurationSeconds;
                if (elapsed <= easeOutStart) return 1f;

                float progress = (elapsed - easeOutStart) /
                    definition.EaseOutSeconds;
                return 1f - SmoothStep(Math.Clamp(progress, 0, 1));
            }

            public void BeginForcedEaseOut(
                long now,
                ApprenticeAnimationDefinition definition)
            {
                if (forcedEaseOutStartedMs.HasValue) return;

                // The caller freezes the current authored frame. Weight then
                // returns the whole rigid chain to the same engine pose over
                // one shared transition.
                forcedActionTimeSeconds = GetActionTimeSeconds(
                    now,
                    definition
                );
                forcedStartWeight = GetWeight(now, definition);
                forcedEaseOutStartedMs = now;
            }

            public bool IsFinished(
                long now,
                ApprenticeAnimationDefinition definition)
            {
                if (forcedEaseOutStartedMs is long stopStarted)
                {
                    return now - stopStarted >=
                        definition.EaseOutSeconds * 1000f;
                }

                return now - StartedMs >=
                    definition.TotalActionSeconds * 1000f;
            }

            private static float SmoothStep(float value) =>
                value * value * (3f - 2f * value);
        }
    }
}
