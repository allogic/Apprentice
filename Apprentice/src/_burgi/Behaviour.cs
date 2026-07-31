using HarmonyLib;

using ImGuiNET;

using System;
using System.Collections.Generic;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VSImGui;
using VSImGui.API;
using static Apprentice.src._burgi.Behaviour.CharacterController;

// TODO: refactor Directional8 on dash quadrant angle
// TODO: update frame buffer sizes at runtime

namespace Apprentice.src._burgi
{
	internal class Behaviour
	{
		internal class CharacterController : EntityBehavior
		{
			public static bool enable = false;
			public static bool enableRunAnimations = true;
			public static bool enableInverseKinematic = true;
			public static bool enableBlendAttackPose = false;

			internal enum Directional8
			{
				DIR8_CENTER = -1,
				DIR8_FORWARD = 0,
				DIR8_FORWARD_LEFT,
				DIR8_LEFT,
				DIR8_BACK_LEFT,
				DIR8_BACK,
				DIR8_BACK_RIGHT,
				DIR8_RIGHT,
				DIR8_FORWARD_RIGHT,
			}
			internal enum SequenceType
			{
				SEQUENCE_TYPE_NONE = 0,
				SEQUENCE_TYPE_DASH,
				SEQUENCE_TYPE_ATTACK,
				SEQUENCE_TYPE_JUMP,
			}
			internal enum DashSequenceState
			{
				DASH_SEQUENCE_STATE_IDLE = 0,
				DASH_SEQUENCE_STATE_START,
				DASH_SEQUENCE_STATE_DASH,
				DASH_SEQUENCE_STATE_RETRACT,
				DASH_SEQUENCE_STATE_STOP,
			}
			internal enum AttackSequenceState
			{
				ATTACK_SEQUENCE_STATE_IDLE = 0,
				ATTACK_SEQUENCE_STATE_START,
				ATTACK_SEQUENCE_STATE_ATTACK,
				ATTACK_SEQUENCE_STATE_STOP,
			}
			internal enum JumpSequenceState
			{
				JUMP_SEQUENCE_STATE_IDLE = 0,
				JUMP_SEQUENCE_STATE_START,
				JUMP_SEQUENCE_STATE_JUMP,
				JUMP_SEQUENCE_STATE_STOP,
			}

			private static Directional8 currDirection8 = Directional8.DIR8_CENTER;
			private static Directional8 prevDirection8 = Directional8.DIR8_CENTER;
			private static SequenceType sequenceType = SequenceType.SEQUENCE_TYPE_NONE;
			private static DashSequenceState dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_IDLE;
			private static AttackSequenceState attackSequenceState = AttackSequenceState.ATTACK_SEQUENCE_STATE_IDLE;
			private static JumpSequenceState jumpSequenceState = JumpSequenceState.JUMP_SEQUENCE_STATE_IDLE;

			private static IList<string> whitelistedAnimationCodes = [
				// Movement
				"dash-forward",
				"dash-back",
				"dash-left",
				"dash-right",

				"sprint-forward",
				"sprint-back",

				"strafe-forward-right-45",
				"strafe-forward-left-45",
				"strafe-forward-right-90",
				"strafe-forward-left-90",

				"strafe-back-right-45",
				"strafe-back-left-45",
				"strafe-back-right-90",
				"strafe-back-left-90",

				// Combat
				"hold-weapon-combat-passive",

				// Game
				"swordhit",
				"swordhit2",
				"cleaverhit",
				"bowaimlong",
			];

