using Cairo;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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

	internal class DebugLineRenderer : IRenderer
	{
		private readonly IClientEventAPI eventApi;
		private readonly IRenderAPI renderApi;

		public double RenderOrder => 1.0;
		public int RenderRange => 10;

		private MeshData? mesh = null;
		private MeshRef? meshRef = null;

		public DebugLineRenderer(ICoreClientAPI api, int numLines)
		{
			eventApi = api.Event;
			renderApi = api.Render;

			mesh = new(numLines * 2, numLines * 2, false, false, true, false);
			mesh?.mode = EnumDrawMode.Lines;

			api.Event.RegisterRenderer(this, EnumRenderStage.Done);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.Done) return;

			if (meshRef != null)
			{
				IShaderProgram program = renderApi.CurrentActiveShader;

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

	internal class UchigatanaDashBehaviour : EntityBehavior
	{
		private readonly ICoreClientAPI clientApi;
		private readonly IInputAPI inputApi;

		EntityPlayer? entityPlayer = null;
		DebugLineRenderer? lineRenderer = null;

		private bool Init { get; set; }
		private bool Playing { get; set; }

		private float AnimationDuration { get; set; } = 4.0F;
		private float AnimationStep { get; set; } = 0.2F;
		private float DashImpulse { get; set; } = 0.3F;

		// private float Duration { get; set; } = 1.0F;
		// private float Attenuator { get; set; } = 5.0F;
		// private float Dampening { get; set; } = 0.002F;
		// private float EngageAngle { get; set; } = 0.01745329251994329547F * 30.0F;

		private float AnimationTime { get; set; } = 0.0F;
		private float GroundEpsilon { get; set; } = 0.0F;

		public UchigatanaDashBehaviour(ICoreClientAPI api, Entity entity) : base(entity)
		{
			clientApi = api;
			inputApi = api.Input;

			entityPlayer = api.World.Player.Entity;

			lineRenderer = new(api, 1000);

			inputApi.RegisterHotKey("play_blood_scythe_anim", "Play a test sequence", GlKeys.F, HotkeyType.MovementControls);
			inputApi.SetHotKeyHandler("play_blood_scythe_anim", OnReset);
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviourName";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (entityPlayer == null) return;

			EntityPos transform = entityPlayer.Pos;
			Vec3d forward = Vec3d.Zero;
			float initalAngle = 0.0F;

			if (Playing)
			{
				if (Init)
				{
					Init = false;

					AnimationTime = AnimationDuration;

					initalAngle = transform.Pitch;
					// forward = new Vec3d(GameMath.Sin(transform.Yaw), 0, GameMath.Cos(transform.Yaw)).Normalize();
					forward = transform.GetViewVector().ToVec3d();

					transform.SetPos(transform.X, transform.Y + GroundEpsilon, transform.Z);
					transform.Motion.Set(0.0F, transform.Motion.Y, 0.0F);

					if (lineRenderer != null)
					{
						lineRenderer.Reset();
						lineRenderer.AddLine(
							(float)transform.X, (float)transform.Y, (float)transform.Z,
							(float)transform.Motion.X, (float)transform.Motion.Y, (float)transform.Motion.Z,
							ColorUtil.ToRgba(0xFF, 0xFF, 0x00, 0x00));
						lineRenderer.Commit();
					}

					// BlockSelection? blockSelection = null;
					// EntitySelection? entitySelection = null;

					// entity.World.RayTraceForSelection(entity.Pos.XYZ, target, ref blockSelection, ref entitySelection);

					// entity.StartAnimation(""); // TODO
					// entity.StopAnimation..
				}

				// transform.Add(Velocity);
				// transform.SetPos(transform.X, transform.Y, transform.Z);
				// transform.SetAngles(transform.Roll, transform.Yaw, transform.Pitch + EngageAngle);

				transform.Motion.Add(forward * DashImpulse);

				AnimationTime -= AnimationStep;

				if (0.0F > AnimationTime)
				{
					AnimationTime = AnimationDuration;

					// transform.Motion.Set(0.0F, transform.Y, 0.0F);

					Playing = false;
					Init = true;
				}
			}

			// TODO: check out these flags..
			// entity.ApplyGravity
			// entity.requirePosesOnServer
		}

		private bool OnReset(KeyCombination combination)
		{
			// TODO: add reset timer..

			if (Playing == false)
			{
				Playing = true;
				Init = true;
			}

			return true;
		}
	}
}
