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

		[Tool("zombieland/infection_medical_state", Description = "Run compact medical patch contracts for zombie-bite natural healing, Pawn.Tick state sync, ShouldRemove persistence, and remove-body-part surgery targeting.")]
		public static object InfectionMedicalState()
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

			var spawnedPawns = new List<Pawn>();
			var oldInfectionChance = ZombieSettings.Values.zombieBiteInfectionChance;
			try
			{
				ZombieSettings.Values.zombieBiteInfectionChance = 1f;
				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 16f, out var patientCell, out var spawnError) == false)
					return spawnError;

				var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(patient, patientCell, map, WipeMode.Vanish);
				DisablePawnWork(patient);
				spawnedPawns.Add(patient);

				if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var hiddenBite, out var error) == false)
					return InfectionMedicalFailure(patient, null, error);
				ForceHiddenBite(hiddenBite);
				if (ZombieRuntimeActions.AddZombieBite(patient, "harmless", out var harmlessBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);
				if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var infectableBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);
				if (ZombieRuntimeActions.AddZombieBite(patient, "final", out var infectingBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);

				var ordinaryCut = CreateOrdinaryCut(patient);
				var animalCase = CreateAnimalBiteCase(map, patient.Position, spawnedPawns);

				var healCases = new[]
				{
					DescribeNaturalHealingCase("hidden", hiddenBite),
					DescribeNaturalHealingCase("harmless", harmlessBite),
					DescribeNaturalHealingCase("infectable", infectableBite),
					DescribeNaturalHealingCase("infecting", infectingBite),
					DescribeNaturalHealingCase("ordinaryCut", ordinaryCut)
				};
				var animalHealCase = animalCase.bite == null ? null : DescribeNaturalHealingCase("animalZombieBite", animalCase.bite);

				hiddenBite.Severity = 0f;
				harmlessBite.Severity = 0f;
				infectableBite.Severity = 0f;
				infectingBite.Severity = 0f;
				if (ordinaryCut != null)
					ordinaryCut.Severity = 0f;

				var removalCases = new[]
				{
					DescribeShouldRemoveCase("hidden", hiddenBite),
					DescribeShouldRemoveCase("harmless", harmlessBite),
					DescribeShouldRemoveCase("infectable", infectableBite),
					DescribeShouldRemoveCase("infecting", infectingBite),
					DescribeShouldRemoveCase("ordinaryCut", ordinaryCut)
				};

				var removePartWorker = RecipeDefOf.RemoveBodyPart.Worker;
				var parts = removePartWorker.GetPartsToApplyOn(patient, RecipeDefOf.RemoveBodyPart).ToArray();
				var bittenParts = new[] { hiddenBite, harmlessBite, infectableBite, infectingBite }
					.Select(bite => bite.Part)
					.Where(part => part != null)
					.Distinct()
					.ToArray();
				var duplicateParts = parts
					.Where(part => part != null)
					.GroupBy(part => part)
					.Where(group => group.Count() > 1)
					.Select(group => DescribeBodyPart(group.Key))
					.ToArray();
				var missingBittenParts = bittenParts
					.Where(part => parts.Contains(part) == false)
					.Select(DescribeBodyPart)
					.ToArray();

				var naturalHealingValid = healCases.First(c => c.name == "hidden").canHealNaturally == false
					&& healCases.First(c => c.name == "harmless").canHealNaturally
					&& healCases.First(c => c.name == "infectable").canHealNaturally == false
					&& healCases.First(c => c.name == "infecting").canHealNaturally == false
					&& healCases.First(c => c.name == "ordinaryCut").canHealNaturally
					&& (animalHealCase == null || animalHealCase.canHealNaturally);
				var shouldRemoveValid = removalCases.First(c => c.name == "hidden").shouldRemove == false
					&& removalCases.First(c => c.name == "harmless").shouldRemove
					&& removalCases.First(c => c.name == "infectable").shouldRemove == false
					&& removalCases.First(c => c.name == "infecting").shouldRemove == false
					&& removalCases.First(c => c.name == "ordinaryCut").shouldRemove;
				var removeBodyPartValid = bittenParts.Length > 0
					&& missingBittenParts.Length == 0
					&& duplicateParts.Length == 0;
				var pawnTick = VerifyPawnTickPatch(map, patient.Position + new IntVec3(12, 0, 0), spawnedPawns);

				return new
				{
					success = naturalHealingValid && shouldRemoveValid && removeBodyPartValid && ObjectSuccess(pawnTick),
					patient = DescribePawn(patient),
					infection = ZombieRuntimeActions.DescribePawnInfection(patient),
					naturalHealingValid,
					shouldRemoveValid,
					removeBodyPartValid,
					pawnTick,
					healCases,
					animalHealCase,
					animalCase = animalCase.description,
					removalCases,
					removeBodyPart = new
					{
						totalPartCount = parts.Length,
						bittenParts = bittenParts.Select(DescribeBodyPart).ToArray(),
						missingBittenParts,
						duplicateParts,
						firstParts = parts.Take(12).Select(DescribeBodyPart).ToArray()
					}
				};
			}
			finally
			{
				ZombieSettings.Values.zombieBiteInfectionChance = oldInfectionChance;
				foreach (var pawn in spawnedPawns.Where(pawn => pawn != null && pawn.Destroyed == false).ToArray())
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyPawnTickPatch(Map map, IntVec3 root, List<Pawn> spawnedPawns)
		{
			var tickMethod = typeof(Pawn).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
			if (tickMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find protected Verse.Pawn.Tick() by reflection."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var infectionCell, out var infectionSpawnError) == false)
				return infectionSpawnError;
			if (TryFindClearSpawnCell(map, infectionCell + new IntVec3(4, 0, 0), 16f, out var cleanCell, out var cleanSpawnError) == false)
				return cleanSpawnError;
			if (TryFindClearSpawnCell(map, cleanCell + new IntVec3(4, 0, 0), 16f, out var contaminatedCell, out var contaminatedSpawnError) == false)
				return contaminatedSpawnError;

			var oldContamination = Constants.CONTAMINATION;
			var oldEffectivenessPercentage = ZombieSettings.Values.contamination.contaminationEffectivenessPercentage;
			var spawnedThings = new List<Thing>();
			try
			{
				Constants.CONTAMINATION = true;
				ZombieSettings.Values.contamination.contaminationEffectivenessPercentage = 0.95f;

				var infectionPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var cleanPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var contaminatedPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(infectionPawn, infectionCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(cleanPawn, cleanCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(contaminatedPawn, contaminatedCell, map, WipeMode.Vanish);
				spawnedPawns.AddRange(new[] { infectionPawn, cleanPawn, contaminatedPawn });
				DisablePawnWork(infectionPawn);
				DisablePawnWork(cleanPawn);
				DisablePawnWork(contaminatedPawn);

				if (ZombieRuntimeActions.AddZombieBite(infectionPawn, "final", out var bite, out var biteError) == false)
				{
					return new
					{
						success = false,
						pawn = DescribePawn(infectionPawn),
						error = biteError
					};
				}

				var biteState = bite.TendDuration?.GetInfectionState() ?? InfectionState.None;
				var customStateBefore = infectionPawn.InfectionState();
				tickMethod.Invoke(infectionPawn, Array.Empty<object>());
				var customStateAfter = infectionPawn.InfectionState();
				var infectionStateUpdated = customStateBefore == InfectionState.None
					&& customStateAfter == biteState
					&& biteState == InfectionState.Infecting;

				if (TryEquipRunningChainsaw(cleanPawn, cleanCell, spawnedThings, out var cleanChainsaw, out var cleanRefuelable, out var cleanError) == false)
					return cleanError;
				if (TryEquipRunningChainsaw(contaminatedPawn, contaminatedCell, spawnedThings, out var contaminatedChainsaw, out var contaminatedRefuelable, out var contaminatedError) == false)
					return contaminatedError;

				contaminatedPawn.SetContamination(0.7f);
				var cleanEffectiveness = cleanPawn.GetEffectiveness();
				var contaminatedEffectiveness = contaminatedPawn.GetEffectiveness();
				var cleanFuelBefore = cleanRefuelable.Fuel;
				var contaminatedFuelBefore = contaminatedRefuelable.Fuel;

				const int probeTicks = 40;
				Rand.PushState(94031);
				try
				{
					for (var i = 0; i < probeTicks; i++)
						tickMethod.Invoke(cleanPawn, Array.Empty<object>());
					for (var i = 0; i < probeTicks; i++)
						tickMethod.Invoke(contaminatedPawn, Array.Empty<object>());
				}
				finally
				{
					Rand.PopState();
				}

				var cleanFuelAfter = cleanRefuelable.Fuel;
				var contaminatedFuelAfter = contaminatedRefuelable.Fuel;
				var cleanFuelDelta = cleanFuelBefore - cleanFuelAfter;
				var contaminatedFuelDelta = contaminatedFuelBefore - contaminatedFuelAfter;
				var contaminationGateReducedTicks = cleanFuelDelta > 0f
					&& contaminatedPawn.Spawned
					&& contaminatedPawn.Dead == false
					&& contaminatedPawn.Downed == false
					&& contaminatedEffectiveness < cleanEffectiveness
					&& contaminatedFuelDelta >= 0f
					&& contaminatedFuelDelta < cleanFuelDelta * 0.5f;

				return new
				{
					success = infectionStateUpdated && contaminationGateReducedTicks,
					infection = new
					{
						pawn = DescribePawn(infectionPawn),
						biteState = biteState.ToString(),
						customStateBefore = customStateBefore.ToString(),
						customStateAfter = customStateAfter.ToString(),
						infectionStateUpdated
					},
					contamination = new
					{
						probeTicks,
						cleanPawn = DescribePawn(cleanPawn),
						contaminatedPawn = DescribePawn(contaminatedPawn),
						contaminatedPawnSpawnedAfterTicks = contaminatedPawn.Spawned,
						contaminatedPawnDeadAfterTicks = contaminatedPawn.Dead,
						contaminatedPawnDownedAfterTicks = contaminatedPawn.Downed,
						cleanEffectiveness,
						contaminatedEffectiveness,
						cleanChainsaw = ZombieRuntimeActions.StableThingId(cleanChainsaw),
						contaminatedChainsaw = ZombieRuntimeActions.StableThingId(contaminatedChainsaw),
						cleanFuelBefore,
						cleanFuelAfter,
						cleanFuelDelta,
						contaminatedFuelBefore,
						contaminatedFuelAfter,
						contaminatedFuelDelta,
						contaminationGateReducedTicks
					}
				};
			}
			finally
			{
				Constants.CONTAMINATION = oldContamination;
				ZombieSettings.Values.contamination.contaminationEffectivenessPercentage = oldEffectivenessPercentage;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static bool TryEquipRunningChainsaw(Pawn pawn, IntVec3 pawnCell, List<Thing> spawnedThings, out Chainsaw chainsaw, out CompRefuelable refuelable, out object error)
		{
			chainsaw = null;
			refuelable = null;
			error = null;
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var chainsawCell = pawnCell + IntVec3.East;
			if (chainsawCell.InBounds(pawn.Map) == false || chainsawCell.Standable(pawn.Map) == false)
				chainsawCell = pawnCell;

			chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				error = new
				{
					success = false,
					pawn = DescribePawn(pawn),
					error = "Could not create Chainsaw."
				};
				return false;
			}

			GenSpawn.Spawn(chainsaw, chainsawCell, pawn.Map, WipeMode.Vanish);
			refuelable = chainsaw.TryGetComp<CompRefuelable>();
			if (refuelable == null)
			{
				error = new
				{
					success = false,
					pawn = DescribePawn(pawn),
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have a refuelable comp."
				};
				return false;
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, chainsawCell + IntVec3.South, pawn.Map, WipeMode.Vanish);
			spawnedThings.Add(fuel);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			pawn.equipment.AddEquipment(chainsaw);
			chainsaw.StartMotor(true);
			return true;
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

			sealed class MedicalHealCase
			{
				public string name { get; set; }
				public string hediffDef { get; set; }
				public string part { get; set; }
				public float severity { get; set; }
				public string infectionState { get; set; }
				public bool canHealNaturally { get; set; }
			}

			sealed class MedicalRemovalCase
			{
				public string name { get; set; }
				public string hediffDef { get; set; }
				public string part { get; set; }
				public float severity { get; set; }
				public string infectionState { get; set; }
				public bool shouldRemove { get; set; }
			}

			static object InfectionMedicalFailure(Pawn patient, Pawn animal, string error)
			{
				return new
				{
					success = false,
					patient = DescribePawn(patient),
					animal = DescribePawn(animal),
					error
				};
			}

			static void ForceHiddenBite(Hediff_Injury_ZombieBite bite)
			{
				var infector = bite?.TendDuration?.ZombieInfector;
				if (infector == null)
					return;

				infector.infectionKnownDelay = GenTicks.TicksAbs + GenDate.TicksPerHour;
				infector.infectionStartTime = infector.infectionKnownDelay + GenDate.TicksPerHour;
				infector.infectionEndTime = infector.infectionStartTime + GenDate.TicksPerHour;
			}

			static Hediff_Injury CreateOrdinaryCut(Pawn pawn)
			{
				var part = pawn.health.hediffSet
					.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
					.Where(part => part.def.IsSolid(part, pawn.health.hediffSet.hediffs) == false)
					.FirstOrDefault();
				if (part == null)
					return null;

				var cut = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
				cut.Severity = 2f;
				pawn.health.AddHediff(cut, part, new DamageInfo(DamageDefOf.Cut, 2f));
				return cut;
			}

			static (Hediff_Injury_ZombieBite bite, object description) CreateAnimalBiteCase(Map map, IntVec3 near, List<Pawn> spawnedPawns)
			{
				var animalKind = DefDatabase<PawnKindDef>.AllDefs
					.FirstOrDefault(kind => kind.race?.race?.Animal == true && kind.race.race.IsFlesh);
				if (animalKind == null)
				{
					return (null, new
					{
						success = false,
						reason = "No flesh animal pawn kind was available."
					});
				}
				if (TryFindClearSpawnCell(map, near + new IntVec3(3, 0, 0), 10f, out var animalCell, out var cellError) == false)
				{
					return (null, new
					{
						success = false,
						reason = "No animal spawn cell was available.",
						cellError
					});
				}

				var animal = PawnGenerator.GeneratePawn(animalKind);
				GenSpawn.Spawn(animal, animalCell, map, WipeMode.Vanish);
				spawnedPawns.Add(animal);
				var part = animal.health.hediffSet
					.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
					.Where(part => part.def.IsSolid(part, animal.health.hediffSet.hediffs) == false)
					.FirstOrDefault();
				if (part == null)
				{
					return (null, new
					{
						success = false,
						animal = DescribePawn(animal),
						reason = "The animal had no valid non-solid bite part."
					});
				}

				var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(HediffDef.Named("ZombieBite"), animal, part);
				bite.Severity = 2f;
				animal.health.AddHediff(bite, part, new DamageInfo(CustomDefs.ZombieBite, 2f));
				return (bite, new
				{
					success = true,
					animal = DescribePawn(animal),
					part = DescribeBodyPart(part),
					infectionState = bite.TendDuration?.GetInfectionState().ToString()
				});
			}

			static MedicalHealCase DescribeNaturalHealingCase(string name, Hediff_Injury hediff)
			{
				var bite = hediff as Hediff_Injury_ZombieBite;
				return new MedicalHealCase
				{
					name = name,
					hediffDef = hediff?.def?.defName,
					part = DescribeBodyPart(hediff?.Part),
					severity = hediff?.Severity ?? 0f,
					infectionState = bite?.TendDuration?.GetInfectionState().ToString(),
					canHealNaturally = hediff != null && HediffUtility.CanHealNaturally(hediff)
				};
			}

			static MedicalRemovalCase DescribeShouldRemoveCase(string name, Hediff_Injury hediff)
			{
				var bite = hediff as Hediff_Injury_ZombieBite;
				return new MedicalRemovalCase
				{
					name = name,
					hediffDef = hediff?.def?.defName,
					part = DescribeBodyPart(hediff?.Part),
					severity = hediff?.Severity ?? 0f,
					infectionState = bite?.TendDuration?.GetInfectionState().ToString(),
					shouldRemove = hediff?.ShouldRemove ?? false
				};
			}

			static string DescribeBodyPart(BodyPartRecord part)
			{
				return part == null ? null : $"{part.def?.defName}:{part.Label}";
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
