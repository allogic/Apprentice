using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using VSImGui.Debug;

namespace Apprentice.Weapon
{
	internal class UshigatanaDialog : GuiDialog
	{
		public override string ToggleKeyCombinationCode => "ushigatana_dialog";

		public UshigatanaDialog(ICoreClientAPI api) : base(api)
		{
			ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
			bgBounds.BothSizing = ElementSizing.FitToChildren;
			bgBounds.WithChildren(ElementBounds.Fixed(10, 10, 250, 800));

			// Create the dialog
			SingleComposer = capi.Gui.CreateCompo("Ushigatana", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftTop))
				.AddShadedDialogBG(bgBounds)
				.AddDialogTitleBar("Ushigatana Controls", () => { TryClose(); })
				.AddSlider(value => { return true; }, ElementBounds.Fixed(10, 60, 230, 50))
				.AddSlider(value => { return true; }, ElementBounds.Fixed(10, 120, 230, 50))
				.AddSlider(value => { return true; }, ElementBounds.Fixed(10, 180, 230, 50))
				.Compose();
			//.AddStaticText("This", CairoFont.WhiteDetailText(), ElementBounds.Fixed(10, 60, 230, 50))
			//.AddToggleButton("Hello", CairoFont.WhiteDetailText(), value => { }, ElementBounds.Fixed(0, 40, 250, 800))
			//.AddToggleButton("Hello", CairoFont.WhiteDetailText(), value => { }, ElementBounds.Fixed(0, 40, 250, 800))
			//.AddToggleButton("Hello", CairoFont.WhiteDetailText(), value => { }, ElementBounds.Fixed(0, 40, 250, 800))
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

	/*
	[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(AnimationMetaData)])]
	public static class AnimationManager_StartAnimation_Overload0
	{
		public static class AnimationBlocker
		{
			private static readonly HashSet<AnimationManager> blocked = new();

			public static void Block(AnimationManager manager) { blocked.Add(manager); }
			public static void Unblock(AnimationManager manager) { blocked.Remove(manager); }
			public static bool IsBlocked(AnimationManager manager) { return blocked.Contains(manager); }
		}

		public static bool Prefix(AnimationManager instance, AnimationMetaData animdata)
		{
			if (AnimationBlocker.IsBlocked(instance))
			{
				return false;
			}

			return true;
		}
	}
	[HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.StartAnimation), [typeof(string)])]
	public static class AnimationManager_StartAnimation_Overload1
	{
		public static class AnimationBlocker
		{
			private static readonly HashSet<AnimationManager> blocked = new();

			public static void Block(AnimationManager manager) { blocked.Add(manager); }
			public static void Unblock(AnimationManager manager) { blocked.Remove(manager); }
			public static bool IsBlocked(AnimationManager manager) { return blocked.Contains(manager); }
		}

		public static bool Prefix(AnimationManager instance, string configCode)
		{
			if (AnimationBlocker.IsBlocked(instance))
			{
				return false;
			}

			return true;
		}
	}
	*/

	internal class UchigatanaDashBehaviour : EntityBehavior
	{
		private static bool enableAnimationWhitelist = false;

		private static IList<string> whitelistedAnimationCodes = [
			"dash-forward",
			"dash-back",
			"dash-left",
			"dash-right",
		];

		internal class UshigatanaAnimationManager
		{
			public static bool StartAnimation(AnimationMetaData animdata)
			{
				if (enableAnimationWhitelist)
				{
					if (whitelistedAnimationCodes.Contains(animdata.Code))
					{
						return true;
					}
				}

				return false;
			}
			public static bool StartAnimation(string configCode)
			{
				if (enableAnimationWhitelist)
				{
					if (whitelistedAnimationCodes.Contains(configCode))
					{
						return true;
					}
				}

				return false;
			}
		}

		private readonly AssetLocation dashSound1 = new("apprentice", "sounds/dash-1");
		private readonly AssetLocation dashSound2 = new("apprentice", "sounds/dash-2");
		private readonly AssetLocation dashRecoverSound1 = new("apprentice", "sounds/dash-recover-1");
		private readonly AssetLocation dashRecoverSound2 = new("apprentice", "sounds/dash-recover-2");
		private readonly AssetLocation ushigatanaDashSound = new("apprentice", "sounds/ushigatana-dash");