			[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(AnimationMetaData)])]
			internal class AnimationManager_StartAnimation0_Patch
			{
				public static bool Prefix(AnimationManager __instance, AnimationMetaData animdata)
				{
					if (enable == false) return true; // Don't skip the original method

					if (whitelistedAnimationCodes.Contains(animdata.Code))
					{
						return true; // Don't skip the original method
					}

					return false; // Skip the original method
				}
			}
			[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(string)])]
			internal class AnimationManager_StartAnimation1_Patch
			{
				public static bool Prefix(AnimationManager __instance, string configCode)
				{
					if (enable == false) return true; // Don't skip the original method

					if (whitelistedAnimationCodes.Contains(configCode))
					{
						return true; // Don't skip the original method
					}

					return false; // Skip the original method
				}
			}

			private static readonly AssetLocation dashSound1 = new("apprentice", "sounds/dash-1");
			private static readonly AssetLocation dashSound2 = new("apprentice", "sounds/dash-2");
			private static readonly AssetLocation dashRecoverSound1 = new("apprentice", "sounds/dash-recover-1");
			private static readonly AssetLocation dashRecoverSound2 = new("apprentice", "sounds/dash-recover-2");
			private static readonly AssetLocation ushigatanaDashSound = new("apprentice", "sounds/ushigatana-dash");
			private static readonly AssetLocation wooshSound1 = new("apprentice", "sounds/woosh-1");
			private static readonly AssetLocation wooshSound2 = new("apprentice", "sounds/woosh-2");
			private static readonly AssetLocation wooshSound3 = new("apprentice", "sounds/woosh-3");
			private static readonly AssetLocation footstepGrassSound1 = new("apprentice", "sounds/footstep-grass-1");
			private static readonly AssetLocation footstepGrassSound2 = new("apprentice", "sounds/footstep-grass-2");
			private static readonly AssetLocation footstepGrassSound3 = new("apprentice", "sounds/footstep-grass-3");
			private static readonly AssetLocation footstepGrassSound4 = new("apprentice", "sounds/footstep-grass-4");
			private static readonly AssetLocation footstepGrassSound5 = new("apprentice", "sounds/footstep-grass-5");

			private static bool isPhysicActive = false;
			private static bool isDoubleDashActive = false;
			private static bool dashAllowed = true;
			private static bool attackAllowed = true;
			private static bool jumpAllowed = true;
			private static bool doubleDashAllowed = true;
			private static bool isRunning = false;
			public static bool suppressMouseInput = false;

			public static float physicSpeedFactor = 8.356F;
			public static float maxVelocity = 0.3F;

			public static float dashHorizontalImpulseGrounded = 1.0F;
			public static float dashHorizontalImpulseAirbourne = 0.036F;
			public static float dashVerticalImpulseGrounded = 0.02F;
			public static float dashVerticalImpulseAirbourne = 0.04F;
			public static float attackHorizontalImpulse = 0.15F;
			public static float jumpHorizontalImpulse = 0.15F;

			public static float runAnimationDeadzone = 0.01F;

			public static float animationSpeedDash = 2.5F;
			public static float animationSpeedJump = 2.5F;
			public static float animationSpeedSwordHit = 2.5F;
			public static float animationSpeedSwordHit2 = 2.5F;
			public static float animationSpeedCleaverHit = 2.5F;
			public static float animationSpeedSprintForward = 0.7F;
			public static float animationSpeedSprintBack = 0.5F;
			public static float animationSpeedStrafeForwardLeft90 = 0.6F;
			public static float animationSpeedStrafeForwardRight90 = 0.6F;
			public static float animationSpeedStrafeForwardLeft45 = 0.6F;
			public static float animationSpeedStrafeForwardRight45 = 0.6F;
			public static float animationSpeedRunMultiplier = 2.1F;

			public static float motionSpeedSprintForward = 1.0F;
			public static float motionSpeedSprintBack = 1.0F;
			public static float motionSpeedStrafeForwardLeft90 = 1.0F;
			public static float motionSpeedStrafeForwardLeft45 = 1.0F;
			public static float motionSpeedStrafeForwardRight90 = 1.0F;
			public static float motionSpeedStrafeForwardRight45 = 1.0F;

			public static int dashCooldownMs = 1500;
			public static int jumpCooldownMs = 150;
			public static int attackCooldownMs = 300;

			private static float physicFrame = 0.0F;
			private static int animationFrame = 0;

			public static int dashFrameCount = 18;
			public static int dashRetractFrameCount = 0;
			public static int attackFrameCount = 10;
			public static int jumpFrameCount = 10;

			private static Vec3d dashDirection = new(0, 0, 0);
			private static Vec3d attackDirection = new(0, 0, 0);
			private static Vec3d jumpDirection = new(0, 0, 0);

			private static RunningAnimation? runningRunAnimation = null;
			private static RunningAnimation? runningCombatAnimation = null;

			// private static float currAnimationFrame = 0;
			// private static float prevAnimationFrame = 0;

			// private static bool leftFootSoundReset = true;
			// private static bool rightFootSoundReset = true;

			private static Dictionary<string, float> strafeAimWeights = new Dictionary<string, float>()
			{
				{ "UpperTorso", 0.0F },
				{ "UpperArmR", 0.0F },
				{ "UpperArmL", 0.0F },
				{ "Neck", 0.0F },
				{ "UpperBackAttachment", 0.0F },
				{ "LowerArmR", 0.0F },
				{ "LowerArmL", 0.0F },
				{ "ShoulderAttachment", 0.0F },
				{ "Head", 0.0F },
				{ "ItemAnchor", 0.0F },
				{ "ItemAnchorL", 0.0F },
			};
			private static Dictionary<string, EnumAnimationBlendMode> strafeAimBlendModes = new Dictionary<string, EnumAnimationBlendMode>()
			{
				{ "UpperTorso", EnumAnimationBlendMode.Add },
				{ "UpperArmR", EnumAnimationBlendMode.Add },
				{ "UpperArmL", EnumAnimationBlendMode.Add },
				{ "Neck", EnumAnimationBlendMode.Add },
				{ "UpperBackAttachment", EnumAnimationBlendMode.Add },
				{ "LowerArmR", EnumAnimationBlendMode.Add },
				{ "LowerArmL", EnumAnimationBlendMode.Add },
				{ "ShoulderAttachment", EnumAnimationBlendMode.Add },
				{ "Head", EnumAnimationBlendMode.Add },
				{ "ItemAnchor", EnumAnimationBlendMode.Add },
				{ "ItemAnchorL", EnumAnimationBlendMode.Add },
			};

			#region Dash Animations
			private static AnimationMetaData dashForwardData = new()
			{
				Animation = "dash-forward",
				Code = "dash-forward",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 1.0F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "root", 1.0F },
				},
				ElementBlendMode = {
					{ "root", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData dashBackData = new()
			{
				Animation = "dash-back",
				Code = "dash-back",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 1.0F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "root", 1.0F },
				},
				ElementBlendMode = {
					{ "root", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData dashLeftData = new()
			{
				Animation = "dash-left",
				Code = "dash-left",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 1.0F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "root", 1.0F },
				},
				ElementBlendMode = {
					{ "root", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData dashRightData = new()
			{
				Animation = "dash-right",
				Code = "dash-right",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 1.0F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "root", 1.0F },
				},
				ElementBlendMode = {
					{ "root", EnumAnimationBlendMode.Add },
				},
			};
			#endregion

			#region Strafing Animations
			private static AnimationMetaData[] strafingAnimations =
			{
				// DIR8_FORWARD
				new AnimationMetaData {
					Animation = "sprint-forward",
					Code = "sprint-forward",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_FORWARD_LEFT
				new AnimationMetaData {
					Animation = "strafe-forward-left-45",
					Code = "strafe-forward-left-45",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_LEFT
				new AnimationMetaData {
					Animation = "strafe-forward-left-90",
					Code = "strafe-forward-left-90",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_BACK_LEFT
				new AnimationMetaData {
					Animation = "sprint-back-left-45",
					Code = "sprint-back-left-45",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_BACK
				new AnimationMetaData {
					Animation = "sprint-back",
					Code = "sprint-back",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_BACK_RIGHT
				new AnimationMetaData {
					Animation = "strafe-back-right-45",
					Code = "strafe-back-right-45",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_RIGHT
				new AnimationMetaData {
					Animation = "strafe-forward-right-90",
					Code = "strafe-forward-right-90",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
				// DIR8_FORWARD_RIGHT
				new AnimationMetaData {
					Animation = "strafe-forward-right-45",
					Code = "strafe-forward-right-45",
					Weight = 1.0F,
					SupressDefaultAnimation = true,
					ClientSide = true,
					AnimationSpeed = 1.0F,
					BlendMode = EnumAnimationBlendMode.Add,
					ElementWeight = { },
					ElementBlendMode = { },
				},
			};
			#endregion

			#region Combat Animations
			private static AnimationMetaData holdWeaponCombatPassiveData = new()
			{
				Animation = "hold-weapon-combat-passive",
				Code = "hold-weapon-combat-passive",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 1.0F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "root", 0.0F },
					{ "LowerTorso", 0.0F },
					{ "UpperTorso", 1.0F },
					{ "UpperArmR", 1.0F },
					{ "UpperArmL", 1.0F },
					{ "Neck", 1.0F },
					{ "UpperBackAttachment", 1.0F },
				},
				ElementBlendMode = {
					{ "root", EnumAnimationBlendMode.Add },
					{ "LowerTorso", EnumAnimationBlendMode.Add },
					{ "UpperTorso", EnumAnimationBlendMode.Add },
					{ "UpperArmR", EnumAnimationBlendMode.Add },
					{ "UpperArmL", EnumAnimationBlendMode.Add },
					{ "Neck", EnumAnimationBlendMode.Add },
					{ "UpperBackAttachment", EnumAnimationBlendMode.Add },
				},
			};
			#endregion

			#region Internal Animations
			private static AnimationMetaData swordHitData = new()
			{
				Animation = "SwordHit",
				Code = "swordhit",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 0.8F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "UpperTorso", 1.0F },
				},
					ElementBlendMode = {
					{ "UpperTorso", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData swordHit2Data = new()
			{
				Animation = "SwordHit2",
				Code = "swordhit2",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 0.8F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "UpperTorso", 1.0F },
				},
					ElementBlendMode = {
					{ "UpperTorso", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData cleaverHitData = new()
			{
				Animation = "cleaverhit",
				Code = "cleaverhit",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 0.8F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "UpperTorso", 1.0F },
				},
					ElementBlendMode = {
					{ "UpperTorso", EnumAnimationBlendMode.Add },
				},
			};
			private static AnimationMetaData bowAimLongData = new()
			{
				Animation = "BowAimLong",
				Code = "bowaimlong",
				Weight = 1.0F,
				SupressDefaultAnimation = true,
				ClientSide = true,
				AnimationSpeed = 0.5F,
				BlendMode = EnumAnimationBlendMode.Add,
				ElementWeight = {
					{ "UpperTorso", 1.0F },
				},
					ElementBlendMode = {
					{ "UpperTorso", EnumAnimationBlendMode.Add },
				},
			};
			#endregion

			public CharacterController(Entity entity) : base(entity)
			{
				// Apply all harmony patches
				Main.apiCommon.CreateClassProcessor(typeof(AnimationManager_StartAnimation0_Patch)).Patch();
				Main.apiCommon.CreateClassProcessor(typeof(AnimationManager_StartAnimation1_Patch)).Patch();

				// Register hotkey's
				Main.clientApi.Input.RegisterHotKey("sprint_w", "", GlKeys.W, HotkeyType.MovementControls);
				Main.clientApi.Input.RegisterHotKey("sprint_a", "", GlKeys.A, HotkeyType.MovementControls);
				Main.clientApi.Input.RegisterHotKey("sprint_s", "", GlKeys.S, HotkeyType.MovementControls);
				Main.clientApi.Input.RegisterHotKey("sprint_d", "", GlKeys.D, HotkeyType.MovementControls);
				Main.clientApi.Input.RegisterHotKey("sprint", "", GlKeys.ShiftLeft, HotkeyType.MovementControls);
				Main.clientApi.Input.RegisterHotKey("reset", "", GlKeys.B, HotkeyType.GUIOrOtherControls);

				// Register hotkey handler's
				Main.clientApi.Input.SetHotKeyHandler("sprint_w", OnSprintW);
				Main.clientApi.Input.SetHotKeyHandler("sprint_a", OnSprintA);
				Main.clientApi.Input.SetHotKeyHandler("sprint_s", OnSprintS);
				Main.clientApi.Input.SetHotKeyHandler("sprint_d", OnSprintD);
				Main.clientApi.Input.SetHotKeyHandler("sprint", OnSprint);
				Main.clientApi.Input.SetHotKeyHandler("reset", OnReset);

				// Register event's
				Main.clientApi.Event.MouseDown += OnMouseDown;
			}

			public override string PropertyName()
			{
				return "CharacterControllerBehaviour";
			}
			public override void OnGameTick(float deltaTime)
			{
				if (enable == false) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityControls controls = entityPlayer.Controls;
				EntityPos transform = entityPlayer.Pos;

				// Check if not running anymore
				if (!Main.clientApi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] &&
					!Main.clientApi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight])
				{
					isRunning = false;
				}

				// Adjust move speed of player
				// if (isRunning)
				// {
				// 	entityPlayer.Stats.Set("walkspeed", "mymod", 1.5f, true);
				// 	entityPlayer.Stats.Set("runspeed", "mymod", 1.5f, true);
				// 	entityPlayer.Stats.Set("jumpheight", "mymod", 1.2f, true);
				// }
				// else
				// {
				// 	float speed = entityPlayer.Stats.Get
				// }

				// Force body yaw
				// transform.Yaw = 0.0F;
				// entityPlayer.BodyYaw = 0.0F;
				// clientApi.World.Player.CameraYaw = 0.0F;

				// Execute based on sequence type
				switch (sequenceType)
				{
					case SequenceType.SEQUENCE_TYPE_DASH: DashSequenceTick(deltaTime); break;
					case SequenceType.SEQUENCE_TYPE_ATTACK: AttackSequenceTick(deltaTime); break;
					case SequenceType.SEQUENCE_TYPE_JUMP: JumpSequenceTick(deltaTime); break;
				}

				// Disable controls while in sequence (TODO: revalidate this..)
				if (sequenceType != SequenceType.SEQUENCE_TYPE_NONE)
				{
					controls.Forward = false;
					controls.Backward = false;
					controls.Left = false;
					controls.Right = false;
				}

				// TODO: Adjust animation speed with player motion vector..
				// Handle strafing
				if (enableRunAnimations)
				{
					float motionLength = (float)transform.Motion.Length();

					if (motionLength > runAnimationDeadzone)
					{
						// Compute local direction
						Vec3d localForward = transform.GetViewVector().ToVec3d();
						Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();

						// Compute quadrant angle of motion vector
						double x = transform.Motion.Dot(localRight);
						double y = transform.Motion.Dot(localForward);
						double angle = Math.Atan2(x, y) * GameMath.RAD2DEG;

						// Normalize to 0-360
						double degrees = angle;
						if (degrees < 0) degrees += 360.0;

						// Round to nearest 45 degrees
						prevDirection8 = currDirection8;
						currDirection8 = (Directional8)((int)Math.Round(degrees / 45.0) % 8);

						// Start strafe animation based on quadrant angle
						if (entityPlayer.AnimManager != null)
						{
							if (entityPlayer.AnimManager.Animator != null)
							{
								if (currDirection8 != prevDirection8)
								{
									// Stop animation
									if (runningRunAnimation != null)
									{
										entity.AnimManager.StopAnimation(runningRunAnimation.Animation.Code);

										runningRunAnimation = null;
									}

									// Start animation
									if (entity.AnimManager.StartAnimation(strafingAnimations[(int)currDirection8]))
									{
										runningRunAnimation = entity.AnimManager.GetAnimationState(strafingAnimations[(int)currDirection8].Code);
									}

									// Set initial running animation data
									if (runningRunAnimation != null)
									{
										runningRunAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
										runningRunAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
									}
								}

								// Set runtime animation data
								if (runningRunAnimation != null)
								{
									// Set weights
									strafingAnimations[(int)currDirection8].ElementWeight = enableBlendAttackPose
										? strafeAimWeights
										: [];

									// Set blend mode
									strafingAnimations[(int)currDirection8].ElementBlendMode = enableBlendAttackPose
										? strafeAimBlendModes
										: [];

									// Set animation speed
									float targetSpeed = 1.0F;
									switch (currDirection8)
									{
										case Directional8.DIR8_FORWARD: targetSpeed = animationSpeedSprintForward * motionSpeedSprintForward; break;
										case Directional8.DIR8_FORWARD_LEFT: targetSpeed = animationSpeedStrafeForwardLeft45 * motionSpeedStrafeForwardLeft45; break;
										case Directional8.DIR8_LEFT: targetSpeed = animationSpeedStrafeForwardLeft90 * motionSpeedStrafeForwardLeft90; break;
										case Directional8.DIR8_BACK_LEFT: break; // TODO
										case Directional8.DIR8_BACK: break; // TODO
										case Directional8.DIR8_BACK_RIGHT: break; // TODO
										case Directional8.DIR8_RIGHT: targetSpeed = animationSpeedStrafeForwardRight90 * motionSpeedStrafeForwardRight90; break;
										case Directional8.DIR8_FORWARD_RIGHT: targetSpeed = animationSpeedStrafeForwardRight45 * motionSpeedStrafeForwardRight45; break;
									}
									if (isRunning)
									{
										targetSpeed *= animationSpeedRunMultiplier;
									}
									strafingAnimations[(int)currDirection8].AnimationSpeed = targetSpeed;

									/*
									// Play footstep sounds
									int totalFrames = currRunningAnimation.Animation.QuantityFrames;
									prevAnimationFrame = currAnimationFrame;
									currAnimationFrame = Math.Min(totalFrames - 1, (int)(currRunningAnimation.CurrentFrame * totalFrames));
									if (currAnimationFrame < prevAnimationFrame)
									{
										leftFootSoundReset = true;
										rightFootSoundReset = true;
									}
									if (currAnimationFrame != prevAnimationFrame)
									{
										AssetLocation[] footsteps = [footstepGrassSound1, footstepGrassSound2, footstepGrassSound3, footstepGrassSound4, footstepGrassSound5];
										AssetLocation footstep = footsteps[Random.Shared.Next(footsteps.Length)];

										if (leftFootSoundReset)
										{
											if (currAnimationFrame >= 3)
											{
												clientApi.World.PlaySoundAt(footstep, entity);
												leftFootSoundReset = false;
											}
										}

										if (rightFootSoundReset)
										{
											if (currAnimationFrame >= 15)
											{
												clientApi.World.PlaySoundAt(footstep, entity);
												rightFootSoundReset = false;
											}
										}
									}
									*/
								}
							}
						}
					}
					else
					{
						// Stop animation
						if (runningRunAnimation != null)
						{
							entity.AnimManager.StopAnimation(runningRunAnimation.Animation.Code);

							runningRunAnimation = null;
						}
					}
				}

				// Handle inverse kinematics
				if (enableInverseKinematic)
				{
					ElementPose pose = entity.AnimManager.Animator.GetPosebyName("LowerArmR");

					Vec3f cameraPosition = entityPlayer.CameraPos.ToVec3f();
					Vec3f localForward = transform.GetViewVector();

					pose.AnimModelMatrix = [
							1, 0, 0, 0,
							0, 1, 0, 0,
							0, 0, 1, 0,
							0, 0, 0, 1
						];
					// pose.AnimModelMatrix[12] = cameraPosition.X + localForward.X * 3.0F;
					// pose.AnimModelMatrix[13] = cameraPosition.Y + localForward.Y * 3.0F;
					// pose.AnimModelMatrix[14] = cameraPosition.Z + localForward.Z * 3.0F;
				}

				// Apply motion blur
				if (sequenceType == SequenceType.SEQUENCE_TYPE_NONE)
				{
					Main.motionBlur.blurEnable = false;
				}
				else
				{
					Main.motionBlur.blurEnable = true;
					Main.motionBlur.blurLength = (float)transform.Motion.Length();
				}

				// Update obama prism
				if (Main.obamaPrism.obamaEnable)
				{
					Main.obamaPrism.Update(deltaTime);
				}
			}

			private void DashSequenceTick(float deltaTime)
			{
				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityControls controls = entityPlayer.Controls;
				EntityPos transform = entityPlayer.Pos;

				// Sequence tree
				switch (dashSequenceState)
				{
					case DashSequenceState.DASH_SEQUENCE_STATE_IDLE:
						{
							break;
						}
					case DashSequenceState.DASH_SEQUENCE_STATE_START:
						{
							// Reset frame counter
							physicFrame = 0.0F;
							animationFrame = 0;

							// Compute local direction
							Vec3d localForward = transform.GetViewVector().ToVec3d();
							Vec3d localBack = localForward.Clone().Mul(-1);
							Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();
							Vec3d localLeft = localRight.Clone().Mul(-1);

							// Compute dash direction
							if (isDoubleDashActive)
							{
								// Reset dash direction
								dashDirection = Vec3d.Zero;

								// Apply local input direction
								if (controls.Forward) dashDirection += localForward;
								if (controls.Backward) dashDirection += localBack;
								if (controls.Left) dashDirection += localRight;
								if (controls.Right) dashDirection += localLeft;

								// Reset up direction
								attackDirection.Y = 0.0F;
								attackDirection.Normalize();
							}
							else
							{
								// Reset dash direction
								dashDirection = Vec3d.Zero;

								// Apply local input direction
								if (controls.Forward) dashDirection += localForward;
								if (controls.Backward) dashDirection += localBack;
								if (controls.Left) dashDirection += localRight;
								if (controls.Right) dashDirection += localLeft;

								// Reset up direction
								attackDirection.Y = 0.0F;
								attackDirection.Normalize();
							}

							// Stop dash animations only
							if (entity.AnimManager.IsAnimationActive([dashForwardData.Code])) entity.AnimManager.StopAnimation(dashForwardData.Code);
							if (entity.AnimManager.IsAnimationActive([dashBackData.Code])) entity.AnimManager.StopAnimation(dashBackData.Code);
							if (entity.AnimManager.IsAnimationActive([dashRightData.Code])) entity.AnimManager.StopAnimation(dashRightData.Code);
							if (entity.AnimManager.IsAnimationActive([dashLeftData.Code])) entity.AnimManager.StopAnimation(dashLeftData.Code);

							// Compute quadrant angle of motion vector
							double x = transform.Motion.Dot(localRight);
							double y = transform.Motion.Dot(localForward);
							double angle = Math.Atan2(x, y) * GameMath.RAD2DEG;

							// Start dash animation based on quadrant angle
							if ((angle > -45.0F) && (angle < 45.0F))
							{
								// Set runtime animation data
								dashForwardData.AnimationSpeed = animationSpeedDash;

								// Dash forward
								entity.AnimManager.StartAnimation(dashForwardData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashForwardData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else if ((angle > 45.0F) && (angle < 135.0F))
							{
								// Set runtime animation data
								dashLeftData.AnimationSpeed = animationSpeedDash;

								// Dash left
								entity.AnimManager.StartAnimation(dashLeftData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashLeftData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else if ((angle < -45.0F) && (angle > -135.0F))
							{
								// Set runtime animation data
								dashRightData.AnimationSpeed = animationSpeedDash;

								// Dash right
								entity.AnimManager.StartAnimation(dashRightData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashRightData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else
							{
								// Set runtime animation data
								dashBackData.AnimationSpeed = animationSpeedDash;

								// Dash back
								entity.AnimManager.StartAnimation(dashBackData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashBackData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}

							dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_DASH;

							break;
						}
					case DashSequenceState.DASH_SEQUENCE_STATE_DASH:
						{
							// Check exit condition
							if (animationFrame >= dashFrameCount)
							{
								animationFrame = 0;

								// Stop dash animations only
								if (entity.AnimManager.IsAnimationActive([dashForwardData.Code])) entity.AnimManager.StopAnimation(dashForwardData.Code);
								if (entity.AnimManager.IsAnimationActive([dashBackData.Code])) entity.AnimManager.StopAnimation(dashBackData.Code);
								if (entity.AnimManager.IsAnimationActive([dashRightData.Code])) entity.AnimManager.StopAnimation(dashRightData.Code);
								if (entity.AnimManager.IsAnimationActive([dashLeftData.Code])) entity.AnimManager.StopAnimation(dashLeftData.Code);

								// entity.AnimManager.StopAllAnimations();

								// Start retract animation
								// entity.AnimManager.StartAnimation(dashForwardRetractData);
								// RunningAnimation animation = entity.AnimManager.GetAnimationState(dashForwardRetractData.Code);
								// animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
								// animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Stop;

								dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_RETRACT;
							}

							// Increment animation frame
							animationFrame++;

							break;
						}
					case DashSequenceState.DASH_SEQUENCE_STATE_RETRACT:
						{
							// Check exit condition
							if ((animationFrame >= dashRetractFrameCount) || (entityPlayer.OnGround))
							{
								animationFrame = 0;

								// Stop all animations
								// entity.AnimManager.StopAllAnimations();

								dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_STOP;
							}

							// Increment animation frame
							animationFrame++;

							break;
						}
					case DashSequenceState.DASH_SEQUENCE_STATE_STOP:
						{
							// Reset sequence
							sequenceType = SequenceType.SEQUENCE_TYPE_NONE;
							dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_IDLE;

							break;
						}
				}

				// Apply some physics
				if (isPhysicActive)
				{
					Vec3d force = Vec3d.Zero;

					// Compute horizontal force
					force += entityPlayer.OnGround
						? BurgiMath.EaseOutElastic(physicFrame) * dashHorizontalImpulseGrounded * dashDirection
						: BurgiMath.EaseOutElastic(physicFrame) * dashHorizontalImpulseAirbourne * dashDirection;

					// Compute vertical force
					force += isDoubleDashActive
						? BurgiMath.EaseOutCirc(physicFrame) * dashVerticalImpulseGrounded * BurgiMath.WorldUp
						: BurgiMath.EaseOutElastic(physicFrame) * dashVerticalImpulseAirbourne * BurgiMath.WorldUp;

					// Apply force
					transform.Motion.Add(force);

					// Clamp velocity
					if (transform.Motion.LengthSq() > (maxVelocity * maxVelocity))
					{
						transform.Motion = transform.Motion.Normalize() * maxVelocity;
					}

					// Advance frame
					physicFrame += physicSpeedFactor * deltaTime;
					if (physicFrame >= 1.0F)
					{
						isPhysicActive = false;
					}
				}
			}
			private void JumpSequenceTick(float deltaTime)
			{
				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityControls controls = entityPlayer.Controls;
				EntityPos transform = entityPlayer.Pos;

				// Sequence tree
				switch (jumpSequenceState)
				{
					case JumpSequenceState.JUMP_SEQUENCE_STATE_IDLE:
						{
							break;
						}
					case JumpSequenceState.JUMP_SEQUENCE_STATE_START:
						{
							// Reset frame counter
							physicFrame = 0.0F;
							animationFrame = 0;

							// Compute local direction
							Vec3d localForward = transform.GetViewVector().ToVec3d();
							Vec3d localBack = localForward.Clone().Mul(-1);
							Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();
							Vec3d localLeft = localRight.Clone().Mul(-1);

							// Reset jump direction
							jumpDirection = Vec3d.Zero;

							// Apply local input direction
							if (controls.Forward) jumpDirection += localForward;
							if (controls.Backward) jumpDirection += localBack;
							if (controls.Left) jumpDirection += localRight;
							if (controls.Right) jumpDirection += localLeft;

							// Reset up direction
							jumpDirection.Y = 0.0F;
							jumpDirection.Normalize();

							// Stop dash animations only
							if (entity.AnimManager.IsAnimationActive([dashForwardData.Code])) entity.AnimManager.StopAnimation(dashForwardData.Code);
							if (entity.AnimManager.IsAnimationActive([dashBackData.Code])) entity.AnimManager.StopAnimation(dashBackData.Code);
							if (entity.AnimManager.IsAnimationActive([dashRightData.Code])) entity.AnimManager.StopAnimation(dashRightData.Code);
							if (entity.AnimManager.IsAnimationActive([dashLeftData.Code])) entity.AnimManager.StopAnimation(dashLeftData.Code);

							// Compute quadrant angle of motion vector
							double x = transform.Motion.Dot(localRight);
							double y = transform.Motion.Dot(localForward);
							double angle = Math.Atan2(x, y) * GameMath.RAD2DEG;

							// Start dash animation based on quadrant angle
							if ((angle > -45.0F) && (angle < 45.0F))
							{
								// Set runtime animation data
								dashForwardData.AnimationSpeed = animationSpeedJump;

								// Dash forward
								entity.AnimManager.StartAnimation(dashForwardData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashForwardData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else if ((angle > 45.0F) && (angle < 135.0F))
							{
								// Set runtime animation data
								dashLeftData.AnimationSpeed = animationSpeedJump;

								// Dash left
								entity.AnimManager.StartAnimation(dashLeftData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashLeftData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else if ((angle < -45.0F) && (angle > -135.0F))
							{
								// Set runtime animation data
								dashRightData.AnimationSpeed = animationSpeedJump;

								// Dash right
								entity.AnimManager.StartAnimation(dashRightData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashRightData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}
							else
							{
								// Set runtime animation data
								dashBackData.AnimationSpeed = animationSpeedJump;

								// Dash back
								entity.AnimManager.StartAnimation(dashBackData);
								RunningAnimation animation = entity.AnimManager.GetAnimationState(dashBackData.Code);
								animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
								animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
							}

							jumpSequenceState = JumpSequenceState.JUMP_SEQUENCE_STATE_JUMP;

							break;
						}
					case JumpSequenceState.JUMP_SEQUENCE_STATE_JUMP:
						{
							// Check exit condition
							if (animationFrame >= jumpFrameCount)
							{
								animationFrame = 0;

								// Stop dash animations only
								if (entity.AnimManager.IsAnimationActive([dashForwardData.Code])) entity.AnimManager.StopAnimation(dashForwardData.Code);
								if (entity.AnimManager.IsAnimationActive([dashBackData.Code])) entity.AnimManager.StopAnimation(dashBackData.Code);
								if (entity.AnimManager.IsAnimationActive([dashRightData.Code])) entity.AnimManager.StopAnimation(dashRightData.Code);
								if (entity.AnimManager.IsAnimationActive([dashLeftData.Code])) entity.AnimManager.StopAnimation(dashLeftData.Code);

								jumpSequenceState = JumpSequenceState.JUMP_SEQUENCE_STATE_STOP;
							}

							// Increment animation frame
							animationFrame++;

							break;
						}
					case JumpSequenceState.JUMP_SEQUENCE_STATE_STOP:
						{
							// Reset sequence
							sequenceType = SequenceType.SEQUENCE_TYPE_NONE;
							jumpSequenceState = JumpSequenceState.JUMP_SEQUENCE_STATE_IDLE;

							break;
						}
				}

				// Apply some physics
				if (isPhysicActive)
				{
					Vec3d force = Vec3d.Zero;

					// Compute horizontal force
					force += BurgiMath.EaseOutElastic(physicFrame) * jumpHorizontalImpulse * jumpDirection;

					// Apply force
					transform.Motion.Add(force);

					// Clamp velocity
					if (transform.Motion.LengthSq() > (maxVelocity * maxVelocity))
					{
						transform.Motion = transform.Motion.Normalize() * maxVelocity;
					}

					// Advance frame
					physicFrame += physicSpeedFactor * deltaTime;
					if (physicFrame >= 1.0F)
					{
						isPhysicActive = false;
					}
				}
			}
			private void AttackSequenceTick(float deltaTime)
			{
				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityControls controls = entityPlayer.Controls;
				EntityPos transform = entityPlayer.Pos;

				// Sequence tree
				switch (attackSequenceState)
				{
					case AttackSequenceState.ATTACK_SEQUENCE_STATE_IDLE:
						{
							break;
						}
					case AttackSequenceState.ATTACK_SEQUENCE_STATE_START:
						{
							// Reset frame counter
							physicFrame = 0.0F;
							animationFrame = 0;

							// Compute local direction
							Vec3d localForward = transform.GetViewVector().ToVec3d();
							Vec3d localBack = localForward.Clone().Mul(-1);
							Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();
							Vec3d localLeft = localRight.Clone().Mul(-1);

							// Reset attack direction
							attackDirection = Vec3d.Zero;

							// Apply local input direction
							attackDirection += localForward;

							// Reset up direction
							attackDirection.Y = 0.0F;
							attackDirection.Normalize();

							// Stop attack animation only
							if (entity.AnimManager.IsAnimationActive([swordHitData.Code])) entity.AnimManager.StopAnimation(swordHitData.Code);
							if (entity.AnimManager.IsAnimationActive([swordHit2Data.Code])) entity.AnimManager.StopAnimation(swordHit2Data.Code);
							if (entity.AnimManager.IsAnimationActive([cleaverHitData.Code])) entity.AnimManager.StopAnimation(cleaverHitData.Code);

							// Set runtime animation data
							swordHitData.AnimationSpeed = animationSpeedSwordHit;
							swordHit2Data.AnimationSpeed = animationSpeedSwordHit2;
							cleaverHitData.AnimationSpeed = animationSpeedCleaverHit;

							// Start random attack animation
							AnimationMetaData[] animations = { swordHitData, swordHit2Data, cleaverHitData };
							AnimationMetaData animationData = animations[Random.Shared.Next(animations.Length)];
							entity.AnimManager.StartAnimation(animationData);
							RunningAnimation animation = entity.AnimManager.GetAnimationState(animationData.Code);
							animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Stop;
							animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Stop;

							attackSequenceState = AttackSequenceState.ATTACK_SEQUENCE_STATE_ATTACK;

							break;
						}
					case AttackSequenceState.ATTACK_SEQUENCE_STATE_ATTACK:
						{
							// Check exit condition
							if (animationFrame >= attackFrameCount)
							{
								animationFrame = 0;

								attackSequenceState = AttackSequenceState.ATTACK_SEQUENCE_STATE_STOP;
							}

							// Increment animation frame
							animationFrame++;

							break;
						}
					case AttackSequenceState.ATTACK_SEQUENCE_STATE_STOP:
						{
							// Reset sequence
							sequenceType = SequenceType.SEQUENCE_TYPE_NONE;
							attackSequenceState = AttackSequenceState.ATTACK_SEQUENCE_STATE_IDLE;

							break;
						}
				}

				// Apply some physics
				if (isPhysicActive)
				{
					Vec3d force = Vec3d.Zero;

					// Compute horizontal force
					force += BurgiMath.EaseOutElastic(physicFrame) * attackHorizontalImpulse * attackDirection;

					// Apply force
					transform.Motion.Add(force);

					// Clamp velocity
					if (transform.Motion.LengthSq() > (maxVelocity * maxVelocity))
					{
						transform.Motion = transform.Motion.Normalize() * maxVelocity;
					}

					// Advance frame
					physicFrame += physicSpeedFactor * deltaTime;
					if (physicFrame >= 1.0F)
					{
						isPhysicActive = false;
					}
				}
			}

			private bool OnSprintW(KeyCombination keyComb)
			{
				if (enable == false) return false;

				currDirection8 = Directional8.DIR8_CENTER;

				return true;
			}
			private bool OnSprintA(KeyCombination keyComb)
			{
				if (enable == false) return false;

				currDirection8 = Directional8.DIR8_CENTER;

				return true;
			}
			private bool OnSprintS(KeyCombination keyComb)
			{
				if (enable == false) return false;

				currDirection8 = Directional8.DIR8_CENTER;

				return true;
			}
			private bool OnSprintD(KeyCombination keyComb)
			{
				if (enable == false) return false;

				currDirection8 = Directional8.DIR8_CENTER;

				return true;
			}
			private bool OnDashReset(KeyCombination keyComb)
			{
				if (enable == false) return false;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;
				BlockPos soundPos = new(entityPlayer.Pos.XYZInt, 0);

				// Check for dashes
				if (dashAllowed && (transform.Motion.Length() > 0.025F))
				{
					// Reset state
					isPhysicActive = true;
					isDoubleDashActive = false;
					dashAllowed = false;
					doubleDashAllowed = true;

					// Enable sequence
					sequenceType = SequenceType.SEQUENCE_TYPE_DASH;
					dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_START;

					// Play dash sounds
					Main.clientApi.World.PlaySoundAt(dashSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);
					Main.clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);

					// Register fixed dash recover action
					Main.clientApi.World.RegisterCallback(_ =>
					{
						dashAllowed = true;
						Main.clientApi.World.PlaySoundAt(dashRecoverSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);
					}, dashCooldownMs);
				}
				else
				{
					// Check for double dashes
					if (doubleDashAllowed)
					{
						if (dashSequenceState == DashSequenceState.DASH_SEQUENCE_STATE_IDLE)
						{
							// Reset state
							isPhysicActive = true;
							isDoubleDashActive = true;
							doubleDashAllowed = false;

							// Enable sequence
							sequenceType = SequenceType.SEQUENCE_TYPE_DASH;
							dashSequenceState = DashSequenceState.DASH_SEQUENCE_STATE_START;

							// Play dash sounds
							Main.clientApi.World.PlaySoundAt(dashSound2, soundPos, 0.0, null, true, 64.0F, 1.0F);
							Main.clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);
						}
					}
				}

				return true;
			}
			private bool OnSprint(KeyCombination keyComb)
			{
				if (enable == false) return false;

				isRunning = true;

				return true;
			}
			private bool OnReset(KeyCombination keyComb)
			{
				if (enable == false) return false;

				// Stop all animations
				entity.AnimManager.StopAllAnimations();

				return true;
			}

			private void OnAttackReset()
			{
				if (enable == false) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;
				BlockPos soundPos = new(entityPlayer.Pos.XYZInt, 0);

				// Check for attacks
				if (attackAllowed)
				{
					// Reset state
					isPhysicActive = true;
					attackAllowed = false;

					// Enable sequence
					sequenceType = SequenceType.SEQUENCE_TYPE_ATTACK;
					attackSequenceState = AttackSequenceState.ATTACK_SEQUENCE_STATE_START;

					// Play woosh sounds
					Main.clientApi.World.PlaySoundAt(wooshSound3, soundPos, 0.0, null, true, 64.0F, 10.0F);

					// Register fixed attack recover action
					Main.clientApi.World.RegisterCallback(_ =>
					{
						attackAllowed = true;
					}, attackCooldownMs);
				}
			}
			private void OnJumpReset()
			{
				if (enable == false) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;
				BlockPos soundPos = new(entityPlayer.Pos.XYZInt, 0);

				// Check for jumps
				if (jumpAllowed && entityPlayer.OnGround)
				{
					// Reset state
					isPhysicActive = true;
					jumpAllowed = false;

					// Enable sequence
					sequenceType = SequenceType.SEQUENCE_TYPE_JUMP;
					jumpSequenceState = JumpSequenceState.JUMP_SEQUENCE_STATE_START;

					// Play woosh sounds
					Main.clientApi.World.PlaySoundAt(wooshSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);

					// Register fixed jump recover action
					Main.clientApi.World.RegisterCallback(_ =>
					{
						jumpAllowed = true;
					}, jumpCooldownMs);
				}
			}
			private void OnToggleBlendAttackPose()
			{
				enableBlendAttackPose = !enableBlendAttackPose;

				if (enableBlendAttackPose)
				{
					// Start animation
					entity.AnimManager.StartAnimation(holdWeaponCombatPassiveData);

					// Get running animation
					runningCombatAnimation = entity.AnimManager.GetAnimationState(holdWeaponCombatPassiveData.Code);

					// Set initial animation state
					if (runningCombatAnimation != null)
					{
						runningCombatAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
						runningCombatAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
					}
				}
				else
				{
					// Stop animation
					if (runningCombatAnimation != null)
					{
						entity.AnimManager.StopAnimation(runningCombatAnimation.Animation.Code);
					}
				}
			}
			private void OnMouseDown(MouseEvent e)
			{
				if (enable == false) return;
				if (suppressMouseInput == true) return;

				if (e.Button == EnumMouseButton.Left)
				{
					OnAttackReset();
				}
				else if (e.Button == EnumMouseButton.Right)
				{
					// OnJumpReset();
					OnToggleBlendAttackPose();
				}

				e.Handled = true;
			}
		}
		internal class TrueThirdPerson : EntityBehavior
		{
			public static bool enable = false;
			public static bool enableSpringLinearVelocity = true;
			public static bool enableSpringAngularVelocity = true;
			public static bool enableBoneBobbing = true;
			public static bool enableRandomRotation = true;
			public static bool enableMotionBobbing = true;

			private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosInRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosIn");
			private static readonly AccessTools.FieldRef<Camera, Vec3d> originPosRef = AccessTools.FieldRefAccess<Camera, Vec3d>("originPos");
			private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosOutTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosOutTmp");
			private static readonly AccessTools.FieldRef<Camera, EnumCameraMode> cameraModeRef = AccessTools.FieldRefAccess<Camera, EnumCameraMode>("CameraMode");

			// General settings
			public static Vec3f cameraRootOffset = new(-0.17F, 0.03F, -0.33F);

			// Physic spring
			public static float springLinearStiffness = 22.0F;
			public static float springAngularStiffness = 22.0F;
			public static float springLinearDamping = 8.0F;
			public static float springAngularDamping = 8.0F;

			// Random rotation
			public static float randomYawRotationIntensity = 0.012F;
			public static float randomPitchRotationIntensity = 0.02F;
			public static float randomYawRotationSpeed = 1.0F;
			public static float randomPitchRotationSpeed = 1.0F;

			// Bone bobbing
			public static float boneBobSpeed = 2.0F;

			// Motion bobbing
			public static float motionBobSpeed = 160.0F;
			public static float motionBobDeadzone = 0.05F;
			public static float motionSmoothFactor = 1.0F;
			public static float motionBobVerticalIntensity = 0.04F;
			public static float motionBobHorizontalIntensity = 0.01F;
			public static float motionBobPitchIntensity = 0.003F;
			public static float motionBobRollIntensity = 0.004F;

			private static double lastBoneY = 0.0;

			private static double smoothedSpeed = 0.0;

			private static Vec3d linearVelocity = new(0, 0, 0);
			private static double yawVelocity = 0.0;
			private static double pitchVelocity = 0.0;

			private static double bobMotionTime = 0.0;
			private static double yawRotationTime = 0.0;
			private static double pitchRotationTime = 0.0;

			[HarmonyPatch(typeof(Camera), nameof(Camera.Update), [typeof(float), typeof(AABBIntersectionTest)])]
			internal class Camera_Update_Patch
			{
				public static bool Prefix(Camera __instance, float deltaTime, AABBIntersectionTest intersectionTester)
				{
					if (enable == false) return true; // Don't skip the original method

					EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
					EntityPos transform = entityPlayer.Pos;

					// Set third person mode forever
					cameraModeRef(__instance) = EnumCameraMode.ThirdPerson;

					// Compute local direction
					Vec3d localForward = transform.GetViewVector().ToVec3d();
					Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();
					Vec3d localUp = localForward.Cross(localRight);

					// Compute target camera position
					Vec3d targetPosition = cameraRootOffset.ToVec3d();
					targetPosition = BurgiMath.RotateAroundAxis(targetPosition, BurgiMath.WorldRight, transform.Pitch);
					targetPosition = BurgiMath.RotateAroundAxis(targetPosition, BurgiMath.WorldUp, transform.Yaw);

					// Compute target speed
					double targetSpeed = entityPlayer.Pos.Motion.Length();

					// Get target camera rotation
					double targetPitch = transform.Pitch;
					double targetYaw = transform.Yaw;
					double targetRoll = transform.Roll;

					// Linear interpolation
					Vec3d linearDisplacement = targetPosition - __instance.OriginPosition;
					Vec3d linearAcceleration = linearDisplacement * springLinearStiffness - linearVelocity * springLinearDamping;
					linearVelocity += linearAcceleration * deltaTime;

					// Angular interpolation
					double yawDisplacement = BurgiMath.AngleDifference(targetYaw, __instance.Yaw);
					double pitchDisplacement = targetPitch - __instance.Pitch;
					double yawAcceleration = yawDisplacement * springAngularStiffness - yawVelocity * springAngularDamping;
					double pitchAcceleration = pitchDisplacement * springAngularStiffness - pitchVelocity * springAngularDamping;
					yawVelocity += yawAcceleration * deltaTime;
					pitchVelocity += pitchAcceleration * deltaTime;

					// Apply bone bobbing
					double bobBoneY = 0.0;
					if (entityPlayer.AnimManager != null)
					{
						if (entityPlayer.AnimManager.Animator != null)
						{
							ElementPose pose = entityPlayer.AnimManager.Animator.GetPosebyName("UpperTorso");

							double boneX = pose.AnimModelMatrix[12];
							double boneY = pose.AnimModelMatrix[13];
							double boneZ = pose.AnimModelMatrix[14];

							bobBoneY = (boneY - lastBoneY);
							lastBoneY = boneY;
						}
					}

					// Apply motion bobbing
					smoothedSpeed += (targetSpeed - smoothedSpeed) * Math.Min(deltaTime * motionSmoothFactor, 1.0);
					if (entityPlayer.OnGround)
					{
						bobMotionTime += deltaTime * smoothedSpeed * motionBobSpeed;
					}
					double bobVertical = Math.Sin(bobMotionTime) * motionBobVerticalIntensity;
					double bobHorizontal = Math.Cos(bobMotionTime) * motionBobHorizontalIntensity;
					double bobPitch = Math.Sin(bobMotionTime) * motionBobPitchIntensity;
					double bobRoll = Math.Cos(bobMotionTime) * motionBobRollIntensity;

					// Apply random rotation
					double deltaYaw = 0.0;
					double deltaPitch = 0.0;
					deltaYaw = Math.Sin(yawRotationTime * randomYawRotationSpeed) * randomYawRotationIntensity;
					deltaPitch = Math.Cos(pitchRotationTime * randomPitchRotationSpeed) * randomPitchRotationIntensity;
					yawRotationTime += deltaTime;
					pitchRotationTime += deltaTime;

					// Apply camera position
					if (enableSpringLinearVelocity)
					{
						targetPosition += linearVelocity;
					}

					// Apply camera rotation
					if (enableSpringAngularVelocity)
					{
						targetPitch += pitchVelocity;
						targetYaw += yawVelocity;
					}

					// Apply bone bobbing
					if (enableBoneBobbing)
					{
						targetPosition.Y += bobBoneY * boneBobSpeed; // TODO: * deltaTime
					}

					// Apply random camera rotation
					if (enableRandomRotation)
					{
						targetPitch += deltaPitch; // TODO: * deltaTime
						targetYaw += deltaYaw; // TODO: * deltaTime
					}

					// Apply motion position bobbing
					if (enableMotionBobbing)
					{
						targetPosition += localRight * bobHorizontal; // TODO: * deltaTime
						targetPosition += BurgiMath.WorldUp * bobVertical; // TODO: * deltaTime
					}

					// Apply motion rotation bobbing
					if (enableMotionBobbing)
					{
						targetPitch += bobPitch; // TODO: * deltaTime
						targetRoll += bobRoll; // TODO: * deltaTime
					}

					// Compute camera stuff
					__instance.OriginPosition = targetPosition;
					__instance.Pitch = targetPitch;
					__instance.Yaw = targetYaw;
					__instance.Roll = targetRoll;
					__instance.CameraMatrix = __instance.GetCameraMatrix(camEyePosInRef(__instance), camEyePosInRef(__instance), __instance.Yaw, __instance.Pitch, intersectionTester);
					__instance.CameraEyePos.Set(camEyePosOutTmpRef(__instance));
					__instance.CameraMatrixOrigin = __instance.GetCameraMatrix(originPosRef(__instance), camEyePosInRef(__instance), __instance.Yaw, __instance.Pitch, intersectionTester);

					// Compute rolled matrix
					double[] cameraMatrixOrigin = __instance.CameraMatrixOrigin;
					double[] cameraMatrixOrigin2 = __instance.CameraMatrixOrigin;
					double[] array = new double[3];
					array[0] = 1.0;
					Mat4d.Rotate(cameraMatrixOrigin, cameraMatrixOrigin2, __instance.Roll, array);
					for (int i = 0; i < 16; i++)
					{
						__instance.CameraMatrixOriginf[i] = (float)__instance.CameraMatrixOrigin[i];
					}

					return false; // Skip the original method
				}
			}

			public TrueThirdPerson(Entity entity) : base(entity)
			{
				// Apply all harmony patches
				Main.clientNoObf.CreateClassProcessor(typeof(Camera_Update_Patch)).Patch();
			}

			public override string PropertyName()
			{
				return "TrueThirdPersonBehaviour";
			}
		}
		internal class AmbientCutscene : EntityBehavior
		{
			public static bool enable = false;
			public static bool isInit = true;
			public static bool isReset = false;
			public static bool isRunning = false;
			public static bool enableLoop = true;

			private static readonly AccessTools.FieldRef<Camera, Vec3d> camTargetTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camTargetTmp");
			private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosOutTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosOutTmp");

			internal class Point
			{
				public Matrixf transform = new();
			}

			[HarmonyPatch(typeof(Camera), nameof(Camera.GetCameraMatrix), [typeof(Vec3d), typeof(Vec3d), typeof(double), typeof(double), typeof(AABBIntersectionTest)])]
			internal class Camera_GetCameraMatrix_Patch
			{
				public static bool Prefix(Camera __instance, Vec3d camEyePosIn, Vec3d worldPos, double yaw, double pitch, AABBIntersectionTest intersectionTester, ref double[] __result)
				{
					if (enable == false) return true; // Don't skip the original method
					if (isRunning == false) return true; // Don't skip the original method

					EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
					EntityPos transform = entityPlayer.Pos;

					// Compute local direction
					Vec3d localForward = __instance.forwardVec;
					Vec3d localRight = BurgiMath.WorldUp.Cross(localForward).Normalize();
					Vec3d localUp = localForward.Cross(localRight);

					// Do not touch
					camEyePosOutTmpRef(__instance).X = camEyePosIn.X + entityPlayer.LocalEyePos.X + __instance.forwardVec.X * 0.2;
					camEyePosOutTmpRef(__instance).Y = camEyePosIn.Y + entityPlayer.LocalEyePos.Y + __instance.forwardVec.Y * 0.2;
					camEyePosOutTmpRef(__instance).Z = camEyePosIn.Z + entityPlayer.LocalEyePos.Z + __instance.forwardVec.Z * 0.2;
					camTargetTmpRef(__instance).X = camEyePosOutTmpRef(__instance).X + __instance.forwardVec.X;
					camTargetTmpRef(__instance).Y = camEyePosOutTmpRef(__instance).Y + __instance.forwardVec.Y;
					camTargetTmpRef(__instance).Z = camEyePosOutTmpRef(__instance).Z + __instance.forwardVec.Z;

					// Compute camera position
					Vec3d eye = camEyePosOutTmpRef(__instance);
					Vec3d up = BurgiMath.WorldUp;
					Vec3d center = camTargetTmpRef(__instance);

					__result = [
						1, 0, 0, 0,
						0, 1, 0, 0,
						0, 0, 1, 0,
						0, 0, 0, 1,
					];

					Mat4d.LookAt(__result, eye.ToDoubleArray(), center.ToDoubleArray(), up.ToDoubleArray());

					return false; // Skip the original method
				}
			}

			private static List<Point> points = [];

			public static float animationSpeed = 1.0F;
			public static float animationLength = 1.0F;

			public static float timeAcc = 0.0F;

			private static int pointIndex = 0;

			public AmbientCutscene(Entity entity) : base(entity)
			{
				// Apply all harmony patches
				Main.clientNoObf.CreateClassProcessor(typeof(Camera_GetCameraMatrix_Patch)).Patch();

				// Register realm discovered
				RealmDiscoveryManager.Callbacks += OnRealmDiscovered;
			}

			public override string PropertyName()
			{
				return "AmbientCutsceneBehaviour";
			}
			public override void OnGameTick(float deltaTime)
			{
				if (enable == false) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityControls controls = entityPlayer.Controls;
				EntityPos transform = entityPlayer.Pos;

				// Loop animation
				if (isRunning)
				{
					if (isInit)
					{
						isInit = false;
						isReset = false;

						timeAcc = 0.0F;
						pointIndex = 0;
					}

					Point currPoint = points[pointIndex];
					Point nextPoint = points[pointIndex + 1];

					// TODO

					timeAcc += deltaTime * 0.5F;
					timeAcc += deltaTime * animationSpeed;
					timeAcc += deltaTime * 0.5F;

					if (timeAcc > animationLength)
					{
						isInit = true;
						isReset = true;

						isRunning = enableLoop;
					}
				}
			}

			public static void PushPoint(Matrixf transform)
			{
				Point point = new();

				point.transform = transform;

				points.Add(point);
			}
			public static void ClearPoints()
			{
				points.Clear();
			}

			public static void Play()
			{
				if (enable == false) return;

				isRunning = !isRunning;
			}
			public static void Stop()
			{
				if (enable == false) return;

				isRunning = false;
			}
			public static void Reset()
			{
				if (enable == false) return;

				isInit = true;
				isReset = false;
				isRunning = false;
			}

			private static void OnRealmDiscovered()
			{
				int x = 42;

				// Build animation
				for (int i = 0; i < 10; i++)
				{
					Matrixf transform = new();

					transform.Identity();

					PushPoint(transform);
				}

				Reset();
				Play();
			}
		}
	}
}
