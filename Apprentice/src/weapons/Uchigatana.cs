using Cairo;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Apprentice.Weapon
{
	internal class AnimationStuff
	{
		private static void AnimationStuffHandling(ICoreClientAPI api, long entityId)
		{
			Entity? entity = api.World.GetEntityById(entityId);
			EntityShapeRenderer? renderer = entity?.Properties.Client.Renderer as EntityShapeRenderer;

			IAnimationManager? animManager = entity?.AnimManager;

			IDictionary<string, AnimationMetaData>? animMetaDataByName = animManager?.ActiveAnimationsByAnimCode;

			animMetaDataByName?["test"].AnimationSpeed = 0.5F;

			RunningAnimation? runAnim = animManager?.GetAnimationState("");

			IAnimator? animator = animManager?.Animator;

			// ElementPose elemPose = animator?.GetPosebyName("Root");

			// animator?.CalculateMatrices = true;

			// AttachmentPointAndPose? rootPose = animator.GetAttachmentPointPose("Root");

			// rootPose.

			// animManager.

			// renderer.
		}
	}

	// TODO: refactor me pls..
	internal class LineGizmo : IRenderer
	{
		private readonly IClientEventAPI eventApi;
		private readonly IRenderAPI renderApi;

		public double RenderOrder => 1.0;
		public int RenderRange => 10;

		private MeshData? mesh = null;
		private MeshRef? meshRef = null;

		public LineGizmo(ICoreClientAPI api, int numLines)
		{
			eventApi = api.Event;
			renderApi = api.Render;

			mesh = new(numLines * 2, numLines * 2, false, false, true, false);
			mesh?.mode = EnumDrawMode.Lines;

			eventApi.RegisterRenderer(this, EnumRenderStage.Done);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.Done) return;

			if (meshRef != null)
			{
				IShaderProgram program = renderApi.CurrentActiveShader;

				// renderApi.CameraMatrix
				// renderApi.ViewMatrix
				// renderApi.ProjectionMatrix
				// renderApi.CurrentProjectionMatrix

				renderApi.GlDisableCullFace();
				renderApi.RenderMesh(meshRef);
				renderApi.GlEnableCullFace();
			}
		}

		public void AddLine(
			float x0, float y0, float z0,
			float x1, float y1, float z1,
			int color)
		{
			if (mesh == null) return;

			mesh.AddVertexSkipTex(x0, y0, z0, color);
			mesh.AddVertexSkipTex(x1, y1, z1, color);
			mesh.AddIndex(0);
			mesh.AddIndex(1);
		}
		public void AddCircle(
			float x, float y, float z,
			float radius, int numSegments, int color)
		{
			// TODO
		}

		public void Reset()
		{
			if (mesh == null) return;

			mesh.Clear();
		}
		public void Commit()
		{
			if (mesh == null) return;

			meshRef?.Dispose();
			meshRef = renderApi.UploadMesh(mesh);
		}
		public void Dispose()
		{
			eventApi.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);

			meshRef?.Dispose();
		}
	}
	internal class DashBlur : IRenderer
	{
		private readonly IClientEventAPI eventApi;
		private readonly IRenderAPI renderApi;
		private readonly IShaderAPI shaderApi;

		private readonly MeshRef meshRef;

		private readonly IShaderProgram program;

		public double RenderOrder => 1.0;
		public int RenderRange => 9999;

		public DashBlur(ICoreClientAPI api)
		{
			eventApi = api.Event;
			renderApi = api.Render;
			shaderApi = api.Shader;

			program = shaderApi.NewShaderProgram();

			program.AssetDomain = "apprentice";
			program.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
			program.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);

			int programId = shaderApi.RegisterFileShaderProgram("dash-blur", program);

			program = renderApi.GetShader(programId);

			program.Compile();

			meshRef = renderApi.UploadMesh(QuadMeshUtil.GetQuad());

			eventApi.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.AfterFinalComposition) return;

			program.Use();

			// program.BindTexture2D("tex", );
			program.Uniform("strength", 0.3F);

			renderApi.RenderMesh(meshRef);
		}

		public void Dispose()
		{
			eventApi.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);

			program.Dispose();
			meshRef.Dispose();
		}
	}

	internal class UchigatanaDashBehaviour : EntityBehavior
	{
		private readonly AssetLocation dashSound1 = new("apprentice", "sounds/dash-1");
		private readonly AssetLocation dashSound2 = new("apprentice", "sounds/dash-2");
		private readonly AssetLocation dashRecoverSound1 = new("apprentice", "sounds/dash-recover-1");
		private readonly AssetLocation dashRecoverSound2 = new("apprentice", "sounds/dash-recover-2");

		private readonly ICoreClientAPI clientApi;
		private readonly IInputAPI inputApi;

		private const float animationStep = 1.0F / 60;
		private const float animationFrames = animationStep * 30.0F;
		private const float animationSpeed = 15.0F;

		private const float impulseAirbourne = 0.28F;
		private const float impulseGrounded = 0.8F;

		private const int dashCooldownMs = 800;

		EntityPlayer? entityPlayer = null;
		LineGizmo? lineGizmo = null;
		DashBlur? dashBlur = null;

		private bool isInit = true;
		private bool isPlaying = false;

		private float animationFrame = 0.0F;
		private bool dashOnCooldown = false;
		private Vec3d dashDirection = Vec3d.Zero;

		public UchigatanaDashBehaviour(ICoreClientAPI api, Entity entity) : base(entity)
		{
			clientApi = api;
			inputApi = api.Input;

			entityPlayer = api.World.Player.Entity;
			lineGizmo = new(api, 1000);
			// dashBlur = new(api);

			inputApi.RegisterHotKey("play_blood_scythe_anim", "Play a test sequence", GlKeys.ShiftLeft, HotkeyType.MovementControls);
			inputApi.SetHotKeyHandler("play_blood_scythe_anim", OnReset);
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (entityPlayer == null) return;

			EntityPos transform = entityPlayer.Pos;

			if (isPlaying)
			{
				if (isInit)
				{
					isInit = false;

					animationFrame = 0.0F;

					Vec3d worldUp = new(0, 1, 0);

					Vec3d localForward = transform.GetViewVector().ToVec3d();
					Vec3d localBack = localForward.Clone().Mul(-1);
					Vec3d localRight = worldUp.Cross(localForward).Normalize();
					Vec3d localLeft = localRight.Clone().Mul(-1);

					EntityControls controls = clientApi.World.Player.Entity.Controls;

					dashDirection = Vec3d.Zero;

					if (controls.Forward) dashDirection += localForward;
					if (controls.Backward) dashDirection += localBack;
					if (controls.Left) dashDirection += localRight;
					if (controls.Right) dashDirection += localLeft;

					dashDirection.Y = 0.0F;

					if (dashDirection.LengthSq() > 0)
					{
						dashDirection.Normalize();
					}

					if (lineGizmo != null)
					{
						lineGizmo.Reset();
						lineGizmo.AddLine(
							(float)transform.X,        (float)transform.Y,        (float)transform.Z,
							(float)transform.Motion.X, (float)transform.Motion.Y, (float)transform.Motion.Z,
							ColorUtil.ToRgba(0xFF, 0xFF, 0x00, 0x00));
						lineGizmo.Commit();
					}

					// BlockSelection? blockSelection = null;
					// EntitySelection? entitySelection = null;
					// entity.World.RayTraceForSelection(entity.Pos.XYZ, target, ref blockSelection, ref entitySelection);

					// entity.StartAnimation("");
					// entity.StopAnimation..
				}

				float impulseAttenuator = entity.OnGround
					? impulseGrounded
					: impulseAirbourne;

				Vec3d force = dashDirection * EaseOutElastic(animationFrame / animationFrames) * impulseAttenuator;

				transform.Motion.Add(force);

				animationFrame += animationStep * animationSpeed;

				if (animationFrame >= animationFrames)
				{
					isPlaying = false;
					isInit = true;
				}
			}
		}

		// Pirate's Life https://easings.net/
		float EaseInCirc(float x)
		{
			return 1.0F - (float)Math.Sqrt(1.0F - (float)Math.Pow(x, 2.0F));
		}
		float EaseInOutElastic(float x)
		{
			float c5 = (2.0F * (float)Math.PI) / 4.5F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: x < 0.5F
				? -((float)Math.Pow(2.0F,  20.0F * x - 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F
				:  ((float)Math.Pow(2.0F, -20.0F * x + 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F + 1.0F;
		}
		float EaseOutElastic(float x)
		{
			float c4 = (2.0F * (float)Math.PI) / 3.0F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: (float)Math.Pow(2.0F, -10.0F * x) * (float)Math.Sin((x * 10.0F - 0.75F) * c4) + 1.0F;
		}

		private bool OnReset(KeyCombination combination)
		{
			if (entityPlayer == null) return true;
			if (dashOnCooldown) return true;

			dashOnCooldown = true;
			isPlaying = true;
			isInit = true;

			clientApi.World.PlaySoundAt(dashSound1, new BlockPos(entityPlayer.Pos.XYZInt, 0), 0.0, null, true, 64.0F, 1.0F);

			clientApi.World.RegisterCallback(_ =>
			{
				dashOnCooldown = false;

				clientApi.World.PlaySoundAt(dashRecoverSound1, new BlockPos(entityPlayer.Pos.XYZInt, 0), 0.0, null, true, 64.0F, 1.0F);

			}, dashCooldownMs);

			return true;
		}
	}
}
