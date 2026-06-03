using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class ZombieCorpse : Corpse
	{
		public static Type type = typeof(ZombieCorpse);
		public ZombieCorpseAppearance appearance;

		public override bool IngestibleNow => false;

		public ZombieCorpseAppearance Appearance
		{
			get
			{
				if (appearance == null && InnerPawn is Zombie zombie)
					appearance = ZombieCorpseAppearance.From(zombie);
				return appearance;
			}
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);

			if (InnerPawn is Zombie zombie)
				appearance ??= ZombieCorpseAppearance.From(zombie);
			if (InnerPawn != null)
				InnerPawn.Rotation = Rot4.Random;
			this.SetForbidden(false, false);

			GetComps<CompRottable>()
				.Select(comp => comp.props)
				.OfType<CompProperties_Rottable>()
				.Do(rotcomp =>
				{
					var t = (float)ZombieSettings.Values.corpsesHoursToDessicated / GenDate.HoursPerDay;
					rotcomp.daysToRotStart = t / 2f;
					rotcomp.daysToDessicated = t;
				});

			var tickManager = map.GetComponent<TickManager>();
			tickManager.allZombieCorpses.Add(this);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			try
			{
				var tickManager = Map.GetComponent<TickManager>();
				_ = tickManager?.allZombieCorpses.Remove(this);
			}
			catch
			{
			}

			if (InnerPawn is Zombie zombie)
				zombie.Dispose();

			try
			{
				base.Destroy(mode);
			}
			catch
			{
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref appearance, "zombieCorpseAppearance");
		}

		public override void DrawExtraSelectionOverlays()
		{
		}

		public override void DrawGUIOverlay()
		{
		}

		public override void TickRare()
		{
			var comps = AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTickRare();

			if (Spawned && Bugged == false)
			{
				switch (this.GetRotStage())
				{
					case RotStage.Rotting:
						var num = GasUtility.RotStinkToSpawnForCorpse(this);
						if (num > 0)
							GasUtility.AddGas(Position, Map, GasType.RotStink, num);
						break;
					case RotStage.Dessicated:
						Destroy(DestroyMode.Vanish);
						return;
				}

				if (Map.thingGrid.ThingsListAtFast(Position).Any(thing => thing is Blueprint || thing is Frame))
				{
					Destroy(DestroyMode.Vanish);
					return;
				}
			}

			if (InnerPawn == null)
				return;

			comps = InnerPawn.AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTickRare();
		}
	}
}