		private readonly ICoreClientAPI clientApi;
		private readonly IInputAPI inputApi;

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
		private UshigatanaDialog? debugDialog = null;
		private Harmony? harmonyInstance = null;

		private MethodInfo? originalStartAnimationOverload0 = null;
		private MethodInfo? originalStartAnimationOverload1 = null;
		private MethodInfo? patchedStartAnimationOverload0 = null;
		private MethodInfo? patchedStartAnimationOverload1 = null;

		private bool isPhysicActive = false;
		private bool isDoubleDashActive = false;
		private bool dashAllowed = true;
		private bool doubleDashAllowed = true;
		private bool groundedWhileOnCooldown = false;

		private float physicSpeedFactor = 8.356F;
		private float horizontalImpulseGrounded = 1.0F;
		private float horizontalImpulseAirbourne = 0.036F;
		private float verticalImpulseFirstDash = 0.025F;
		private float verticalImpulseSecondDash = 0.04F;
		private float airbourneDashDirectionSpeedFactor = 1.0F;

		private int dashCooldownMs = 1500;

		private float physicFrame = 0.0F;
		private int animationFrame = 0;

		private int dashForwardFrameCount = 18;
		private int dashForwardRetractFrameCount = 42;

		private Vec3d initialDashDirection = new(0, 0, 0);
		private Vec3d dashDirection = new(0, 0, 0);
		private Vec3d worldUp = new(0, 1, 0);

		private AnimationMetaData dashForwardData = new AnimationMetaData()
		{
			Animation = "dash-forward",
			Code = "dash-forward",
			Weight = 1.0F,
			SupressDefaultAnimation = true,
			ClientSide = true,
			AnimationSpeed = 4.0F,
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
			AnimationSpeed = 4.0F,
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
			AnimationSpeed = 4.0F,
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
			AnimationSpeed = 4.0F,
			BlendMode = EnumAnimationBlendMode.Add,
			ElementWeight = {
				{ "root", 1.0F },
			},
			ElementBlendMode = {
				{ "root", EnumAnimationBlendMode.Add },
			},
		};

