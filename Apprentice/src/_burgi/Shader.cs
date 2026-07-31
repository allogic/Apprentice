using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Apprentice.src._burgi
{
	internal class Shader
	{
		internal class LineGizmo : IRenderer
		{
			private IShaderProgram lineProgram = null!;

			private MeshData? mesh = null;
			private MeshRef? meshRef = null;

			public bool gizmoEnable = false;

			public double RenderOrder => 1.0;
			public int RenderRange => 10;

			public LineGizmo(int numLines)
			{
				// Create mesh
				mesh = new(numLines * 2, numLines * 2, false, false, true, false);
				mesh?.mode = EnumDrawMode.Lines;

				// Register renderer
				Main.clientApi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (stage != EnumRenderStage.Opaque) return;

				if (meshRef == null) return;

				if (gizmoEnable)
				{
					CompileOrUpdatePrograms();

					Main.clientApi.Render.GlDisableCullFace();
					Main.clientApi.Render.GlToggleBlend(false);

					Main.clientApi.Render.LineWidth = 10;

					// Draw the gizmo
					lineProgram.Use();
					lineProgram.UniformMatrix("projectionMatrix", Main.clientApi.Render.CurrentProjectionMatrix);
					lineProgram.UniformMatrix("viewMatrix", Main.clientApi.Render.CurrentModelviewMatrix);
					Main.clientApi.Render.RenderMesh(meshRef);
					lineProgram.Stop();

					Main.clientApi.Render.GlToggleBlend(true);
					Main.clientApi.Render.GlEnableCullFace();
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

				meshRef = Main.clientApi.Render.UploadMesh(mesh);
			}
			public void Dispose()
			{
				// Unregister renderer
				Main.clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

				mesh?.Dispose();
			}

			private void CompileOrUpdatePrograms()
			{
				if ((lineProgram == null) || lineProgram.Disposed)
				{
					lineProgram = Main.clientApi.Shader.NewShaderProgram();
					lineProgram.AssetDomain = "apprentice";
					lineProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					lineProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("line-shader", lineProgram);
					lineProgram.Compile();
				}
			}
		}
		internal class MotionBlur : IRenderer
		{
			private IShaderProgram blitProgram = null!;
			private IShaderProgram blurProgram = null!;

			private RawTexture? blitTexture = null;
			private RawTexture? accTextureA = null;
			private RawTexture? accTextureB = null;

			private MeshRef? meshRef = null;
			private FrameBufferRef? frameBufferBlitRef = null;
			private FrameBufferRef? frameBufferARef = null;
			private FrameBufferRef? frameBufferBRef = null;

			public bool blurEnable = false;
			public float blurLength = 0.0F;
			public float blurIntensity = 2.7F;

			public double RenderOrder => 1.0;
			public int RenderRange => 9999;

			public MotionBlur()
			{
				// Create mesh
				meshRef = Main.clientApi.Render.UploadMesh(QuadMeshUtil.GetQuad());

				// Create blitTexture render target
				blitTexture = new RawTexture();
				blitTexture.MinFilter = EnumTextureFilter.Nearest;
				blitTexture.MagFilter = EnumTextureFilter.Nearest;
				blitTexture.WrapS = EnumTextureWrap.ClampToEdge;
				blitTexture.WrapT = EnumTextureWrap.ClampToEdge;
				blitTexture.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
				blitTexture.Width = Main.clientApi.Render.FrameWidth; // TODO: update these values when main framebuffer changes size
				blitTexture.Height = Main.clientApi.Render.FrameHeight;
				blitTexture.TextureId = 0;
				Main.clientApi.Render.GenTexture(blitTexture);

				// Create accumulator render target A
				accTextureA = new RawTexture();
				accTextureA.MinFilter = EnumTextureFilter.Nearest;
				accTextureA.MagFilter = EnumTextureFilter.Nearest;
				accTextureA.WrapS = EnumTextureWrap.ClampToEdge;
				accTextureA.WrapT = EnumTextureWrap.ClampToEdge;
				accTextureA.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
				accTextureA.Width = Main.clientApi.Render.FrameWidth; // TODO: update these values when main framebuffer changes size
				accTextureA.Height = Main.clientApi.Render.FrameHeight;
				accTextureA.TextureId = 0;
				Main.clientApi.Render.GenTexture(accTextureA);

				// Create accumulator render target B
				accTextureB = new RawTexture();
				accTextureB.MinFilter = EnumTextureFilter.Nearest;
				accTextureB.MagFilter = EnumTextureFilter.Nearest;
				accTextureB.WrapS = EnumTextureWrap.ClampToEdge;
				accTextureB.WrapT = EnumTextureWrap.ClampToEdge;
				accTextureB.PixelInternalFormat = EnumTextureInternalFormat.Rgba8;
				accTextureB.Width = Main.clientApi.Render.FrameWidth; // TODO: update these values when main framebuffer changes size
				accTextureB.Height = Main.clientApi.Render.FrameHeight;
				accTextureB.TextureId = 0;
				Main.clientApi.Render.GenTexture(accTextureB);

				// Create blit frame buffer
				FramebufferAttrs frameBufferBlitAttribs = new("blit", Main.clientApi.Render.FrameWidth, Main.clientApi.Render.FrameHeight);
				frameBufferBlitAttribs.Attachments = new FramebufferAttrsAttachment[1];
				frameBufferBlitAttribs.Attachments[0] = new();
				frameBufferBlitAttribs.Attachments[0].Texture = blitTexture;
				frameBufferBlitAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
				frameBufferBlitRef = Main.clientApi.Render.CreateFrameBuffer(frameBufferBlitAttribs);

				// Create ping pong frame buffer A
				FramebufferAttrs frameBufferAAttribs = new("accA", Main.clientApi.Render.FrameWidth, Main.clientApi.Render.FrameHeight);
				frameBufferAAttribs.Attachments = new FramebufferAttrsAttachment[1];
				frameBufferAAttribs.Attachments[0] = new();
				frameBufferAAttribs.Attachments[0].Texture = accTextureA;
				frameBufferAAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
				frameBufferARef = Main.clientApi.Render.CreateFrameBuffer(frameBufferAAttribs);

				// Create ping pong frame buffer B
				FramebufferAttrs frameBufferBAttribs = new("accB", Main.clientApi.Render.FrameWidth, Main.clientApi.Render.FrameHeight);
				frameBufferBAttribs.Attachments = new FramebufferAttrsAttachment[1];
				frameBufferBAttribs.Attachments[0] = new();
				frameBufferBAttribs.Attachments[0].Texture = accTextureB;
				frameBufferBAttribs.Attachments[0].AttachmentType = EnumFramebufferAttachment.ColorAttachment0;
				frameBufferBRef = Main.clientApi.Render.CreateFrameBuffer(frameBufferBAttribs);

				// Register renderer
				Main.clientApi.Event.RegisterRenderer(this, EnumRenderStage.Done);
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
					CompileOrUpdatePrograms();

					// Blit render target
					Main.clientApi.Render.CurrentFrameBuffer = frameBufferBlitRef;
					blitProgram.Use();
					blitProgram.BindTexture2D("tex", Main.clientApi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0], 0);
					Main.clientApi.Render.RenderMesh(meshRef);
					blitProgram.Stop();

					// Accumulate motion blur
					Main.clientApi.Render.CurrentFrameBuffer = frameBufferARef;
					blurProgram.Use();
					blurProgram.BindTexture2D("blitTex", frameBufferBlitRef.ColorTextureIds[0], 0);
					blurProgram.BindTexture2D("accTex", frameBufferBRef.ColorTextureIds[0], 1);
					blurProgram.Uniform("blurLength", blurLength);
					blurProgram.Uniform("blurIntensity", blurIntensity);
					Main.clientApi.Render.RenderMesh(meshRef);
					blurProgram.Stop();

					// Blit render target
					Main.clientApi.Render.CurrentFrameBuffer = null;
					blitProgram.Use();
					blitProgram.BindTexture2D("tex", frameBufferARef.ColorTextureIds[0], 0);
					Main.clientApi.Render.RenderMesh(meshRef);
					blitProgram.Stop();
				}

				// Swap frame accumulator
				FrameBufferRef tmp = frameBufferARef;
				frameBufferARef = frameBufferBRef;
				frameBufferBRef = tmp;
			}

			public void Dispose()
			{
				// Unregister renderer
				Main.clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Done);

				// Destroy framebuffer
				Main.clientApi.Render.DestroyFrameBuffer(frameBufferARef);
				Main.clientApi.Render.DestroyFrameBuffer(frameBufferBRef);
				Main.clientApi.Render.DestroyFrameBuffer(frameBufferBlitRef);

				// Destroy textures
				if (blitTexture != null) Main.clientApi.Render.GLDeleteTexture(blitTexture.TextureId);
				if (accTextureA != null) Main.clientApi.Render.GLDeleteTexture(accTextureA.TextureId);
				if (accTextureB != null) Main.clientApi.Render.GLDeleteTexture(accTextureB.TextureId);
			}

			private void CompileOrUpdatePrograms()
			{
				if ((blitProgram == null) || blitProgram.Disposed)
				{
					blitProgram = Main.clientApi.Shader.NewShaderProgram();
					blitProgram.AssetDomain = "apprentice";
					blitProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					blitProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("blit-shader", blitProgram);
					blitProgram.Compile();
				}

				if ((blurProgram == null) || blurProgram.Disposed)
				{
					blurProgram = Main.clientApi.Shader.NewShaderProgram();
					blurProgram.AssetDomain = "apprentice";
					blurProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					blurProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("blur-shader", blurProgram);
					blurProgram.Compile();
				}
			}
		}
		internal class DarkAges : IRenderer
		{
			private IShaderProgram darkProgram = null!;

			private MeshRef? meshRef = null;

			public bool darkEnable = false;
			public float darkIntensity = 0.007F;
			public float darkRadius = 0.5F;
			public float depthFactor = 15.0F; // TODO: remove me..

			public double RenderOrder => 1.0;
			public int RenderRange => 9999;

			public DarkAges()
			{
				// Create mesh
				meshRef = Main.clientApi.Render.UploadMesh(QuadMeshUtil.GetQuad());

				// Register renderer
				Main.clientApi.Event.RegisterRenderer(this, EnumRenderStage.OIT);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (stage != EnumRenderStage.OIT) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				if (meshRef == null) return;

				if (darkEnable)
				{
					CompileOrUpdatePrograms();

					Main.clientApi.Render.GLDisableDepthTest();
					Main.clientApi.Render.GlDisableStencilTest();
					Main.clientApi.Render.GlToggleBlend(false);

					// Extract near and far plane
					float m22 = Main.clientApi.Render.CurrentProjectionMatrix[10];
					float m32 = Main.clientApi.Render.CurrentProjectionMatrix[14];
					float nearZ = m32 / (m22 - 1.0F);
					float farZ = m32 / (m22 + 1.0F);

					// TODO: make blit..

					// Make it dark
					Main.clientApi.Render.CurrentFrameBuffer = Main.clientApi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
					darkProgram.Use();
					float[] playerPosition = { (float)transform.X, (float)transform.Y, (float)transform.Z };
					darkProgram.Uniforms4("playerPosition", 1, playerPosition);
					darkProgram.BindTexture2D("colorTex", 0 /* Use blit color */, 0);
					darkProgram.BindTexture2D("depthTex", Main.clientApi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 0);
					float[] screenSize = { Main.clientApi.Render.FrameWidth, Main.clientApi.Render.FrameHeight, 0, 0 };
					darkProgram.Uniforms4("screenSize", 1, playerPosition);
					darkProgram.UniformMatrix("projectionMatrix", Main.clientApi.Render.CurrentProjectionMatrix);
					darkProgram.UniformMatrix("viewMatrix", Main.clientApi.Render.CurrentModelviewMatrix);
					darkProgram.Uniform("darkIntensity", darkIntensity);
					darkProgram.Uniform("darkRadius", darkRadius);
					darkProgram.Uniform("depthFactor", depthFactor);
					darkProgram.Uniform("nearZ", nearZ);
					darkProgram.Uniform("farZ", farZ);
					Main.clientApi.Render.RenderMesh(meshRef);
					darkProgram.Stop();

					Main.clientApi.Render.GlToggleBlend(true);
					Main.clientApi.Render.GLEnableDepthTest();
					Main.clientApi.Render.GLEnableDepthTest();
				}
			}

			public void Dispose()
			{
				// Unregister renderer
				Main.clientApi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
			}

			private void CompileOrUpdatePrograms()
			{
				if ((darkProgram == null) || darkProgram.Disposed)
				{
					darkProgram = Main.clientApi.Shader.NewShaderProgram();
					darkProgram.AssetDomain = "apprentice";
					darkProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					darkProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("dark-ages", darkProgram);
					darkProgram.Compile();
				}
			}
		}
		internal class HealthBar : IRenderer
		{
			private IShaderProgram healthProgram = null!;

			private MeshRef? backgroundRectRef = null;
			private MeshRef? healthRectRef = null;

			public bool healthEnable = false;
			public float renderDistance = 30.0F;

			private Matrixf mvMatrix = new();

			public double RenderOrder => 1.0;
			public int RenderRange => 9999;

			public HealthBar()
			{
				// Create mesh
				backgroundRectRef = Main.clientApi.Render.UploadMesh(QuadMeshUtil.GetQuad());
				healthRectRef = Main.clientApi.Render.UploadMesh(QuadMeshUtil.GetQuad());

				// Register renderer
				Main.clientApi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (healthEnable)
				{
					EntityPlayer playerEntity = Main.clientApi.World.Player.Entity;

					foreach (Entity entity in Main.clientApi.World.LoadedEntities.Values)
					{
						if (entity == playerEntity) continue;
						if (!HasAttrib(entity, "health")) continue;

						double distance = entity.Pos.DistanceTo(playerEntity.Pos.XYZ);

						if (distance > renderDistance) continue;
						if (!IsEntityVisible(entity, playerEntity)) continue;

						RenderHealthBar(entity);
					}
				}
			}

			public void Dispose()
			{
				// Unregister renderer
				Main.clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

				// Destroy meshes
				Main.clientApi.Render.DeleteMesh(backgroundRectRef);
				Main.clientApi.Render.DeleteMesh(healthRectRef);
			}

			private bool HasAttrib(Entity entity, string attribName)
			{
				return entity.WatchedAttributes.HasAttribute(attribName);
			}
			private bool IsEntityVisible(Entity entity, Entity playerEntity)
			{
				Vec3d entityPosition = entity.Pos.XYZ;
				Vec3d cameraPosition = Main.clientApi.World.Player.Entity.Pos.XYZ;
				Vec3d dir = entityPosition - cameraPosition;

				double distance = dir.Length();

				if (distance > RenderRange) return false;

				Vec3f look = playerEntity.Pos.GetViewVector();

				dir.Normalize();

				double dot = dir.ToVec3f().Dot(look);

				return dot > 0.1;
			}
			private void RenderHealthBar(Entity entity)
			{
				ITreeAttribute healthTree = entity.WatchedAttributes.GetTreeAttribute("health");

				if (healthTree == null) return;

				float health = healthTree.GetFloat("currenthealth");
				float maxHealth= healthTree.GetFloat("maxhealth");
				float percentage = Math.Clamp((health + 1) / maxHealth, 0.0F, 1.0F);

				Vec3d position = entity.Pos.XYZ;
				Vec3d cameraPosition = Main.clientApi.World.Player.Entity.Pos.XYZ;

				mvMatrix
					.Set(Main.clientApi.Render.CurrentModelviewMatrix)
					.Translate(
						position.X - cameraPosition.X,
						position.Y + entity.SelectionBox.Y2 + 0.3F - cameraPosition.Y,
						position.Z - cameraPosition.Z
					)
					.Scale(1.5F, 1.0F, 1.0F);

				CompileOrUpdatePrograms();

				healthProgram.Use();

				healthProgram.UniformMatrix("projectionMatrix", Main.clientApi.Render.CurrentProjectionMatrix);
				healthProgram.UniformMatrix("modelViewMatrix", mvMatrix.Values);
				healthProgram.Uniform("color", new Vec4f(0, 0, 0, 0.7F));

				Main.clientApi.Render.RenderMesh(backgroundRectRef);

				mvMatrix
					.Set(Main.clientApi.Render.CurrentModelviewMatrix)
					.Translate(
						position.X - cameraPosition.X,
						position.Y + entity.SelectionBox.Y2 + 0.3F - cameraPosition.Y,
						position.Z - cameraPosition.Z
					)
					.Scale(1.5F * percentage, 0.15F, 1);

				healthProgram.UniformMatrix("modelViewMatrix", mvMatrix.Values);
				healthProgram.Uniform("color", new Vec4f(1, 0, 0, 1));

				Main.clientApi.Render.RenderMesh(healthRectRef);

				healthProgram.Stop();
			}

			private void CompileOrUpdatePrograms()
			{
				if ((healthProgram == null) || healthProgram.Disposed)
				{
					healthProgram = Main.clientApi.Shader.NewShaderProgram();
					healthProgram.AssetDomain = "apprentice";
					healthProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					healthProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("health-shader", healthProgram);
					healthProgram.Compile();
				}
			}
		}
		internal class ObamaPrism : IRenderer
		{
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

			private IShaderProgram obamaProgram = null!;

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

			public ObamaPrism(int obamaCount)
			{
				// Create obamas
				for (int i = 0; i < obamaCount; i++)
				{
					obamas.Add(new Obama(i, random));
				}

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
				meshRef = Main.clientApi.Render.UploadMesh(mesh);

				// Create obama texture
				obamaTexture = Main.clientApi.Render.GetOrLoadTexture(new AssetLocation("apprentice", "textures/real-obama.png"));

				// Register renderer
				Main.clientApi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
			}

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (stage != EnumRenderStage.Opaque) return;
				if (meshRef == null) return;

				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
				EntityPos transform = entityPlayer.Pos;

				if (obamaEnable)
				{
					CompileOrUpdatePrograms();

					Main.clientApi.Render.GlDisableStencilTest();
					Main.clientApi.Render.GlToggleBlend(false);
					Main.clientApi.Render.GlEnableCullFace();

					// Make it obama
					Main.clientApi.Render.CurrentFrameBuffer = Main.clientApi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
					obamaProgram.Use();
					obamaProgram.UniformMatrix("projectionMatrix", Main.clientApi.Render.CurrentProjectionMatrix);
					obamaProgram.UniformMatrix("viewMatrix", Main.clientApi.Render.CurrentModelviewMatrix);
					obamaProgram.BindTexture2D("tex", obamaTexture, 0);
					foreach (Obama obama in obamas)
					{
						obamaProgram.UniformMatrix("modelMatrix", obama.transform.Values);
						Main.clientApi.Render.RenderMesh(meshRef);
					}
					obamaProgram.Stop();

					Main.clientApi.Render.GlToggleBlend(true);
					Main.clientApi.Render.GlEnableStencilTest();
					Main.clientApi.Render.GlDisableCullFace();
				}
			}

			// TODO: Why is this update here..
			//       change it into OnRenderFrame
			public void Update(float deltaTime)
			{
				EntityPlayer entityPlayer = Main.clientApi.World.Player.Entity;
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
				// Unregister renderer
				Main.clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

				// Destroy textures
				Main.clientApi.Render.GLDeleteTexture(obamaTexture);
			}

			private void CompileOrUpdatePrograms()
			{
				if ((obamaProgram == null) || obamaProgram.Disposed)
				{
					obamaProgram = Main.clientApi.Shader.NewShaderProgram();
					obamaProgram.AssetDomain = "apprentice";
					obamaProgram.VertexShader = Main.clientApi.Shader.NewShader(EnumShaderType.VertexShader);
					obamaProgram.FragmentShader = Main.clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
					Main.clientApi.Shader.RegisterFileShaderProgram("obama-shader", obamaProgram);
					obamaProgram.Compile();
				}
			}
		}
	}
}
