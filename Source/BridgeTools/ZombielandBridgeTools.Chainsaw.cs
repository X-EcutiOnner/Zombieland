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
		[Tool("zombieland/zap_zombies_with_shocker", Description = "Build a powered zombie shocker room, run the real ZapZombies job, and verify a zombie in the room is paralyzed.")]
		public static object ZapZombiesWithShocker()
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
			if (TryFindShockerFixtureCell(map, root, 24f, out var shockerCell, out var fixtureError) == false)
				return fixtureError;

			var wallDef = ThingDefOf.Wall;
			var conduitDef = DefDatabase<ThingDef>.GetNamed("PowerConduit", false);
			var batteryDef = DefDatabase<ThingDef>.GetNamed("Battery", false);
			if (wallDef == null || conduitDef == null || batteryDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Wall, PowerConduit, or Battery was not found."
				};
			}

			var fixtureThings = new List<Thing>();
			for (var dx = -2; dx <= 2; dx++)
			{
				for (var dz = 0; dz <= 4; dz++)
				{
					if (dx != -2 && dx != 2 && dz != 0 && dz != 4)
						continue;

					var wallCell = shockerCell + new IntVec3(dx, 0, dz);
					var wall = ThingMaker.MakeThing(wallDef, ThingDefOf.WoodLog) as Building;
					if (wall == null)
						continue;
					GenSpawn.Spawn(wall, wallCell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					fixtureThings.Add(wall);
				}
			}

			var shocker = ThingMaker.MakeThing(CustomDefs.ZombieShocker) as ZombieShocker;
			if (shocker == null)
			{
				return new
				{
					success = false,
					error = "Could not create ZombieShocker."
				};
			}
			shocker.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(shocker, shockerCell, map, Rot4.North, WipeMode.Vanish, false);
			fixtureThings.Add(shocker);

			var conduitCell = shockerCell + IntVec3.South;
			var batteryCell = shockerCell + new IntVec3(1, 0, -3);
			var bridgeConduitCell = batteryCell + new IntVec3(0, 0, 2);
			var actorCell = shockerCell + IntVec3.South + IntVec3.West;
			var zombieCell = shockerCell + new IntVec3(0, 0, 2);
			var conduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), conduitCell, map, WipeMode.Vanish) as Building;
			var bridgeConduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), bridgeConduitCell, map, WipeMode.Vanish) as Building;
			var battery = GenSpawn.Spawn(ThingMaker.MakeThing(batteryDef), batteryCell, map, WipeMode.Vanish) as Building;
			conduit?.SetFaction(Faction.OfPlayer);
			bridgeConduit?.SetFaction(Faction.OfPlayer);
			battery?.SetFaction(Faction.OfPlayer);
			if (conduit != null)
				fixtureThings.Add(conduit);
			if (bridgeConduit != null)
				fixtureThings.Add(bridgeConduit);
			if (battery != null)
				fixtureThings.Add(battery);

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var shockerPower = shocker.GetComp<CompPowerTrader>();
			var conduitPower = conduit?.GetComp<CompPower>();
			var bridgeConduitPower = bridgeConduit?.GetComp<CompPower>();
			var batteryPower = battery?.GetComp<CompPowerBattery>();
			batteryPower?.SetStoredEnergyPct(1f);
			if (conduitPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(conduitPower);
			if (bridgeConduitPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(bridgeConduitPower);
			if (batteryPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(batteryPower);
			map.powerNetManager.UpdatePowerNetsAndConnections_First();
			if (shockerPower?.PowerNet == null && conduitPower != null)
				shockerPower.ConnectToTransmitter(conduitPower);
			if (shockerPower?.PowerNet == null && bridgeConduitPower != null)
				shockerPower.ConnectToTransmitter(bridgeConduitPower);
			if (shockerPower?.PowerNet == null && batteryPower != null)
				shockerPower.ConnectToTransmitter(batteryPower);
			AdvanceGameTicks(1);
			if (shockerPower != null)
				shockerPower.PowerOn = true;

			var selectedRotation = shocker.Rotation;
			foreach (var rot in Rot4.AllRotations)
			{
				shocker.Rotation = rot;
				if (shocker.HasValidRoom())
				{
					selectedRotation = rot;
					break;
				}
			}
			shocker.Rotation = selectedRotation;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					shockerCell = ZombieRuntimeActions.DescribeCell(shockerCell),
					error = "ZombieGenerator.SpawnZombie returned no shocker test zombie."
				};
			}
			zombie.ropedBy = null;
			zombie.paralyzedUntil = 0;

			var room = ZombieShocker.GetValidRoom(map, shockerCell + IntVec3.North);
			var roomCellCount = room?.Cells.Count(cell => cell.Standable(map)) ?? 0;
			var hasValidRoom = shocker.HasValidRoom();
			var canReserveAndReach = actor.CanReach(shocker, PathEndMode.InteractionCell, Danger.Deadly)
				&& actor.CanReserve(shocker);
			var batteryCount = shockerPower?.PowerNet?.batteryComps?.Count ?? 0;
			var storedEnergyBefore = batteryPower?.StoredEnergy ?? 0f;
			var ropedBefore = zombie.ropedBy != null;
			var paralyzedUntilBefore = zombie.paralyzedUntil;
			var zapMotesBefore = CountZombieZapMotesNear(map, zombieCell, 2f);

			var job = JobMaker.MakeJob(CustomDefs.ZapZombies, shocker);
			job.playerForced = true;
			_ = actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var startedJob = actor.CurJobDef?.defName;
			var maxTicks = 90 + 45 + roomCellCount + 20;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var ropedNow = zombie.ropedBy != null;
				var paralyzedNow = zombie.paralyzedUntil > GenTicks.TicksAbs;
				var zapMotesNow = CountZombieZapMotesNear(map, zombieCell, 2f);
				var hitNow = paralyzedNow && zapMotesNow > zapMotesBefore;
				if (tick == 1 || tick == 90 || tick == 135 || tick == maxTicks || tick % 30 == 0 || hitNow)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						roped = ropedNow,
						paralyzed = paralyzedNow,
						zombie.paralyzedUntil,
						zapMotes = zapMotesNow
					});
				}

				if (hitNow)
				{
					tickHit = tick;
					break;
				}
			}

			var zapMotesAfter = CountZombieZapMotesNear(map, zombieCell, 2f);
			var storedEnergyAfter = batteryPower?.StoredEnergy ?? 0f;
			var ropedAfter = zombie.ropedBy != null;
			var paralyzedAfter = zombie.paralyzedUntil > GenTicks.TicksAbs;

			return new
			{
				success = hasValidRoom
					&& canReserveAndReach
					&& shockerPower?.PowerOn == true
					&& batteryCount > 0
					&& startedJob == CustomDefs.ZapZombies.defName
					&& ropedBefore == false
					&& ropedAfter == false
					&& paralyzedAfter
					&& tickHit > 0
					&& zapMotesAfter > zapMotesBefore,
				destroyedZombies,
				shocker = new
				{
					id = ZombieRuntimeActions.StableThingId(shocker),
					cell = ZombieRuntimeActions.DescribeCell(shockerCell),
					rotation = shocker.Rotation.ToString(),
					powerOn = shockerPower?.PowerOn,
					hasPowerNet = shockerPower?.PowerNet != null,
					batteryCount,
					hasValidRoom,
					onWall = shocker.OnWall()
				},
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				cells = new
				{
					shocker = ZombieRuntimeActions.DescribeCell(shockerCell),
					conduit = ZombieRuntimeActions.DescribeCell(conduitCell),
					bridgeConduit = ZombieRuntimeActions.DescribeCell(bridgeConduitCell),
					battery = ZombieRuntimeActions.DescribeCell(batteryCell),
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				fixtureThingCount = fixtureThings.Count,
				roomCellCount,
				canReserveAndReach,
				startedJob,
				maxTicks,
				tickHit,
				ropedBefore,
				ropedAfter,
				paralyzedUntilBefore,
				paralyzedUntilAfter = zombie.paralyzedUntil,
				paralyzedAfter,
				zapMotesBefore,
				zapMotesAfter,
				zapMoteDelta = zapMotesAfter - zapMotesBefore,
				storedEnergyBefore,
				storedEnergyAfter,
				storedEnergyDelta = storedEnergyBefore - storedEnergyAfter,
				samples
			};
		}

		[Tool("zombieland/thumper_impact_cycle", Description = "Spawn and fuel a real Zombie Thumper, run its source-derived cycle to impact, and verify fuel is consumed.")]
		public static object ThumperImpactCycle()
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
			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, root, 24f, out var thumperCell, out var footprintError) == false)
				return footprintError;

			var thumper = ThingMaker.MakeThing(CustomDefs.Thumper) as ZombieThumper;
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "Could not create ZombieThumper."
				};
			}

			thumper.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			var refuelable = thumper.TryGetComp<CompRefuelable>();
			var switchable = thumper.TryGetComp<CompSwitchable>();
			var chemfuelDef = ThingDefOf.Chemfuel;
			if (refuelable == null || chemfuelDef == null)
			{
				return new
				{
					success = false,
					thumperCell = ZombieRuntimeActions.DescribeCell(thumperCell),
					error = "The spawned thumper did not have a refuelable comp or Chemfuel was unavailable."
				};
			}

			var fuel = ThingMaker.MakeThing(chemfuelDef);
			fuel.stackCount = Math.Min(chemfuelDef.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, thumperCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			if (switchable != null)
				switchable.isActive = true;

			thumper.intensity = 0.05f;
			thumper.intervalTicks = GenDate.TicksPerHour / 25;
			var upTicks = Mathf.FloorToInt(ZombieThumper.upwardsTicks * thumper.intensity);
			var fallTicks = Mathf.FloorToInt(Mathf.Sqrt(upTicks / ZombieThumper.accelerationFactor));
			var impactByTicks = Math.Max(thumper.intervalTicks, 30 + upTicks) + fallTicks + 3;
			var fuelBefore = refuelable.Fuel;
			var isActiveBefore = thumper.IsActive;
			var radiusBefore = thumper.Radius;
			var stateField = typeof(ZombieThumper).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
			var stateValueField = typeof(ZombieThumper).GetField("stateValue", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactTicksField = typeof(ZombieThumper).GetField("lastImpactTicks", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactBefore = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);
			var samples = new List<object>();
			var tickHit = -1;

			for (var tick = 1; tick <= impactByTicks; tick++)
			{
				AdvanceGameTicks(1);
				var fuelNow = refuelable.Fuel;
				if (tick == 1 || tick == upTicks + 30 || tick == impactByTicks || tick % 25 == 0 || fuelNow < fuelBefore)
				{
					samples.Add(new
					{
						tick,
						state = stateField?.GetValue(thumper)?.ToString(),
						stateValue = (int)(stateValueField?.GetValue(thumper) ?? 0),
						fuel = fuelNow,
						lastImpactTicks = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0)
					});
				}

				if (fuelNow < fuelBefore)
				{
					tickHit = tick;
					break;
				}
			}

			var fuelAfter = refuelable.Fuel;
			var lastImpactAfter = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);

			return new
			{
				success = thumper.Spawned
					&& isActiveBefore
					&& radiusBefore > 0
					&& tickHit > 0
					&& fuelAfter < fuelBefore
					&& lastImpactAfter > lastImpactBefore,
				thumper = new
				{
					id = ZombieRuntimeActions.StableThingId(thumper),
					cell = ZombieRuntimeActions.DescribeCell(thumperCell),
					spawned = thumper.Spawned,
					hitPoints = thumper.HitPoints,
					intensity = thumper.intensity,
					intervalTicks = thumper.intervalTicks,
					radius = thumper.Radius,
					isActive = thumper.IsActive,
					state = stateField?.GetValue(thumper)?.ToString(),
					stateValue = (int)(stateValueField?.GetValue(thumper) ?? 0)
				},
				upTicks,
				fallTicks,
				impactByTicks,
				tickHit,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				lastImpactBefore,
				lastImpactAfter,
				lastImpactDelta = lastImpactAfter - lastImpactBefore,
				hasFuelAfter = refuelable.HasFuel,
				samples
			};
		}

		[Tool("zombieland/chainsaw_equip_toggle", Description = "Equip a real fueled chainsaw, start it through its gizmo, tick it while equipped, then verify undrafting stops it.")]
		public static object ChainsawEquipToggle()
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
			var chainsawCell = actorCell + IntVec3.East;
			if (chainsawCell.InBounds(map) == false || chainsawCell.Standable(map) == false)
				chainsawCell = actorCell;

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
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, chainsawCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			var fuelBeforeEquip = refuelable.Fuel;
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			actor.drafter.Drafted = true;
			var equipped = ReferenceEquals(actor.equipment.Primary, chainsaw);
			var pawnSet = ReferenceEquals(chainsaw.pawn, actor);
			var gizmos = chainsaw.GetGizmos().ToArray();
			var toggle = gizmos.OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			var toggleAvailable = toggle != null;
			toggle?.action();
			var runningAfterToggle = chainsaw.running;
			var fuelAfterToggle = refuelable.Fuel;
			var samples = new List<object>();

			for (var tick = 1; tick <= 20; tick++)
			{
				AdvanceGameTicks(1);
				if (tick == 1 || tick == 20 || refuelable.Fuel < fuelAfterToggle)
				{
					samples.Add(new
					{
						tick,
						chainsaw.running,
						chainsaw.swinging,
						chainsaw.inactiveCounter,
						chainsaw.stalledCounter,
						fuel = refuelable.Fuel
					});
				}
			}

			var fuelAfterTicks = refuelable.Fuel;
			actor.drafter.Drafted = false;
			var runningAfterUndraft = chainsaw.running;

			return new
			{
				success = equipped
					&& pawnSet
					&& toggleAvailable
					&& runningAfterToggle
					&& fuelAfterTicks < fuelAfterToggle
					&& runningAfterUndraft == false
					&& breakable.broken == false,
				actor = DescribePawn(actor),
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					thingId = chainsaw.ThingID,
					spawned = chainsaw.Spawned,
					equipped,
					pawnSet,
					hitPoints = chainsaw.HitPoints,
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.inactiveCounter,
					chainsaw.stalledCounter,
					description = chainsaw.DescriptionDetailed
				},
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				chainsawCell = ZombieRuntimeActions.DescribeCell(chainsawCell),
				gizmoCount = gizmos.Length,
				toggleAvailable,
				runningAfterToggle,
				runningAfterUndraft,
				fuelBeforeEquip,
				fuelAfterToggle,
				fuelAfterTicks,
				fuelDeltaWhileRunning = fuelAfterToggle - fuelAfterTicks,
				hasFuelAfter = refuelable.HasFuel,
				destroyedZombies,
				samples
			};
		}

		[Tool("zombieland/chainsaw_slaughter_zombie", Description = "Run a fueled chainsaw against an adjacent live zombie and verify the chainsaw tick kills it through the custom slaughter path.")]
		public static object ChainsawSlaughterZombie()
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

			var zombieCell = IntVec3.Invalid;
			var targetIndex = -1;
			var adjacent = GenAdj.AdjacentCellsAround;
			for (var i = 0; i < adjacent.Length; i++)
			{
				var candidate = actorCell + adjacent[i];
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				zombieCell = candidate;
				targetIndex = i;
				break;
			}
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No adjacent zombie target cell was available."
				};
			}

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.drafter.Drafted = true;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no chainsaw target zombie."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var victimHeadsBefore = tickManager?.victimHeads?.Count ?? 0;
			var hitPointsBefore = chainsaw.HitPoints;
			var fuelBefore = refuelable.Fuel;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			toggle?.action();
			chainsaw.angle = targetIndex * 45f + 22.5f;
			var runningAfterToggle = chainsaw.running;
			var samples = new List<object>();
			var tickHit = -1;

			for (var tick = 1; tick <= 10; tick++)
			{
				AdvanceGameTicks(1);
				samples.Add(new
				{
					tick,
					zombieDead = zombie.Dead,
					zombieDestroyed = zombie.Destroyed,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.angle,
					chainsawHitPoints = chainsaw.HitPoints,
					fuel = refuelable.Fuel,
					victimHeads = tickManager?.victimHeads?.Count ?? 0
				});

				if (zombie.Dead)
				{
					tickHit = tick;
					break;
				}
			}

			var victimHeadsAfter = tickManager?.victimHeads?.Count ?? 0;
			var fuelAfter = refuelable.Fuel;
			var hitPointsAfter = chainsaw.HitPoints;

			return new
			{
				success = runningAfterToggle
					&& tickHit > 0
					&& zombie.Dead
					&& victimHeadsAfter > victimHeadsBefore
					&& hitPointsAfter < hitPointsBefore
					&& fuelAfter < fuelBefore
					&& breakable.broken == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				targetIndex,
				targetOffset = ZombieRuntimeActions.DescribeCell(adjacent[targetIndex]),
				runningAfterToggle,
				tickHit,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsBefore - hitPointsAfter,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				victimHeadsBefore,
				victimHeadsAfter,
				victimHeadDelta = victimHeadsAfter - victimHeadsBefore,
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					equipped = ReferenceEquals(actor.equipment.Primary, chainsaw),
					pawnSet = ReferenceEquals(chainsaw.pawn, actor),
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.angle
				},
				samples
			};
		}

		[Tool("zombieland/chainsaw_hits_building_contract", Description = "Verify a running chainsaw aimed at an adjacent building damages the building, breaks, and drops.")]
		public static object ChainsawHitsBuildingContract()
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

			var adjacent = GenAdj.AdjacentCellsAround;
			var buildingCell = IntVec3.Invalid;
			var buildingIndex = -1;
			var zombieCell = IntVec3.Invalid;
			for (var i = 0; i < adjacent.Length; i++)
			{
				var candidate = actorCell + adjacent[i];
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				if (buildingCell.IsValid == false)
				{
					buildingCell = candidate;
					buildingIndex = i;
				}
				else if (candidate.Standable(map))
				{
					zombieCell = candidate;
					break;
				}
			}
			if (buildingCell.IsValid == false || zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					buildingCell = buildingCell.IsValid ? ZombieRuntimeActions.DescribeCell(buildingCell) : null,
					zombieCell = zombieCell.IsValid ? ZombieRuntimeActions.DescribeCell(zombieCell) : null,
					error = "No adjacent building/zombie fixture cells were available for chainsaw building impact."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
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

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no chainsaw work-branch zombie."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.drafter.Drafted = true;

			var wallHitPointsBefore = wall.HitPoints;
			var chainsawHitPointsBefore = chainsaw.HitPoints;
			var fuelBefore = refuelable.Fuel;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			toggle?.action();
			chainsaw.angle = buildingIndex * 45f + 22.5f;
			var runningAfterToggle = chainsaw.running;
			AdvanceGameTicks(1);

			var wallHitPointsAfter = wall.Destroyed ? 0 : wall.HitPoints;
			var chainsawHitPointsAfter = chainsaw.Destroyed ? 0 : chainsaw.HitPoints;
			var fuelAfter = refuelable.Fuel;
			var stillEquipped = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			var trackedAsBroken = map.GetComponent<BrokenManager>()?.brokenThings?.Contains(chainsaw) ?? false;

			return new
			{
				success = runningAfterToggle
					&& wallHitPointsAfter < wallHitPointsBefore
					&& stillEquipped == false
					&& chainsaw.Spawned
					&& breakable.broken
					&& trackedAsBroken
					&& chainsawHitPointsAfter < chainsawHitPointsBefore
					&& fuelAfter < fuelBefore
					&& zombie.Dead == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					building = ZombieRuntimeActions.DescribeCell(buildingCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				buildingIndex,
				buildingOffset = ZombieRuntimeActions.DescribeCell(adjacent[buildingIndex]),
				runningAfterToggle,
				stillEquipped,
				chainsawSpawned = chainsaw.Spawned,
				chainsawPosition = chainsaw.Spawned ? ZombieRuntimeActions.DescribeCell(chainsaw.Position) : null,
				chainsawHitPointsBefore,
				chainsawHitPointsAfter,
				chainsawHitPointDelta = chainsawHitPointsBefore - chainsawHitPointsAfter,
				chainsawBroken = breakable.broken,
				trackedAsBroken,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				wall = new
				{
					id = ZombieRuntimeActions.StableThingId(wall),
					wall.Destroyed,
					hitPointsBefore = wallHitPointsBefore,
					hitPointsAfter = wallHitPointsAfter,
					hitPointDelta = wallHitPointsBefore - wallHitPointsAfter
				}
			};
		}

		[Tool("zombieland/chainsaw_crowd_pressure_drop_contract", Description = "Surround a drafted colonist from too many hostile angles and verify the equipped running chainsaw drops through its pressure branch.")]
		public static object ChainsawCrowdPressureDropContract()
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
			var adjacent = GenAdj.AdjacentCellsAround;
			var pressureIndices = new[] { 0, 2, 4, 6 };
			var actorCell = IntVec3.Invalid;
			object actorSpawnError = null;
			if (TryFindClearSpawnCell(map, root, 16f, out var firstCandidate, out actorSpawnError) == false)
				return actorSpawnError;
			foreach (var candidate in GenRadial.RadialCellsAround(firstCandidate, 16f, true))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (pressureIndices.All(index =>
				{
					var cell = candidate + adjacent[index];
					return cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.Fogged(map) == false
						&& cell.GetFirstThing<Mineable>(map) == null
						&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
				}))
				{
					actorCell = candidate;
					break;
				}
			}
			if (actorCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear chainsaw pressure fixture with four alternate adjacent zombie cells."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			actor.drafter.Drafted = true;

			var zombies = new List<Zombie>();
			foreach (var index in pressureIndices)
			{
				var zombie = ZombieRuntimeActions.SpawnZombie(actorCell + adjacent[index], map, ZombieType.Normal, true);
				if (zombie != null)
					zombies.Add(zombie);
			}

			var fuelBefore = refuelable.Fuel;
			var hitPointsBefore = chainsaw.HitPoints;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			toggle?.action();
			var runningAfterToggle = chainsaw.running;
			var equippedBeforeTick = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			AdvanceGameTicks(1);

			var fuelAfter = refuelable.Fuel;
			var hitPointsAfter = chainsaw.Destroyed ? 0 : chainsaw.HitPoints;
			var equippedAfterTick = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			var chainsawSpawned = chainsaw.Spawned;
			var pawnCleared = chainsaw.pawn == null;
			var zombieStates = zombies.Select(zombie => new
			{
				zombie = DescribeZombie(zombie),
				alive = zombie.Dead == false && zombie.Destroyed == false,
				hostileToActor = zombie.HostileTo(actor)
			}).ToArray();

			return new
			{
				success = zombies.Count == pressureIndices.Length
					&& runningAfterToggle
					&& equippedBeforeTick
					&& equippedAfterTick == false
					&& chainsawSpawned
					&& pawnCleared
					&& chainsaw.running == false
					&& chainsaw.swinging == false
					&& breakable.broken == false
					&& hitPointsAfter == hitPointsBefore
					&& fuelAfter < fuelBefore
					&& zombieStates.All(state => state.alive && state.hostileToActor),
				destroyedZombies,
				actor = DescribePawn(actor),
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombies = pressureIndices
						.Select(index => new
						{
							index,
							offset = ZombieRuntimeActions.DescribeCell(adjacent[index]),
							cell = ZombieRuntimeActions.DescribeCell(actorCell + adjacent[index])
						})
						.ToArray()
				},
				pressureIndices,
				runningAfterToggle,
				equippedBeforeTick,
				equippedAfterTick,
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					spawned = chainsawSpawned,
					cell = chainsawSpawned ? ZombieRuntimeActions.DescribeCell(chainsaw.Position) : null,
					pawnCleared,
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsBefore - hitPointsAfter
				},
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				zombieStates
			};
		}

	}
}
