using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	static class ZombieRenderCompat
	{
		sealed class CustomPawnGraphics
		{
			public Graphic Body;
			public Graphic Head;
		}

		static readonly ConditionalWeakTable<Pawn, CustomPawnGraphics> customGraphics = new();

		public static Pawn Pawn(PawnRenderer renderer)
		{
			if (renderer == null)
				return null;
			if (renderer.renderTree?.pawn != null)
				return renderer.renderTree.pawn;
			return AccessTools.Field(typeof(PawnRenderer), "pawn")?.GetValue(renderer) as Pawn;
		}

		public static void SetDirty(Pawn pawn)
		{
			pawn?.Drawer?.renderer?.SetAllGraphicsDirty();
			pawn?.Drawer?.renderer?.renderTree?.SetDirty();
		}

		public static void ResolveAllGraphics(Pawn pawn)
		{
			pawn?.Drawer?.renderer?.EnsureGraphicsInitialized();
			pawn?.Drawer?.renderer?.renderTree?.EnsureInitialized(pawn.Drawer.renderer.DefaultRenderFlagsNow);
		}

		public static void SetBodyGraphic(Pawn pawn, Graphic graphic)
		{
			if (pawn != null)
				customGraphics.GetValue(pawn, _ => new CustomPawnGraphics()).Body = graphic;
			SetDirty(pawn);
		}

		public static void SetHeadGraphic(Pawn pawn, Graphic graphic)
		{
			if (pawn != null)
				customGraphics.GetValue(pawn, _ => new CustomPawnGraphics()).Head = graphic;
			SetDirty(pawn);
		}

		public static bool TryGetBodyGraphic(Pawn pawn, out Graphic graphic)
		{
			graphic = null;
			return pawn != null
				&& customGraphics.TryGetValue(pawn, out var graphics)
				&& (graphic = graphics.Body) != null;
		}

		public static bool TryGetHeadGraphic(Pawn pawn, out Graphic graphic)
		{
			graphic = null;
			return pawn != null
				&& customGraphics.TryGetValue(pawn, out var graphics)
				&& (graphic = graphics.Head) != null;
		}

		public static void RemoveApparelGraphic(Pawn pawn, ThingDef apparelDef)
		{
			SetDirty(pawn);
		}

		public static void DrawPawn(Pawn pawn, PawnRenderer renderer, Vector3 drawLoc, Rot4 facing, RotDrawMode rotDrawMode, PawnRenderFlags flags)
		{
			if (pawn == null || renderer?.renderTree == null)
				return;

			var parms = PawnDrawParms.DefaultFor(pawn);
			parms.matrix = Matrix4x4.TRS(drawLoc, Quaternion.identity, Vector3.one);
			parms.facing = facing;
			parms.rotDrawMode = rotDrawMode;
			parms.flags = flags;
			renderer.renderTree.Draw(parms);
		}
	}
}
