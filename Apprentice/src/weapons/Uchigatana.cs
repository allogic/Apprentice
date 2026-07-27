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

// TODO: refactor Directional8 on dash quadrant angle
// TODO: add attack pose on top of run animation based on mouse right down/up

namespace Apprentice.Weapon
{
	internal class MathUtil
	{
		public static readonly Vec3d WORLD_RIGHT = new(1, 0, 0);
		public static readonly Vec3d WORLD_UP = new(0, 1, 0);
		public static readonly Vec3d WORLD_FORWARD = new(0, 0, 1);
		public static readonly Vec3d WORLD_LEFT = new(-1, 0, 0);
		public static readonly Vec3d WORLD_DOWN = new(0, -1, 0);
		public static readonly Vec3d WORLD_BACK = new(0, 0, -1);

		public static readonly float RAD_TO_DEG = 57.29577951308232286465F;
		public static readonly float DEG_TO_RAD = 0.017453292519943295470F;

		public static Vec3d RotateAroundAxis(Vec3d v, Vec3d axis, double angle)
		{
			axis = axis.Clone().Normalize();

			double cos = Math.Cos(angle);
			double sin = Math.Sin(angle);

			Vec3d term1 = v * cos;
			Vec3d term2 = axis.Cross(v) * sin;
			Vec3d term3 = axis * (axis.Dot(v) * (1.0 - cos));

			return term1 + term2 + term3;
		}

		public static double AngleDifference(double target, double current)
		{
			double diff = target - current;

			while (diff > Math.PI)
				diff -= Math.Tau;

			while (diff < -Math.PI)
				diff += Math.Tau;

			return diff;
		}
	}

	internal class LineGizmo : IRenderer
	{
		private readonly ICoreClientAPI clientApi;
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
			clientApi = api;
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

			eventApi.RegisterRenderer(this, EnumRenderStage.Opaque);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (stage != EnumRenderStage.Opaque) return;

			if (meshRef == null) return;

			renderApi.GlDisableCullFace();
			renderApi.GlToggleBlend(false);

			renderApi.LineWidth = 10;

			// Draw the gizmo
			lineProgram.Use();
			lineProgram.UniformMatrix("projectionMatrix", renderApi.CurrentProjectionMatrix);
			lineProgram.UniformMatrix("viewMatrix", renderApi.CurrentModelviewMatrix);
			renderApi.RenderMesh(meshRef);
			lineProgram.Stop();

			renderApi.GlToggleBlend(true);
			renderApi.GlEnableCullFace();
		}

