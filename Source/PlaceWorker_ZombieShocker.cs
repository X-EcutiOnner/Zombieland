﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class PlaceWorker_ZombieShocker : PlaceWorker
	{
		public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
		{
			var map = Find.CurrentMap;
			var cell = center + IntVec3.North.RotatedBy(rot);
			var room = cell.GetRoom(map);
			if (room == null || room.IsHuge || room.Fogged || room.IsDoorway)
			{
				GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Color.white, null);
				return;
			}
			GenDraw.DrawFieldEdges(room.Cells.Where(c => c.Standable(map)).ToList(), new Color(1f, 1f, 1f, 0.5f), null);
		}

		public override AcceptanceReport AllowsPlacing(BuildableDef def, IntVec3 center, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
		{
			var msg = "WrongPlacementZombieShocker".Translate();

			var cell = center + IntVec3.North.RotatedBy(rot);
			var room = cell.GetRoom(map);
			if (room == null || room.IsHuge || room.Fogged || room.IsDoorway)
				return msg;

			cell = center + IntVec3.North.RotatedBy(rot);
			if (cell.Standable(map) == false)
				return msg;

			return true;
		}
	}
}
