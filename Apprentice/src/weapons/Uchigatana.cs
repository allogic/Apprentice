using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
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

			RunningAnimation? runAnim = animManager?.GetAnimationState("runAnim");

			IAnimator? animator = animManager?.Animator;

			// ElementPose elemPose = animator?.GetPosebyName("Root");

			// animator?.CalculateMatrices = true;

			// AttachmentPointAndPose? rootPose = animator.GetAttachmentPointPose("Root");

			// rootPose.

			// animManager.

			// renderer.
		}
	}

	internal class LineGizmo : IRenderer
	{
		private readonly IClientEventAPI eventApi;
		private readonly IRenderAPI renderApi;
		private readonly IShaderAPI shaderApi;

		private readonly IShaderProgram lineProgram;

		private MeshData? mesh = null;
		private MeshRef? meshRef = null;

		public double RenderOrder => 1.0;
		public int RenderRange => 10;

		public LineGizmo(ICoreClientAPI api, int numLines)
		{
			eventApi = api.Event;
			renderApi = api.Render;
			shaderApi = api.Shader;

			// Create line program
			lineProgram = shaderApi.NewShaderProgram();
			lineProgram.AssetDomain = "apprentice";
			lineProgram.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
			lineProgram.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);
			shaderApi.RegisterFileShaderProgram("line-shader", lineProgram);
			lineProgram.Compile();

			// Create mesh
			mesh = new(numLines * 2, numLines * 2, false, false, true, false);
			mesh?.mode = EnumDrawMode.Lines;

			eventApi.RegisterRenderer(this, EnumRenderStage.Done);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.Done) return;

			if (meshRef == null) return;

			renderApi.GlDisableCullFace();

			// Draw tha gizmo
			lineProgram.Use();
			// lineProgram.Uniform("color", 0); // TODO
			renderApi.RenderMesh(meshRef);
			lineProgram.Stop();

			renderApi.GlEnableCullFace();
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

			lineProgram.Dispose();

			meshRef?.Dispose();
		}
	}
	internal class DashBlur : IRenderer
	{
		private readonly IClientEventAPI eventApi;
		private readonly IRenderAPI renderApi;
		private readonly IShaderAPI shaderApi;

		private readonly RawTexture blitTexture;
		private readonly RawTexture accTextureA;
		private readonly RawTexture accTextureB;

		private readonly IShaderProgram blitProgram;
		private readonly IShaderProgram blurProgram;

		private MeshRef? meshRef = null;
		private FrameBufferRef? frameBufferBlitRef = null;
		private FrameBufferRef? frameBufferARef = null;
		private FrameBufferRef? frameBufferBRef = null;

		public bool BlurEnable = false;
		public float BlurIntensity = 0.0F;

		public double RenderOrder => 1.0;
		public int RenderRange => 9999;

		public DashBlur(ICoreClientAPI api)
		{
			eventApi = api.Event;
			renderApi = api.Render;
			shaderApi = api.Shader;

			// Create blit program
			blitProgram = shaderApi.NewShaderProgram();
			blitProgram.AssetDomain = "apprentice";
			blitProgram.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
			blitProgram.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);
			shaderApi.RegisterFileShaderProgram("blit-shader", blitProgram);
			blitProgram.Compile();

			// Create blur program
			blurProgram = shaderApi.NewShaderProgram();
			blurProgram.AssetDomain = "apprentice";
			blurProgram.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
			blurProgram.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);
			shaderApi.RegisterFileShaderProgram("dash-blur", blurProgram);
			blurProgram.Compile();

			meshRef = renderApi.UploadMesh(QuadMeshUtil.GetQuad());

			// Create blitTexture render target
			blitTexture = new RawTexture();
			blitTexture.MinFilter = EnumTextureFilter.Nearest;
			blitTexture.MagFilter = EnumTextureFilter.Nearest;
			blitTexture.WrapS = EnumTextureWrap.ClampToEdge;
			blitTexture.WrapT = EnumTextureWrap.ClampToEdge;
			blitTexture.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
			blitTexture.Width = renderApi.FrameWidth; // TODO: update these values when main framebuffer changes size
			blitTexture.Height = renderApi.FrameHeight;
			blitTexture.TextureId = 0;
			renderApi.GenTexture(blitTexture);

			// Create accumulator render target A
			accTextureA = new RawTexture();
			accTextureA.MinFilter = EnumTextureFilter.Nearest;
			accTextureA.MagFilter = EnumTextureFilter.Nearest;
			accTextureA.WrapS = EnumTextureWrap.ClampToEdge;
			accTextureA.WrapT = EnumTextureWrap.ClampToEdge;
			accTextureA.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
			accTextureA.Width = renderApi.FrameWidth; // TODO: update these values when main framebuffer changes size
			accTextureA.Height = renderApi.FrameHeight;
			accTextureA.TextureId = 0;
			renderApi.GenTexture(accTextureA);

			// Create accumulator render target B
			accTextureB = new RawTexture();
			accTextureB.MinFilter = EnumTextureFilter.Nearest;
			accTextureB.MagFilter = EnumTextureFilter.Nearest;
			accTextureB.WrapS = EnumTextureWrap.ClampToEdge;
			accTextureB.WrapT = EnumTextureWrap.ClampToEdge;
			accTextureB.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
			accTextureB.Width = renderApi.FrameWidth; // TODO: update these values when main framebuffer changes size
			accTextureB.Height = renderApi.FrameHeight;
			accTextureB.TextureId = 0;
			renderApi.GenTexture(accTextureB);

			// Create blit frame buffer
			FramebufferAttrs frameBufferBlitAttribs = new("blit", renderApi.FrameWidth, renderApi.FrameHeight);
			frameBufferBlitAttribs.Attachments = new FramebufferAttrsAttachment[1];
			frameBufferBlitAttribs.Attachments[0] = new();
			frameBufferBlitAttribs.Attachments[0].Texture = blitTexture;
			frameBufferBlitAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
			frameBufferBlitRef = renderApi.CreateFrameBuffer(frameBufferBlitAttribs);

			// Create ping pong frame buffer A
			FramebufferAttrs frameBufferAAttribs = new("accA", renderApi.FrameWidth, renderApi.FrameHeight);
			frameBufferAAttribs.Attachments = new FramebufferAttrsAttachment[1];
			frameBufferAAttribs.Attachments[0] = new();
			frameBufferAAttribs.Attachments[0].Texture = accTextureA;
			frameBufferAAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
			frameBufferARef = renderApi.CreateFrameBuffer(frameBufferAAttribs);

			// Create ping pong frame buffer B
			FramebufferAttrs frameBufferBAttribs = new("accB", renderApi.FrameWidth, renderApi.FrameHeight);
			frameBufferBAttribs.Attachments = new FramebufferAttrsAttachment[1];
			frameBufferBAttribs.Attachments[0] = new();
			frameBufferBAttribs.Attachments[0].Texture = accTextureB;
			frameBufferBAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
			frameBufferBRef = renderApi.CreateFrameBuffer(frameBufferBAttribs);

			eventApi.RegisterRenderer(this, EnumRenderStage.Done);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.Done) return;

			if (meshRef == null) return;
			if (frameBufferBlitRef == null) return;
			if (frameBufferARef == null) return;
			if (frameBufferBRef == null) return;

			// Blit render target
			renderApi.CurrentFrameBuffer = frameBufferBlitRef;
			blitProgram.Use();
			blitProgram.BindTexture2D("tex", renderApi.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0], 0);
			renderApi.RenderMesh(meshRef);
			blitProgram.Stop();

			// Accumulate motion blur
			renderApi.CurrentFrameBuffer = frameBufferARef;
			blurProgram.Use();
			blurProgram.BindTexture2D("blitTex", frameBufferBlitRef.ColorTextureIds[0], 0);
			blurProgram.BindTexture2D("accTex", frameBufferBRef.ColorTextureIds[0], 1);
			blurProgram.Uniform("blurIntensity", BlurIntensity);
			renderApi.RenderMesh(meshRef);
			blurProgram.Stop();

			if (BlurEnable)
			{
				// Blit render target
				renderApi.CurrentFrameBuffer = null;
				blitProgram.Use();
				blitProgram.BindTexture2D("tex", frameBufferARef.ColorTextureIds[0], 0);
				renderApi.RenderMesh(meshRef);
				blitProgram.Stop();
			}

			// Swap frame accumulator
			FrameBufferRef tmp = frameBufferARef;
			frameBufferARef = frameBufferBRef;
			frameBufferBRef = tmp;
		}

		public void Dispose()
		{
			eventApi.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);

			renderApi.DestroyFrameBuffer(frameBufferARef);
			renderApi.DestroyFrameBuffer(frameBufferBRef);
			renderApi.DestroyFrameBuffer(frameBufferBlitRef);

			renderApi.GLDeleteTexture(blitTexture.TextureId);
			renderApi.GLDeleteTexture(accTextureA.TextureId);
			renderApi.GLDeleteTexture(accTextureB.TextureId);

			blurProgram.Dispose();
			blitProgram.Dispose();

			meshRef?.Dispose();
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
		private readonly IAnimationManager animManager;

		private const float animationStep = 1.0F / 60;
		private const float animationFrames = animationStep * 30.0F;
		private const float animationSpeed = 15.0F;

		private const float impulseAirbourne = 0.28F;
		private const float impulseGrounded = 0.8F;

		private const int dashCooldownMs = 800;
		private const int dashBlurEnableMs = 250;

		EntityPlayer? entityPlayer = null;
		LineGizmo? lineGizmo = null;
		DashBlur? dashBlur = null;

		private bool isInit = true;
		private bool isPlaying = false;

		private float animationFrame = 0.0F;
		private bool dashOnCooldown = false;
		private Vec3d dashDirection = Vec3d.Zero;

		private RunningAnimation runningAnimation = new();
		private RunningAnimation sprintAnimation = new();

		private Animation currAnimation = new();

		public UchigatanaDashBehaviour(ICoreClientAPI api, Entity entity) : base(entity)
		{
			clientApi = api;
			inputApi = api.Input;
			animManager = entity.AnimManager;

			entityPlayer = api.World.Player.Entity;
			lineGizmo = new(api, 1000);
			dashBlur = new(api);

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

					// animManager.LoadAnimator();
					// animManager.OnAnimationReceived

					sprintAnimation = animManager.GetAnimationState("walk");
					sprintAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Stop;

					runningAnimation = animManager.GetAnimationState("swordhit");
					runningAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Stop;

					// IAnimator? animator = animManager.Animator;
					// animator.ActiveAnimationCount

					// RunningAnimation[] animations = entity.AnimManager.Animator.Animations;
					// int animationCount = entity.AnimManager.Animator.Animations.Length;
					// for (int i = 0; i < animationCount; i++)
					// {
					// 	animations[i].
					// }

					// entity.AnimManager.StartAnimation(new AnimationMetaData()
					// {
					// 	Animation = "walk",
					// 	Code = "walk",
					// 	Weight = 0.5F,
					// 	SupressDefaultAnimation = true,
					// 	ClientSide = true,
					// 	AnimationSpeed = 0.7F,
					// 	BlendMode = EnumAnimationBlendMode.Add,
					// 	ElementWeight = {
					// 		{ "root", 100.0F },
					// 	},
					// 	ElementBlendMode = {
					// 		{ "root", EnumAnimationBlendMode.Add },
					// 	},
					// });

					entity.AnimManager.StartAnimation(new AnimationMetaData()
					{
						Animation = "jump",
						Code = "newjump",
						Weight = 0.5F,
						SupressDefaultAnimation = true,
						ClientSide = true,
						AnimationSpeed = 0.5F,
						BlendMode = EnumAnimationBlendMode.Add,
						ElementWeight = {
							{ "root", 100.0F },
						},
						ElementBlendMode = {
							{ "root", EnumAnimationBlendMode.Add },
						},
					});

					entity.AnimManager.StartAnimation(new AnimationMetaData()
					{
						Animation = "swordhit",
						Code = "swordhit",
						EaseInSpeed = 8.0F,
						EaseOutSpeed = 8.0F,
						Weight = 1.0F,
						SupressDefaultAnimation = true,
						ClientSide = true,
						AnimationSpeed = 2.2F,
						BlendMode = EnumAnimationBlendMode.AddAverage,
						ElementWeight = {
							{ "UpperArmR", 20.0F },
							{ "LowerArmR", 20.0F },
							{ "UpperArmL", 20.0F },
							{ "LowerArmL", 20.0F }
						},
						ElementBlendMode = {
							{ "UpperArmR", EnumAnimationBlendMode.AddAverage },
							{ "LowerArmR", EnumAnimationBlendMode.AddAverage },
							{ "UpperArmL", EnumAnimationBlendMode.AddAverage },
							{ "LowerArmL", EnumAnimationBlendMode.AddAverage }
						},
					});
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

				dashBlur?.BlurIntensity = (float)transform.Motion.Length();
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
			if (dashBlur == null) return true;

			dashOnCooldown = true;
			isPlaying = true;
			isInit = true;

			dashBlur.BlurEnable = true;

			clientApi.World.AddCameraShake(2.0F);

			clientApi.World.PlaySoundAt(dashSound1, new BlockPos(entityPlayer.Pos.XYZInt, 0), 0.0, null, true, 64.0F, 1.0F);

			clientApi.World.RegisterCallback(_ =>
			{
				dashOnCooldown = false;
				clientApi.World.PlaySoundAt(dashRecoverSound1, new BlockPos(entityPlayer.Pos.XYZInt, 0), 0.0, null, true, 64.0F, 1.0F);
			}, dashCooldownMs);

			clientApi.World.RegisterCallback(_ =>
			{
				dashBlur.BlurEnable = false;
			}, dashBlurEnableMs);

			return true;
		}
	}

	internal class TrueThirdPersonBehaviour : EntityBehavior
	{
		private readonly ICoreClientAPI clientApi;

		EntityPlayer? entityPlayer = null;

		public TrueThirdPersonBehaviour(ICoreClientAPI api, Entity entity) : base(entity)
		{
			clientApi = api;

			entityPlayer = api.World.Player.Entity;
		}

		public override string PropertyName()
		{
			return "TrueThirdPersonBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (entityPlayer == null) return;

			EntityPos transform = entityPlayer.Pos;

			transform.HeadPitch = 45.0F;
			transform.HeadYaw = 0.0F;

			// clientApi.World.Player.CameraRoll = 45.0F;
			// clientApi.World.Player.camera

			// clientApi.Render.CurrentModelviewMatrix
			// clientApi.Render.CameraOffset.ScaleXYZ.X = 10.0F;
		}
	}
}
