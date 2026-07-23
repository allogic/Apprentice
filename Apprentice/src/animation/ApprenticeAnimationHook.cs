using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Apprentice
{
    internal static class ApprenticeAnimationHook
    {
        private const string HarmonyId =
            "apprentice.animation.temporary-pose.1_22_3";

        private static Harmony? harmony;
        private static MethodInfo? target;
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
                    "The Apprentice animation hook already has an owner."
                );
            }

            api = clientApi;
            owner = animationSystem;
            Enabled = false;
            InjectionPointCount = 0;
            runtimeFailureReported = false;

            try
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
                target = typeof(ClientAnimator).GetMethod(
                    "calculateMatrices",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: signature,
                    modifiers: null
                ) ?? throw new MissingMethodException(
                    "ClientAnimator.calculateMatrices(int, float, List<ElementPose>, ShapeElementWeights[][], float[], List<ElementPose>[], List<ElementPose>[], int)"
                );

                MethodInfo transpiler = typeof(ApprenticeAnimationHook)
                    .GetMethod(
                        nameof(Transpiler),
                        BindingFlags.Static | BindingFlags.NonPublic
                    ) ?? throw new MissingMethodException(nameof(Transpiler));

                harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(transpiler)
                );

                int ownedTranspilers = Harmony.GetPatchInfo(target)?
                    .Transpilers.Count(patch => patch.owner == HarmonyId) ?? 0;
                if (InjectionPointCount != 1 || ownedTranspilers != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected one matrix injection and one owned transpiler, found {InjectionPointCount} and {ownedTranspilers}."
                    );
                }

                Enabled = true;
                clientApi.Logger.Notification(
                    "[Apprentice] Temporary-pose hook enabled: target={0}; injectionPoints=1; owner={1}.",
                    target,
                    HarmonyId
                );
            }
            catch (Exception exception)
            {
                harmony?.UnpatchAll(HarmonyId);
                harmony = null;
                target = null;
                Enabled = false;
                clientApi.Logger.Error(
                    "[Apprentice] War Scythe custom animation is disabled because the Vintage Story 1.22.3 temporary-pose signature did not pass its exact-one self-test: {0}",
                    exception.Message
                );
            }
        }

        public static void Uninstall(ApprenticeAnimationSystem animationSystem)
        {
            if (!ReferenceEquals(owner, animationSystem)) return;

            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            target = null;
            owner = null;
            api = null;
            Enabled = false;
            InjectionPointCount = 0;
            runtimeFailureReported = false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo localTransform = typeof(ShapeElement).GetMethod(
                nameof(ShapeElement.GetLocalTransformMatrix),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[]
                {
                    typeof(int),
                    typeof(float[]),
                    typeof(ElementPose)
                },
                modifiers: null
            ) ?? throw new MissingMethodException(
                nameof(ShapeElement.GetLocalTransformMatrix)
            );
            MethodInfo prepare = typeof(ApprenticeAnimationHook).GetMethod(
                nameof(PrepareFinalPose),
                BindingFlags.Static | BindingFlags.NonPublic
            ) ?? throw new MissingMethodException(nameof(PrepareFinalPose));

            List<CodeInstruction> result = new();
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(localTransform))
                {
                    matches++;
                    // The original ElementPose is already on the evaluation
                    // stack. Replace only the matrix-call argument with a
                    // render-only copy and leave the animator-owned pose intact.
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, prepare));
                }

                result.Add(instruction);
            }

            InjectionPointCount = matches;
            return result;
        }

        private static ElementPose PrepareFinalPose(
            ElementPose pose,
            ClientAnimator animator)
        {
            if (!Enabled || owner == null) return pose;

            try
            {
                return owner.PrepareFinalPose(animator, pose);
            }
            catch (Exception exception)
            {
                Enabled = false;
                if (runtimeFailureReported) return pose;

                runtimeFailureReported = true;
                api?.Logger.Error(
                    "[Apprentice] War Scythe temporary-pose hook failed closed and will not run again this session: {0}",
                    exception
                );
                return pose;
            }
        }
    }
}
