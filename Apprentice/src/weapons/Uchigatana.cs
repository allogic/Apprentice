using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VSImGui.Debug;

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

		private static bool enableAnimationWhitelist = false;

		private static IList<string> whitelistedAnimationCodes = [
			// Movement
			"dash-forward",
			"dash-back",
			"dash-left",
			"dash-right",

			// Combat
			"hold-weapon-combat-passive",

			// Game
			"bowaimlong",
		];

		[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(AnimationMetaData)])]
		class AnimationManager_StartAnimation0_Patch
		{
			public static bool Prefix(AnimationManager __instance, AnimationMetaData animdata)
			{
				if (enableAnimationWhitelist)
				{
					if (whitelistedAnimationCodes.Contains(animdata.Code))
					{
						return true;
					}

					return false;
				}

				return true; // Don't skip the original method
			}
		}
		[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(string)])]
		class AnimationManager_StartAnimation1_Patch
		{
			public static bool Prefix(AnimationManager __instance, string configCode)
			{
				if (enableAnimationWhitelist)
				{
					if (whitelistedAnimationCodes.Contains(configCode))
					{
						return true;
					}

					return false;
				}

				return true; // Don't skip the original method
			}
		}

		private readonly AssetLocation dashSound1 = new("apprentice", "sounds/dash-1");
		private readonly AssetLocation dashSound2 = new("apprentice", "sounds/dash-2");
		private readonly AssetLocation dashRecoverSound1 = new("apprentice", "sounds/dash-recover-1");
		private readonly AssetLocation dashRecoverSound2 = new("apprentice", "sounds/dash-recover-2");
		private readonly AssetLocation ushigatanaDashSound = new("apprentice", "sounds/ushigatana-dash");

		private enum SequenceState
		{
			SEQUENCE_STATE_IDLE,
			SEQUENCE_STATE_START,
			SEQUENCE_STATE_DASH,
			SEQUENCE_STATE_RETRACT,
			SEQUENCE_STATE_STOP,
		}

		private SequenceState sequenceState = SequenceState.SEQUENCE_STATE_IDLE;

		private LineGizmo? lineGizmo = null;
		private DashBlur? dashBlur = null;
		private Harmony? harmonyInstance = null;

		private bool isPhysicActive = false;
		private bool isDoubleDashActive = false;
		private bool dashAllowed = true;
		private bool doubleDashAllowed = true;

		private float physicSpeedFactor = 8.356F;
		private float horizontalImpulseGrounded = 1.0F;
		private float horizontalImpulseAirbourne = 0.036F;
		private float verticalImpulseGrounded = 0.02F;
		private float verticalImpulseAirbourne = 0.04F;
		private float airbourneDashDirectionSpeedFactor = 1.0F;

		private float animationSpeedDashForward = 2.5F;
		private float animationSpeedDashBack = 2.5F;
		private float animationSpeedDashLeft = 2.5F;
		private float animationSpeedDashRight = 2.5F;

		private float motionBlurIntensity = 2.75F;

		private int dashCooldownMs = 1500;

		private float physicFrame = 0.0F;
		private int animationFrame = 0;

		private int dashForwardFrameCount = 18;
		private int dashForwardRetractFrameCount = 0;

		private bool attackToggle = false;

		private Vec3d dashDirection = new(0, 0, 0);

		// Movement Animations
		private AnimationMetaData dashForwardData = new AnimationMetaData()
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
		private AnimationMetaData dashBackData = new AnimationMetaData()
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
		private AnimationMetaData dashLeftData = new AnimationMetaData()
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
		private AnimationMetaData dashRightData = new AnimationMetaData()
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

		// Combat Animations
		private AnimationMetaData holdWeaponCombatPassiveData = new AnimationMetaData()
		{
			Animation = "hold-weapon-combat-passive",
			Code = "hold-weapon-combat-passive",
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

		// Game Animations
		private AnimationMetaData bowAimLongData = new AnimationMetaData()
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

		public UchigatanaDashBehaviour(Entity entity) : base(entity)
		{
			if (clientApi == null) return;

			// TODO: fix api injection
			dashBlur = new(clientApi);
			harmonyInstance = new("Vintagestory.API.Common");
#if DEBUG
			lineGizmo = new(clientApi, 1000);
#endif

			// Apply all harmony patches
			harmonyInstance.CreateClassProcessor(typeof(AnimationManager_StartAnimation0_Patch)).Patch();
			harmonyInstance.CreateClassProcessor(typeof(AnimationManager_StartAnimation1_Patch)).Patch();

			// Register hotkey's
			clientApi.Event.MouseDown += OnMouseDown;
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (clientApi == null) return;
			if (lineGizmo == null) return;
			if (dashBlur == null) return;
			if (harmonyInstance == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityControls controls = entityPlayer.Controls;
			EntityPos transform = entityPlayer.Pos;

#if true
			DebugWidgets.IntSlider("Ushigatana", "General", "dashCooldownMs", 0, 5000, () => { return dashCooldownMs; }, (v) => { dashCooldownMs = v; });

			DebugWidgets.FloatSlider("Ushigatana", "Shader", "motionBlurIntensity", 0.0F, 10.0F, () => { return motionBlurIntensity; }, (v) => { motionBlurIntensity = v; });

			DebugWidgets.FloatSlider("Ushigatana", "Physic", "physicSpeedFactor", -50.0F, 50.0F, () => { return physicSpeedFactor; }, (v) => { physicSpeedFactor = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "horizontalImpulseGrounded", -10.0F, 10.0F, () => { return horizontalImpulseGrounded; }, (v) => { horizontalImpulseGrounded = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "horizontalImpulseAirbourne", -1.0F, 1.0F, () => { return horizontalImpulseAirbourne; }, (v) => { horizontalImpulseAirbourne = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "verticalImpulseGrounded", -0.1F, 0.1F, () => { return verticalImpulseGrounded; }, (v) => { verticalImpulseGrounded = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "verticalImpulseAirbourne", -0.1F, 0.1F, () => { return verticalImpulseAirbourne; }, (v) => { verticalImpulseAirbourne = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "airbourneDashDirectionSpeedFactor", -10.0F, 10.0F, () => { return airbourneDashDirectionSpeedFactor; }, (v) => { airbourneDashDirectionSpeedFactor = v; });

			DebugWidgets.IntSlider("Ushigatana", "Animation", "dashForwardFrameFrames", 0, 100, () => { return dashForwardFrameCount; }, (v) => { dashForwardFrameCount = v; });
			DebugWidgets.IntSlider("Ushigatana", "Animation", "dashForwardRetractFrames", 0, 100, () => { return dashForwardRetractFrameCount; }, (v) => { dashForwardRetractFrameCount = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Animation", "animationSpeedDashForward", 0.0F, 20.0F, () => { return animationSpeedDashForward; }, (v) => { animationSpeedDashForward = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Animation", "animationSpeedDashBack", 0.0F, 20.0F, () => { return animationSpeedDashBack; }, (v) => { animationSpeedDashBack = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Animation", "animationSpeedDashLeft", 0.0F, 20.0F, () => { return animationSpeedDashLeft; }, (v) => { animationSpeedDashLeft = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Animation", "animationSpeedDashRight", 0.0F, 20.0F, () => { return animationSpeedDashRight; }, (v) => { animationSpeedDashRight = v; });
#endif

			// TODO: need adjustments..
			// Check if we are allowed to execute double jump and
			// we havent touched the ground since the start of our dash
			// if (isDoubleDashActive)
			// {
			// 	if (entityPlayer.OnGround)
			// 	{
			// 		if (groundedWhileOnCooldown == false)
			// 		{
			// 			groundedWhileOnCooldown = true;
			// 
			// 			// Goto idle instead
			// 			sequenceState = SequenceState.SEQUENCE_STATE_STOP;
			// 		}
			// 	}
			// }

			switch (sequenceState)
			{
				case SequenceState.SEQUENCE_STATE_IDLE:
					{
						break;
					}
				case SequenceState.SEQUENCE_STATE_START:
					{
						// Reset frame counter
						physicFrame = 0.0F;
						animationFrame = 0;

						// Compute local direction
						Vec3d localForward = transform.GetViewVector().ToVec3d();
						Vec3d localBack = localForward.Clone().Mul(-1);
						Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
						Vec3d localLeft = localRight.Clone().Mul(-1);

						if (isDoubleDashActive)
						{
							// Reset dash direction
							dashDirection = Vec3d.Zero;

							// Apply local input direction
							if (controls.Forward) dashDirection += airbourneDashDirectionSpeedFactor * localForward;
							if (controls.Backward) dashDirection += airbourneDashDirectionSpeedFactor * localBack;
							if (controls.Left) dashDirection += airbourneDashDirectionSpeedFactor * localRight;
							if (controls.Right) dashDirection += airbourneDashDirectionSpeedFactor * localLeft;

							// Normalize direction
							if (dashDirection.LengthSq() > 0)
							{
								dashDirection.Normalize();
							}

							// Reset up direction
							dashDirection.Y = 0.0F;
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
							dashDirection.Y = 0.0F;

							// Normalize direction
							if (dashDirection.LengthSq() > 0)
							{
								dashDirection.Normalize();
							}

							// Reset up direction
							dashDirection.Y = 0.0F;
						}

#if DEBUG
						// Add start point position
						lineGizmo.AddBox(
							(float)transform.X, (float)transform.Y, (float)transform.Z,
							0.5F, 0.5F, 0.5F,
							ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
						);
#endif

						// Enable whitelist in the original animation manager
						enableAnimationWhitelist = true;

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
							dashForwardData.AnimationSpeed = animationSpeedDashForward;

							// Dash forward
							entity.AnimManager.StartAnimation(dashForwardData);
							RunningAnimation animation = entity.AnimManager.GetAnimationState(dashForwardData.Code);
							animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else if ((angle > 45.0F) && (angle < 135.0F))
						{
							// Set runtime animation data
							dashLeftData.AnimationSpeed = animationSpeedDashLeft;

							// Dash left
							entity.AnimManager.StartAnimation(dashLeftData);
							RunningAnimation animation = entity.AnimManager.GetAnimationState(dashLeftData.Code);
							animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else if ((angle < -45.0F) && (angle > -135.0F))
						{
							// Set runtime animation data
							dashRightData.AnimationSpeed = animationSpeedDashRight;

							// Dash right
							entity.AnimManager.StartAnimation(dashRightData);
							RunningAnimation animation = entity.AnimManager.GetAnimationState(dashRightData.Code);
							animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else
						{
							// Set runtime animation data
							dashBackData.AnimationSpeed = animationSpeedDashBack;

							// Dash back
							entity.AnimManager.StartAnimation(dashBackData);
							RunningAnimation animation = entity.AnimManager.GetAnimationState(dashBackData.Code);
							animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}

						sequenceState = SequenceState.SEQUENCE_STATE_DASH;

						break;
					}
				case SequenceState.SEQUENCE_STATE_DASH:
					{
						// Check exit condition
						if (animationFrame >= dashForwardFrameCount)
						{
							animationFrame = 0;

							sequenceState = SequenceState.SEQUENCE_STATE_RETRACT;

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
						}

						// Increment animation frame
						animationFrame++;

						break;
					}
				case SequenceState.SEQUENCE_STATE_RETRACT:
					{
						// Check exit condition
						if ((animationFrame >= dashForwardRetractFrameCount) || (entityPlayer.OnGround))
						{
							animationFrame = 0;

							sequenceState = SequenceState.SEQUENCE_STATE_STOP;

							// Stop all animations
							// entity.AnimManager.StopAllAnimations();
						}

						// Increment animation frame
						animationFrame++;

						break;
					}
				case SequenceState.SEQUENCE_STATE_STOP:
					{
						sequenceState = SequenceState.SEQUENCE_STATE_IDLE;

						// Disable motion blur
						dashBlur.BlurEnable = false;

						// Disable whitelist in the original animation manager
						// enableAnimationWhitelist = false;

#if DEBUG
						// Add end point position
						lineGizmo.AddBox(
							(float)transform.X, (float)transform.Y, (float)transform.Z,
							0.5F, 0.5F, 0.5F,
							ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
						);
#endif

						break;
					}
			}

			// Apply some physics
			if (isPhysicActive)
			{
				Vec3d force = Vec3d.Zero;

				// Compute horizontal force
				force += entityPlayer.OnGround
					? EaseOutElastic(physicFrame) * horizontalImpulseGrounded * dashDirection
					: EaseOutElastic(physicFrame) * horizontalImpulseAirbourne * dashDirection;

				// Compute vertical force
				force += isDoubleDashActive
					? EaseOutCirc(physicFrame) * verticalImpulseGrounded * MathUtil.WORLD_UP
					: EaseOutElastic(physicFrame) * verticalImpulseAirbourne * MathUtil.WORLD_UP;

				// Apply force
				transform.Motion.Add(force);

				// Advance animation
				physicFrame += physicSpeedFactor * deltaTime;
				if (physicFrame >= 1.0F)
				{
					isPhysicActive = false;
				}
			}

			// Disable controls while in dash (TODO: Revalidate this..)
			if (sequenceState != SequenceState.SEQUENCE_STATE_IDLE)
			{
				controls.Forward = false;
				controls.Backward = false;
				controls.Left = false;
				controls.Right = false;
			}

			// Apply blur intensity based on motion vector
			if (sequenceState != SequenceState.SEQUENCE_STATE_IDLE)
			{
				dashBlur.BlurIntensity = (float)transform.Motion.Length() * motionBlurIntensity;
			}

#if DEBUG
			if (sequenceState != SequenceState.SEQUENCE_STATE_IDLE)
			{
				// Track motion trajectory
				lineGizmo.AddLine(
					(float)transform.X,
					(float)transform.Y,
					(float)transform.Z,
					(float)transform.X + (float)transform.Motion.X * 10.0F,
					(float)transform.Y + (float)transform.Motion.Y * 10.0F,
					(float)transform.Z + (float)transform.Motion.Z * 10.0F,
					ColorUtil.ToRgba(0xFF, 0xFF, 0xFF, 0xFF)
				);

				// Upload memory
				lineGizmo.Commit();
			}
#endif

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

		private void OnDashReset()
		{
			if (clientApi == null) return;
			if (dashBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityPos entityPos = entityPlayer.Pos;
			BlockPos soundPos = new(entityPlayer.Pos.XYZInt, 0);

			// Check for dashes
			if (dashAllowed)
			{
				// Reset state
				isPhysicActive = true;
				isDoubleDashActive = false;
				dashAllowed = false;
				doubleDashAllowed = true;

				// Enable sequence
				sequenceState = SequenceState.SEQUENCE_STATE_START;

				// Enable motion blur
				dashBlur.BlurEnable = true;

				// Play dash dounds
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
					if (sequenceState == SequenceState.SEQUENCE_STATE_IDLE)
					{
						// Reset state
						isPhysicActive = true;
						isDoubleDashActive = true;
						doubleDashAllowed = false;

						// Enable sequence
						sequenceState = SequenceState.SEQUENCE_STATE_START;

						// Enable motion blur
						dashBlur.BlurEnable = true;

						// Play dash dounds
						clientApi.World.PlaySoundAt(dashSound2, soundPos, 0.0, null, true, 64.0F, 1.0F);
						clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);
					}
				}
			}
		}
		private void OnAttackReset()
		{
			if (clientApi == null) return;
			if (dashBlur == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityPos entityPos = entityPlayer.Pos;
			BlockPos soundPos = new(entityPlayer.Pos.XYZInt, 0);

			attackToggle = !attackToggle;

			if (attackToggle)
			{
				// Start attack windup
				entity.AnimManager.StartAnimation(holdWeaponCombatPassiveData);
				RunningAnimation animation = entity.AnimManager.GetAnimationState(holdWeaponCombatPassiveData.Code);
				animation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
				animation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
			}
			else
			{
				entity.AnimManager.StopAnimation(holdWeaponCombatPassiveData.Code);
			}
		}

		private void OnMouseDown(MouseEvent e)
		{
			if (e.Button == EnumMouseButton.Left)
			{
				OnAttackReset();
			}
			else if (e.Button == EnumMouseButton.Right)
			{
				OnDashReset();
			}

			e.Handled = true;
		}
	}
	internal class TrueThirdPersonBehaviour : EntityBehavior
	{
		public static ICoreClientAPI? clientApi = null;

		private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosInRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosIn");
		private static readonly AccessTools.FieldRef<Camera, Vec3d> originPosRef = AccessTools.FieldRefAccess<Camera, Vec3d>("originPos");
		private static readonly AccessTools.FieldRef<Camera, Vec3d> camEyePosOutTmpRef = AccessTools.FieldRefAccess<Camera, Vec3d>("camEyePosOutTmp");
		private static readonly AccessTools.FieldRef<Camera, EnumCameraMode> cameraModeRef = AccessTools.FieldRefAccess<Camera, EnumCameraMode>("CameraMode");

		private static LineGizmo? lineGizmo = null;

		private static Vec3f cameraRootPosition = new(-0.5F, 0.0F, -1.5F);
		private static Vec3f cameraRootRotation = new(0, 0, 0);

		[HarmonyPatch(typeof(Camera), nameof(Camera.Update), [typeof(float), typeof(AABBIntersectionTest)])]
		internal class Camera_Update_Patch
		{
			public static bool Prefix(Camera __instance, float deltaTime, AABBIntersectionTest intersectionTester)
			{
				if (clientApi == null) return true; // Don't skip the original method
				if (lineGizmo == null) return true; // Don't skip the original method

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				// Set third person mode forever
				cameraModeRef(__instance) = EnumCameraMode.ThirdPerson;

				// Compute local direction
				Vec3d localForward = transform.GetViewVector().ToVec3d();
				Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
				Vec3d localUp = localForward.Cross(localRight);

				// Compute local offset
				Vec3d localOffset = cameraRootPosition.ToVec3d();
				localOffset = MathUtil.RotateAroundAxis(localOffset, MathUtil.WORLD_RIGHT, transform.Pitch);
				localOffset = MathUtil.RotateAroundAxis(localOffset, MathUtil.WORLD_UP, transform.Yaw);
				//localOffset = MathUtil.RotateAroundAxis(localOffset, localForward, transform.Roll);

#if false
				lineGizmo.Reset();

				// Draw local right
				lineGizmo.AddLine(
					(float)transform.X,
					(float)transform.Y,
					(float)transform.Z,
					(float)transform.X + (float)localRight.X * 10.0F,
					(float)transform.Y + (float)localRight.Y * 10.0F,
					(float)transform.Z + (float)localRight.Z * 10.0F,
					ColorUtil.ToRgba(0xFF, 0xFF, 0x0, 0x0)
				);

				// Draw local up
				lineGizmo.AddLine(
					(float)transform.X,
					(float)transform.Y,
					(float)transform.Z,
					(float)transform.X + (float)localUp.X * 10.0F,
					(float)transform.Y + (float)localUp.Y * 10.0F,
					(float)transform.Z + (float)localUp.Z * 10.0F,
					ColorUtil.ToRgba(0xFF, 0x0, 0xFF, 0x0)
				);

				// Draw local forward
				lineGizmo.AddLine(
					(float)transform.X,
					(float)transform.Y,
					(float)transform.Z,
					(float)transform.X + (float)localForward.X * 10.0F,
					(float)transform.Y + (float)localForward.Y * 10.0F,
					(float)transform.Z + (float)localForward.Z * 10.0F,
					ColorUtil.ToRgba(0xFF, 0x0, 0x0, 0xFF)
				);

				lineGizmo.Commit();
#endif

				// Apply our offset
				__instance.OriginPosition = localOffset;
				__instance.CameraMatrix = __instance.GetCameraMatrix(camEyePosInRef(__instance), camEyePosInRef(__instance), __instance.Yaw, __instance.Pitch, intersectionTester);
				__instance.CameraEyePos.Set(camEyePosOutTmpRef(__instance));
				__instance.CameraMatrixOrigin = __instance.GetCameraMatrix(originPosRef(__instance), camEyePosInRef(__instance), __instance.Yaw, __instance.Pitch, intersectionTester);

				double[] cameraMatrixOrigin = __instance.CameraMatrixOrigin;
				double[] cameraMatrixOrigin2 = __instance.CameraMatrixOrigin;

				// Compute rolled matrix
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
				if (clientApi == null) return true; // Don't skip the original method

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				// Compute local direction
				Vec3d localForward = transform.GetViewVector().ToVec3d();
				Vec3d localRight = MathUtil.WORLD_UP.Cross(localForward).Normalize();
				Vec3d localUp = localForward.Cross(localRight);

				// Compute camera position
				Vec3d eye = camEyePosIn;
				// eye += localRight * cameraOffset.X;
				// eye += localUp * cameraOffset.Y;
				// eye += localForward * cameraOffset.Z;
				Vec3d up = MathUtil.WORLD_UP;
				Vec3d center = eye + entityPlayer.Pos.GetViewVector().ToVec3d();

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

		public TrueThirdPersonBehaviour(Entity entity) : base(entity)
		{
			if (clientApi == null) return;

			// TODO: fix api injection
			harmonyInstance = new("Vintagestory.Client.NoObf");
#if DEBUG
			lineGizmo = new(clientApi, 1000);
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

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityControls controls = entityPlayer.Controls;
			EntityPos transform = entityPlayer.Pos;

#if true
			DebugWidgets.Float3Drag("TrueThirdPerson", "Camera", "cameraRootPosition", () => { return cameraRootPosition; }, (v) => { cameraRootPosition = v; });
			DebugWidgets.Float3Drag("TrueThirdPerson", "Camera", "cameraRootRotation", () => { return cameraRootRotation; }, (v) => { cameraRootRotation = v; });
#endif
		}
	}
}