		public void AddLine(
			float x0, float y0, float z0,
			float x1, float y1, float z1,
			int color)
		{
			if (mesh == null) return;

			int vertexCount = mesh.VerticesCount;
			mesh.AddVertexSkipTex(x0, y0, z0, color);
			mesh.AddVertexSkipTex(x1, y1, z1, color);
			mesh.AddIndex(vertexCount + 0);
			mesh.AddIndex(vertexCount + 1);
		}
		public void AddBox(
			float x0, float y0, float z0,
			float sx, float sy, float sz,
			int color)
		{
			if (mesh == null) return;

			int vertexCount = mesh.VerticesCount;

			mesh.AddVertexSkipTex(x0, y0, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0, y0, z0, color);

			mesh.AddVertexSkipTex(x0, y0, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0 + sz, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0 + sz, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0 + sz, color);
			mesh.AddVertexSkipTex(x0, y0, z0 + sz, color);

			mesh.AddVertexSkipTex(x0, y0, z0, color);
			mesh.AddVertexSkipTex(x0, y0, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0, z0 + sz, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0 + sx, y0 + sy, z0 + sz, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0, color);
			mesh.AddVertexSkipTex(x0, y0 + sy, z0 + sz, color);

			mesh.AddIndex(vertexCount + 0);
			mesh.AddIndex(vertexCount + 1);
			mesh.AddIndex(vertexCount + 2);
			mesh.AddIndex(vertexCount + 3);
			mesh.AddIndex(vertexCount + 4);
			mesh.AddIndex(vertexCount + 5);
			mesh.AddIndex(vertexCount + 6);
			mesh.AddIndex(vertexCount + 7);

			mesh.AddIndex(vertexCount + 8);
			mesh.AddIndex(vertexCount + 9);
			mesh.AddIndex(vertexCount + 10);
			mesh.AddIndex(vertexCount + 11);
			mesh.AddIndex(vertexCount + 12);
			mesh.AddIndex(vertexCount + 13);
			mesh.AddIndex(vertexCount + 14);
			mesh.AddIndex(vertexCount + 15);

			mesh.AddIndex(vertexCount + 16);
			mesh.AddIndex(vertexCount + 17);
			mesh.AddIndex(vertexCount + 18);
			mesh.AddIndex(vertexCount + 19);
			mesh.AddIndex(vertexCount + 20);
			mesh.AddIndex(vertexCount + 21);
			mesh.AddIndex(vertexCount + 22);
			mesh.AddIndex(vertexCount + 23);
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

			meshRef = renderApi.UploadMesh(mesh);
		}
		public void Dispose()
		{
			eventApi.UnregisterRenderer(this, EnumRenderStage.Opaque);
		}
	}
	internal class MotionBlur : IRenderer
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

		public MotionBlur(ICoreClientAPI api)
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
			eventApi.UnregisterRenderer(this, EnumRenderStage.Done);

			renderApi.DestroyFrameBuffer(frameBufferARef);
			renderApi.DestroyFrameBuffer(frameBufferBRef);
			renderApi.DestroyFrameBuffer(frameBufferBlitRef);

			renderApi.GLDeleteTexture(blitTexture.TextureId);
			renderApi.GLDeleteTexture(accTextureA.TextureId);
			renderApi.GLDeleteTexture(accTextureB.TextureId);
		}
	}

	internal class UchigatanaDashBehaviour : EntityBehavior
	{
		public static ICoreClientAPI? clientApi = null;

		public static bool enable = true;
		public static bool enableLineGizmo = false;
		public static bool enableMotionBlur = true;
		public static bool enableRunAnimations = true;
		public static bool enableBlendAttackPose = false;

		internal enum Directional8
		{
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

				return whitelistedAnimationCodes.Contains(animdata.Code);
			}
		}
		[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(string)])]
		internal class AnimationManager_StartAnimation1_Patch
		{
			public static bool Prefix(AnimationManager __instance, string configCode)
			{
				if (enable == false) return true; // Don't skip the original method

				return whitelistedAnimationCodes.Contains(configCode);
			}
		}

		private readonly AssetLocation dashSound1 = new("apprentice", "sounds/dash-1");
		private readonly AssetLocation dashSound2 = new("apprentice", "sounds/dash-2");
		private readonly AssetLocation dashRecoverSound1 = new("apprentice", "sounds/dash-recover-1");
		private readonly AssetLocation dashRecoverSound2 = new("apprentice", "sounds/dash-recover-2");
		private readonly AssetLocation ushigatanaDashSound = new("apprentice", "sounds/ushigatana-dash");
		private readonly AssetLocation wooshSound1 = new("apprentice", "sounds/woosh-1");
		private readonly AssetLocation wooshSound2 = new("apprentice", "sounds/woosh-2");
		private readonly AssetLocation wooshSound3 = new("apprentice", "sounds/woosh-3");
		// TODO: add missing footstep sfx..

		private LineGizmo? lineGizmo = null;
		private MotionBlur? motionBlur = null;
		private Harmony? harmonyInstance = null;
		private ImGuiModSystem? imguiInstance = null;

		private bool isPhysicActive = false;
		private bool isDoubleDashActive = false;
		private bool dashAllowed = true;
		private bool attackAllowed = true;
		private bool jumpAllowed = true;
		private bool doubleDashAllowed = true;

		private float physicSpeedFactor = 8.356F;
		private float maxVelocity = 0.3F;

		private float dashHorizontalImpulseGrounded = 1.0F;
		private float dashHorizontalImpulseAirbourne = 0.036F;
		private float dashVerticalImpulseGrounded = 0.02F;
		private float dashVerticalImpulseAirbourne = 0.04F;
		private float attackHorizontalImpulse = 0.15F;
		private float jumpHorizontalImpulse = 0.15F;

		private float runAnimationDeadzone = 0.001F;

		private float animationSpeedDash = 2.5F;
		private float animationSpeedJump = 2.5F;
		private float animationSpeedSwordHit = 2.5F;
		private float animationSpeedSwordHit2 = 2.5F;
		private float animationSpeedCleaverHit = 2.5F;
		private float animationSpeedSprintForward = 0.7F;
		private float animationSpeedSprintBack = 0.7F;
		private float animationSpeedStrafeForwardLeft90 = 0.6F;
		private float animationSpeedStrafeForwardRight90 = 0.6F;
		private float animationSpeedStrafeForwardLeft45 = 0.6F;
		private float animationSpeedStrafeForwardRight45 = 0.6F;

		private float motionBlurIntensity = 2.7F;

		private int dashCooldownMs = 1500;
		private int jumpCooldownMs = 150;
		private int attackCooldownMs = 300;

		private float physicFrame = 0.0F;
		private int animationFrame = 0;

		private int dashFrameCount = 18;
		private int dashRetractFrameCount = 0;
		private int attackFrameCount = 10;
		private int jumpFrameCount = 10;

		private Vec3d dashDirection = new(0, 0, 0);
		private Vec3d attackDirection = new(0, 0, 0);
		private Vec3d jumpDirection = new(0, 0, 0);

		#region Dash Animations
		private AnimationMetaData dashForwardData = new()
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
		private AnimationMetaData dashBackData = new()
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
		private AnimationMetaData dashLeftData = new()
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
		private AnimationMetaData dashRightData = new()
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

		#region Run Animations
		private AnimationMetaData sprintForwardData = new()
		{
			Animation = "sprint-forward",
			Code = "sprint-forward",
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
		private AnimationMetaData sprintBackData = new()
		{
			Animation = "sprint-back",
			Code = "sprint-back",
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

		#region Strafe Animations
		private AnimationMetaData strafeForwardLeft90Data = new()
		{
			Animation = "strafe-forward-left-90",
			Code = "strafe-forward-left-90",
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
		private AnimationMetaData strafeForwardLeft45Data = new()
		{
			Animation = "strafe-forward-left-45",
			Code = "strafe-forward-left-45",
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
		private AnimationMetaData strafeForwardRight90Data = new()
		{
			Animation = "strafe-forward-right-90",
			Code = "strafe-forward-right-90",
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
		private AnimationMetaData strafeForwardRight45Data = new()
		{
			Animation = "strafe-forward-right-45",
			Code = "strafe-forward-right-45",
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

		#region Combat Animations
		private AnimationMetaData holdWeaponCombatPassiveData = new()
		{
			Animation = "hold-weapon-combat-passive",
			Code = "hold-weapon-combat-passive",
			Weight = 1.0F,
			SupressDefaultAnimation = true,
			ClientSide = true,
			AnimationSpeed = 0.8F,
			BlendMode = EnumAnimationBlendMode.Add,
			ElementWeight = {
				{ "UpperTorso", 0.2F },
				{ "UpperArmR", 1.0F },
				{ "UpperArmL", 1.0F },
				{ "Neck", 1.0F },
				{ "UpperBackAttachment", 1.0F },
			},
			ElementBlendMode = {
				{ "UpperTorso", EnumAnimationBlendMode.AddAverage },
				{ "UpperArmR", EnumAnimationBlendMode.Add },
				{ "UpperArmL", EnumAnimationBlendMode.Add },
				{ "Neck", EnumAnimationBlendMode.Add },
				{ "UpperBackAttachment", EnumAnimationBlendMode.Add },
			},
		};
		#endregion

		#region Internal Animations
		private AnimationMetaData swordHitData = new()
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
		private AnimationMetaData swordHit2Data = new()
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
		private AnimationMetaData cleaverHitData = new()
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
		private AnimationMetaData bowAimLongData = new()
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

		public UchigatanaDashBehaviour(Entity entity) : base(entity)
		{
			if (clientApi == null) return;

			// TODO: fix api injection
			motionBlur = new(clientApi);
			harmonyInstance = new("Vintagestory.API.Common");
#if DEBUG
			lineGizmo = new(clientApi, 1000);

			imguiInstance = clientApi.ModLoader.GetModSystem<ImGuiModSystem>();
			imguiInstance?.Draw += OnImGuiDraw;
#endif

			// Apply all harmony patches
			harmonyInstance.CreateClassProcessor(typeof(AnimationManager_StartAnimation0_Patch)).Patch();
			harmonyInstance.CreateClassProcessor(typeof(AnimationManager_StartAnimation1_Patch)).Patch();

			// Register hotkey's
			clientApi.Input.RegisterHotKey("dash_reset", "", GlKeys.ShiftLeft, HotkeyType.MovementControls);
			clientApi.Input.RegisterHotKey("reset", "", GlKeys.B, HotkeyType.GUIOrOtherControls);

			// Register hotkey handler's
			clientApi.Input.SetHotKeyHandler("dash_reset", OnDashReset);
			clientApi.Input.SetHotKeyHandler("reset", OnReset);

			// Register event's
			clientApi.Event.MouseDown += OnMouseDown;
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (clientApi == null) return;
			if (motionBlur == null) return;
			if (harmonyInstance == null) return;

#if DEBUG
			imguiInstance?.Show();
#endif

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityControls controls = entityPlayer.Controls;
			EntityPos transform = entityPlayer.Pos;

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
			// Play animation when player is running
			if (enableRunAnimations)
			{
				if (transform.Motion.Length() > runAnimationDeadzone)
				{
					// Compute local direction
					Vec3d localForward = transform.GetViewVector().ToVec3d();
					Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();

					// Stop animations
					if (entity.AnimManager.IsAnimationActive([sprintForwardData.Code])) entity.AnimManager.StopAnimation(sprintForwardData.Code);
					if (entity.AnimManager.IsAnimationActive([sprintBackData.Code])) entity.AnimManager.StopAnimation(sprintBackData.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardLeft90Data.Code])) entity.AnimManager.StopAnimation(strafeForwardLeft90Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardLeft45Data.Code])) entity.AnimManager.StopAnimation(strafeForwardLeft45Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardRight90Data.Code])) entity.AnimManager.StopAnimation(strafeForwardRight90Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardRight45Data.Code])) entity.AnimManager.StopAnimation(strafeForwardRight45Data.Code);

					// Compute quadrant angle of motion vector
					double x = transform.Motion.Dot(localRight);
					double y = transform.Motion.Dot(localForward);
					double angle = Math.Atan2(x, y) * MathUtil.RAD_TO_DEG;

					// Normalize to 0-360
					double degrees = angle;
					if (degrees < 0) degrees += 360.0;

					// Round to nearest 45 degrees
					Directional8 direction = (Directional8)((int)Math.Round(degrees / 45.0) % 8);

					// Start strafe animation based on quadrant angle
					if (entityPlayer.AnimManager != null)
					{
						if (entityPlayer.AnimManager.Animator != null)
						{
							switch (direction)
							{
								case Directional8.DIR8_FORWARD:
									{
										// Set runtime animation data
										sprintForwardData.AnimationSpeed = animationSpeedSprintForward;

										// Sprint forward
										if (entity.AnimManager.StartAnimation(sprintForwardData))
										{
											RunningAnimation animation = entity.AnimManager.GetAnimationState(sprintForwardData.Code);
											animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
											animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
										}

										break;
									}
								case Directional8.DIR8_FORWARD_LEFT:
									{
										// Set runtime animation data
										strafeForwardLeft45Data.AnimationSpeed = animationSpeedStrafeForwardLeft45;

										// Strafe forward left 45
										if (entity.AnimManager.StartAnimation(strafeForwardLeft45Data))
										{
											RunningAnimation animation = entity.AnimManager.GetAnimationState(strafeForwardLeft45Data.Code);
											animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
											animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
										}

										break;
									}
								case Directional8.DIR8_LEFT:
									{
										// Set runtime animation data
										strafeForwardLeft90Data.AnimationSpeed = animationSpeedStrafeForwardLeft90;

										// Strafe forward left 90
										if (entity.AnimManager.StartAnimation(strafeForwardLeft90Data))
										{
											RunningAnimation animation = entity.AnimManager.GetAnimationState(strafeForwardLeft90Data.Code);
											animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
											animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
										}

										break;
									}
								case Directional8.DIR8_BACK_LEFT:
									{
										break;
									}
								case Directional8.DIR8_BACK:
									{
										break;
									}
								case Directional8.DIR8_BACK_RIGHT:
									{
										break;
									}
								case Directional8.DIR8_RIGHT:
									{
										// Set runtime animation data
										strafeForwardRight90Data.AnimationSpeed = animationSpeedStrafeForwardRight90;

										// Strafe forward right 90
										if (entity.AnimManager.StartAnimation(strafeForwardRight90Data))
										{
											RunningAnimation animation = entity.AnimManager.GetAnimationState(strafeForwardRight90Data.Code);
											animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
											animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
										}

										break;
									}
								case Directional8.DIR8_FORWARD_RIGHT:
									{
										// Set runtime animation data
										strafeForwardRight45Data.AnimationSpeed = animationSpeedStrafeForwardRight45;

										// Strafe forward right 45
										if (entity.AnimManager.StartAnimation(strafeForwardRight45Data))
										{
											RunningAnimation animation = entity.AnimManager.GetAnimationState(strafeForwardRight45Data.Code);
											animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;
											animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;
										}

										break;
									}
							}
						}
					}
				}
				else
				{
					// Stop animations
					if (entity.AnimManager.IsAnimationActive([sprintForwardData.Code])) entity.AnimManager.StopAnimation(sprintForwardData.Code);
					if (entity.AnimManager.IsAnimationActive([sprintBackData.Code])) entity.AnimManager.StopAnimation(sprintBackData.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardLeft90Data.Code])) entity.AnimManager.StopAnimation(strafeForwardLeft90Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardLeft45Data.Code])) entity.AnimManager.StopAnimation(strafeForwardLeft45Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardRight90Data.Code])) entity.AnimManager.StopAnimation(strafeForwardRight90Data.Code);
					if (entity.AnimManager.IsAnimationActive([strafeForwardRight45Data.Code])) entity.AnimManager.StopAnimation(strafeForwardRight45Data.Code);
				}
			}

			// Apply motion blur
			if ((sequenceType == SequenceType.SEQUENCE_TYPE_NONE) || (enableMotionBlur == false))
			{
				motionBlur.BlurEnable = false;
			}
			else
			{
				motionBlur.BlurEnable = true;
				motionBlur.BlurIntensity = (float)transform.Motion.Length() * motionBlurIntensity;
			}

