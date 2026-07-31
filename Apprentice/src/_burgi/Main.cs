using HarmonyLib;
using ImGuiNET;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.Common;
using VSImGui;
using VSImGui.API;

// TODO: implement world copy/past

namespace Apprentice.src._burgi
{
	using static Apprentice.src._burgi.Behaviour;
	using static Apprentice.src._burgi.BurgiMath;
	using static Apprentice.src._burgi.Shader;
	using static Apprentice.src._burgi.WorldGen;

	internal class Main
	{
		public static ICoreClientAPI clientApi = null!;

		// Shader
		public static LineGizmo lineGizmo = null!;
		public static MotionBlur motionBlur = null!;
		public static DarkAges darkAges = null!;
		public static HealthBar healthBar = null!;
		public static ObamaPrism obamaPrism = null!;

		// Harmony
		public static Harmony apiCommon = null!;
		public static Harmony clientNoObf = null!;

		// Imgui
		public static ImGuiModSystem imguiInstance = null!;

		public static void Init(ICoreClientAPI api)
		{
			// Set all globals
			clientApi = api;
			motionBlur = new();
			darkAges = new();
			healthBar = new();
			obamaPrism = new(32);
			apiCommon = new("Vintagestory.API.Common");
			clientNoObf = new("Vintagestory.Client.NoObf");
#if DEBUG
			lineGizmo = new(1000);
			imguiInstance = api.ModLoader.GetModSystem<ImGuiModSystem>();

			if (imguiInstance != null)
			{
				imguiInstance.Draw += OnImGuiDraw;

				// I got no idea when Hide() is getting called..
				api.World.RegisterGameTickListener((float deltaTime) =>
					{
						imguiInstance.Show();
					}, 0);
			}
#endif

			// Register custom behaviours
			api.Event.PlayerJoin += (IClientPlayer byPlayer) =>
			{
				// TODO: Check if the player is actually the
				//       local/current player playing this game..

				// Don't ever attach these to more players..
				// There is only one instance of it so multiple instances
				// would interfer with each other.
				byPlayer.Entity.AddBehavior(new CharacterController(byPlayer.Entity));
				byPlayer.Entity.AddBehavior(new TrueThirdPerson(byPlayer.Entity));
				byPlayer.Entity.AddBehavior(new AmbientCutscene(byPlayer.Entity));
			};
		}

#if DEBUG
		private static CallbackGUIStatus OnImGuiDraw(float deltaSeconds)
		{
			if (lineGizmo == null) return CallbackGUIStatus.DontGrabMouse;
			if (motionBlur == null) return CallbackGUIStatus.DontGrabMouse;
			if (darkAges == null) return CallbackGUIStatus.DontGrabMouse;
			if (healthBar == null) return CallbackGUIStatus.DontGrabMouse;
			if (obamaPrism == null) return CallbackGUIStatus.DontGrabMouse;

			ImGui.Begin("Shader");
			ImGui.SeparatorText("Line Gizmo");
			ImGui.Checkbox("enableLineGizmo", ref lineGizmo.gizmoEnable);
			ImGui.SeparatorText("Motion Blur");
			ImGui.Checkbox("enableMotionBlur (Don't touch)", ref motionBlur.blurEnable);
			ImGui.DragFloat("motionBlurIntensity", ref motionBlur.blurIntensity, 0.1F, 0.0F, 10.0F);
			ImGui.SeparatorText("Dark Ages");
			ImGui.Checkbox("enableDarkAges", ref darkAges.darkEnable);
			ImGui.DragFloat("darkIntensity", ref darkAges.darkIntensity, 0.1F, 0.0F, 10.0F);
			ImGui.DragFloat("darkRadius", ref darkAges.darkRadius, 0.001F, -10000.0F, 10000.0F);
			ImGui.DragFloat("depthFactor", ref darkAges.depthFactor, 0.001F, -10000.0F, 10000.0F);
			ImGui.SeparatorText("Health Bar");
			ImGui.Checkbox("healthEnable", ref healthBar.healthEnable);
			ImGui.DragFloat("renderDistance", ref healthBar.renderDistance, 0.1F);
			ImGui.SeparatorText("Obama");
			ImGui.Checkbox("obamaEnable", ref obamaPrism.obamaEnable);
			ImGui.DragFloat("obamaMaxVelocity", ref obamaPrism.obamaMaxVelocity, 0.1F);
			ImGui.DragFloat("obamaRandDistance", ref obamaPrism.obamaRandDistance, 0.1F);
			ImGui.DragFloat("obamaUpOffset", ref obamaPrism.obamaUpOffset, 0.1F);
			ImGui.DragFloat("obamaForwardOffset", ref obamaPrism.obamaForwardOffset, 0.1F);
			ImGui.DragInt("obamaUpdateFrames", ref obamaPrism.obamaUpdateFrames);
			ImGui.EndTabItem();
			ImGui.End();

			ImGui.Begin("Settings");
			if (ImGui.BeginTabBar("Behaviours", ImGuiTabBarFlags.None))
			{
				if (ImGui.BeginTabItem("CharacterController"))
				{
					if (ImGui.BeginTabBar("TabBar", ImGuiTabBarFlags.None))
					{
						if (ImGui.BeginTabItem("General"))
						{
							ImGui.Checkbox("enable", ref CharacterController.enable);
							ImGui.Checkbox("suppressMouseInput", ref CharacterController.suppressMouseInput);
							ImGui.SeparatorText("Cooldowns");
							ImGui.DragInt("dashCooldownMs", ref CharacterController.dashCooldownMs);
							ImGui.DragInt("jumpCooldownMs", ref CharacterController.jumpCooldownMs);
							ImGui.DragInt("attackCooldownMs", ref CharacterController.attackCooldownMs);
							ImGui.EndTabItem();
						}
						if (ImGui.BeginTabItem("Physic"))
						{
							ImGui.DragFloat("physicSpeedFactor", ref CharacterController.physicSpeedFactor, 0.1F, -50.0F, 50.0F);
							ImGui.DragFloat("maxVelocity", ref CharacterController.maxVelocity, 0.01F, -10.0F, 10.0F);
							ImGui.SeparatorText("Dash");
							ImGui.DragFloat("dashHorizontalImpulseGrounded", ref CharacterController.dashHorizontalImpulseGrounded, 0.1F, -10.0F, 10.0F);
							ImGui.DragFloat("dashHorizontalImpulseAirbourne", ref CharacterController.dashHorizontalImpulseAirbourne, 0.1F, -1.0F, 1.0F);
							ImGui.DragFloat("dashVerticalImpulseGrounded", ref CharacterController.dashVerticalImpulseGrounded, 0.1F, -0.1F, 0.1F);
							ImGui.DragFloat("dashVerticalImpulseAirbourne", ref CharacterController.dashVerticalImpulseAirbourne, 0.1F, -0.1F, 0.1F);
							ImGui.SeparatorText("Jump");
							ImGui.DragFloat("jumpHorizontalImpulse", ref CharacterController.jumpHorizontalImpulse, 0.1F, -10.0F, 10.0F);
							ImGui.SeparatorText("Attack");
							ImGui.DragFloat("attackHorizontalImpulse", ref CharacterController.attackHorizontalImpulse, 0.1F, -10.0F, 10.0F);
							ImGui.EndTabItem();
						}
						if (ImGui.BeginTabItem("Animation"))
						{
							ImGui.Checkbox("enableRunAnimations", ref CharacterController.enableRunAnimations);
							ImGui.Checkbox("enableInverseKinematic", ref CharacterController.enableInverseKinematic);
							ImGui.DragFloat("runAnimationDeadzone", ref CharacterController.runAnimationDeadzone, 0.1F);
							ImGui.SeparatorText("Animation Speed");
							ImGui.DragFloat("animationSpeedDash", ref CharacterController.animationSpeedDash, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedJump", ref CharacterController.animationSpeedJump, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedSwordHit", ref CharacterController.animationSpeedSwordHit, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedSwordHit2", ref CharacterController.animationSpeedSwordHit2, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedCleaverHit", ref CharacterController.animationSpeedCleaverHit, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedSprintForward", ref CharacterController.animationSpeedSprintForward, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedSprintBack", ref CharacterController.animationSpeedSprintBack, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedStrafeForwardLeft90", ref CharacterController.animationSpeedStrafeForwardLeft90, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedStrafeForwardRight90", ref CharacterController.animationSpeedStrafeForwardRight90, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedStrafeForwardLeft45", ref CharacterController.animationSpeedStrafeForwardLeft45, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedStrafeForwardRight45", ref CharacterController.animationSpeedStrafeForwardRight45, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("animationSpeedRunMultiplier", ref CharacterController.animationSpeedRunMultiplier, 0.1F, 0.0F, 20.0F);
							ImGui.SeparatorText("Motion Speed");
							ImGui.DragFloat("motionSpeedSprintForward", ref CharacterController.motionSpeedSprintForward, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("motionSpeedSprintBack", ref CharacterController.motionSpeedSprintBack, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("motionSpeedStrafeForwardLeft90", ref CharacterController.motionSpeedStrafeForwardLeft90, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("motionSpeedStrafeForwardRight90", ref CharacterController.motionSpeedStrafeForwardRight90, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("motionSpeedStrafeForwardLeft45", ref CharacterController.motionSpeedStrafeForwardLeft45, 0.1F, 0.0F, 20.0F);
							ImGui.DragFloat("motionSpeedStrafeForwardRight45", ref CharacterController.motionSpeedStrafeForwardRight45, 0.1F, 0.0F, 20.0F);
							ImGui.SeparatorText("Frame Counts");
							ImGui.DragInt("dashFrameCount", ref CharacterController.dashFrameCount);
							ImGui.DragInt("dashRetractFrameCount", ref CharacterController.dashRetractFrameCount);
							ImGui.DragInt("jumpFrameCount", ref CharacterController.jumpFrameCount);
							ImGui.DragInt("attackFrameCount", ref CharacterController.attackFrameCount);
							ImGui.EndTabItem();
						}
						ImGui.EndTabBar();
					}
					ImGui.EndTabItem();
				}
				if (ImGui.BeginTabItem("TrueThirdPerson"))
				{
					if (ImGui.BeginTabBar("TabBar", ImGuiTabBarFlags.None))
					{
						if (ImGui.BeginTabItem("General"))
						{
							ImGui.Checkbox("enable", ref TrueThirdPerson.enable);
							Vector3 p = new(TrueThirdPerson.cameraRootOffset.X, TrueThirdPerson.cameraRootOffset.Y, TrueThirdPerson.cameraRootOffset.Z);
							if (ImGui.DragFloat3("cameraRootOffset", ref p, 0.01F)) TrueThirdPerson.cameraRootOffset.Set(p.X, p.Y, p.Z);
							ImGui.EndTabItem();
						}
						if (ImGui.BeginTabItem("Immersion"))
						{
							ImGui.SeparatorText("Physic Spring");
							ImGui.Checkbox("enableSpringLinearVelocity", ref TrueThirdPerson.enableSpringLinearVelocity);
							ImGui.Checkbox("enableSpringAngularVelocity", ref TrueThirdPerson.enableSpringAngularVelocity);
							ImGui.DragFloat("springLinearStiffness", ref TrueThirdPerson.springLinearStiffness, 0.1F);
							ImGui.DragFloat("springAngularStiffness", ref TrueThirdPerson.springAngularStiffness, 0.1F);
							ImGui.DragFloat("springLinearDamping", ref TrueThirdPerson.springLinearDamping, 0.1F);
							ImGui.DragFloat("springAngularDamping", ref TrueThirdPerson.springAngularDamping, 0.1F);
							ImGui.SeparatorText("Random Rotation");
							ImGui.Checkbox("enableRandomRotation", ref TrueThirdPerson.enableRandomRotation);
							ImGui.DragFloat("randomYawRotationIntensity", ref TrueThirdPerson.randomYawRotationIntensity, 0.1F);
							ImGui.DragFloat("randomPitchRotationIntensity", ref TrueThirdPerson.randomPitchRotationIntensity, 0.1F);
							ImGui.DragFloat("randomYawRotationSpeed", ref TrueThirdPerson.randomYawRotationSpeed, 0.1F);
							ImGui.DragFloat("randomPitchRotationSpeed", ref TrueThirdPerson.randomPitchRotationSpeed, 0.1F);
							ImGui.SeparatorText("Bone Bobbing");
							ImGui.Checkbox("enableBoneBobbing", ref TrueThirdPerson.enableBoneBobbing);
							ImGui.DragFloat("boneBobSpeed", ref TrueThirdPerson.boneBobSpeed, 0.1F);
							ImGui.SeparatorText("Motion Bobbing");
							ImGui.Checkbox("enableMotionBobbing", ref TrueThirdPerson.enableMotionBobbing);
							ImGui.DragFloat("motionBobSpeed", ref TrueThirdPerson.motionBobSpeed, 0.1F);
							ImGui.DragFloat("motionBobDeadzone", ref TrueThirdPerson.motionBobDeadzone, 0.1F);
							ImGui.DragFloat("motionSmoothFactor", ref TrueThirdPerson.motionSmoothFactor, 0.1F);
							ImGui.DragFloat("motionBobVerticalIntensity", ref TrueThirdPerson.motionBobVerticalIntensity, 0.1F);
							ImGui.DragFloat("motionBobHorizontalIntensity", ref TrueThirdPerson.motionBobHorizontalIntensity, 0.1F);
							ImGui.DragFloat("motionBobPitchIntensity", ref TrueThirdPerson.motionBobPitchIntensity, 0.1F);
							ImGui.DragFloat("motionBobRollIntensity", ref TrueThirdPerson.motionBobRollIntensity, 0.1F);
							ImGui.EndTabItem();
						}
						ImGui.EndTabBar();
					}
					ImGui.EndTabItem();
				}
				if (ImGui.BeginTabItem("AmbientCutscene"))
				{
					if (ImGui.BeginTabBar("TabBar", ImGuiTabBarFlags.None))
					{
						if (ImGui.BeginTabItem("General"))
						{
							ImGui.Checkbox("enable", ref AmbientCutscene.enable);
							ImGui.DragFloat("animationSpeed", ref AmbientCutscene.animationSpeed, 0.1F);
							ImGui.DragFloat("animationLength", ref AmbientCutscene.animationLength, 0.1F);
							ImGui.SeparatorText("Controls");
							if (ImGui.Button("Play")) AmbientCutscene.Play();
							ImGui.SameLine();
							if (ImGui.Button("Stop")) AmbientCutscene.Stop();
							ImGui.SameLine();
							if (ImGui.Button("Reset")) AmbientCutscene.Reset();
							ImGui.SameLine();
							ImGui.Checkbox("enableLoop", ref AmbientCutscene.enableLoop);
							ImGui.SeparatorText("State");
							ImGui.Text("isInit: " + AmbientCutscene.isInit.ToString());
							ImGui.Text("isRunning: " + AmbientCutscene.isRunning.ToString());
							ImGui.Text("isReset: " + AmbientCutscene.isReset.ToString());
							ImGui.Text("timeAcc: " + AmbientCutscene.timeAcc.ToString());
							ImGui.EndTabItem();
						}
						ImGui.EndTabBar();
					}
					ImGui.EndTabItem();
				}
				ImGui.EndTabBar();
			}

			return CallbackGUIStatus.DontGrabMouse;
		}
#endif
	}
}