		private AnimationMetaData dashForwardRetractData = new AnimationMetaData()
		{
			Animation = "dash-forward-retract",
			Code = "dash-forward-retract",
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

		// private AnimationMetaData ushigatanaSwingRightToLeftData = new AnimationMetaData()
		// {
		// 	Animation = "ushigatana-swing-right-to-left",
		// 	Code = "ushigatana-swing-right-to-left",
		// 	Weight = 1.0F,
		// 	SupressDefaultAnimation = true,
		// 	ClientSide = true,
		// 	AnimationSpeed = 1.0F,
		// 	BlendMode = EnumAnimationBlendMode.AddAverage,
		// 	ElementWeight = {
		// 		{ "UpperTorso", 20.0F },
		// 	},
		// 	ElementBlendMode = {
		// 		{ "UpperTorso", EnumAnimationBlendMode.AddAverage },
		// 	},
		// };

		public UchigatanaDashBehaviour(ICoreClientAPI api, Entity entity) : base(entity)
		{
			clientApi = api;
			inputApi = api.Input;

			lineGizmo = new(api, 1000);
			dashBlur = new(api);
			debugDialog = new(api); // TODO: refactor me..
			harmonyInstance = new("Vintagestory.API.Common");

			// Find original functions that could cause problems
			originalStartAnimationOverload0 = typeof(AnimationManager).GetMethod("StartAnimation", [typeof(AnimationMetaData)]);
			originalStartAnimationOverload1 = typeof(AnimationManager).GetMethod("StartAnimation", [typeof(string)]);
			patchedStartAnimationOverload0 = typeof(UshigatanaAnimationManager).GetMethod("StartAnimation", [typeof(AnimationMetaData)]);
			patchedStartAnimationOverload1 = typeof(UshigatanaAnimationManager).GetMethod("StartAnimation", [typeof(string)]);

			// Enable animation patches
			harmonyInstance.Patch(originalStartAnimationOverload0, patchedStartAnimationOverload0);
			harmonyInstance.Patch(originalStartAnimationOverload1, patchedStartAnimationOverload1);

			// Register hotkey's
			inputApi.RegisterHotKey("ushigatana_dash_anim", "", GlKeys.ShiftLeft, HotkeyType.MovementControls);
			inputApi.RegisterHotKey("ushigatana_dialog", "", GlKeys.P, HotkeyType.GUIOrOtherControls);

			// Register hotkey handler's
			inputApi.SetHotKeyHandler("ushigatana_dash_anim", OnDashReset);
			inputApi.SetHotKeyHandler("ushigatana_dialog", OnToggleDebugDialog);
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviour";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (dashBlur == null) return;
			if (harmonyInstance == null) return;

			EntityPlayer entityPlayer = clientApi.World.Player.Entity;
			EntityControls controls = entityPlayer.Controls;
			EntityPos transform = entityPlayer.Pos;

#if false
			DebugWidgets.IntSlider("Ushigatana", "General", "dashCooldownMs", 0, 5000, () => { return dashCooldownMs; }, (v) => { dashCooldownMs = v; });

			DebugWidgets.FloatSlider("Ushigatana", "Physic", "physicSpeedFactor", -50.0F, 50.0F, () => { return physicSpeedFactor; }, (v) => { physicSpeedFactor = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "horizontalImpulseGrounded", -10.0F, 10.0F, () => { return horizontalImpulseGrounded; }, (v) => { horizontalImpulseGrounded = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "horizontalImpulseAirbourne", -1.0F, 1.0F, () => { return horizontalImpulseAirbourne; }, (v) => { horizontalImpulseAirbourne = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "verticalImpulseFirstDash", -0.1F, 0.1F, () => { return verticalImpulseFirstDash; }, (v) => { verticalImpulseFirstDash = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "verticalImpulseSecondDash", -0.1F, 0.1F, () => { return verticalImpulseSecondDash; }, (v) => { verticalImpulseSecondDash = v; });
			DebugWidgets.FloatSlider("Ushigatana", "Physic", "airbourneDashDirectionSpeedFactor", -10.0F, 10.0F, () => { return airbourneDashDirectionSpeedFactor; }, (v) => { airbourneDashDirectionSpeedFactor = v; });

			DebugWidgets.IntSlider("Ushigatana", "Animation", "dashForwardFrameFrames", 0, 100, () => { return dashForwardFrameCount; }, (v) => { dashForwardFrameCount = v; });
			DebugWidgets.IntSlider("Ushigatana", "Animation", "dashForwardRetractFrames", 0, 100, () => { return dashForwardRetractFrameCount; }, (v) => { dashForwardRetractFrameCount = v; });
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
						Vec3d localRight = worldUp.Cross(localForward).Normalize();
						Vec3d localLeft = localRight.Clone().Mul(-1);

						if (isDoubleDashActive)
						{
							// Reset dash direction
							dashDirection = initialDashDirection;

							// Apply local input direction
							if (controls.Forward) dashDirection += airbourneDashDirectionSpeedFactor * localForward;
							if (controls.Backward) dashDirection += airbourneDashDirectionSpeedFactor * localBack;
							if (controls.Left) dashDirection += airbourneDashDirectionSpeedFactor * localRight;
							if (controls.Right) dashDirection += airbourneDashDirectionSpeedFactor * localLeft;
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
							else
							{
								// Default forward dash if no key would be held..
								dashDirection = new Vec3d(localForward.X, 0.0F, localForward.Z);
								dashDirection.Normalize();
							}

							initialDashDirection = dashDirection;
						}

#if false
						if (lineGizmo != null)
						{
							lineGizmo.Reset();
							lineGizmo.AddLine(
								(float)transform.X,        (float)transform.Y,        (float)transform.Z,
								(float)transform.Motion.X, (float)transform.Motion.Y, (float)transform.Motion.Z,
								ColorUtil.ToRgba(0xFF, 0xFF, 0x00, 0x00));
							lineGizmo.Commit();
						}
#endif

						// Enable whitelist in the original animation manager
						enableAnimationWhitelist = true;

						// Stop all animations
						entity.AnimManager.StopAllAnimations();

						// Compute quadrant angle of motion vector
						double x = transform.Motion.Dot(localRight);
						double y = transform.Motion.Dot(localForward);
						double angle = Math.Atan2(x, y) * 57.29577951308232286465F;

						// Start dash animation based on quadrant angle
						if ((angle > -45.0F) && (angle < 45.0F))
						{
							// Dash forward
							entity.AnimManager.StartAnimation(dashForwardData);
							RunningAnimation dashForwardAnimation = entity.AnimManager.GetAnimationState(dashForwardData.Code);
							dashForwardAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							dashForwardAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else if ((angle > 45.0F) && (angle < 135.0F))
						{
							// Dash left
							entity.AnimManager.StartAnimation(dashLeftData);
							RunningAnimation dashForwardAnimation = entity.AnimManager.GetAnimationState(dashLeftData.Code);
							dashForwardAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							dashForwardAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else if ((angle < -45.0F) && (angle > -135.0F))
						{
							// Dash right
							entity.AnimManager.StartAnimation(dashRightData);
							RunningAnimation dashForwardAnimation = entity.AnimManager.GetAnimationState(dashRightData.Code);
							dashForwardAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							dashForwardAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}
						else
						{
							// Dash back
							entity.AnimManager.StartAnimation(dashBackData);
							RunningAnimation dashForwardAnimation = entity.AnimManager.GetAnimationState(dashBackData.Code);
							dashForwardAnimation.Animation.OnAnimationEnd = EnumEntityAnimationEndHandling.Hold;
							dashForwardAnimation.Animation.OnActivityStopped = EnumEntityActivityStoppedHandling.PlayTillEnd;
						}

						// Enable motion blur
						dashBlur.BlurEnable = true;

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

							// Stop all animations
							entity.AnimManager.StopAllAnimations();

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
							entity.AnimManager.StopAllAnimations();
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
						enableAnimationWhitelist = false;

						break;
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
					? EaseOutCirc(physicFrame) * verticalImpulseSecondDash * worldUp
					: EaseOutElastic(physicFrame) * verticalImpulseFirstDash * worldUp;

				// Apply force
				transform.Motion.Add(force);

				// Advance animation
				physicFrame += physicSpeedFactor * deltaTime;
				if (physicFrame >= 1.0F)
				{
					isPhysicActive = false;
				}

				// Apply blur intensity based on motion vector
				dashBlur.BlurIntensity = (float)transform.Motion.Length();
			}
		}

		// Pirate's Life https://easings.net/
		private float EaseInCirc(float x)
		{
			return 1.0F - (float)Math.Sqrt(1.0F - (float)Math.Pow(x, 2.0F));
		}
		private float EaseOutCirc(float x)
		{
			return (float)Math.Sqrt(1.0F - (float)Math.Pow(x - 1.0F, 2.0F));
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
		private float EaseOutElastic(float x)
		{
			float c4 = (2.0F * (float)Math.PI) / 3.0F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: (float)Math.Pow(2.0F, -10.0F * x) * (float)Math.Sin((x * 10.0F - 0.75F) * c4) + 1.0F;
		}

		private bool OnToggleDebugDialog(KeyCombination comb)
		{
			if (debugDialog == null) return true;
			if (debugDialog.IsOpened()) debugDialog.TryClose();
			else debugDialog.TryOpen();
			return true;
		}
		private bool OnDashReset(KeyCombination combination)
		{
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
				groundedWhileOnCooldown = false;

				// Enable sequence
				sequenceState = SequenceState.SEQUENCE_STATE_START;

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
					// Reset state
					isPhysicActive = true;
					isDoubleDashActive = true;
					doubleDashAllowed = false;

					// Enable sequence
					sequenceState = SequenceState.SEQUENCE_STATE_START;

					// Play dash dounds
					clientApi.World.PlaySoundAt(dashSound2, soundPos, 0.0, null, true, 64.0F, 1.0F);
					clientApi.World.PlaySoundAt(ushigatanaDashSound, soundPos, 0.0, null, false, 64.0F, 6.0F);
				}
			}

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
