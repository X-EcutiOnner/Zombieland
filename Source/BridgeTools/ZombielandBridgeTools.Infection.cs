using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/remove_all_zombies", Description = "Destroy all spawned Zombieland pawns on the current map and clear the cached zombie set.")]
		public static object RemoveAllZombies()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			return new
			{
				success = true,
				destroyed = ZombieRuntimeActions.DestroyZombies(map)
			};
		}

		[Tool("zombieland/get_pawn_infection", Description = "Read compact zombie bite and infection state for a spawned non-zombie pawn.")]
		public static object GetPawnInfection([ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target)
		{
			var map = CurrentMap;
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			return new
			{
				success = true,
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/apply_zombie_bite", Description = "Apply a Zombieland bite to a spawned pawn and return the resulting infection state.")]
		public static object ApplyZombieBite(
			[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
			[ToolParameter(Description = "Bite state to apply: harmful, final, or harmless.", Required = false, DefaultValue = "harmful")] string stage = "harmful")
		{
			var map = CurrentMap;
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (ZombieRuntimeActions.AddZombieBite(pawn, stage, out var bite, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			return new
			{
				success = true,
				biteLabel = bite.LabelCap,
				stage = stage ?? "harmful",
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/remove_pawn_infections", Description = "Make zombie bites harmless and remove active Zombieland infection hediffs from a spawned pawn.")]
		public static object RemovePawnInfections([ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target)
		{
			var map = CurrentMap;
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			return new
			{
				success = true,
				removedInfectionHediffs = ZombieRuntimeActions.RemoveZombieInfections(pawn),
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/cure_zombie_infection_recipe", Description = "Apply the real cure-infection recipe worker with 100% serum and verify the cured corpse no longer queues conversion.")]
		public static object CureZombieInfectionRecipe()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var doctorCell, out var doctorSpawnError) == false)
				return doctorSpawnError;

			var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(doctor, doctorCell, map, WipeMode.Vanish);
			if (TryFindAdjacentClearCell(doctor, out var patientCell) == false
				&& TryFindClearSpawnCell(map, doctor.Position, 8f, out patientCell, out var patientSpawnError) == false)
				return patientSpawnError;

			var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(patient, patientCell, map, WipeMode.Vanish);

			if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var bite, out var error) == false)
			{
				return new
				{
					success = false,
					patient = DescribePawn(patient),
					error
				};
			}

			var recipe = CustomDefs.CureZombieInfection;
			var worker = recipe?.Worker;
			var partsBefore = worker?.GetPartsToApplyOn(patient, recipe).ToArray() ?? Array.Empty<BodyPartRecord>();
			var serumDef = DefDatabase<ThingDef>.GetNamed("ZombieSerumSimple", false);
			if (recipe == null || worker == null || serumDef == null || partsBefore.Length == 0)
			{
				return new
				{
					success = false,
					doctor = DescribePawn(doctor),
					patient = DescribePawn(patient),
					infection = ZombieRuntimeActions.DescribePawnInfection(patient),
					recipeFound = recipe != null,
					workerFound = worker != null,
					serumFound = serumDef != null,
					curablePartCount = partsBefore.Length,
					error = "The cure recipe fixture could not find a recipe worker, serum, or curable bite part."
				};
			}

			var part = partsBefore.First();
			var serum = ThingMaker.MakeThing(serumDef);
			var infectionBefore = ZombieRuntimeActions.DescribePawnInfection(patient);
			worker.ApplyOnPawn(patient, part, doctor, new List<Thing> { serum }, null);
			var infectionAfter = ZombieRuntimeActions.DescribePawnInfection(patient);
			var partsAfter = worker.GetPartsToApplyOn(patient, recipe).ToArray();
			var biteStateAfter = bite.TendDuration?.GetInfectionState().ToString();
			var mayBecomeZombieWhenDeadAfter = bite.mayBecomeZombieWhenDead;

			if (ZombieRuntimeActions.KillPawnToCorpse(patient, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					doctor = DescribePawn(doctor),
					patient = DescribePawn(patient),
					infectionBefore,
					infectionAfter,
					error
				};
			}

			var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
			var queueCountBeforeRot = queue?.Count ?? -1;
			var queuedBeforeRot = queue?.Contains(corpse) ?? false;
			var rotTriggered = ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error);
			var queueCountAfterRot = queue?.Count ?? -1;
			var queuedAfterRot = queue?.Contains(corpse) ?? false;

			return new
			{
				success = partsBefore.Length > 0
					&& partsAfter.Length == 0
					&& mayBecomeZombieWhenDeadAfter == false
					&& rotTriggered
					&& queuedBeforeRot == false
					&& queuedAfterRot == false,
				doctor = DescribePawn(doctor),
				patientCorpse = DescribeCorpse(corpse),
				doctorCell = ZombieRuntimeActions.DescribeCell(doctorCell),
				patientCell = ZombieRuntimeActions.DescribeCell(patientCell),
				biteLabel = bite.LabelCap,
				curedPart = part.def?.defName,
				infectionBefore,
				infectionAfter,
				biteStateAfter,
				mayBecomeZombieWhenDeadAfter,
				curablePartCountBefore = partsBefore.Length,
				curablePartCountAfter = partsAfter.Length,
				serumDef = serum.def.defName,
				rotTriggered,
				rotStageBefore = rotStageBefore.ToString(),
				rotStageAfter = rotStageAfter.ToString(),
				rotError = error,
				queueCountBeforeRot,
				queueCountAfterRot,
				queuedBeforeRot,
				queuedAfterRot
			};
		}

		[Tool("zombieland/convert_pawn_to_zombie", Description = "Convert a spawned non-zombie pawn to a Zombieland zombie and return before/after state for smoke tests.")]
		public static object ConvertPawnToZombie(
			[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
			[ToolParameter(Description = "Pass true to force conversion even if the pawn normally would not convert.", Required = false, DefaultValue = true)] bool force = true)
		{
			var map = CurrentMap;
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			if (pawn is Zombie || pawn is ZombieBlob || pawn is ZombieSpitter)
			{
				return new
				{
					success = false,
					error = "Target is already a Zombieland pawn."
				};
			}

			var before = CurrentZombies(map);
			var beforeIds = new HashSet<string>(before.Select(ZombieRuntimeActions.StableThingId));
			var targetId = ZombieRuntimeActions.StableThingId(pawn);
			var targetThingId = pawn.ThingID;
			var targetLabel = pawn.LabelCap;

			ZombieRuntimeActions.ConvertPawnToZombie(pawn, map, force);

			var after = CurrentZombies(map);
			var newZombies = after
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = newZombies.Length > 0,
				targetId,
				targetThingId,
				targetLabel,
				force,
				beforeCount = before.Length,
				afterCount = after.Length,
				newZombieCount = newZombies.Length,
				newZombies
			};
		}

	}
}
