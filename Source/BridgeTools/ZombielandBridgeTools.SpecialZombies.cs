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
		[Tool("zombieland/fix_broken_chainsaw_job", Description = "Break a spawned chainsaw, run the real FixBrokenChainsaw workgiver/job with a component, and verify repair.")]
		public static object FixBrokenChainsawJob()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			actor.skills?.GetSkill(SkillDefOf.Construction).Notify_SkillDisablesChanged();
			actor.skills.GetSkill(SkillDefOf.Construction).Level = 20;

			if (TryFindAdjacentClearCell(actor, out var chainsawCell) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No adjacent cell was available for the broken chainsaw."
				};
			}

			var componentCell = actorCell + IntVec3.South;
			if (componentCell.InBounds(map) == false || componentCell.Standable(map) == false)
				componentCell = actorCell;

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}
			GenSpawn.Spawn(chainsaw, chainsawCell, map, WipeMode.Vanish);
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have a breakable comp."
				};
			}
			breakable.DoBreakdown(map);
			map.areaManager.Home[chainsaw.Position] = true;
			chainsaw.SetForbidden(false, false);

			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			component.stackCount = 1;
			GenSpawn.Spawn(component, componentCell, map, WipeMode.Vanish);
			component.SetForbidden(false, false);

			var manager = map.GetComponent<BrokenManager>();
			var workGiver = new WorkGiver_FixBrokenChainsaw();
			var hasJob = workGiver.HasJobOnThing(actor, chainsaw, true);
			var job = hasJob ? workGiver.JobOnThing(actor, chainsaw, true) : null;
			if (job != null)
				job.playerForced = true;

			var started = job != null && actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var maxTicks = 1250;
			var tickHit = -1;
			var samples = new List<object>();

			Rand.PushState(3);
			try
			{
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var brokenNow = breakable.broken;
					if (tick == 1 || tick == maxTicks || tick % 200 == 0 || brokenNow == false)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							broken = brokenNow,
							componentSpawned = component.Spawned,
							managerBrokenCount = manager?.brokenThings?.Count ?? 0
						});
					}

					if (brokenNow == false)
					{
						tickHit = tick;
						break;
					}
				}
			}
			finally
			{
				Rand.PopState();
			}

			var trackedAfter = manager?.brokenThings?.Contains(chainsaw) ?? false;

			return new
			{
				success = hasJob
					&& job != null
					&& started
					&& tickHit > 0
					&& breakable.broken == false
					&& trackedAfter == false
					&& component.Destroyed,
				destroyedZombies,
				actor = DescribePawn(actor),
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					cell = ZombieRuntimeActions.DescribeCell(chainsawCell),
					spawned = chainsaw.Spawned,
					faction = chainsaw.Faction?.Name,
					forbidden = chainsaw.IsForbidden(actor),
					breakable.broken,
					trackedAsBroken = trackedAfter
				},
				component = new
				{
					id = ZombieRuntimeActions.StableThingId(component),
					cell = ZombieRuntimeActions.DescribeCell(componentCell),
					spawned = component.Spawned,
					destroyed = component.Destroyed
				},
				hasJob,
				jobDef = job?.def?.defName,
				started,
				maxTicks,
				tickHit,
				samples
			};
		}

		[Tool("zombieland/damage_dark_slimer", Description = "Apply real bullet damage to a dark slimer and verify the damage-worker patch creates custom TarSmoke.")]
		public static object DamageDarkSlimer(
			[ToolParameter(Description = "Optional dark slimer zombie id, ThingID, label, or short name. When omitted, a fresh dark slimer is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Bullet damage amount.", Required = false, DefaultValue = 1)] int damage = 1)
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

			Zombie darkSlimer;
			var spawnedDarkSlimer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				darkSlimer = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.DarkSlimer, true);
				spawnedDarkSlimer = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				darkSlimer = pawn as Zombie;
			}

			if (darkSlimer == null || darkSlimer.isDarkSlimer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(darkSlimer),
					error = "Target is not a dark slimer."
				};
			}

			var cappedDamage = Math.Max(1, Math.Min(damage, 20));
			var position = darkSlimer.Position;
			var smokeRadius = 1f + Tools.Difficulty();
			var countRadius = smokeRadius + 1f;
			var ticksToRun = Math.Max(1, (int)Math.Ceiling(smokeRadius * 1.5f) + 2);
			var tarSmokeThingsBefore = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionBefore = position.GetGas(map)?.def?.defName;
			var before = DescribeZombie(darkSlimer);
			var dinfo = new DamageInfo(DamageDefOf.Bullet, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var damageResult = darkSlimer.TakeDamage(dinfo);
			AdvanceGameTicks(ticksToRun);
			var tarSmokeThingsAfter = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionAfter = position.GetGas(map)?.def?.defName;

			return new
			{
				success = tarSmokeThingsAfter > tarSmokeThingsBefore && gasAtPositionAfter == CustomDefs.TarSmoke.defName,
				spawnedDarkSlimer,
				damage = cappedDamage,
				damageTotal = damageResult.totalDamageDealt,
				smokeRadius,
				countRadius,
				ticksToRun,
				position = ZombieRuntimeActions.DescribeCell(position),
				gasAtPositionBefore,
				gasAtPositionAfter,
				tarSmokeThingsBefore,
				tarSmokeThingsAfter,
				tarSmokeThingDelta = tarSmokeThingsAfter - tarSmokeThingsBefore,
				before,
				after = DescribeZombie(darkSlimer)
			};
		}

		[Tool("zombieland/tar_smoke_blocks_ranged_targeting", Description = "Verify real TarSmoke from damaging a dark slimer blocks a real ranged verb from targeting that zombie.")]
		public static object TarSmokeBlocksRangedTargeting()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear line-of-sight target cell was found for the TarSmoke targeting fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No test ranged weapon def was available."
				};
			}
			actor.equipment.AddEquipment(weapon);
			actor.drafter.Drafted = true;

			var darkSlimer = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.DarkSlimer, true);
			if (darkSlimer == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					error = "ZombieGenerator.SpawnZombie returned no dark slimer."
				};
			}

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					weaponDef = weaponDef.defName,
					error = "The equipped ranged weapon had no primary verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, darkSlimer);
			var aimChanceBeforeSmoke = ShotReport.HitReportFor(actor, verb, darkSlimer).AimOnTargetChance_StandardTarget;
			var gasAtTargetBefore = darkSlimer.Position.GetGas(map)?.def?.defName;
			var tarSmokeThingsBefore = CountThingsNear(map, darkSlimer.Position, CustomDefs.TarSmoke, 3f);
			var damageResult = darkSlimer.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 1, 0f, -1f, actor, null, weaponDef, DamageInfo.SourceCategory.ThingOrUnknown, darkSlimer, true, true));
			AdvanceGameTicks(5);
			var gasAtTargetAfter = darkSlimer.Position.GetGas(map)?.def?.defName;
			var tarSmokeThingsAfter = CountThingsNear(map, darkSlimer.Position, CustomDefs.TarSmoke, 3f);
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, darkSlimer);
			var aimChanceAfterSmoke = ShotReport.HitReportFor(actor, verb, darkSlimer).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& tarSmokeThingsAfter > tarSmokeThingsBefore
					&& canHitAfterSmoke == false
					&& aimChanceAfterSmoke == 0f,
				destroyedZombies,
				actor = DescribePawn(actor),
				darkSlimer = DescribeZombie(darkSlimer),
				weaponDef = weaponDef.defName,
				verbLabel = verb.verbProps?.label,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceAfterSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter,
				tarSmokeThingsBefore,
				tarSmokeThingsAfter,
				tarSmokeDelta = tarSmokeThingsAfter - tarSmokeThingsBefore,
				damageTotal = damageResult.totalDamageDealt
			};
		}

		[Tool("zombieland/tar_smoke_blocks_human_ranged_targeting", Description = "Verify TarSmoke blocks ranged targeting for ordinary human targets too, matching its dense visual-obstruction role.")]
		public static object TarSmokeBlocksHumanRangedTargeting()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear line-of-sight human target cell was found for the TarSmoke targeting fixture."
				};
			}

			ClearGasAt(map, targetCell);
			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No test ranged weapon def was available."
				};
			}
			actor.equipment.AddEquipment(weapon);
			actor.drafter.Drafted = true;

			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(target, targetCell, map, Rot4.South);
			DisablePawnWork(target);

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					weaponDef = weaponDef.defName,
					error = "The equipped ranged weapon had no primary verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceBeforeSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;
			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceAfterSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke == false
					&& aimChanceAfterSmoke == 0f,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				weaponDef = weaponDef.defName,
				verbLabel = verb.verbProps?.label,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceAfterSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter
			};
		}

		[Tool("zombieland/sticky_goo_toxic_buildup_contract", Description = "Move a real colonist off StickyGoo and verify the Position patch applies source-derived toxic buildup.")]
		public static object StickyGooToxicBuildupContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var startCell, out var spawnError) == false)
				return spawnError;

			static float ToxicBuildupSeverity(Pawn pawn)
			{
				return pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup)?.Severity ?? 0f;
			}

			static void RemoveStickyGooAt(Map map, IntVec3 cell)
			{
				foreach (var thing in cell.GetThingList(map).Where(thing => thing.def == CustomDefs.StickyGoo).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}

			bool TryFindMovePair(Pawn pawn, out IntVec3 cleanDestination, out IntVec3 gooDestination)
			{
				cleanDestination = IntVec3.Invalid;
				gooDestination = IntVec3.Invalid;
				foreach (var cleanOffset in GenAdj.AdjacentCells)
				{
					var cleanCandidate = pawn.Position + cleanOffset;
					if (cleanCandidate.InBounds(map) == false || cleanCandidate.Standable(map) == false || cleanCandidate.Fogged(map))
						continue;
					if (cleanCandidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
						continue;
					foreach (var gooOffset in GenAdj.AdjacentCells)
					{
						var gooCandidate = cleanCandidate + gooOffset;
						if (gooCandidate == pawn.Position)
							continue;
						if (gooCandidate.InBounds(map) == false || gooCandidate.Standable(map) == false || gooCandidate.Fogged(map))
							continue;
						if (gooCandidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
							continue;
						cleanDestination = cleanCandidate;
						gooDestination = gooCandidate;
						return true;
					}
				}
				return false;
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, startCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, true);
			if (TryFindMovePair(actor, out var cleanDestination, out var gooDestination) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No two-step movement fixture was available for StickyGoo toxic buildup."
				};
			}

			RemoveStickyGooAt(map, startCell);
			RemoveStickyGooAt(map, cleanDestination);
			RemoveStickyGooAt(map, gooDestination);

			var beforeCleanMove = ToxicBuildupSeverity(actor);
			actor.Position = cleanDestination;
			actor.Notify_Teleported(false, false);
			var afterCleanMove = ToxicBuildupSeverity(actor);

			var madeGoo = FilthMaker.TryMakeFilth(cleanDestination, map, CustomDefs.StickyGoo, actor.Name?.ToStringShort, 1);
			var stickyGooCount = cleanDestination.GetThingList(map).Count(thing => thing.def == CustomDefs.StickyGoo);
			var expectedPerFilth = 0.023006668f * Mathf.Max(1f - actor.GetStatValue(StatDefOf.ToxicResistance, true, -1), 0f);
			if (ModsConfig.BiotechActive)
				expectedPerFilth *= Mathf.Max(1f - actor.GetStatValue(StatDefOf.ToxicEnvironmentResistance, true, -1), 0f);
			var expectedDelta = expectedPerFilth * stickyGooCount;

			var beforeGooMove = ToxicBuildupSeverity(actor);
			actor.Position = gooDestination;
			actor.Notify_Teleported(false, false);
			var afterGooMove = ToxicBuildupSeverity(actor);
			var cleanDelta = afterCleanMove - beforeCleanMove;
			var gooDelta = afterGooMove - beforeGooMove;
			var tolerance = 0.0001f;

			return new
			{
				success = Mathf.Abs(cleanDelta) <= tolerance
					&& madeGoo
					&& stickyGooCount > 0
					&& expectedDelta > 0f
					&& Mathf.Abs(gooDelta - expectedDelta) <= tolerance,
				sourcePath = "Thing.Position setter prefix -> StickyGoo at pawn.Position -> HealthUtility.AdjustSeverity(ToxicBuildup)",
				actor = DescribePawn(actor),
				cells = new
				{
					start = ZombieRuntimeActions.DescribeCell(startCell),
					cleanDestination = ZombieRuntimeActions.DescribeCell(cleanDestination),
					gooDestination = ZombieRuntimeActions.DescribeCell(gooDestination)
				},
				madeGoo,
				stickyGooCount,
				expectedPerFilth,
				expectedDelta,
				beforeCleanMove,
				afterCleanMove,
				cleanDelta,
				beforeGooMove,
				afterGooMove,
				gooDelta
			};
		}

		[Tool("zombieland/mine_with_miner", Description = "Place a mineable block next to a miner zombie and verify Zombieland's mining code damages it.")]
		public static object MineWithMiner(
			[ToolParameter(Description = "Optional miner zombie id, ThingID, label, or short name. When omitted, a fresh miner is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
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

			Zombie miner;
			var spawnedMiner = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				miner = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Miner, true);
				spawnedMiner = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				miner = pawn as Zombie;
			}

			if (miner == null || miner.isMiner == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "Target is not a miner."
				};
			}

			if (TryFindAdjacentClearCell(miner, out var mineableCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "No clear adjacent cell was found for the mineable test block."
				};
			}

			var mineable = GenSpawn.Spawn(ThingDefOf.MineableSteel, mineableCell, map, WipeMode.Vanish) as Mineable;
			if (mineable == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					cell = ZombieRuntimeActions.DescribeCell(mineableCell),
					error = "Spawning MineableSteel did not produce a Mineable."
				};
			}

			var hitPointsBefore = mineable.HitPoints;
			var miningCounterBefore = miner.miningCounter;
			var mined = ZombieStateHandler.Mine(null, miner, true);
			var mineableDestroyed = mineable.Destroyed;
			var hitPointsAfter = mineableDestroyed ? 0 : mineable.HitPoints;
			var miningCounterAfter = miner.miningCounter;

			return new
			{
				success = mined && hitPointsAfter < hitPointsBefore && miningCounterAfter > miningCounterBefore,
				spawnedMiner,
				mined,
				miner = DescribeZombie(miner),
				mineableCell = ZombieRuntimeActions.DescribeCell(mineableCell),
				mineableDef = mineable.def.defName,
				mineableDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				miningCounterBefore,
				miningCounterAfter
			};
		}

		[Tool("zombieland/mine_with_miner_job", Description = "Put a mineable in a miner's wander direction and verify the real Stumble job mines it.")]
		public static object MineWithMinerJob(
			[ToolParameter(Description = "Optional miner zombie id, ThingID, label, or short name. When omitted, a fresh miner is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
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

			Zombie miner;
			var spawnedMiner = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				miner = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Miner, true);
				spawnedMiner = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				miner = pawn as Zombie;
			}

			if (miner == null || miner.isMiner == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "Target is not a miner."
				};
			}

			if (TryFindAdjacentClearCell(miner, out var mineableCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "No clear adjacent cell was found for the mineable test block."
				};
			}

			var mineable = GenSpawn.Spawn(ThingDefOf.MineableSteel, mineableCell, map, WipeMode.Vanish) as Mineable;
			if (mineable == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					cell = ZombieRuntimeActions.DescribeCell(mineableCell),
					error = "Spawning MineableSteel did not produce a Mineable."
				};
			}

			var bodyTypeBefore = miner.story?.bodyType?.defName;
			if (miner.story != null)
				miner.story.bodyType = BodyTypeDefOf.Male;
			miner.pather?.StopDead();
			miner.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			miner.state = ZombieState.Wandering;
			miner.wanderDestination = mineableCell;
			miner.miningCounter = 0;
			var clearedPheromoneRadius = 2f;
			ClearPheromones(map, miner.Position, clearedPheromoneRadius);

			var before = DescribeZombie(miner);
			var hitPointsBefore = mineable.HitPoints;
			var samples = new List<object>();
			miner.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (miner.jobs.curDriver is JobDriver_Stumble stumbleDriver)
				stumbleDriver.destination = IntVec3.Invalid;

			for (var i = 0; i < 2; i++)
			{
				AdvanceGameTicks(1);
				var currentJob = miner.CurJobDef?.defName;
				var stumbleDestination = miner.jobs.curDriver is JobDriver_Stumble currentStumbleDriver
					? currentStumbleDriver.destination
					: IntVec3.Invalid;
				samples.Add(new
				{
					tick = i + 1,
					currentJob,
					stumbleDestination = ZombieRuntimeActions.DescribeCell(stumbleDestination),
					mineableDestroyed = mineable.Destroyed,
					mineableHitPoints = mineable.Destroyed ? 0 : mineable.HitPoints,
					miner.miningCounter
				});
				if (mineable.Destroyed || mineable.HitPoints < hitPointsBefore)
					break;
			}

			var mineableDestroyed = mineable.Destroyed;
			var hitPointsAfter = mineableDestroyed ? 0 : mineable.HitPoints;

			return new
			{
				success = (mineableDestroyed || hitPointsAfter < hitPointsBefore) && miner.miningCounter > 0,
				spawnedMiner,
				bodyTypeBefore,
				bodyTypeDuringTest = miner.story?.bodyType?.defName,
				clearedPheromoneRadius,
				minerCell = ZombieRuntimeActions.DescribeCell(miner.Position),
				mineableCell = ZombieRuntimeActions.DescribeCell(mineableCell),
				mineableDef = mineable.def.defName,
				mineableDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				miningCounterAfter = miner.miningCounter,
				before,
				after = DescribeZombie(miner),
				samples
			};
		}

		[Tool("zombieland/move_tanky", Description = "Move a tanky zombie one valid adjacent cell and verify that it leaves a pheromone trace for other zombies.")]
		public static object MoveTanky(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
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

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			if (TryFindAdjacentMoveCell(tanky, out var destination) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "No valid adjacent move cell was found."
				};
			}

			var radius = Constants.TANKY_PHEROMONE_RADIUS + 1f;
			var before = DescribeZombie(tanky);
			var origin = tanky.Position;
			ClearPheromones(map, destination, radius);
			var pheromonesBefore = SnapshotPheromones(map, destination, radius);
			tanky.pather?.StopDead();
			tanky.Position = destination;
			tanky.Notify_Teleported(false, false);
			var pheromoneChange = DescribePheromoneChange(map, pheromonesBefore, out var changedCount);

			return new
			{
				success = tanky.Position == destination && changedCount > 0,
				spawnedTanky,
				radius,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				before,
				after = DescribeZombie(tanky),
				pheromoneChange
			};
		}

		[Tool("zombieland/damage_albino", Description = "Apply real bullet and explosive damage to an albino zombie and verify its damage filter blocks only non-explosive hits.")]
		public static object DamageAlbino(
			[ToolParameter(Description = "Optional albino zombie id, ThingID, label, or short name. When omitted, a fresh albino zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for the repeated bullet damage sample.", Required = false, DefaultValue = 31337)] int seed = 31337,
			[ToolParameter(Description = "Number of one-damage bullet attempts to sample.", Required = false, DefaultValue = 20)] int bulletAttempts = 20)
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

			Zombie albino;
			var spawnedAlbino = false;
			IntVec3 spawnRoot;
			if (string.IsNullOrWhiteSpace(target))
			{
				spawnRoot = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, spawnRoot, 16f, out var cell, out var error) == false)
					return error;

				albino = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Albino, true);
				spawnedAlbino = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				albino = pawn as Zombie;
				spawnRoot = albino?.Position ?? new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			}

			if (albino == null || albino.isAlbino == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(albino),
					error = "Target is not an albino zombie."
				};
			}

			var cappedAttempts = Math.Max(4, Math.Min(bulletAttempts, 60));
			var before = DescribeZombie(albino);
			var bulletDamageTotals = new List<float>(cappedAttempts);
			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < cappedAttempts; i++)
				{
					var dinfo = new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
					bulletDamageTotals.Add(albino.TakeDamage(dinfo).totalDamageDealt);
					if ((bulletDamageTotals.Any(total => total > 0f) && bulletDamageTotals.Any(total => total <= 0f)) || albino.Dead)
						break;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var explosiveAlbino = albino;
			var spawnedExplosiveAlbino = false;
			if (albino.Dead || string.IsNullOrWhiteSpace(target) == false)
			{
				if (TryFindClearSpawnCell(map, spawnRoot + new IntVec3(3, 0, 0), 16f, out var explosiveCell, out var explosiveError) == false)
					return explosiveError;
				explosiveAlbino = ZombieRuntimeActions.SpawnZombie(explosiveCell, map, ZombieType.Albino, true);
				spawnedExplosiveAlbino = true;
			}
			var explosiveBefore = DescribeZombie(explosiveAlbino);
			var explosiveInfo = new DamageInfo(DamageDefOf.Bomb, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var explosiveDamage = explosiveAlbino.TakeDamage(explosiveInfo).totalDamageDealt;
			var bulletHits = bulletDamageTotals.Count(total => total > 0f);
			var bulletBlocked = bulletDamageTotals.Count(total => total <= 0f);

			return new
			{
				success = bulletHits > 0 && bulletBlocked > 0 && explosiveDamage > 0f,
				spawnedAlbino,
				spawnedExplosiveAlbino,
				seed,
				bulletAttempts = bulletDamageTotals.Count,
				bulletHits,
				bulletBlocked,
				bulletDamageTotal = bulletDamageTotals.Sum(),
				bulletDamageTotals = bulletDamageTotals.ToArray(),
				explosiveDamage,
				before,
				after = DescribeZombie(albino),
				explosiveBefore,
				explosiveAfter = DescribeZombie(explosiveAlbino)
			};
		}

		[Tool("zombieland/scream_with_albino", Description = "Start a real albino sabotage job and verify its 40-tick scream pulse forces a nearby colonist to vomit and stuns them.")]
		public static object ScreamWithAlbino()
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

			var colonist = map.mapPawns.FreeColonists
				.Where(pawn => pawn.Spawned && pawn.Dead == false && pawn.health.Downed == false && pawn.InMentalState == false)
				.OrderBy(pawn => pawn.Position.x)
				.ThenBy(pawn => pawn.Position.z)
				.FirstOrDefault();
			if (colonist == null)
			{
				return new
				{
					success = false,
					error = "No spawned free colonist was available as an albino scream target."
				};
			}

			if (TryFindAdjacentClearCell(colonist, out var albinoCell) == false)
			{
				return new
				{
					success = false,
					colonist = DescribePawn(colonist),
					error = "No clear adjacent cell was found for the albino scream test."
				};
			}

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			if (albino == null)
			{
				return new
				{
					success = false,
					colonist = DescribePawn(colonist),
					error = "ZombieGenerator.SpawnZombie returned no albino test zombie."
				};
			}
			albino.SetFaction(Faction.OfPlayer);

			var jobBefore = colonist.CurJobDef?.defName;
			var stunnedBefore = colonist.stances?.stunner?.Stunned ?? false;
			albino.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced, null, true, true);
			AdvanceGameTicks(1);

			var driver = albino.jobs.curDriver as JobDriver_Sabotage;
			if (driver == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					error = "Albino did not enter the sabotage job driver."
				};
			}

			albino.pather?.StopDead();
			if (albino.Position != albinoCell)
			{
				albino.Position = albinoCell;
				albino.Notify_Teleported(false, false);
			}
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.hackTarget = null;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			albino.scream = 0;
			var pulseTick = 40;
			var samples = new List<object>();
			for (var tick = 1; tick <= pulseTick; tick++)
			{
				AdvanceGameTicks(1);
				if (tick == 1 || tick == pulseTick || tick % 10 == 0)
				{
					samples.Add(new
					{
						tick,
						scream = albino.scream,
						colonistJob = colonist.CurJobDef?.defName,
						colonistStunned = colonist.stances?.stunner?.Stunned ?? false
					});
				}
			}

			var jobAfter = colonist.CurJobDef?.defName;
			var stunnedAfter = colonist.stances?.stunner?.Stunned ?? false;
			var distanceSquared = colonist.Position.DistanceToSquared(albino.Position);

			return new
			{
				success = albino.scream >= pulseTick && jobAfter == JobDefOf.Vomit.defName && stunnedAfter,
				pulseTick,
				distanceSquared,
				albino = DescribeZombie(albino),
				colonist = DescribePawn(colonist),
				albinoCell = ZombieRuntimeActions.DescribeCell(albinoCell),
				jobBefore,
				jobAfter,
				stunnedBefore,
				stunnedAfter,
				screamAfter = albino.scream,
				samples
			};
		}

		[Tool("zombieland/hack_flickable_with_albino", Description = "Start a real albino sabotage job and verify its 240-tick hacking branch switches off a flickable building.")]
		public static object HackFlickableWithAlbino()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var error) == false)
				return error;

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			if (albino == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no albino test zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(albino, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					error = "No clear adjacent building cell was found for the albino hacking test."
				};
			}

			var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
			if (lampDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef StandingLamp was not found."
				};
			}

			var lamp = GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), buildingCell, map, WipeMode.Vanish) as Building;
			lamp?.SetFaction(Faction.OfPlayer);
			var flickable = lamp?.TryGetComp<CompFlickable>();
			if (lamp == null || flickable == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
					error = "The spawned StandingLamp did not provide a flickable building."
				};
			}

			flickable.SwitchIsOn = true;
			var switchBefore = flickable.SwitchIsOn;
			albino.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced, null, true, true);
			AdvanceGameTicks(1);

			var driver = albino.jobs.curDriver as JobDriver_Sabotage;
			if (driver == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					building = lamp.LabelCap,
					error = "Albino did not enter the sabotage job driver."
				};
			}

			albino.pather?.StopDead();
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.hackTarget = lamp;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			albino.scream = -1;

			var hackStartTick = 1;
			var hackActionTicks = 240;
			var totalTicks = hackStartTick + hackActionTicks;
			var samples = new List<object>();
			for (var tick = 1; tick <= totalTicks; tick++)
			{
				AdvanceGameTicks(1);
				if (tick == 1 || tick == totalTicks || tick % 60 == 0)
				{
					samples.Add(new
					{
						tick,
						driver.hackCounter,
						switchIsOn = flickable.SwitchIsOn,
						hackTarget = driver.hackTarget?.ThingID
					});
				}
			}

			var switchAfter = flickable.SwitchIsOn;

			return new
			{
				success = switchBefore && switchAfter == false && driver.hackCounter == 0 && driver.hackTarget == null,
				totalTicks,
				hackActionTicks,
				albino = DescribeZombie(albino),
				building = lamp.LabelCap,
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				switchBefore,
				switchAfter,
				hackCounterAfter = driver.hackCounter,
				hackTargetAfter = driver.hackTarget?.ThingID,
				samples
			};
		}

		[Tool("zombieland/damage_tanky_armor", Description = "Apply real bullet damage to a tanky zombie and verify the tanky armor patch absorbs it by degrading armor.")]
		public static object DamageTankyArmor(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Bullet damage amount used for the absorption sample.", Required = false, DefaultValue = 50)] int damage = 50,
			[ToolParameter(Description = "Deterministic Rand seed for hit-part selection.", Required = false, DefaultValue = 424242)] int seed = 424242)
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

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			var cappedDamage = Math.Max(1, Math.Min(damage, 500));
			var before = DescribeZombie(tanky);
			var armorBefore = DescribeTankyArmor(tanky);
			var healthBefore = tanky.health.summaryHealth.SummaryHealthPercent;
			DamageWorker.DamageResult result;
			Rand.PushState(seed);
			try
			{
				var dinfo = new DamageInfo(DamageDefOf.Bullet, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
				result = tanky.TakeDamage(dinfo);
			}
			finally
			{
				Rand.PopState();
			}
			var healthAfter = tanky.health.summaryHealth.SummaryHealthPercent;

			var shieldChanged = tanky.hasTankyShield < 1f;
			var helmetChanged = tanky.hasTankyHelmet < 1f;
			var suitChanged = tanky.hasTankySuit < 1f;
			var anyArmorChanged = shieldChanged || helmetChanged || suitChanged;

			return new
			{
				success = anyArmorChanged && result.totalDamageDealt <= 0f && healthAfter >= healthBefore,
				spawnedTanky,
				seed,
				damage = cappedDamage,
				totalDamageDealt = result.totalDamageDealt,
				healthBefore,
				healthAfter,
				armorBefore,
				armorAfter = DescribeTankyArmor(tanky),
				shieldChanged,
				helmetChanged,
				suitChanged,
				before,
				after = DescribeZombie(tanky)
			};
		}

		[Tool("zombieland/smash_with_tanky", Description = "Put a wall on a tanky zombie route and verify the real stumble-to-AttackStatic job path damages it.")]
		public static object SmashWithTanky(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for the melee attack sample.", Required = false, DefaultValue = 616161)] int seed = 616161)
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

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(tanky, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "No clear adjacent wall cell was found."
				};
			}

			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			if (wall == null)
			{
				return new
				{
					success = false,
					error = "Could not create test wall."
				};
			}
			GenSpawn.Spawn(wall, buildingCell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);

			tanky.pather?.StopDead();
			tanky.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			tanky.state = ZombieState.Wandering;
			tanky.checkSmashable = true;
			tanky.tankDestination = buildingCell;

			var info = ZombieWanderer.GetMapInfo(map);
			var recalc = info.RecalculateAll(new[] { buildingCell }, CurrentZombies(map).OfType<Zombie>());
			var recalcSteps = 0;
			while (recalcSteps < 2048 && recalc.MoveNext())
				recalcSteps++;
			var routeParentIgnoringBuildings = info.GetParent(tanky.Position, true);
			var routeParentRespectingBuildings = info.GetParent(tanky.Position, false);

			var before = DescribeZombie(tanky);
			var hitPointsBefore = wall.HitPoints;
			var wallId = ZombieRuntimeActions.StableThingId(wall);
			var samples = new List<object>();
			var sawAttackStaticJob = false;
			tanky.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (tanky.jobs.curDriver is JobDriver_Stumble stumbleDriver)
				stumbleDriver.destination = IntVec3.Invalid;

			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < 3; i++)
				{
					AdvanceGameTicks(1);
					var currentJob = tanky.CurJobDef?.defName;
					var stumbleDestination = tanky.jobs.curDriver is JobDriver_Stumble currentStumbleDriver
						? currentStumbleDriver.destination
						: IntVec3.Invalid;
					if (currentJob == JobDefOf.AttackStatic.defName)
						sawAttackStaticJob = true;
					samples.Add(new
					{
						tick = i + 1,
						currentJob,
						stumbleDestination = ZombieRuntimeActions.DescribeCell(stumbleDestination),
						fullBodyBusy = tanky.stances?.FullBodyBusy ?? false,
						wallDestroyed = wall.Destroyed,
						wallHitPoints = wall.Destroyed ? 0 : wall.HitPoints
					});
					if (wall.Destroyed || wall.HitPoints < hitPointsBefore)
						break;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var wallDestroyed = wall.Destroyed;
			var hitPointsAfter = wallDestroyed ? 0 : wall.HitPoints;

			return new
			{
				success = (wallDestroyed || hitPointsAfter < hitPointsBefore)
					&& sawAttackStaticJob,
				spawnedTanky,
				seed,
				sawAttackStaticJob,
				tankyCell = ZombieRuntimeActions.DescribeCell(tanky.Position),
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				routeParentIgnoringBuildings = ZombieRuntimeActions.DescribeCell(routeParentIgnoringBuildings),
				routeParentRespectingBuildings = ZombieRuntimeActions.DescribeCell(routeParentRespectingBuildings),
				recalcSteps,
				wallId,
				wallDef = wall.def.defName,
				wallDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				before,
				after = DescribeZombie(tanky),
				samples
			};
		}

	}
}
