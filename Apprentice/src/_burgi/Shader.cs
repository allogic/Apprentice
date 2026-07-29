using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice.src._burgi
{
	internal class Shader
	{
		internal class LineGizmo : IRenderer
		{
			private readonly ICoreClientAPI clientApi;
			private readonly IClientEventAPI eventApi;
			private readonly IRenderAPI renderApi;
			private readonly IShaderAPI shaderApi;

			private readonly IShaderProgram lineProgram;

			private MeshData? mesh = null;
			private MeshRef? meshRef = null;

			public bool gizmoEnable = false;

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

				if (gizmoEnable)
				{
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

			public bool blurEnable = false;
			public float blurLength = 0.0F;
			public float blurIntensity = 2.7F;

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

				if (blurEnable)
				{
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
					blurProgram.Uniform("blurLength", blurLength);
					blurProgram.Uniform("blurIntensity", blurIntensity);
					renderApi.RenderMesh(meshRef);
					blurProgram.Stop();

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
		internal class DarkAges : IRenderer
		{
			private readonly ICoreClientAPI clientApi;
			private readonly IClientEventAPI eventApi;
			private readonly IRenderAPI renderApi;
			private readonly IShaderAPI shaderApi;

			private readonly IShaderProgram darkProgram;

			private MeshRef? meshRef = null;

			public bool darkEnable = false;
			public float darkIntensity = 0.007F;
			public float darkRadius = 0.5F;
			public float depthFactor = 15.0F; // TODO: remove me..

			public double RenderOrder => 1.0;
			public int RenderRange => 9999;

			public DarkAges(ICoreClientAPI api)
			{
				clientApi = api;
				eventApi = api.Event;
				renderApi = api.Render;
				shaderApi = api.Shader;

				// Create dark program
				darkProgram = shaderApi.NewShaderProgram();
				darkProgram.AssetDomain = "apprentice";
				darkProgram.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
				darkProgram.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);
				shaderApi.RegisterFileShaderProgram("dark-ages", darkProgram);
				darkProgram.Compile();

				meshRef = renderApi.UploadMesh(QuadMeshUtil.GetQuad());

				eventApi.RegisterRenderer(this, EnumRenderStage.OIT);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (stage != EnumRenderStage.OIT) return;

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				if (meshRef == null) return;

				if (darkEnable)
				{
					renderApi.GLDisableDepthTest();
					renderApi.GlDisableStencilTest();
					renderApi.GlToggleBlend(false);

					// Extract near and far plane
					float m22 = renderApi.CurrentProjectionMatrix[10];
					float m32 = renderApi.CurrentProjectionMatrix[14];
					float nearZ = m32 / (m22 - 1.0F);
					float farZ = m32 / (m22 + 1.0F);

					// TODO: make blit..

					// Make it dark
					renderApi.CurrentFrameBuffer = renderApi.FrameBuffers[(int)EnumFrameBuffer.Primary];
					darkProgram.Use();
					float[] playerPosition = { (float)transform.X, (float)transform.Y, (float)transform.Z };
					darkProgram.Uniforms4("playerPosition", 1, playerPosition);
					darkProgram.BindTexture2D("colorTex", 0 /* Use blit color */, 0);
					darkProgram.BindTexture2D("depthTex", renderApi.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 0);
					float[] screenSize = { renderApi.FrameWidth, renderApi.FrameHeight, 0, 0 };
					darkProgram.Uniforms4("screenSize", 1, playerPosition);
					darkProgram.UniformMatrix("projectionMatrix", renderApi.CurrentProjectionMatrix);
					darkProgram.UniformMatrix("viewMatrix", renderApi.CurrentModelviewMatrix);
					darkProgram.Uniform("darkIntensity", darkIntensity);
					darkProgram.Uniform("darkRadius", darkRadius);
					darkProgram.Uniform("depthFactor", depthFactor);
					darkProgram.Uniform("nearZ", nearZ);
					darkProgram.Uniform("farZ", farZ);
					renderApi.RenderMesh(meshRef);
					darkProgram.Stop();

					renderApi.GlToggleBlend(true);
					renderApi.GLEnableDepthTest();
					renderApi.GLEnableDepthTest();
				}
			}

			public void Dispose()
			{
				eventApi.UnregisterRenderer(this, EnumRenderStage.OIT);
			}
		}
		internal class HealthBar : IRenderer
		{
			public double RenderOrder { get { return 1; } }
			public int RenderRange { get { return 10; } }

			private readonly ICoreClientAPI capi;

			private readonly MeshRef backgroundRectRef;
			private readonly MeshRef healthRectRef;

			private readonly Matrixf mvMatrix = new();

			private readonly IShaderProgram program;

			public HealthBar(ICoreClientAPI capi)
			{
				this.capi = capi;

				IShaderProgram shader = capi.Shader.NewShaderProgram();
				shader.AssetDomain = "apprentice";
				shader.VertexShader = capi.Shader.NewShader(
					EnumShaderType.VertexShader
				);
				shader.FragmentShader = capi.Shader.NewShader(
					EnumShaderType.FragmentShader
				);

				try
				{
					capi.Shader.RegisterFileShaderProgram(
						"apprenticehealthbar",
						shader
					);
					if (!shader.Compile())
					{
						throw new InvalidOperationException(
							"The Apprentice health-bar shader did not compile."
						);
					}
				}
				catch
				{
					shader.Dispose();
					throw;
				}

				program = shader;
				backgroundRectRef = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
				healthRectRef = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());

				capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
			}

			#region IRenderer Impl
			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				EntityPlayer playerEntity = capi.World.Player.Entity;

				foreach (Entity entity in capi.World.LoadedEntities.Values)
				{
					if (entity == playerEntity) continue;
					if (!HasAttrib(entity, "health")) continue;

					double distance = entity.Pos.DistanceTo(playerEntity.Pos.XYZ);

					if (distance > RenderRange) continue;
					if (!IsEntityVisible(entity, playerEntity)) continue;

					RenderHealthBar(entity);
				}
			}
			public void Dispose()
			{
				capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

				capi.Render.DeleteMesh(backgroundRectRef);
				capi.Render.DeleteMesh(healthRectRef);

				program.Dispose();
			}
			#endregion

			#region Private Impl
			private bool HasAttrib(Entity entity, string attribName)
			{
				return entity.WatchedAttributes.HasAttribute(attribName);
			}
			private bool IsEntityVisible(Entity entity, Entity playerEntity)
			{
				Vec3d entityPosition = entity.Pos.XYZ;
				Vec3d cameraPosition = capi.World.Player.Entity.Pos.XYZ;
				Vec3d dir = entityPosition.SubCopy(cameraPosition);

				double distance = dir.Length();

				if (distance > RenderRange) return false;

				Vec3f look = playerEntity.Pos.GetViewVector();

				dir.Normalize();

				double dot = dir.ToVec3f().Dot(look);

				return dot > 0.1;
			}
			private void RenderHealthBar(Entity entity)
			{
				float health = entity.WatchedAttributes.GetFloat("health");
				float maxHealth = entity.WatchedAttributes.GetFloat("maxhealth");
				if (!float.IsFinite(health) || !float.IsFinite(maxHealth) ||
					maxHealth <= 0)
				{
					return;
				}
				float percentage = Math.Clamp(health / maxHealth, 0, 1);

				Vec3d position = entity.Pos.XYZ;
				Vec3d cameraPosition = capi.World.Player.Entity.Pos.XYZ;

				mvMatrix
					.Set(capi.Render.CurrentModelviewMatrix)
					.Translate(
						position.X - cameraPosition.X,
						position.Y + entity.SelectionBox.Y2 + 0.3 - cameraPosition.Y,
						position.Z - cameraPosition.Z
					)
					.Scale(1.5f, 0.15f, 1);

				if (!program.Disposed)
				{
					program.Use();

					program.Uniform("rgbaIn", new Vec4f(0, 0, 0, 0.7f));
					program.UniformMatrix("modelViewMatrix", mvMatrix.Values);
					program.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);

					capi.Render.RenderMesh(backgroundRectRef);

					mvMatrix
						.Set(capi.Render.CurrentModelviewMatrix)
						.Translate(
							position.X - cameraPosition.X,
							position.Y + entity.SelectionBox.Y2 + 0.3 - cameraPosition.Y,
							position.Z - cameraPosition.Z
						)
						.Scale(1.5f * percentage, 0.15f, 1);

					program.Uniform("rgbaIn", new Vec4f(1, 0, 0, 1));
					program.UniformMatrix("modelViewMatrix", mvMatrix.Values);

					capi.Render.RenderMesh(healthRectRef);

					program.Stop();
				}
			}
			#endregion
		}
		internal class ObamaPrism : IRenderer
		{
			private readonly ICoreClientAPI clientApi;
			private readonly IClientEventAPI eventApi;
			private readonly IRenderAPI renderApi;
			private readonly IShaderAPI shaderApi;

			internal class Obama
			{
				public int index;
				public Vec3f randomOffset;
				public Matrixf transform;
				public Vec3f targetPosition;
				public Vec4f targetRotation;
				public Vec3f prevTargetPosition; // TODO: maybe obsolete..
				public Vec4f prevTargetRotation; // TODO: maybe obsolete..
				public Vec3f linearVelocity;

				public Obama(int i, Random random)
				{
					index = i;
					randomOffset = new(
						(float)random.NextDouble(),
						(float)random.NextDouble(),
						(float)random.NextDouble());
					transform = Matrixf.Create();
					targetPosition = new(0, 0, 0);
					targetRotation = new(0, 0, 0, 1);
					prevTargetPosition = new(0, 0, 0);
					prevTargetRotation = new(0, 0, 0, 1);
					linearVelocity = new(0, 0, 0);
				}
			}

			private readonly IShaderProgram obamaProgram;

			private MeshData? mesh = null;
			private MeshRef? meshRef = null;
			private Random random = new();

			private int obamaTexture = 0;

			public bool obamaEnable = false;
			public float obamaMaxVelocity = 0.05F;
			public float obamaRandDistance = 2.0F;
			public float obamaUpOffset = 2.0F;
			public float obamaForwardOffset = 2.0F;
			public int obamaUpdateFrames = 120;

			private int obamaFrame = 0;

			private float deltaTimeAcc = 0.0F;

			public IList<Obama> obamas = [];

			public double RenderOrder => 1.0;
			public int RenderRange => 9999;

			public ObamaPrism(ICoreClientAPI api, int obamaCount)
			{
				clientApi = api;
				eventApi = api.Event;
				renderApi = api.Render;
				shaderApi = api.Shader;

				// Create obamas
				for (int i = 0; i < obamaCount; i++)
				{
					obamas.Add(new Obama(i, random));
				}

				// Create dark program
				obamaProgram = shaderApi.NewShaderProgram();
				obamaProgram.AssetDomain = "apprentice";
				obamaProgram.VertexShader = shaderApi.NewShader(EnumShaderType.VertexShader);
				obamaProgram.FragmentShader = shaderApi.NewShader(EnumShaderType.FragmentShader);
				shaderApi.RegisterFileShaderProgram("obama-shader", obamaProgram);
				obamaProgram.Compile();

				// Create mesh
				mesh = new(20, 60, false, true, false, false);
				if (mesh != null)
				{
					int vertexCount = 0;

					// Left
					vertexCount = mesh.VerticesCount;
					mesh.AddVertex(-0.5F, 0.0F, -0.5F, 1, 1);
					mesh.AddVertex(0.0F, 0.6F, 0.0F, 0.5F, -0.134F);
					mesh.AddVertex(-0.5F, 0.0F, 0.5F, 0, 1);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 1);

					// Right
					vertexCount = mesh.VerticesCount;
					mesh.AddVertex(0.5F, 0.0F, 0.5F, 1, 1);
					mesh.AddVertex(0.0F, 0.6F, 0.0F, 0.5F, -0.134F);
					mesh.AddVertex(0.5F, 0.0F, -0.5F, 0, 1);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 1);

					// Front
					vertexCount = mesh.VerticesCount;
					mesh.AddVertex(-0.5F, 0.0F, 0.5F, 1, 1);
					mesh.AddVertex(0.0F, 0.6F, 0.0F, 0.5F, -0.134F);
					mesh.AddVertex(0.5F, 0.0F, 0.5F, 0, 1);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 1);

					// Back
					vertexCount = mesh.VerticesCount;
					mesh.AddVertex(0.5F, 0.0F, -0.5F, 1, 1);
					mesh.AddVertex(0.0F, 0.6F, 0.0F, 0.5F, -0.134F);
					mesh.AddVertex(-0.5F, 0.0F, -0.5F, 0, 1);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 1);

					// Bottom
					vertexCount = mesh.VerticesCount;
					mesh.AddVertex(-0.5F, 0.0F, -0.5F, 0, 0);
					mesh.AddVertex(0.5F, 0.0F, -0.5F, 0, 0);
					mesh.AddVertex(0.5F, 0.0F, 0.5F, 0, 0);
					mesh.AddVertex(-0.5F, 0.0F, 0.5F, 0, 0);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 1);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 0);
					mesh.AddIndex(vertexCount + 2);
					mesh.AddIndex(vertexCount + 3);

					mesh.mode = EnumDrawMode.Triangles;
				}
				meshRef = renderApi.UploadMesh(mesh);

				// Create obama texture
				obamaTexture = renderApi.GetOrLoadTexture(new AssetLocation("apprentice", "textures/real-obama.png"));

				// Register renderer
				eventApi.RegisterRenderer(this, EnumRenderStage.Opaque);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (stage != EnumRenderStage.Opaque) return;

				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				if (meshRef == null) return;

				if (obamaEnable)
				{
					renderApi.GlDisableStencilTest();
					renderApi.GlToggleBlend(false);
					renderApi.GlEnableCullFace();

					// Make it obama
					renderApi.CurrentFrameBuffer = renderApi.FrameBuffers[(int)EnumFrameBuffer.Primary];
					obamaProgram.Use();
					obamaProgram.UniformMatrix("projectionMatrix", renderApi.CurrentProjectionMatrix);
					obamaProgram.UniformMatrix("viewMatrix", renderApi.CurrentModelviewMatrix);
					obamaProgram.BindTexture2D("tex", obamaTexture, 0);
					foreach (Obama obama in obamas)
					{
						obamaProgram.UniformMatrix("modelMatrix", obama.transform.Values);
						renderApi.RenderMesh(meshRef);
					}
					obamaProgram.Stop();

					renderApi.GlToggleBlend(true);
					renderApi.GlEnableStencilTest();
					renderApi.GlDisableCullFace();
				}
			}

			public void Update(float deltaTime)
			{
				EntityPlayer entityPlayer = clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				// Update obamas
				if (obamaFrame > obamaUpdateFrames)
				{
					obamaFrame = 0;

					// Compute local direction
					Vec3f localForward = transform.GetViewVector();
					Vec3f localRight = BurgiMath.WorldUpF.Cross(localForward).Normalize();
					Vec3f localUp = localForward.Cross(localRight);

					foreach (Obama obama in obamas)
					{
						float nextOffsetX = (random.NextSingle() * 2.0F - 1.0F) * obamaRandDistance;
						float nextOffsetY = (random.NextSingle() * 2.0F - 1.0F) * obamaRandDistance;
						float nextOffsetZ = (random.NextSingle() * 2.0F - 1.0F) * obamaRandDistance;

						Vec3f targetPosition = transform.XYZFloat;

						targetPosition += localForward * obamaForwardOffset;
						targetPosition += localUp * obamaUpOffset;

						obama.targetPosition.X = targetPosition.X + nextOffsetX;
						obama.targetPosition.Y = targetPosition.Y + nextOffsetY;
						obama.targetPosition.Z = targetPosition.Z + nextOffsetZ;
					}
				}

				// Update obamas
				foreach (Obama obama in obamas)
				{
					Vec3f currentPosition = new(
						obama.transform.Values[12],
						obama.transform.Values[13],
						obama.transform.Values[14]
					);

					Vec3f linearDisplacement = obama.targetPosition - currentPosition;
					obama.linearVelocity += linearDisplacement * deltaTime;
					Vec3f obamaPosition = currentPosition + obama.linearVelocity;

					// Clamp velocity
					if (obama.linearVelocity.Length() > obamaMaxVelocity)
					{
						obama.linearVelocity = obama.linearVelocity.Normalize() * obamaMaxVelocity;
					}

					obama.transform.Values[12] = obamaPosition.X;
					obama.transform.Values[13] = obamaPosition.Y;
					obama.transform.Values[14] = obamaPosition.Z;

					float rotX = MathF.Sin(deltaTimeAcc * obama.index) * 0.01F;
					float rotZ = MathF.Cos(deltaTimeAcc * obama.index) * 0.01F;
					float rotY = rotX + rotZ * 3.0F;

					obama.transform.RotateX(rotX);
					obama.transform.RotateY(rotY);
					obama.transform.RotateZ(rotZ);
				}

				// Accumulate delta time
				deltaTimeAcc += deltaTime;

				// Increment obama frame
				obamaFrame++;
			}
			public void Dispose()
			{
				eventApi.UnregisterRenderer(this, EnumRenderStage.Opaque);

				renderApi.GLDeleteTexture(obamaTexture);
			}
		}
	}
}
