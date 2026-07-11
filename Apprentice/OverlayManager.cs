#pragma warning disable CS8602

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Apprentice
{
	internal class ExpBarRenderer : IRenderer
	{
		public double RenderOrder { get { return 0; } }
		public int RenderRange { get { return 10; } }

		private readonly ICoreClientAPI? api = null;
		private readonly MeshRef whiteRectangleRef;
		private readonly MeshRef progressQuadRef;
		private readonly Matrixf mvMatrix = new();

		public ExpBarRenderer(ICoreClientAPI api)
		{
			this.api = api;

			whiteRectangleRef = api.Render.UploadMesh(LineMeshUtil.GetRectangle(ColorUtil.WhiteArgb));
			progressQuadRef = api.Render.UploadMesh(QuadMeshUtil.GetQuad());
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			IShaderProgram currShader = api.Render.CurrentActiveShader;

			Vec4f color = new(1, 1, 1, 1);

			currShader.Uniform("rgbaIn", color);
			currShader.Uniform("extraGlow", 0);
			currShader.Uniform("applyColor", 0);
			currShader.Uniform("tex2d", 0);
			currShader.Uniform("noTexture", 1f);

			mvMatrix.Set(api.Render.CurrentModelviewMatrix)
				.Translate(10, 10, 50)
				.Scale(1000, 20, 0)
				.Translate(0.5f, 0.5f, 0)
				.Scale(0.5f, 0.5f, 0);

			currShader.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
			currShader.UniformMatrix("modelViewMatrix", mvMatrix.Values);

			api.Render.RenderMesh(whiteRectangleRef);

			EntityPlayer playerEntity = api.World.Player.Entity;

			// TODO: introduce proper bounds..
			float exp = (float)playerEntity.WatchedAttributes.GetDouble("exp");
			float width = (exp * 100.0f) % 1000;

			mvMatrix.Set(api.Render.CurrentModelviewMatrix)
				.Translate(10, 10, 50)
				.Scale(width, 20, 0)
				.Translate(0.5f, 0.5f, 0)
				.Scale(0.5f, 0.5f, 0)
			;

			currShader.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
			currShader.UniformMatrix("modelViewMatrix", mvMatrix.Values);

			api.Render.RenderMesh(progressQuadRef);
		}

		public void Dispose()
		{
			api.Render.DeleteMesh(whiteRectangleRef);
			api.Render.DeleteMesh(progressQuadRef);
		}
	}

	internal class OverlayManager
	{
		private readonly ICoreClientAPI? api = null;
		private readonly ExpBarRenderer? expBarRenderer = null;

		public OverlayManager(ICoreClientAPI api)
		{
			this.api = api;

			expBarRenderer = new ExpBarRenderer(api);

			api.Event.RegisterRenderer(expBarRenderer, EnumRenderStage.Ortho);
		}
	}
}
