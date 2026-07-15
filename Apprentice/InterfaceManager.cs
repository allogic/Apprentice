using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using Cairo;

namespace Apprentice
{
	internal class SkillTreeBranch
	{
		public Vec2d Position;
		public Vec2d Direction;
		public float Length;

		public SkillTreeBranch? Parent = null;
		public List<SkillTreeBranch> Children = [];

		public Vec2d End => Position + Direction * Length;

		public SkillTreeBranch(Vec2d position, Vec2d direction, float length, SkillTreeBranch? parent = null)
		{
			Position = position;
			Direction = direction;
			Length = length;
			Parent = parent;
		}
	}

	internal class SkillTree
	{
		private Random Rand = new();

		private List<SkillTreeBranch> Branches = [];
		private List<Vec2d> Attractors = [];

		public SkillTree()
		{
			AddBaseBranches(8);
			AddRandomAttractors(400);

			GrowTree();
		}

		public void AddBaseBranches(uint numBranches)
		{
			double step = Math.Tau / numBranches;

			for (uint i = 0; i < numBranches; i++)
			{
				double radius = 100;
				double angle = i * step;
				double x = Math.Sin(angle) * radius;
				double y = Math.Cos(angle) * radius;

				Vec2d position = new(x, y);
				Vec2d dir = (new Vec2d(0, 0) - position).Normalize();

				Branches.Add(new SkillTreeBranch(position, dir, 10));
			}
		}
		public void AddRandomAttractors(uint numAttractors)
		{
			for (uint i = 0; i < numAttractors; i++)
			{
				double radius = Rand.NextDouble() * 500;
				double angle = Rand.NextDouble() * Math.Tau;
				double x = Math.Cos(angle) * radius;
				double y = Math.Sin(angle) * radius;

				Attractors.Add(new Vec2d(x, y));
			}
		}

		public void GrowTree()
		{
			while (Attractors.Count > 0)
			{
				List<SkillTreeBranch> newBranches = [];

				foreach (SkillTreeBranch branch in Branches)
				{
					// Compute growth based on near attractors
					Vec2d growth = new();
					foreach (Vec2d attractor in Attractors)
					{
						Vec2d dir = attractor - branch.End;

						if (dir.Length() < 100)
						{
							growth += dir.Normalize();
						}
					}

					if (growth.Length() > 0)
					{
						Vec2d dir = growth.Normalize();

						newBranches.Add(new SkillTreeBranch(branch.End, dir, 10, branch));
					}
				}

				// Link new branches
				foreach (SkillTreeBranch branch in newBranches)
				{
					branch.Parent?.Children.Add(branch);
					Branches.Add(branch);
				}

				// Remove all attractors near branches
				Attractors.RemoveAll(attractor =>
				{
					foreach (SkillTreeBranch branch in Branches)
					{
						Vec2d dir = attractor - branch.End;

						if (dir.Length() < 100)
						{
							return true;
						}
					}

					return false;
				});
			}
		}
		public void DrawTree(Context ctx, double x, double y)
		{
			foreach (SkillTreeBranch branch in Branches)
			{
				DrawTreeRecursive(ctx, x, y, branch);
			}

			foreach (Vec2d attractor in Attractors)
			{
				ctx.LineWidth = 1;

				ctx.SetSourceRGBA(0, 1, 0, 1);
				ctx.Arc(x + attractor.X, y + attractor.Y, 2, 0, Math.Tau);
				ctx.Fill();
			}
		}

		private void DrawTreeRecursive(Context ctx, double x, double y, SkillTreeBranch branch)
		{
			ctx.LineWidth = 1;

			ctx.SetSourceRGBA(1, 0, 0, 1);
			ctx.MoveTo(x + branch.Position.X, y + branch.Position.Y);
			ctx.LineTo(x + branch.End.X, y + branch.End.Y);
			ctx.Stroke();

			foreach (SkillTreeBranch child in branch.Children)
			{
				DrawTreeRecursive(ctx, x, y, child);
			}
		}
	}

	internal class GuiElementSkillTree : GuiElement
	{
		private SkillTree skillTree = new SkillTree();

		public GuiElementSkillTree(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
		{

		}

		public override void ComposeElements(Context ctx, ImageSurface surface)
		{
			base.ComposeElements(ctx, surface);

			double centerX = Bounds.OuterWidth / 2;
			double centerY = Bounds.OuterHeight / 2;

			skillTree.DrawTree(ctx, centerX, centerY);
		}

		// private void DrawNode(Context ctx, double x, double y, double radius)
		// {
		// 	ctx.LineWidth = 2;
		// 
		// 	ctx.SetSourceRGBA(1, 0, 0, 1);
		// 	ctx.Arc(x, y, radius, 0, Math.Tau);
		// 	ctx.Stroke();
		// }
	}

	internal class SkillTreeDialog : GuiDialog
	{
		public override string ToggleKeyCombinationCode => "skill_tree";
		public override bool PrefersUngrabbedMouse => false;
		public override bool Focusable => true;

		public SkillTreeDialog(ICoreClientAPI capi) : base(capi)
		{

		}

		public override void OnGuiOpened()
		{
			base.OnGuiOpened();

			ElementBounds bounds = ElementBounds.Fill.WithFixedPadding(0);

			SingleComposer = capi.Gui.CreateCompo("Skill Tree", bounds)
				.AddStaticElement(new GuiElementSkillTree(capi, bounds))
				.Compose();
		}
	}

	internal class InterfaceManager
	{
		private readonly ICoreClientAPI? capi = null;
		private readonly GuiDialog? dialog = null;

		public InterfaceManager(ICoreClientAPI capi)
		{
			this.capi = capi;

			dialog = new SkillTreeDialog(capi);

			capi.Input.RegisterHotKey("skill_tree", "", GlKeys.U, HotkeyType.GUIOrOtherControls);
			capi.Input.SetHotKeyHandler("skill_tree", OnToggleExperienceDialog);
		}

		private bool OnToggleExperienceDialog(KeyCombination comb)
		{
			if (dialog == null) return true;
			if (dialog.IsOpened()) dialog.TryClose();
			else dialog.TryOpen();
			return true;
		}
	}
}
