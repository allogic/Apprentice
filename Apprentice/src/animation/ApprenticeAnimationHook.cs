using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class ApprenticeAnimationHook
    {
        private const string HarmonyId =
            "apprentice.animation.overhaullib-reference-pipeline.1_22_3";

        private static readonly Dictionary<ClientAnimator, EntityPlayer>
            Animators = new();
        private static readonly FieldInfo? AnimationManagerEntity =
            typeof(Vintagestory.API.Common.AnimationManager).GetField(
                "entity",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private static Harmony? harmony;
        private static MethodInfo? calculateMatricesTarget;
        private static MethodInfo? animationManagerFrameTarget;
        private static MethodInfo? beforeRenderTarget;
        private static MethodInfo? selfBeforeRenderTarget;
        private static ApprenticeAnimationSystem? owner;
        private static ICoreClientAPI? api;
        private static bool runtimeFailureReported;

        public static bool Enabled { get; private set; }
        public static int InjectionPointCount { get; private set; }

        public static void Install(
            ICoreClientAPI clientApi,
            ApprenticeAnimationSystem animationSystem)
        {
            if (owner != null)
            {
                throw new InvalidOperationException(
                    "The reference animation pipeline already has an owner."
                );
            }

            api = clientApi;
            owner = animationSystem;
            Enabled = false;
            InjectionPointCount = 0;
            runtimeFailureReported = false;
            Animators.Clear();

            try
            {
                calculateMatricesTarget =
                    ResolveCalculateMatricesTarget();
                animationManagerFrameTarget =
                    AccessTools.Method(
                        typeof(
                            Vintagestory.API.Common.AnimationManager),
                        "OnClientFrame"
                    ) ?? throw new MissingMethodException(
                        "AnimationManager.OnClientFrame"
                    );
                beforeRenderTarget =
                    AccessTools.Method(
                        typeof(EntityShapeRenderer),
                        "BeforeRender"
                    ) ?? throw new MissingMethodException(
                        "EntityShapeRenderer.BeforeRender"
                    );
                selfBeforeRenderTarget =
                    AccessTools.Method(
                        typeof(EntityPlayer),
                        nameof(EntityPlayer.OnSelfBeforeRender)
                    ) ?? throw new MissingMethodException(
                        nameof(EntityPlayer.OnSelfBeforeRender)
                    );

                MethodBody body =
                    calculateMatricesTarget.GetMethodBody() ??
                    throw new InvalidOperationException(
                        "ClientAnimator.calculateMatrices has no method body."
                    );
                if (body.LocalVariables.Count <= 4 ||
                    body.LocalVariables[4].LocalType !=
                        typeof(ElementPose))
                {
                    throw new InvalidOperationException(
                        "ClientAnimator.calculateMatrices local 4 is not the current ElementPose required by the reference pipeline."
                    );
                }

                harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    calculateMatricesTarget,
                    transpiler: new HarmonyMethod(
                        AccessTools.Method(
                            typeof(ApprenticeAnimationHook),
                            nameof(CalculateMatricesTranspiler)
                        ) ?? throw new MissingMethodException(
                            nameof(CalculateMatricesTranspiler)
                        )
                    )
                );
                harmony.Patch(
                    animationManagerFrameTarget,
                    prefix: new HarmonyMethod(
                        AccessTools.Method(
                            typeof(ApprenticeAnimationHook),
                            nameof(CaptureAnimator)
                        ) ?? throw new MissingMethodException(
                            nameof(CaptureAnimator)
                        )
                    )
                );
                harmony.Patch(
                    beforeRenderTarget,
                    prefix: new HarmonyMethod(
                        AccessTools.Method(
                            typeof(ApprenticeAnimationHook),
                            nameof(BeforeRender)
                        ) ?? throw new MissingMethodException(
                            nameof(BeforeRender)
                        )
                    )
                );
                harmony.Patch(
                    selfBeforeRenderTarget,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(
                            typeof(ApprenticeAnimationHook),
                            nameof(OnSelfBeforeRender)
                        ) ?? throw new MissingMethodException(
                            nameof(OnSelfBeforeRender)
                        )
                    )
                );

                int ownedTranspilers =
                    Harmony.GetPatchInfo(calculateMatricesTarget)?
                        .Transpilers.Count(
                            patch => patch.owner == HarmonyId
                        ) ?? 0;
                if (InjectionPointCount != 1 ||
                    ownedTranspilers != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected the one reference insertion before ShapeElement.GetLocalTransformMatrix; found {InjectionPointCount} insertion points and {ownedTranspilers} owned transpilers."
                    );
                }

                Enabled = true;
                clientApi.Logger.Notification(
                    "[Apprentice] OverhaulLib reference animation pipeline enabled: one final ElementPose insertion, animator mapping, render-frame composition, and separate FP/TP owners."
                );
            }
            catch (Exception exception)
            {
                harmony?.UnpatchAll(HarmonyId);
                harmony = null;
                Enabled = false;
                clientApi.Logger.Error(
                    "[Apprentice] War Scythe reference animation pipeline is disabled because the Vintage Story 1.22.3 contract did not pass: {0}",
                    exception
                );
            }
        }

        public static void Uninstall(
            ApprenticeAnimationSystem animationSystem)
        {
            if (!ReferenceEquals(owner, animationSystem)) return;

            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            calculateMatricesTarget = null;
            animationManagerFrameTarget = null;
            beforeRenderTarget = null;
            selfBeforeRenderTarget = null;
            owner = null;
            api = null;
            Enabled = false;
            InjectionPointCount = 0;
            runtimeFailureReported = false;
            Animators.Clear();
        }

        private static MethodInfo ResolveCalculateMatricesTarget()
        {
            Type[] signature =
            {
                typeof(int),
                typeof(float),
                typeof(List<ElementPose>),
                typeof(ShapeElementWeights[][]),
                typeof(float[]),
                typeof(List<ElementPose>[]),
                typeof(List<ElementPose>[]),
                typeof(int)
            };
            return typeof(ClientAnimator).GetMethod(
                "calculateMatrices",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: signature,
                modifiers: null
            ) ?? throw new MissingMethodException(
                "ClientAnimator.calculateMatrices(int, float, List<ElementPose>, ShapeElementWeights[][], float[], List<ElementPose>[], List<ElementPose>[], int)"
            );
        }

        private static IEnumerable<CodeInstruction>
            CalculateMatricesTranspiler(
                IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            MethodInfo onFrameInvokeMethod =
                AccessTools.Method(
                    typeof(ApprenticeAnimationHook),
                    nameof(OnFrameInvoke)
                ) ?? throw new MissingMethodException(
                    nameof(OnFrameInvoke)
                );
            MethodInfo getLocalTransformMatrixMethod =
                AccessTools.Method(
                    typeof(ShapeElement),
                    nameof(ShapeElement.GetLocalTransformMatrix)
                ) ?? throw new MissingMethodException(
                    nameof(ShapeElement.GetLocalTransformMatrix)
                );

            int matches = 0;
            for (int index = 0; index < code.Count; index++)
            {
                if (!code[index].Calls(
                    getLocalTransformMatrixMethod))
                {
                    continue;
                }

                matches++;
                code.Insert(
                    index,
                    new CodeInstruction(OpCodes.Ldarg_0)
                );
                code.Insert(
                    index + 1,
                    new CodeInstruction(OpCodes.Ldloc, 4)
                );
                code.Insert(
                    index + 2,
                    new CodeInstruction(
                        OpCodes.Call,
                        onFrameInvokeMethod
                    )
                );
                break;
            }

            InjectionPointCount = matches;
            return code;
        }

        private static void CaptureAnimator(
            Vintagestory.API.Common.AnimationManager __instance)
        {
            if (!Enabled || owner == null) return;
            if (AnimationManagerEntity?.GetValue(__instance) is not
                    EntityPlayer entity ||
                __instance.Animator is not ClientAnimator animator)
            {
                return;
            }

            Animators[animator] = entity;
        }

        private static void OnFrameInvoke(
            ClientAnimator? animator,
            ElementPose pose)
        {
            if (!Enabled || owner == null || animator == null ||
                !Animators.TryGetValue(
                    animator,
                    out EntityPlayer? player))
            {
                return;
            }

            try
            {
                owner.OnReferenceFrame(player, pose, animator);
            }
            catch (Exception exception)
            {
                ReportRuntimeFailure(exception);
            }
        }

        private static void BeforeRender(
            EntityShapeRenderer __instance,
            float dt)
        {
            if (!Enabled || owner == null) return;
            if (__instance.entity is EntityPlayer player &&
                IsLocalPlayer(player))
            {
                return;
            }

            try
            {
                owner.OnBeforeReferenceFrame(
                    __instance.entity,
                    dt
                );
            }
            catch (Exception exception)
            {
                ReportRuntimeFailure(exception);
            }
        }

        private static void OnSelfBeforeRender(
            EntityPlayer __instance,
            float dt)
        {
            if (!Enabled || owner == null) return;
            try
            {
                owner.OnBeforeReferenceFrame(__instance, dt);
            }
            catch (Exception exception)
            {
                ReportRuntimeFailure(exception);
            }
        }

        private static bool IsLocalPlayer(EntityPlayer player) =>
            player.Api is ICoreClientAPI clientApi &&
            clientApi.World?.Player?.Entity?.EntityId ==
                player.EntityId;

        private static void ReportRuntimeFailure(
            Exception exception)
        {
            if (runtimeFailureReported) return;
            runtimeFailureReported = true;
            api?.Logger.Error(
                "[Apprentice] The OverhaulLib reference animation callback failed. The hook remains installed, but this exact error is reported only once: {0}",
                exception
            );
        }
    }
}
