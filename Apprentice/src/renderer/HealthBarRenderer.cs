#pragma warning disable IDE0130
#pragma warning disable IDE0290

using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice
{
	internal class HealthBarRenderer : IRenderer
	{
		public double RenderOrder { get { return 1; } }
		public int RenderRange { get { return 10; } }

		private readonly ICoreClientAPI capi;

		private readonly MeshRef backgroundRectRef;
		private readonly MeshRef healthRectRef;

		private readonly Matrixf mvMatrix = new();

		private readonly IShaderProgram program;

		public HealthBarRenderer(ICoreClientAPI capi)
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
}
