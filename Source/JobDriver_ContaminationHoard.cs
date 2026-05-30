using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ContaminationHoard : JobDriver
	{
		public enum State
		{
			findThing,
			moveToThing,
			moveToStorage,
		}

		public List<IntVec3> storage = new();
		public HashSet<Thing> rejectedThings = new();
		public Building_Bed bed;
		public Room room;
		public Thing thing;
		public IntVec3 cell = IntVec3.Invalid;
		public State state;

		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref storage, "storage", LookMode.Value);
			Scribe_References.Look(ref bed, "bed");
			Scribe_References.Look(ref thing, "thing");
			Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
			Scribe_Values.Look(ref state, "state", State.findThing);

			var rejectedThingsList = rejectedThings?
				.Where(thing => thing != null && thing.Spawned && thing.Destroyed == false)
				.ToList();
			Scribe_Collections.Look(ref rejectedThingsList, "rejectedThings", LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				storage ??= new List<IntVec3>();
				rejectedThings = rejectedThingsList?.Where(rejected => rejected != null).ToHashSet() ?? new HashSet<Thing>();
				EnsureRoom();
			}
		}

		void EnsureRoom()
		{
			if (room != null || Map == null)
				return;
			bed ??= Map.listerBuildings?.allBuildingsColonist
				.OfType<Building_Bed>()
				.FirstOrDefault(bed => bed.GetAssignedPawn() == pawn);
			room = bed?.GetRoom(RegionType.Set_All);
		}

		Thing FindNextThing()
		{
			var things = Map.regionGrid.allRooms
				.Where(r => r.IsHuge == false && r != room)
				.SelectMany(room => room.ContainedAndAdjacentThings)
				.Where(t => rejectedThings.Contains(t) == false
					&& t.def.EverHaulable
					&& pawn.holdingOwner.CanAcceptAnyOf(t, true)
					&& pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly))
				.ToList();
			if (things.Count == 0)
				return null;
			return things.RandomElementByWeightWithDefault(thing => thing.MarketValue, 0f);
		}

		void InitAction()
		{
			bed = Map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()
				.FirstOrDefault(bed => bed.GetAssignedPawn() == pawn);
			room = bed?.GetRoom(RegionType.Set_All);
			if (room == null)
			{
				EndJobWith(JobCondition.Succeeded);
				return;
			}

			storage = room.Cells.Where(cell => cell.Standable(Map) && pawn.CanReach(cell, PathEndMode.ClosestTouch, Danger.Deadly)).ToList();
			if (storage.Count == 0)
			{
				EndJobWith(JobCondition.Succeeded);
				return;
			}

			state = State.findThing;
		}

		void TickAction()
		{
			EnsureRoom();

			if (state == State.findThing)
			{
				cell = storage.RandomElement();
				storage.Remove(cell);
				thing = FindNextThing();
				if (thing == null || storage.Count == 0)
				{
					EndJobWith(JobCondition.Succeeded);
					return;
				}
				state = State.moveToThing;
			}

			if (state == State.moveToThing)
			{
				if (thing == null || thing.Destroyed)
				{
					state = State.findThing;
					return;
				}
				if (pawn.pather.Moving == false)
					pawn.pather.StartPath(thing.Position, PathEndMode.ClosestTouch);
				return;
			}

			if (state == State.moveToStorage)
			{
				if (pawn.carryTracker.CarriedThing == null)
				{
					thing = null;
					state = State.findThing;
					return;
				}
				if (pawn.pather.Moving == false)
					pawn.pather.StartPath(cell, PathEndMode.ClosestTouch);
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();

			switch (state)
			{
				case State.moveToThing:
				{
					int num = pawn.carryTracker.AvailableStackSpace(thing.def);
					int num2 = Mathf.Min(num, thing.stackCount);
					if (pawn.carryTracker.TryStartCarry(thing, num2, true) > 0)
						state = State.moveToStorage;
					else
						state = State.findThing;
					break;
				}
				case State.moveToStorage:
				{
					var slotGroup = pawn.Map.haulDestinationManager.SlotGroupAt(cell);
					if (slotGroup != null && slotGroup.Settings.AllowedToAccept(pawn.carryTracker.CarriedThing))
						pawn.Map.designationManager.TryRemoveDesignationOn(pawn.carryTracker.CarriedThing, DesignationDefOf.Haul);
					if (!pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _, null))
						_ = pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _);
					state = State.findThing;
					break;
				}
			}
		}

		public override void Notify_PatherFailed()
		{
			if (state == State.moveToThing)
			{
				if (thing != null)
					rejectedThings.Add(thing);
				thing = null;
				state = State.findThing;
				return;
			}

			if (state == State.moveToStorage)
			{
				_ = pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
				thing = null;
				state = State.findThing;
				return;
			}

			EndJobWith(JobCondition.Succeeded);
		}
	}
}