#if DEBUG
			if ((sequenceType != SequenceType.SEQUENCE_TYPE_NONE) && (enableLineGizmo == true))
			{
				// Track motion trajectory
				lineGizmo?.AddLine(
					(float)transform.X,
					(float)transform.Y,
					(float)transform.Z,
					(float)transform.X + (float)transform.Motion.X * 10.0F,
					(float)transform.Y + (float)transform.Motion.Y * 10.0F,
					(float)transform.Z + (float)transform.Motion.Z * 10.0F,
					ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
				);

				// Upload memory
				lineGizmo?.Commit();
			}
#endif

		}

		private void DashSequenceTick(float deltaTime)
		{
			if (clientApi == null) return;
			if (motionBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
						Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
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

#if DEBUG
						// Add start point position
						if (enableLineGizmo)
						{
							lineGizmo?.AddBox(
								(float)transform.X, (float)transform.Y, (float)transform.Z,
								0.5F, 0.5F, 0.5F,
								ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
							);
						}
#endif

						// Stop dash animations only
						if (entity.AnimManager.IsAnimationActive([dashForwardData.Code])) entity.AnimManager.StopAnimation(dashForwardData.Code);
						if (entity.AnimManager.IsAnimationActive([dashBackData.Code])) entity.AnimManager.StopAnimation(dashBackData.Code);
						if (entity.AnimManager.IsAnimationActive([dashRightData.Code])) entity.AnimManager.StopAnimation(dashRightData.Code);
						if (entity.AnimManager.IsAnimationActive([dashLeftData.Code])) entity.AnimManager.StopAnimation(dashLeftData.Code);

						// Compute quadrant angle of motion vector
						double x = transform.Motion.Dot(localRight);
						double y = transform.Motion.Dot(localForward);
						double angle = Math.Atan2(x, y) * MathUtil.RAD_TO_DEG;

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
#if DEBUG
						// Add end point position
						if (enableLineGizmo)
						{
							lineGizmo?.AddBox(
								(float)transform.X, (float)transform.Y, (float)transform.Z,
								0.5F, 0.5F, 0.5F,
								ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
							);
						}
#endif

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
					? EaseOutElastic(physicFrame) * dashHorizontalImpulseGrounded * dashDirection
					: EaseOutElastic(physicFrame) * dashHorizontalImpulseAirbourne * dashDirection;

				// Compute vertical force
				force += isDoubleDashActive
					? EaseOutCirc(physicFrame) * dashVerticalImpulseGrounded * MathUtil.WORLD_UP
					: EaseOutElastic(physicFrame) * dashVerticalImpulseAirbourne * MathUtil.WORLD_UP;

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
			if (clientApi == null) return;
			if (motionBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
						Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
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
						double angle = Math.Atan2(x, y) * MathUtil.RAD_TO_DEG;

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
				force += EaseOutElastic(physicFrame) * jumpHorizontalImpulse * jumpDirection;

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
			if (clientApi == null) return;
			if (motionBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
						Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
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
				force += EaseOutElastic(physicFrame) * attackHorizontalImpulse * attackDirection;

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

		// Pirate's Life https://easings.net/
		private float EaseInCirc(float x)
		{
			return 1.0F - (float)Math.Sqrt(1.0F - (float)Math.Pow(x, 2.0F));
		}
		private float EaseInOutElastic(float x)
		{
			float c5 = (2.0F * (float)Math.PI) / 4.5F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: x < 0.5F
				? -((float)Math.Pow(2.0F, 20.0F * x - 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F
				: ((float)Math.Pow(2.0F, -20.0F * x + 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F + 1.0F;
		}
		private float EaseOutCirc(float x)
		{
			return (float)Math.Sqrt(1.0F - (float)Math.Pow(x - 1.0F, 2.0F));
		}
		private float EaseOutElastic(float x)
		{
			float c4 = (2.0F * (float)Math.PI) / 3.0F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: (float)Math.Pow(2.0F, -10.0F * x) * (float)Math.Sin((x * 10.0F - 0.75F) * c4) + 1.0F;
		}

		private bool OnDashReset(KeyCombination keyComb)
		{
			if (enable == false) return true;
			if (clientApi == null) return true;
			if (motionBlur == null) return true;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
				clientApi.World.PlaySoundAt(dashSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);
				clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);

				// Register fixed dash recover action
				clientApi.World.RegisterCallback(_ =>
				{
					dashAllowed = true;
					clientApi.World.PlaySoundAt(dashRecoverSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);
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
						clientApi.World.PlaySoundAt(dashSound2, soundPos, 0.0, null, true, 64.0F, 1.0F);
						clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);
					}
				}
			}

			return true;
		}
		private bool OnReset(KeyCombination keyComb)
		{
			// Stop all animations
			entity.AnimManager.StopAllAnimations();

			return true;
		}
		private void OnAttackReset()
		{
			if (enable == false) return;
			if (clientApi == null) return;
			if (motionBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
				clientApi.World.PlaySoundAt(wooshSound3, soundPos, 0.0, null, true, 64.0F, 10.0F);

				// Register fixed attack recover action
				clientApi.World.RegisterCallback(_ =>
				{
					attackAllowed = true;
				}, attackCooldownMs);
			}
		}
		private void OnJumpReset()
		{
			if (enable == false) return;
			if (clientApi == null) return;
			if (motionBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
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
				clientApi.World.PlaySoundAt(wooshSound1, soundPos, 0.0, null, true, 64.0F, 1.0F);

				// Register fixed jump recover action
				clientApi.World.RegisterCallback(_ =>
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
				// Set runtime animation data
				holdWeaponCombatPassiveData.AnimationSpeed = 1.0F; // TODO

				// Start random attack animation
				entity.AnimManager.StartAnimation(holdWeaponCombatPassiveData);
				RunningAnimation animation = entity.AnimManager.GetAnimationState(holdWeaponCombatPassiveData.Code);
				animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
				animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
			}
			else
			{
				// Stop attack animation only
				if (entity.AnimManager.IsAnimationActive([holdWeaponCombatPassiveData.Code])) entity.AnimManager.StopAnimation(holdWeaponCombatPassiveData.Code);
			}
		}

		private void OnMouseDown(MouseEvent e)
		{
			if (enable == false) return;

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

		private CallbackGUIStatus OnImGuiDraw(float deltaSeconds)
		{
			ImGui.Begin("Ushigatana");

			if (ImGui.BeginTabBar("Settings", ImGuiTabBarFlags.None))
			{
				if (ImGui.BeginTabItem("General"))
				{
					ImGui.Checkbox("enable", ref enable);
					ImGui.Checkbox("enableLineGizmo", ref enableLineGizmo);
					ImGui.DragInt("dashCooldownMs", ref dashCooldownMs);
					ImGui.DragInt("jumpCooldownMs", ref jumpCooldownMs);
					ImGui.DragInt("attackCooldownMs", ref attackCooldownMs);
					ImGui.EndTabItem();
				}

				if (ImGui.BeginTabItem("Shader"))
				{
					ImGui.Checkbox("enableMotionBlur", ref enableMotionBlur);
					ImGui.DragFloat("motionBlurIntensity", ref motionBlurIntensity, 0.1F, 0.0F, 10.0F);
					ImGui.EndTabItem();
				}

				if (ImGui.BeginTabItem("Physic"))
				{
					ImGui.DragFloat("physicSpeedFactor", ref physicSpeedFactor, 0.1F, -50.0F, 50.0F);
					ImGui.DragFloat("maxVelocity", ref maxVelocity, 0.01F, -10.0F, 10.0F);
					ImGui.SeparatorText("Dash");
					ImGui.DragFloat("dashHorizontalImpulseGrounded", ref dashHorizontalImpulseGrounded, 0.1F, -10.0F, 10.0F);
					ImGui.DragFloat("dashHorizontalImpulseAirbourne", ref dashHorizontalImpulseAirbourne, 0.1F, -1.0F, 1.0F);
					ImGui.DragFloat("dashVerticalImpulseGrounded", ref dashVerticalImpulseGrounded, 0.1F, -0.1F, 0.1F);
					ImGui.DragFloat("dashVerticalImpulseAirbourne", ref dashVerticalImpulseAirbourne, 0.1F, -0.1F, 0.1F);
					ImGui.SeparatorText("Jump");
					ImGui.DragFloat("jumpHorizontalImpulse", ref jumpHorizontalImpulse, 0.1F, -10.0F, 10.0F);
					ImGui.SeparatorText("Attack");
					ImGui.DragFloat("attackHorizontalImpulse", ref attackHorizontalImpulse, 0.1F, -10.0F, 10.0F);
					ImGui.EndTabItem();
				}

				if (ImGui.BeginTabItem("Animation"))
				{
					ImGui.Checkbox("enableRunAnimations", ref enableRunAnimations);
					ImGui.SeparatorText("Animation Speed");
					ImGui.DragFloat("animationSpeedDash", ref animationSpeedDash, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedJump", ref animationSpeedJump, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedSwordHit", ref animationSpeedSwordHit, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedSwordHit2", ref animationSpeedSwordHit2, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedCleaverHit", ref animationSpeedCleaverHit, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedSprintForward", ref animationSpeedSprintForward, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedSprintBack", ref animationSpeedSprintBack, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedStrafeForwardLeft90", ref animationSpeedStrafeForwardLeft90, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedStrafeForwardRight90", ref animationSpeedStrafeForwardRight90, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedStrafeForwardLeft45", ref animationSpeedStrafeForwardLeft45, 0.1F, 0.0F, 20.0F);
					ImGui.DragFloat("animationSpeedStrafeForwardRight45", ref animationSpeedStrafeForwardRight45, 0.1F, 0.0F, 20.0F);
					ImGui.SeparatorText("Frame Counts");
					ImGui.DragInt("dashFrameCount", ref dashFrameCount);
					ImGui.DragInt("dashRetractFrameCount", ref dashRetractFrameCount);
					ImGui.DragInt("jumpFrameCount", ref jumpFrameCount);
					ImGui.DragInt("attackFrameCount", ref attackFrameCount);
					ImGui.EndTabItem();
				}

				ImGui.EndTabBar();
			}

			ImGui.End();

			return CallbackGUIStatus.DontGrabMouse;
		}
	}
	internal class TrueThirdPersonBehaviour : EntityBehavior
	{
		public static ICoreClientAPI? clientApi = null;

		public static bool enable = true;
		public static bool enableLineGizmo = false;
		public static bool enableLinearVelocity = true;
		public static bool enableAngularVelocity = true;
		public static bool enableBoneBobbing = true;
		public static bool enableRandomRotation = true;

		private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosInRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosIn");
		private static readonly AccessTools.FieldRef<Camera, Vec3d> originPosRef = AccessTools.FieldRefAccess<Camera, Vec3d>("originPos");
		private static readonly AccessTools.FieldRef<Camera, Vec3d> camTargetTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camTargetTmp");
		private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosOutTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosOutTmp");
		private static readonly AccessTools.FieldRef<Camera, EnumCameraMode> cameraModeRef = AccessTools.FieldRefAccess<Camera, EnumCameraMode>("CameraMode");

		private static LineGizmo? lineGizmo = null;

		private static Vec3f cameraRootOffset = new(-0.17F, 0.03F, -0.33F);
		private static float bobbingAmount = 4.0F;
		private static float yawRotationAmount = 0.012F;
		private static float pitchRotationAmount = 0.02F;

		private static Vec3d linearVelocity = new(0, 0, 0);
		private static double yawVelocity = 0.0;
		private static double pitchVelocity = 0.0;

		private static float linearStiffness = 22.0F;
		private static float angularStiffness = 22.0F;
		private static float linearDamping = 8.0F;
		private static float angularDamping = 8.0F;

		private static double lastBoneY = 0.0;
		private static double yawRotationTime = 0.0;
		private static double pitchRotationTime = 0.0;

		[HarmonyPatch(typeof(Camera), nameof(Camera.Update), [typeof(float), typeof(AABBIntersectionTest)])]
		internal class Camera_Update_Patch
		{
			public static bool Prefix(Camera __instance, float deltaTime, AABBIntersectionTest intersectionTester)
			{
				if (enable == false) return true; // Don't skip the original method
				if (clientApi == null) return true; // Don't skip the original method

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				// Set third person mode forever
				cameraModeRef(__instance) = EnumCameraMode.ThirdPerson;

				// Compute local direction
				Vec3d localForward = transform.GetViewVector().ToVec3d();
				Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
				Vec3d localUp = localForward.Cross(localRight);

				// Compute target camera position
				Vec3d targetPosition = cameraRootOffset.ToVec3d();
				targetPosition = MathUtil.RotateAroundAxis(targetPosition, MathUtil.WORLD_RIGHT, transform.Pitch);
				targetPosition = MathUtil.RotateAroundAxis(targetPosition, MathUtil.WORLD_UP, transform.Yaw);

				// Linear interpolation
				Vec3d linearDisplacement = targetPosition - __instance.OriginPosition;
				Vec3d linearAcceleration = linearDisplacement * linearStiffness - linearVelocity * linearDamping;
				linearVelocity += linearAcceleration * deltaTime;
				Vec3d physicCameraPosition = targetPosition + linearVelocity;

				// Angular interpolation
				double targetYaw = transform.Yaw;
				double targetPitch = transform.Pitch;
				double yawDisplacement = MathUtil.AngleDifference(targetYaw, __instance.Yaw);
				double pitchDisplacement = targetPitch - __instance.Pitch;
				double yawAcceleration = yawDisplacement * angularStiffness - yawVelocity * angularDamping;
				double pitchAcceleration = pitchDisplacement * angularStiffness - pitchVelocity * angularDamping;
				yawVelocity += yawAcceleration * deltaTime;
				pitchVelocity += pitchAcceleration * deltaTime;
				double physicCameraYaw = targetYaw + yawVelocity;
				double physicCameraPitch = targetPitch + pitchVelocity;

				// Apply bone bobbing
				double deltaY = 0.0;
				if (enableBoneBobbing)
				{
					if (entityPlayer.AnimManager != null)
					{
						if (entityPlayer.AnimManager.Animator != null)
						{
							ElementPose pose = entityPlayer.AnimManager.Animator.GetPosebyName("UpperTorso");
					
							double boneX = pose.AnimModelMatrix[12];
							double boneY = pose.AnimModelMatrix[13];
							double boneZ = pose.AnimModelMatrix[14];
					
							deltaY = (boneY - lastBoneY);
							lastBoneY = boneY;
						}
					}
				}

				// Apply random camera rotation
				double deltaYaw = 0.0;
				double deltaPitch = 0.0;
				if (enableRandomRotation)
				{
					deltaYaw = Math.Sin(yawRotationTime) * yawRotationAmount;
					deltaPitch = Math.Cos(pitchRotationTime) * pitchRotationAmount;
					yawRotationTime += deltaTime;
					pitchRotationTime += deltaTime;
				}

				// Apply our camera position
				__instance.OriginPosition = enableLinearVelocity
					? physicCameraPosition
					: targetPosition; // TODO: * deltaTime
				__instance.OriginPosition.Y += deltaY * bobbingAmount;
				__instance.Yaw = enableAngularVelocity
					? physicCameraYaw + deltaYaw
					: targetYaw; // TODO: * deltaTime
				__instance.Pitch = enableAngularVelocity
					? physicCameraPitch + deltaPitch
					: targetPitch; // TODO: * deltaTime
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
		[HarmonyPatch(typeof(Camera), nameof(Camera.GetCameraMatrix), [typeof(Vec3d), typeof(Vec3d), typeof(double), typeof(double), typeof(AABBIntersectionTest)])]
		internal class Camera_GetCameraMatrix_Patch
		{
			public static bool Prefix(Camera __instance, Vec3d camEyePosIn, Vec3d worldPos, double yaw, double pitch, AABBIntersectionTest intersectionTester, ref double[] __result)
			{
				if (enable == false) return true; // Don't skip the original method
				if (clientApi == null) return true; // Don't skip the original method

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				// Compute local direction
				Vec3d localForward = __instance.forwardVec;
				Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
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
				Vec3d up = MathUtil.WORLD_UP;
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

		private Harmony? harmonyInstance = null;
		private ImGuiModSystem? imguiInstance = null;

		public TrueThirdPersonBehaviour(Entity entity) : base(entity)
		{
			if (clientApi == null) return;

			// TODO: fix api injection
			harmonyInstance = new("Vintagestory.Client.NoObf");
#if DEBUG
			lineGizmo = new(clientApi, 1000);

			imguiInstance = clientApi.ModLoader.GetModSystem<ImGuiModSystem>();
			imguiInstance?.Draw += OnImGuiDraw;
#endif

			// Apply all harmony patches
			harmonyInstance.CreateClassProcessor(typeof(Camera_Update_Patch)).Patch();
			// harmonyInstance.CreateClassProcessor(typeof(Camera_GetCameraMatrix_Patch)).Patch();
		}

		public override string PropertyName()
		{
			return "TrueThirdPersonBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (clientApi == null) return;
			if (harmonyInstance == null) return;

#if DEBUG
			imguiInstance?.Show();
#endif
		}

		private CallbackGUIStatus OnImGuiDraw(float deltaSeconds)
		{
			ImGui.Begin("TrueThirdPerson");

			if (ImGui.BeginTabBar("Settings", ImGuiTabBarFlags.None))
			{
				if (ImGui.BeginTabItem("General"))
				{
					ImGui.Checkbox("enable", ref enable);
					ImGui.Checkbox("enableLineGizmo", ref enableLineGizmo);
					ImGui.SeparatorText("Camera");
					Vector3 p = new(cameraRootOffset.X, cameraRootOffset.Y, cameraRootOffset.Z);
					if (ImGui.DragFloat3("cameraRootOffset", ref p, 0.01F)) cameraRootOffset.Set(p.X, p.Y, p.Z);
					ImGui.EndTabItem();
				}

				if (ImGui.BeginTabItem("Physic"))
				{
					ImGui.Checkbox("enableLinearVelocity", ref enableLinearVelocity);
					ImGui.Checkbox("enableAngularVelocity", ref enableAngularVelocity);
					ImGui.Checkbox("enableBoneBobbing", ref enableBoneBobbing);
					ImGui.Checkbox("enableRandomRotation", ref enableRandomRotation);
					ImGui.DragFloat("bobbingAmount", ref bobbingAmount, 0.1F);
					ImGui.DragFloat("bobbingAmountYaw", ref yawRotationAmount, 0.1F);
					ImGui.DragFloat("bobbingAmountPitch", ref pitchRotationAmount, 0.1F);
					ImGui.DragFloat("motionStiffness", ref linearStiffness, 0.1F);
					ImGui.DragFloat("angularStiffness", ref angularStiffness, 0.1F);
					ImGui.DragFloat("motionDamping", ref linearDamping, 0.1F);
					ImGui.DragFloat("angularDamping", ref angularDamping, 0.1F);
					ImGui.EndTabItem();
				}

				ImGui.EndTabBar();
			}

			ImGui.End();

			return CallbackGUIStatus.DontGrabMouse;
		}
	}
}
