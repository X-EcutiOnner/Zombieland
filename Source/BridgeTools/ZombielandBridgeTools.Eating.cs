using HarmonyLib;
using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/zombie_eats_corpse_contract", Description = "Run a real Stumble job beside a flesh corpse and verify the source-derived eating delay removes a body part only when corpse eating is enabled.")]
		public static object ZombieEatsCorpseContract()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var settingsSnapshot = SnapshotZombieSettings();
			var allCasesSucceeded = true;

			bool TryFindEatingFixtureCells(IntVec3 rootCell, out IntVec3 zombieCell, out IntVec3 corpseCell, out object error)
			{
				zombieCell = IntVec3.Invalid;
				corpseCell = IntVec3.Invalid;
				error = null;
				bool HasPawnOrCorpse(IntVec3 cell)
					=> cell.GetThingList(map).Any(thing => thing is Pawn || thing is Corpse);
				bool NeighborhoodIsClear(IntVec3 center, IntVec3 allowedTarget)
				{
					foreach (var cell in GenRadial.RadialCellsAround(center, 2.1f, true))
					{
						if (cell.InBounds(map) == false)
							continue;
						if (cell == allowedTarget)
							continue;
						if (HasPawnOrCorpse(cell))
							return false;
					}
					return true;
				}
				foreach (var candidate in GenRadial.RadialCellsAround(rootCell, 20f, true))
				{
					if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
						continue;
					if (HasPawnOrCorpse(candidate))
						continue;
					foreach (var offset in GenAdj.AdjacentCellsAround)
					{
						var adjacent = candidate + offset;
						if (adjacent.InBounds(map) == false || adjacent.Standable(map) == false || adjacent.Fogged(map))
							continue;
						if (HasPawnOrCorpse(adjacent))
							continue;
						if (NeighborhoodIsClear(candidate, adjacent) == false)
							continue;
						zombieCell = candidate;
						corpseCell = adjacent;
						return true;
					}
				}

				error = new
				{
					success = false,
					error = $"No adjacent zombie/corpse eating fixture cells were found near ({rootCell.x}, {rootCell.z})."
				};
				return false;
			}

			static int MissingPartCount(Pawn pawn)
				=> pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_MissingPart>().Count() ?? 0;

			object RunCase(string name, bool eatCorpses, IntVec3 caseRoot)
			{
				if (TryFindEatingFixtureCells(caseRoot, out var zombieCell, out var corpseCell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombiesEatDowned = false;
					settings.zombiesEatCorpses = eatCorpses;
				});

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no eating test zombie.",
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
					};
				}

				zombie.story.bodyType = BodyTypeDefOf.Fat;
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.raging = 0;

				var corpsePawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(corpsePawn, corpseCell, map, WipeMode.Vanish);
				DisablePawnWork(corpsePawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(corpsePawn, out var corpse, out var corpseError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						zombie = DescribeZombie(zombie),
						corpsePawn = DescribePawn(corpsePawn),
						error = corpseError
					};
				}
				corpse.SetForbidden(false, false);

				var missingBefore = MissingPartCount(corpse.InnerPawn);
				var eatDelayTicks = Constants.EAT_DELAY_TICKS / 4;
				var maxTicks = eatCorpses ? eatDelayTicks + 8 : 8;
				var tickHit = -1;
				var samples = new List<object>();
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var driver = zombie.jobs?.curDriver as JobDriver_Stumble;
					var missingNow = MissingPartCount(corpse.InnerPawn);
					if (tick == 1 || tick == maxTicks || tick % 60 == 0 || missingNow > missingBefore)
					{
						samples.Add(new
						{
							tick,
							zombieJob = zombie.CurJobDef?.defName,
							eatTarget = driver?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.eatTarget),
							eatDelayCounter = driver?.eatDelayCounter ?? -1,
							eatDelay = driver?.eatDelay ?? -1,
							missingParts = missingNow,
							corpseSpawned = corpse.Spawned,
							corpseDestroyed = corpse.Destroyed
						});
					}

					if (missingNow > missingBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var driverAfter = zombie.jobs?.curDriver as JobDriver_Stumble;
				var missingAfter = MissingPartCount(corpse.InnerPawn);
				var success = eatCorpses
					? tickHit > 0 && missingAfter > missingBefore && ReferenceEquals(driverAfter?.eatTarget, corpse)
					: tickHit == -1 && missingAfter == missingBefore && driverAfter?.eatTarget == null;
				allCasesSucceeded &= success;

				var result = new
				{
					name,
					success,
					eatCorpses,
					sourcePath = "JobDriver_Stumble.TickAction -> ZombieStateHandler.Eat -> CanIngest -> EatDelay -> EatBodyPart",
					sourceDerivedTicks = new
					{
						baseEatDelay = Constants.EAT_DELAY_TICKS,
						bodyType = zombie.story.bodyType?.defName,
						eatDelayTicks,
						maxTicks
					},
					zombie = DescribeZombie(zombie),
					corpse = new
					{
						id = ZombieRuntimeActions.StableThingId(corpse),
						cell = ZombieRuntimeActions.DescribeCell(corpse.Position),
						spawned = corpse.Spawned,
						destroyed = corpse.Destroyed,
						innerPawn = DescribePawn(corpse.InnerPawn)
					},
					cells = new
					{
						zombie = ZombieRuntimeActions.DescribeCell(zombieCell),
						corpse = ZombieRuntimeActions.DescribeCell(corpseCell)
					},
					missingBefore,
					missingAfter,
					missingDelta = missingAfter - missingBefore,
					tickHit,
					finalEatTarget = driverAfter?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driverAfter.eatTarget),
					finalEatDelayCounter = driverAfter?.eatDelayCounter ?? -1,
					samples
				};

				if (zombie.Destroyed == false)
					zombie.Destroy();
				if (corpse.Destroyed == false)
					corpse.Destroy();
				return result;
			}

			try
			{
				var disabledCase = RunCase("corpseEatingDisabled", false, root + new IntVec3(-8, 0, 8));
				var enabledCase = RunCase("corpseEatingEnabled", true, root + new IntVec3(8, 0, 8));
				return new
				{
					success = allCasesSucceeded,
					destroyedZombies,
					cases = new[] { disabledCase, enabledCase }
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		[Tool("zombieland/zombie_eats_downed_pawn_contract", Description = "Run a real Stumble job beside a downed flesh human and verify the source-derived eating delay removes a body part only when downed-pawn eating is enabled.")]
		public static object ZombieEatsDownedPawnContract()
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

			var targetKind = PawnKindDefOf.Colonist;
			if (targetKind == null)
			{
				return new
				{
					success = false,
					error = "PawnKindDef Colonist was not found."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var settingsSnapshot = SnapshotZombieSettings();
			var allCasesSucceeded = true;

			bool TryFindEatingFixtureCells(IntVec3 rootCell, out IntVec3 zombieCell, out IntVec3 targetCell, out object error)
			{
				zombieCell = IntVec3.Invalid;
				targetCell = IntVec3.Invalid;
				error = null;
				bool HasPawnOrCorpse(IntVec3 cell)
					=> cell.GetThingList(map).Any(thing => thing is Pawn || thing is Corpse);
				bool NeighborhoodIsClear(IntVec3 center, IntVec3 allowedTarget)
				{
					foreach (var cell in GenRadial.RadialCellsAround(center, 2.1f, true))
					{
						if (cell.InBounds(map) == false)
							continue;
						if (cell == allowedTarget)
							continue;
						if (HasPawnOrCorpse(cell))
							return false;
					}
					return true;
				}
				foreach (var candidate in GenRadial.RadialCellsAround(rootCell, 20f, true))
				{
					if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
						continue;
					if (HasPawnOrCorpse(candidate))
						continue;
					foreach (var offset in GenAdj.AdjacentCellsAround)
					{
						var adjacent = candidate + offset;
						if (adjacent.InBounds(map) == false || adjacent.Standable(map) == false || adjacent.Fogged(map))
							continue;
						if (HasPawnOrCorpse(adjacent))
							continue;
						if (NeighborhoodIsClear(candidate, adjacent) == false)
							continue;
						zombieCell = candidate;
						targetCell = adjacent;
						return true;
					}
				}

				error = new
				{
					success = false,
					error = $"No adjacent zombie/downed-pawn eating fixture cells were found near ({rootCell.x}, {rootCell.z})."
				};
				return false;
			}

			static int MissingPartCount(Pawn pawn)
				=> pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_MissingPart>().Count() ?? 0;

			static bool TryMakeDowned(Pawn pawn, out string error)
			{
				error = null;
				var bloodLoss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
				bloodLoss.Severity = 0.45f;
				pawn.health.hediffSet.AddDirect(bloodLoss);
				var anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
				anesthetic.Severity = 1f;
				pawn.health.hediffSet.AddDirect(anesthetic);

				var makeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
				if (makeDowned == null)
				{
					error = "Could not reflect Pawn_HealthTracker.MakeDowned.";
					return false;
				}
				makeDowned.Invoke(pawn.health, new object[makeDowned.GetParameters().Length]);
				if (pawn.health.Downed == false)
				{
					error = "Pawn_HealthTracker.MakeDowned did not leave the pawn downed.";
					return false;
				}
				return true;
			}

			object RunCase(string name, bool eatDowned, IntVec3 caseRoot)
			{
				if (TryFindEatingFixtureCells(caseRoot, out var zombieCell, out var targetCell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombiesEatDowned = eatDowned;
					settings.zombiesEatCorpses = false;
					settings.attackMode = AttackMode.OnlyColonists;
				});

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no downed-pawn eating test zombie.",
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
					};
				}

				zombie.story.bodyType = BodyTypeDefOf.Fat;
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.raging = 0;

				var target = PawnGenerator.GeneratePawn(targetKind, Faction.OfPlayer);
				GenSpawn.Spawn(target, targetCell, map, WipeMode.Vanish);
				if (TryMakeDowned(target, out var downedError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						zombie = DescribeZombie(zombie),
						target = DescribePawn(target),
						error = downedError
					};
				}

				var missingBefore = MissingPartCount(target);
				var eatDelayTicks = Constants.EAT_DELAY_TICKS / 4;
				var maxTicks = eatDowned ? eatDelayTicks + 8 : 8;
				var tickHit = -1;
				var samples = new List<object>();
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var driver = zombie.jobs?.curDriver as JobDriver_Stumble;
					var missingNow = MissingPartCount(target);
					if (tick == 1 || tick == maxTicks || tick % 60 == 0 || missingNow > missingBefore || target.Dead)
					{
						samples.Add(new
						{
							tick,
							zombieJob = zombie.CurJobDef?.defName,
							eatTarget = driver?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.eatTarget),
							eatDelayCounter = driver?.eatDelayCounter ?? -1,
							eatDelay = driver?.eatDelay ?? -1,
							targetDowned = target.health?.Downed ?? false,
							targetDead = target.Dead,
							targetSpawned = target.Spawned,
							missingParts = missingNow
						});
					}

					if (missingNow > missingBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var driverAfter = zombie.jobs?.curDriver as JobDriver_Stumble;
				var missingAfter = MissingPartCount(target);
				var success = eatDowned
					? tickHit > 0 && missingAfter > missingBefore && ReferenceEquals(driverAfter?.eatTarget, target)
					: tickHit == -1 && missingAfter == missingBefore && driverAfter?.eatTarget == null;
				allCasesSucceeded &= success;

				var result = new
				{
					name,
					success,
					eatDowned,
					attackMode = ZombieSettings.Values.attackMode.ToString(),
					sourcePath = "JobDriver_Stumble.TickAction -> ZombieStateHandler.Eat -> CanIngest(downed pawn) -> EatDelay -> EatBodyPart",
					sourceDerivedTicks = new
					{
						baseEatDelay = Constants.EAT_DELAY_TICKS,
						bodyType = zombie.story.bodyType?.defName,
						eatDelayTicks,
						maxTicks
					},
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					cells = new
					{
						zombie = ZombieRuntimeActions.DescribeCell(zombieCell),
						target = ZombieRuntimeActions.DescribeCell(targetCell)
					},
					targetKind = targetKind.defName,
					targetDowned = target.health?.Downed ?? false,
					targetDead = target.Dead,
					missingBefore,
					missingAfter,
					missingDelta = missingAfter - missingBefore,
					tickHit,
					finalEatTarget = driverAfter?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driverAfter.eatTarget),
					finalEatDelayCounter = driverAfter?.eatDelayCounter ?? -1,
					samples
				};

				if (zombie.Destroyed == false)
					zombie.Destroy();
				if (target.Corpse is { Destroyed: false } corpse)
					corpse.Destroy();
				if (target.Destroyed == false)
					target.Destroy();
				return result;
			}

			try
			{
				var disabledCase = RunCase("downedEatingDisabled", false, root + new IntVec3(-8, 0, -8));
				var enabledCase = RunCase("downedEatingEnabled", true, root + new IntVec3(8, 0, -8));
				return new
				{
					success = allCasesSucceeded,
					destroyedZombies,
					cases = new[] { disabledCase, enabledCase }
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}
	}
}
