using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice.src.weapons
{
	internal class Uchigatana
	{
		public float deltaAcc = 0.0F;

		public Uchigatana(ICoreClientAPI capi, long entityId)
		{
			Entity? entity = capi.World.GetEntityById(entityId);
			EntityShapeRenderer? renderer = entity?.Properties.Client.Renderer as EntityShapeRenderer;

			IAnimationManager? animManager = entity?.AnimManager;

			IDictionary<string, AnimationMetaData>? animMetaDataByName = animManager?.ActiveAnimationsByAnimCode;

			animMetaDataByName?["test"].AnimationSpeed = 0.5F;

			RunningAnimation? runAnim = animManager?.GetAnimationState("");

			IAnimator? animator = animManager?.Animator;

			ElementPose elemPose =  animator.GetPosebyName("Root");

			// deltaAcc += 0.05F;
			// elemPose.translateY += (float)Math.Sin(deltaAcc) * 10.0F;
			// if (deltaAcc > Math.Tau)
			// {
			// 	deltaAcc = 0.0F;
			// }

			animator.CalculateMatrices = true;

			AttachmentPointAndPose? rootPose = animator.GetAttachmentPointPose("Root");

			// rootPose.

			// animManager.

			// renderer.
		}
	}

	internal class UchigatanaDashBehaviour : EntityBehavior
	{
		EntityPlayer? entityPlayer = null;

		private bool Init { get; set; }
		private bool Playing { get; set; }

		private float DeltaAcc { get; set; }

		public UchigatanaDashBehaviour(ICoreClientAPI capi, Entity entity) : base(entity)
		{
			entityPlayer = capi.World.Player.Entity;

			capi.Input.RegisterHotKey("play_blood_scythe_anim", "Play a test sequence", GlKeys.F, HotkeyType.MovementControls);
			capi.Input.SetHotKeyHandler("play_blood_scythe_anim", OnReset);
		}

		public override string PropertyName()
		{
			return "UchigatanaDashBehaviourName";
		}
		public override void OnGameTick(float deltaTime)
		{
			if (entityPlayer == null) return;

			if (Playing)
			{
				if (Init)
				{
					Init = false;

					// entity.StartAnimation(""); // TODO
					// entity.StopAnimation..
				}

				DeltaAcc += deltaTime;

				EntityPos position = entityPlayer.Pos;

				position.Y += (float)Math.Sin(DeltaAcc) * 10.0F;

				if (DeltaAcc > Math.Tau)
				{
					Playing = false;
					Init = true;
					DeltaAcc = 0.0F;
				}

				entityPlayer.Pos.SetPos(position.X, position.Y, position.Z);
			}

			// entity.ApplyGravity
			// entity.requirePosesOnServer // TODO: check this flag..
		}

		private bool OnReset(KeyCombination combination)
		{
			Playing = true;
			Init = true;

			return true;
		}
	}
}
