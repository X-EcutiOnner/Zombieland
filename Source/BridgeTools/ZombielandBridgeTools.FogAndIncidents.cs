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
		[Tool("zombieland/fogged_door_spawns_room_zombies", Description = "Build a fogged sealed room, call RimWorld FogGrid.Notify_PawnEnteringDoor, and verify Zombieland spawns sudden room zombies before vanilla unfogging.")]
		public static object FoggedDoorSpawnsRoomZombies()
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
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var door = fixture.door;
			var playerCell = doorCell + IntVec3.South;
			var hostileCell = doorCell + IntVec3.South + IntVec3.East;
			var player = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var hostile = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
			GenSpawn.Spawn(player, playerCell, map, Rot4.North);
			GenSpawn.Spawn(hostile, hostileCell, map, Rot4.North);
			DisablePawnWork(player);
			DisablePawnWork(hostile);

			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(playerCell);
			map.fogGrid.Unfog(hostileCell);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBeforeHostile = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				map.fogGrid.Notify_PawnEnteringDoor(door, hostile);
				var zombiesAfterHostile = CurrentZombies(map).Length;
				var roomFoggedAfterHostile = roomBefore?.Fogged ?? false;
				var interiorFoggedAfterHostile = interiorRect.Cells.Count(cell => cell.Fogged(map));

				map.fogGrid.Notify_PawnEnteringDoor(door, player);
				var zombiesAfterPlayer = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfterPlayer = roomAfter?.Fogged ?? false;
				var interiorFoggedAfterPlayer = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfterHostile == zombiesBeforeHostile
						&& roomFoggedAfterHostile
						&& zombiesAfterPlayer > zombiesAfterHostile
						&& spawnedZombies.Length > 0
						&& roomFoggedAfterPlayer == false,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					player = DescribePawn(player),
					hostile = DescribePawn(hostile),
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfterHostile = roomFoggedAfterHostile,
						foggedAfterPlayer = roomFoggedAfterPlayer,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfterHostile,
						interiorFoggedAfterPlayer
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBeforeHostile,
					zombiesAfterHostile,
					zombiesAfterPlayer,
					zombieDelta = zombiesAfterPlayer - zombiesAfterHostile,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/fog_blocker_removal_spawns_room_zombies", Description = "Build a fogged sealed room, destroy one fog-blocking wall, and verify Zombieland spawns sudden room zombies before vanilla unfogging.")]
		public static object FogBlockerRemovalSpawnsRoomZombies()
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
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var targetWallCell = fixture.targetWallCell;
			var door = fixture.door;
			var targetWall = fixture.targetWall;
			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(targetWallCell + IntVec3.South);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBefore = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				targetWall.Destroy(DestroyMode.Deconstruct);
				var zombiesAfter = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfter = roomAfter?.Fogged ?? false;
				var interiorFoggedAfter = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& targetWall.Destroyed
						&& targetWall.def.MakeFog
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfter > zombiesBefore
						&& spawnedZombies.Length > 0
						&& roomFoggedAfter == false,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					targetWall = new
					{
						id = ZombieRuntimeActions.StableThingId(targetWall),
						position = ZombieRuntimeActions.DescribeCell(targetWallCell),
						destroyed = targetWall.Destroyed,
						defName = targetWall.def?.defName,
						makeFog = targetWall.def?.MakeFog ?? false
					},
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfter = roomFoggedAfter,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfter
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBefore,
					zombiesAfter,
					zombieDelta = zombiesAfter - zombiesBefore,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/fog_blocker_replacement_does_not_spawn_room_zombies", Description = "Build a fogged sealed room, destroy one fog-blocking wall with WillReplace, and verify replacement mode does not spawn sudden room zombies.")]
		public static object FogBlockerReplacementDoesNotSpawnRoomZombies()
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
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var targetWallCell = fixture.targetWallCell;
			var door = fixture.door;
			var targetWall = fixture.targetWall;
			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(targetWallCell + IntVec3.South);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBefore = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				targetWall.Destroy(DestroyMode.WillReplace);
				var zombiesAfter = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfter = roomAfter?.Fogged ?? false;
				var interiorFoggedAfter = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& targetWall.Destroyed
						&& targetWall.def.MakeFog
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfter == zombiesBefore
						&& spawnedZombies.Length == 0
						&& interiorFoggedAfter == interiorFoggedBefore,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					targetWall = new
					{
						id = ZombieRuntimeActions.StableThingId(targetWall),
						position = ZombieRuntimeActions.DescribeCell(targetWallCell),
						destroyed = targetWall.Destroyed,
						defName = targetWall.def?.defName,
						makeFog = targetWall.def?.MakeFog ?? false,
						destroyMode = DestroyMode.WillReplace.ToString()
					},
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfter = roomFoggedAfter,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfter
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBefore,
					zombiesAfter,
					zombieDelta = zombiesAfter - zombiesBefore,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/detonate_suicide_bomber", Description = "Kill a suicide bomber through Zombie.Kill, verify it queued a Zombieland explosion, then execute the explosion.")]
		public static object DetonateSuicideBomber(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned suicide bomber is used.", Required = false, DefaultValue = "")] string target = "")
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

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.IsSuicideBomber);
				if (pawn == null)
				{
					return new
					{
						success = false,
						error = "No spawned suicide bomber was found."
					};
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.IsSuicideBomber == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a suicide bomber."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(zombie),
					error = "The current map has no Zombieland tick manager."
				};
			}

			var beforeZombieCount = CurrentZombies(map).Length;
			var before = DescribeZombie(zombie);
			var position = zombie.Position;
			var queuedBeforeKill = tickManager.explosions?.Count ?? 0;
			zombie.Kill(null);
			var queuedAfterKill = tickManager.explosions?.Count ?? 0;
			tickManager.ExecuteExplosions();
			var queuedAfterExecute = tickManager.explosions?.Count ?? 0;
			var afterZombieCount = CurrentZombies(map).Length;

			return new
			{
				success = zombie.Dead && queuedAfterKill == queuedBeforeKill + 1 && queuedAfterExecute == 0,
				position = ZombieRuntimeActions.DescribeCell(position),
				before,
				dead = zombie.Dead,
				destroyed = zombie.Destroyed,
				queuedBeforeKill,
				queuedAfterKill,
				queuedAfterExecute,
				beforeZombieCount,
				afterZombieCount,
				explosionQueued = queuedAfterKill == queuedBeforeKill + 1,
				explosionExecuted = queuedAfterExecute == 0
			};
		}

		[Tool("zombieland/suicide_bomber_countdown_contract", Description = "Verify a suicide bomber only detonates through the real Stumble countdown after bombWillGoOff is armed.")]
		public static object SuicideBomberCountdownContract()
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

			var maxTicks = 12;
			var sourceCadence = "ZombieStateHandler.ShouldDie uses zombie.EveryNTick(NthTick.Every10)";
			var explosionQueueObservation = "Full game ticks drain TickManager.explosions after the countdown kill; zombieland/detonate_suicide_bomber covers direct Kill queueing.";
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var allCasesSucceeded = true;

			bool TryFindCountdownCell(IntVec3 rootCell, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(rootCell, 20f, true))
				{
					if (candidate.InBounds(map) == false || candidate.Fogged(map) || candidate.Standable(map) == false)
						continue;
					if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					var adjacentHasBuilding = false;
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var adjacent = candidate + direction;
						if (adjacent.InBounds(map) == false)
							continue;
						if (adjacent.GetEdifice(map) != null || adjacent.GetFirstThing<Mineable>(map) != null)
						{
							adjacentHasBuilding = true;
							break;
						}
					}
					if (adjacentHasBuilding)
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear suicide-bomber countdown cell was found near ({rootCell.x}, {rootCell.z})."
				};
				return false;
			}

			object RunCase(string name, bool armed, IntVec3 caseRoot)
			{
				if (TryFindCountdownCell(caseRoot, out var cell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.SuicideBomber, true);
				if (zombie == null || zombie.IsSuicideBomber == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "Could not spawn a suicide bomber.",
						cell = ZombieRuntimeActions.DescribeCell(cell),
						zombie = DescribeZombie(zombie)
					};
				}

				var tickManager = map.GetComponent<TickManager>();
				tickManager?.allZombiesCached?.RemoveWhere(cached => cached == null || cached.Destroyed || cached.Spawned == false || cached.Dead);
				_ = tickManager?.allZombiesCached?.Add(zombie);
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.bombWillGoOff = armed;
				zombie.bombTickingInterval = 1f;
				zombie.lastBombTick = GenTicks.TicksAbs;
				zombie.Rotation = Rot4.South;

				var before = DescribeZombie(zombie);
				var queuedBefore = tickManager?.explosions?.Count ?? 0;
				var samples = new List<object>();
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var queued = tickManager?.explosions?.Count ?? 0;
					samples.Add(new
					{
						tick,
						gameTick = Find.TickManager.TicksGame,
						dead = zombie.Dead,
						destroyed = zombie.Destroyed,
						bombWillGoOff = zombie.bombWillGoOff,
						bombTickingInterval = zombie.bombTickingInterval,
						queuedExplosions = queued,
						currentJob = zombie.CurJobDef?.defName
					});
					if (zombie.Dead || queued > queuedBefore)
						break;
				}

				var queuedAfterCountdown = tickManager?.explosions?.Count ?? 0;
				if (armed && tickManager != null)
					tickManager.ExecuteExplosions();
				var queuedAfterExecute = tickManager?.explosions?.Count ?? 0;
				var success = armed
					? zombie.Dead && queuedAfterCountdown == queuedBefore && queuedAfterExecute == queuedBefore
					: zombie.Dead == false && queuedAfterCountdown == queuedBefore;
				allCasesSucceeded &= success;

				return new
				{
					name,
					success,
					armed,
					sourceCadence,
					explosionQueueObservation,
					maxTicks,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					before,
					after = DescribeZombie(zombie),
					queuedBefore,
					queuedAfterCountdown,
					queuedAfterExecute,
					samples
				};
			}

			var unarmedCase = RunCase("unarmedIntervalDoesNotDetonate", false, root + new IntVec3(-10, 0, 10));
			var armedCase = RunCase("armedIntervalDetonates", true, root + new IntVec3(10, 0, 10));
			var cases = new[] { unarmedCase, armedCase };
			return new
			{
				success = allCasesSucceeded,
				sourceCadence,
				explosionQueueObservation,
				maxTicks,
				cases
			};
		}

		[Tool("zombieland/kill_toxic_splasher", Description = "Kill a toxic splasher through Zombie.Kill and verify that it drops StickyGoo around its death cell.")]
		public static object KillToxicSplasher(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned toxic splasher is used.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Radius around the death cell used to count StickyGoo before and after death.", Required = false, DefaultValue = 8)] int radius = 8)
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

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isToxicSplasher);
				if (pawn == null)
				{
					return new
					{
						success = false,
						error = "No spawned toxic splasher was found."
					};
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.isToxicSplasher == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a toxic splasher."
				};
			}

			var cappedRadius = Math.Max(1, Math.Min(radius, 24));
			var beforeZombieCount = CurrentZombies(map).Length;
			var before = DescribeZombie(zombie);
			var position = zombie.Position;
			var stickyGooBefore = CountThingsNear(map, position, CustomDefs.StickyGoo, cappedRadius);
			zombie.Kill(null);
			var stickyGooAfter = CountThingsNear(map, position, CustomDefs.StickyGoo, cappedRadius);
			var afterZombieCount = CurrentZombies(map).Length;

			return new
			{
				success = zombie.Dead && stickyGooAfter > stickyGooBefore,
				position = ZombieRuntimeActions.DescribeCell(position),
				radius = cappedRadius,
				before,
				dead = zombie.Dead,
				destroyed = zombie.Destroyed,
				stickyGooBefore,
				stickyGooAfter,
				stickyGooDelta = stickyGooAfter - stickyGooBefore,
				beforeZombieCount,
				afterZombieCount
			};
		}

		[Tool("zombieland/move_dark_slimer", Description = "Move a dark slimer one valid adjacent cell and verify that it leaves TarSlime through the position-change patch.")]
		public static object MoveDarkSlimer(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned dark slimer is used.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Radius around the start cell used to count TarSlime before and after the move.", Required = false, DefaultValue = 4)] int radius = 4)
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

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isDarkSlimer);
				if (pawn == null)
				{
					return new
					{
						success = false,
						error = "No spawned dark slimer was found."
					};
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.isDarkSlimer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a dark slimer."
				};
			}

			if (TryFindAdjacentMoveCell(zombie, out var destination) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(zombie),
					error = "No valid adjacent move cell was found."
				};
			}

			var cappedRadius = Math.Max(1, Math.Min(radius, 12));
			var before = DescribeZombie(zombie);
			var origin = zombie.Position;
			var tarSlimeBefore = CountThingsNear(map, origin, CustomDefs.TarSlime, cappedRadius);
			zombie.pather?.StopDead();
			zombie.Position = destination;
			zombie.Notify_Teleported(false, false);
			var tarSlimeAfter = CountThingsNear(map, origin, CustomDefs.TarSlime, cappedRadius);
			var after = DescribeZombie(zombie);

			return new
			{
				success = zombie.Position == destination && tarSlimeAfter > tarSlimeBefore,
				radius = cappedRadius,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				before,
				after,
				tarSlimeBefore,
				tarSlimeAfter,
				tarSlimeDelta = tarSlimeAfter - tarSlimeBefore
			};
		}

		[Tool("zombieland/heal_wounded_zombie", Description = "Use a healer zombie to clear a nearby wounded zombie's hediffs and verify the heal effect queue.")]
		public static object HealWoundedZombie(
			[ToolParameter(Description = "Optional healer zombie id, ThingID, label, or short name. When omitted, the first spawned healer is used, or one is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
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

			Zombie healer;
			var spawnedHealer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				healer = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isHealer);
				if (healer == null)
				{
					var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var healerCell, out var error) == false)
						return error;

					healer = ZombieRuntimeActions.SpawnZombie(healerCell, map, ZombieType.Healer, true);
					spawnedHealer = true;
				}
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
				healer = pawn as Zombie;
			}

			if (healer == null || healer.isHealer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "Target is not a healer."
				};
			}

			if (TryFindAdjacentMoveCell(healer, out var targetCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "No clear adjacent cell was found for the wounded test zombie."
				};
			}

			var wounded = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.Normal, true);
			if (wounded == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "ZombieGenerator.SpawnZombie returned no wounded test zombie."
				};
			}

			var wound = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, wounded);
			wounded.health.AddHediff(wound);
			var healerInfoBefore = healer.healInfo.Count;
			var hediffsBefore = wounded.health.hediffSet.hediffs.Count;
			healer.CustomTick(1f);
			var healerInfoAfter = healer.healInfo.Count;
			var hediffsAfter = wounded.health.hediffSet.hediffs.Count;
			var queuedHealEffect = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));

			return new
			{
				success = hediffsBefore > 0 && hediffsAfter == 0 && healerInfoAfter > healerInfoBefore && queuedHealEffect,
				spawnedHealer,
				healer = DescribeZombie(healer),
				wounded = DescribeZombie(wounded),
				woundedCell = ZombieRuntimeActions.DescribeCell(targetCell),
				hediffsBefore,
				hediffsAfter,
				healerInfoBefore,
				healerInfoAfter,
				queuedHealEffect
			};
		}

		[Tool("zombieland/heal_wounded_zombie_tick", Description = "Use the real zombie tick loop to verify a healer clears a nearby wounded zombie on its Every12 interval.")]
		public static object HealWoundedZombieTick(
			[ToolParameter(Description = "Optional healer zombie id, ThingID, label, or short name. When omitted, a fresh healer is spawned near map center for deterministic tick timing.", Required = false, DefaultValue = "")] string target = "")
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

			Zombie healer;
			var spawnedHealer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var healerCell, out var error) == false)
					return error;

				healer = ZombieRuntimeActions.SpawnZombie(healerCell, map, ZombieType.Healer, true);
				spawnedHealer = true;
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
				healer = pawn as Zombie;
			}

			if (healer == null || healer.isHealer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "Target is not a healer."
				};
			}

			if (TryFindAdjacentMoveCell(healer, out var targetCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "No clear adjacent cell was found for the wounded test zombie."
				};
			}

			var wounded = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.Normal, true);
			if (wounded == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "ZombieGenerator.SpawnZombie returned no wounded test zombie."
				};
			}

			healer.healInfo.Clear();
			var wound = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, wounded);
			wounded.health.AddHediff(wound);

			var healerInfoBefore = healer.healInfo.Count;
			var hediffsBefore = wounded.health.hediffSet.hediffs.Count;
			var interval = Zombie.nthTickValues[(int)NthTick.Every12];
			var maxTicks = interval + 1;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var hediffsNow = wounded.health.hediffSet.hediffs.Count;
				var queuedNow = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));
				samples.Add(new
				{
					tick,
					hediffs = hediffsNow,
					healerInfo = healer.healInfo.Count,
					queuedHealEffect = queuedNow
				});

				if (hediffsNow == 0 && queuedNow)
				{
					tickHit = tick;
					break;
				}
			}

			var healerInfoAfter = healer.healInfo.Count;
			var hediffsAfter = wounded.health.hediffSet.hediffs.Count;
			var queuedHealEffect = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));

			return new
			{
				success = hediffsBefore > 0 && hediffsAfter == 0 && healerInfoAfter > healerInfoBefore && queuedHealEffect && tickHit > 0 && tickHit <= maxTicks,
				spawnedHealer,
				interval,
				maxTicks,
				tickHit,
				healer = DescribeZombie(healer),
				wounded = DescribeZombie(wounded),
				woundedCell = ZombieRuntimeActions.DescribeCell(targetCell),
				hediffsBefore,
				hediffsAfter,
				healerInfoBefore,
				healerInfoAfter,
				queuedHealEffect,
				samples
			};
		}

		[Tool("zombieland/emp_electrifier", Description = "Apply real EMP damage to an electrifier zombie and verify the deactivation patch disables its electric state.")]
		public static object EmpElectrifier(
			[ToolParameter(Description = "Optional electrifier zombie id, ThingID, label, or short name. When omitted, the first spawned electrifier is used, or one is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "EMP damage amount. The disable duration should increase by this amount times 60 ticks.", Required = false, DefaultValue = 5)] int damage = 5)
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

			Zombie electrifier;
			var spawnedElectrifier = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				electrifier = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isElectrifier);
				if (electrifier == null)
				{
					var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
						return error;

					electrifier = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Electrifier, true);
					spawnedElectrifier = true;
				}
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
				electrifier = pawn as Zombie;
			}

			if (electrifier == null || electrifier.isElectrifier == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(electrifier),
					error = "Target is not an electrifier."
				};
			}

			var cappedDamage = Math.Max(1, Math.Min(damage, 60));
			var tickBefore = GenTicks.TicksGame;
			var disabledUntilBefore = electrifier.electricDisabledUntil;
			var activeBefore = electrifier.IsActiveElectric;
			var before = DescribeZombie(electrifier);
			var dinfo = new DamageInfo(DamageDefOf.EMP, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var damageResult = electrifier.TakeDamage(dinfo);
			var disabledUntilAfter = electrifier.electricDisabledUntil;
			var activeAfter = electrifier.IsActiveElectric;

			return new
			{
				success = activeBefore && activeAfter == false && disabledUntilAfter >= tickBefore + cappedDamage * 60,
				spawnedElectrifier,
				damage = cappedDamage,
				tickBefore,
				disabledUntilBefore,
				disabledUntilAfter,
				activeBefore,
				activeAfter,
				damageTotal = damageResult.totalDamageDealt,
				before,
				after = DescribeZombie(electrifier)
			};
		}

		[Tool("zombieland/electrify_powered_building", Description = "Place an active electrifier next to a real power conduit and verify the electrify handler disables it.")]
		public static object ElectrifyPoweredBuilding()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var electrifierCell, out var error) == false)
				return error;

			var electrifier = ZombieRuntimeActions.SpawnZombie(electrifierCell, map, ZombieType.Electrifier, true);
			if (electrifier == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no electrifier test zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(electrifier, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					electrifier = DescribeZombie(electrifier),
					error = "No clear adjacent building cell was found for the electrifier test."
				};
			}

			var conduitDef = DefDatabase<ThingDef>.GetNamed("PowerConduit", false);
			var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
			if (conduitDef == null || lampDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef PowerConduit or StandingLamp was not found."
				};
			}

			var conduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), buildingCell, map, WipeMode.Vanish) as Building;
			var lamp = GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), buildingCell, map, WipeMode.Vanish) as Building;
			conduit?.SetFaction(Faction.OfPlayer);
			lamp?.SetFaction(Faction.OfPlayer);
			var conduitPower = conduit?.GetComp<CompPower>();
			var lampPower = lamp?.GetComp<CompPowerTrader>();
			if (conduitPower != null)
			{
				map.powerNetManager.Notify_TransmitterSpawned(conduitPower);
				map.powerNetManager.UpdatePowerNetsAndConnections_First();
			}
			if (lampPower?.PowerNet == null && conduitPower != null)
				lampPower.ConnectToTransmitter(conduitPower);
			var powerNetBefore = lampPower?.PowerNet;
			electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;
			var activeBefore = electrifier.IsActiveElectric;
			var disabledUntilBefore = electrifier.electricDisabledUntil;
			var ticksGameBefore = GenTicks.TicksGame;
			var fireBefore = CountThingsNear(map, buildingCell, ThingDefOf.Fire, 1.5f);

			ZombieStateHandler.Electrify(electrifier);

			var fireAfter = CountThingsNear(map, buildingCell, ThingDefOf.Fire, 1.5f);
			var disabledUntilAfter = electrifier.electricDisabledUntil;
			var activeAfter = electrifier.IsActiveElectric;
			var expectedMinimumDisableUntil = ticksGameBefore + GenDate.TicksPerHour / 4;

			return new
			{
				success = conduit != null && lamp != null && powerNetBefore != null && activeBefore && activeAfter == false && disabledUntilAfter >= expectedMinimumDisableUntil,
				electrifier = DescribeZombie(electrifier),
				electrifierCell = ZombieRuntimeActions.DescribeCell(electrifierCell),
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				buildingDef = lamp?.def?.defName,
				conduitDef = conduit?.def?.defName,
				hadConduitPower = conduitPower != null,
				hadConduitPowerNet = conduitPower?.PowerNet != null,
				hadPowerNet = powerNetBefore != null,
				activeBefore,
				activeAfter,
				ticksGameBefore,
				disabledUntilBefore,
				disabledUntilAfter,
				expectedMinimumDisableUntil,
				fireBefore,
				fireAfter,
				fireDelta = fireAfter - fireBefore
			};
		}

		[Tool("zombieland/active_electrifier_attack_verb_contract", Description = "Verify ordinary ranged pawns use ranged verbs on normal zombies but not on active electrifiers.")]
		public static object ActiveElectrifierAttackVerbContract()
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

			var targetCells = GenRadial.RadialCellsAround(actorCell, 14f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.Take(2)
				.ToArray();
			if (targetCells.Length < 2)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "Fewer than two clear line-of-sight target cells were found for the active electrifier attack-verb fixture."
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

			var normal = ZombieRuntimeActions.SpawnZombie(targetCells[0], map, ZombieType.Normal, true);
			var electrifier = ZombieRuntimeActions.SpawnZombie(targetCells[1], map, ZombieType.Electrifier, true);
			if (normal == null || electrifier == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					normal = DescribeZombie(normal),
					electrifier = DescribeZombie(electrifier),
					error = "ZombieGenerator.SpawnZombie returned no normal zombie or electrifier test zombie."
				};
			}

			electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;
			var electrifierActive = electrifier.IsActiveElectric;
			var normalVerb = actor.TryGetAttackVerb(normal);
			var electrifierVerb = actor.TryGetAttackVerb(electrifier);
			var normalRanged = normalVerb != null && normalVerb.IsMeleeAttack == false;
			var electrifierSafe = electrifierVerb == null || electrifierVerb.CanHarmElectricZombies();

			return new
			{
				success = normalRanged && electrifierSafe && electrifierActive,
				destroyedZombies,
				actor = DescribePawn(actor),
				weaponDef = weapon.def?.defName,
				normal = DescribeZombie(normal),
				electrifier = DescribeZombie(electrifier),
				electrifierActive,
				normalVerb = DescribeVerb(normalVerb),
				electrifierVerb = DescribeVerb(electrifierVerb),
				normalRanged,
				electrifierSafe
			};
		}

		[Tool("zombieland/active_electrifier_bullet_absorption_contract", Description = "Verify active electrifiers absorb ordinary bullets while disabled electrifiers still take normal zombie injury.")]
		public static object ActiveElectrifierBulletAbsorptionContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var activeCell, out var activeSpawnError) == false)
				return activeSpawnError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(4, 0, 0), 10f, out var disabledCell, out var disabledSpawnError) == false)
				return disabledSpawnError;

			var active = ZombieRuntimeActions.SpawnZombie(activeCell, map, ZombieType.Electrifier, true);
			var disabled = ZombieRuntimeActions.SpawnZombie(disabledCell, map, ZombieType.Electrifier, true);
			if (active == null || disabled == null)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					disabled = DescribeZombie(disabled),
					error = "ZombieGenerator.SpawnZombie returned no active or disabled electrifier test zombie."
				};
			}

			NormalizeFireDamagePawn(active);
			NormalizeFireDamagePawn(disabled);
			active.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabled.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;
			active.absorbAttack.Clear();
			disabled.absorbAttack.Clear();

			var activeInjuryBefore = TotalInjurySeverity(active);
			var disabledInjuryBefore = TotalInjurySeverity(disabled);
			var activeAbsorbBefore = active.absorbAttack.Count;
			var disabledAbsorbBefore = disabled.absorbAttack.Count;
			var activeWasElectric = active.IsActiveElectric;
			var disabledWasElectric = disabled.IsActiveElectric;

			var activeDamage = active.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 20f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
			var disabledDamage = disabled.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 20f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));

			var activeInjuryAfter = TotalInjurySeverity(active);
			var disabledInjuryAfter = TotalInjurySeverity(disabled);
			var activeAbsorbAfter = active.absorbAttack.Count;
			var disabledAbsorbAfter = disabled.absorbAttack.Count;
			var activeInjuryDelta = activeInjuryAfter - activeInjuryBefore;
			var disabledInjuryDelta = disabledInjuryAfter - disabledInjuryBefore;
			var activeAbsorbed = activeInjuryDelta <= 0f && activeDamage.totalDamageDealt <= 0f && activeAbsorbAfter > activeAbsorbBefore;
			var disabledDamaged = disabledInjuryDelta > 0f && disabledDamage.totalDamageDealt > 0f && disabledAbsorbAfter == disabledAbsorbBefore;

			return new
			{
				success = activeWasElectric && disabledWasElectric == false && activeAbsorbed && disabledDamaged,
				destroyedZombies,
				active = DescribeZombie(active),
				disabled = DescribeZombie(disabled),
				activeWasElectric,
				disabledWasElectric,
				activeDamageTotal = activeDamage.totalDamageDealt,
				disabledDamageTotal = disabledDamage.totalDamageDealt,
				activeInjuryBefore,
				activeInjuryAfter,
				activeInjuryDelta,
				disabledInjuryBefore,
				disabledInjuryAfter,
				disabledInjuryDelta,
				activeAbsorbBefore,
				activeAbsorbAfter,
				disabledAbsorbBefore,
				disabledAbsorbAfter,
				activeAbsorbed,
				disabledDamaged
			};
		}

		[Tool("zombieland/active_electrifier_melee_shock_contract", Description = "Verify active electrifier melee hides zombie bite and converts a smokepop-belt hit into ElectricalShock.")]
		public static object ActiveElectrifierMeleeShockContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var activeCell, out var activeSpawnError) == false)
				return activeSpawnError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(2, 0, 0), 8f, out var activeTargetCell, out var activeTargetError) == false)
				return activeTargetError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(5, 0, 0), 10f, out var disabledCell, out var disabledSpawnError) == false)
				return disabledSpawnError;
			if (TryFindClearSpawnCell(map, disabledCell + new IntVec3(2, 0, 0), 8f, out var disabledTargetCell, out var disabledTargetError) == false)
				return disabledTargetError;

			var apparelDef = DefDatabase<ThingDef>.GetNamed("Apparel_SmokepopBelt", false);
			if (apparelDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Apparel_SmokepopBelt was not found."
				};
			}

			var active = ZombieRuntimeActions.SpawnZombie(activeCell, map, ZombieType.Electrifier, true);
			var disabled = ZombieRuntimeActions.SpawnZombie(disabledCell, map, ZombieType.Electrifier, true);
			var activeTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var disabledTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(activeTarget, activeTargetCell, map, Rot4.South);
			GenSpawn.Spawn(disabledTarget, disabledTargetCell, map, Rot4.South);
			DisablePawnWork(activeTarget);
			DisablePawnWork(disabledTarget);
			NormalizeFireDamagePawn(activeTarget);
			NormalizeFireDamagePawn(disabledTarget);
			if (active == null || disabled == null)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					disabled = DescribeZombie(disabled),
					error = "ZombieGenerator.SpawnZombie returned no active or disabled electrifier test zombie."
				};
			}
			NormalizeFireDamagePawn(active);
			NormalizeFireDamagePawn(disabled);
			active.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabled.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;

			var activeBelt = ThingMaker.MakeThing(apparelDef) as Apparel;
			var disabledBelt = ThingMaker.MakeThing(apparelDef) as Apparel;
			if (activeBelt == null || disabledBelt == null)
			{
				return new
				{
					success = false,
					error = "Apparel_SmokepopBelt did not create Apparel."
				};
			}
			activeTarget.apparel.Wear(activeBelt, false);
			disabledTarget.apparel.Wear(disabledBelt, false);

			var activeWasElectric = active.IsActiveElectric;
			var disabledWasElectric = disabled.IsActiveElectric;
			var activeAvailableVerbs = active.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var activeHasZombieBite = activeAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var activeAvailableDamageDefs = activeAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var disabledAvailableVerbs = disabled.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var disabledHasZombieBite = disabledAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var disabledAvailableDamageDefs = disabledAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var activeVerb = active.meleeVerbs.TryGetMeleeVerb(activeTarget);
			var disabledVerb = disabled.meleeVerbs.TryGetMeleeVerb(disabledTarget);

			if (TryMeleeDamageInfosToApply(activeVerb, activeTarget, out var activeDamageInfos, out var activeError) == false)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					activeVerb = DescribeVerb(activeVerb),
					error = activeError
				};
			}
			if (TryMeleeDamageInfosToApply(disabledVerb, disabledTarget, out var disabledDamageInfos, out var disabledError) == false)
			{
				return new
				{
					success = false,
					disabled = DescribeZombie(disabled),
					disabledVerb = DescribeVerb(disabledVerb),
					error = disabledError
				};
			}

			var activeElectricalShock = activeDamageInfos.Any(info => info.Def == CustomDefs.ElectricalShock && info.Weapon == CustomDefs.ElectricalField);
			var disabledElectricalShock = disabledDamageInfos.Any(info => info.Def == CustomDefs.ElectricalShock);
			var activeBiteHidden = activeHasZombieBite == false;
			var disabledBiteHidden = disabledHasZombieBite == false;

			return new
			{
				success = activeWasElectric
					&& disabledWasElectric == false
					&& activeBiteHidden
					&& disabledBiteHidden
					&& activeElectricalShock
					&& disabledElectricalShock == false,
				destroyedZombies,
				active = DescribeZombie(active),
				disabled = DescribeZombie(disabled),
				activeTarget = DescribePawn(activeTarget),
				disabledTarget = DescribePawn(disabledTarget),
				activeWasElectric,
				disabledWasElectric,
				activeVerb = DescribeVerb(activeVerb),
				disabledVerb = DescribeVerb(disabledVerb),
				activeAvailableDamageDefs,
				disabledAvailableDamageDefs,
				activeBiteHidden,
				disabledBiteHidden,
				activeElectricalShock,
				disabledElectricalShock,
				activeDamageInfos = DescribeDamageInfos(activeDamageInfos),
				disabledDamageInfos = DescribeDamageInfos(disabledDamageInfos),
				activeBeltDef = activeBelt.def?.defName,
				disabledBeltDef = disabledBelt.def?.defName
			};
		}

		[Tool("zombieland/albino_melee_bite_hidden_contract", Description = "Verify albino zombies hide ZombieBite from real melee verb selection while normal zombies still expose it.")]
		public static object AlbinoMeleeBiteHiddenContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoSpawnError) == false)
				return albinoSpawnError;
			if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(4, 0, 0), 10f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(2, 0, 0), 8f, out var targetCell, out var targetSpawnError) == false)
				return targetSpawnError;

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			var normal = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(target, targetCell, map, Rot4.South);
			DisablePawnWork(target);
			NormalizeFireDamagePawn(target);
			if (albino == null || normal == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					normal = DescribeZombie(normal),
					error = "ZombieGenerator.SpawnZombie returned no albino or normal zombie."
				};
			}

			var albinoAvailableVerbs = albino.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var albinoHasZombieBite = albinoAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var albinoAvailableDamageDefs = albinoAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var normalAvailableVerbs = normal.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var normalHasZombieBite = normalAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var normalAvailableDamageDefs = normalAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var albinoVerb = albino.meleeVerbs.TryGetMeleeVerb(target);
			var normalVerb = normal.meleeVerbs.TryGetMeleeVerb(target);
			var albinoBiteHidden = albinoHasZombieBite == false;

			return new
			{
				success = albino?.isAlbino == true && albinoBiteHidden && normalHasZombieBite,
				destroyedZombies,
				albino = DescribeZombie(albino),
				normal = DescribeZombie(normal),
				target = DescribePawn(target),
				albinoVerb = DescribeVerb(albinoVerb),
				normalVerb = DescribeVerb(normalVerb),
				albinoAvailableDamageDefs,
				normalAvailableDamageDefs,
				albinoBiteHidden,
				normalHasZombieBite
			};
		}

		[Tool("zombieland/hostility_to_zombies_contract", Description = "Verify real GenHostility.HostileTo zombie rules for player, hostile, animal, and factionless pawns.")]
		public static object HostilityToZombiesContract()
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

			var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (animalKind == null || zombieFaction == null)
			{
				return new
				{
					success = false,
					error = "PawnKindDef Muffalo or zombie faction was not found."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(3, 0, 0), 8f, out var playerCell, out var playerSpawnError) == false)
				return playerSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(6, 0, 0), 10f, out var hostileCell, out var hostileSpawnError) == false)
				return hostileSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(9, 0, 0), 12f, out var factionlessCell, out var factionlessSpawnError) == false)
				return factionlessSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(0, 0, 3), 8f, out var animalCell, out var animalSpawnError) == false)
				return animalSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var player = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var hostile = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
			var factionless = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, null);
			var animal = PawnGenerator.GeneratePawn(animalKind, null);
			GenSpawn.Spawn(player, playerCell, map, Rot4.South);
			GenSpawn.Spawn(hostile, hostileCell, map, Rot4.South);
			GenSpawn.Spawn(factionless, factionlessCell, map, Rot4.South);
			GenSpawn.Spawn(animal, animalCell, map, Rot4.South);
			DisablePawnWork(player);
			DisablePawnWork(hostile);
			DisablePawnWork(factionless);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no normal zombie."
				};
			}

			var settings = ZombieSettings.Values;
			var originalEnemiesAttackZombies = settings.enemiesAttackZombies;
			var originalAnimalsAttackZombies = settings.animalsAttackZombies;
			(bool value, string error) playerThing;
			(bool value, string error) hostileThingDisabled;
			(bool value, string error) hostileThingEnabled;
			(bool value, string error) animalThingDisabled;
			(bool value, string error) animalThingEnabled;
			(bool value, string error) factionlessThing;
			(bool value, string error) factionlessFaction;
			try
			{
				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				playerThing = TryHostileTo(player, zombie);
				hostileThingDisabled = TryHostileTo(hostile, zombie);
				animalThingDisabled = TryHostileTo(animal, zombie);
				factionlessThing = TryHostileTo(factionless, zombie);
				factionlessFaction = TryHostileTo(factionless, zombieFaction);

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				hostileThingEnabled = TryHostileTo(hostile, zombie);
				animalThingEnabled = TryHostileTo(animal, zombie);
			}
			finally
			{
				settings.enemiesAttackZombies = originalEnemiesAttackZombies;
				settings.animalsAttackZombies = originalAnimalsAttackZombies;
			}

			var noErrors = new[] { playerThing, hostileThingDisabled, hostileThingEnabled, animalThingDisabled, animalThingEnabled, factionlessThing, factionlessFaction }
				.All(sample => sample.error == null);

			return new
			{
				success = noErrors
					&& playerThing.value
					&& hostileThingDisabled.value == false
					&& hostileThingEnabled.value
					&& animalThingDisabled.value == false
					&& animalThingEnabled.value
					&& factionlessThing.value == false
					&& factionlessFaction.value == false,
				destroyedZombies,
				zombie = DescribeZombie(zombie),
				player = DescribePawn(player),
				hostile = DescribePawn(hostile),
				factionless = DescribePawn(factionless),
				animal = DescribePawn(animal),
				zombieFaction = zombieFaction.def?.defName,
				playerThing = DescribeHostility(playerThing),
				hostileThingDisabled = DescribeHostility(hostileThingDisabled),
				hostileThingEnabled = DescribeHostility(hostileThingEnabled),
				animalThingDisabled = DescribeHostility(animalThingDisabled),
				animalThingEnabled = DescribeHostility(animalThingEnabled),
				factionlessThing = DescribeHostility(factionlessThing),
				factionlessFaction = DescribeHostility(factionlessFaction),
				noErrors
			};
		}

		[Tool("zombieland/zombie_faction_world_contract", Description = "Verify the loaded world has exactly one zombie faction and mutual hostility relations.")]
		public static object ZombieFactionWorldContract()
		{
			if (Find.FactionManager == null)
			{
				return new
				{
					success = false,
					error = "No RimWorld faction manager is available."
				};
			}

			var factions = Find.FactionManager.AllFactions.ToArray();
			var zombieFactions = factions
				.Where(faction => faction?.def == ZombieDefOf.Zombies)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var playerFaction = Faction.OfPlayer;
			var nonZombieFactions = factions
				.Where(faction => faction != null && faction.def != ZombieDefOf.Zombies)
				.ToArray();
			var relations = zombieFaction == null
				? Array.Empty<object>()
				: nonZombieFactions.Select(faction => new
				{
					faction = faction.def?.defName,
					factionName = faction.Name,
					factionHostileToZombies = faction.HostileTo(zombieFaction),
					zombiesHostileToFaction = zombieFaction.HostileTo(faction),
					factionRelationKind = faction.RelationKindWith(zombieFaction).ToString(),
					zombieRelationKind = zombieFaction.RelationKindWith(faction).ToString()
				}).Cast<object>().ToArray();
			var mutualHostility = zombieFaction != null
				&& nonZombieFactions.All(faction => faction.HostileTo(zombieFaction) && zombieFaction.HostileTo(faction));
			var playerHostility = zombieFaction != null
				&& playerFaction != null
				&& playerFaction.HostileTo(zombieFaction)
				&& zombieFaction.HostileTo(playerFaction);

			return new
			{
				success = zombieFactions.Length == 1
					&& zombieFaction != null
					&& playerHostility
					&& mutualHostility,
				zombieFactionCount = zombieFactions.Length,
				zombieFaction = zombieFaction == null ? null : new
				{
					defName = zombieFaction.def?.defName,
					zombieFaction.Name,
					hidden = zombieFaction.Hidden,
					temporary = zombieFaction.temporary
				},
				playerFaction = playerFaction?.def?.defName,
				playerHostility,
				mutualHostility,
				factionCount = factions.Length,
				nonZombieFactionCount = nonZombieFactions.Length,
				relations
			};
		}

		[Tool("zombieland/zombie_ticking_budget_contract", Description = "Verify reduced zombie ticking counts production ticks and keeps move-speed compensation active.")]
		public static object ZombieTickingBudgetContract()
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

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland tick manager is available on the current map."
				};
			}

			var originalPercents = (float[])ZombieTicker.percentZombiesTicked.Clone();
			var originalPercentIndex = ZombieTicker.percentZombiesTickedIndex;
			var originalZombiesTicked = ZombieTicker.zombiesTicked;
			var originalMaxTicking = ZombieTicker.maxTicking;
			var originalCurrentTicking = ZombieTicker.currentTicking;
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var spawned = new List<Zombie>();

			try
			{
				const int targetCount = 20;
				const float targetPercent = 0.25f;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				for (var i = 0; i < targetCount; i++)
				{
					var candidateRoot = root + new IntVec3((i % 5) * 3, 0, (i / 5) * 3);
					if (TryFindClearSpawnCell(map, candidateRoot, 16f, out var cell, out var spawnError) == false)
						return spawnError;

					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						return new
						{
							success = false,
							destroyedExisting,
							spawnedCount = spawned.Count,
							error = "ZombieGenerator.SpawnZombie returned no normal zombie."
						};
					}
					spawned.Add(zombie);
				}

				tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
				foreach (var zombie in spawned)
					_ = tickManager.allZombiesCached.Add(zombie);

				var liveCachedBefore = tickManager.allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false);
				FillZombieTickPercent(targetPercent);
				var percentBeforeTicking = ZombieTicker.PercentTicking;
				var expectedTicking = Mathf.FloorToInt(liveCachedBefore * percentBeforeTicking);
				ZombieTicker.zombiesTicked = 0;
				tickManager.ZombieTicking();
				var subsetCount = tickManager.currentZombiesTicking?.Length ?? 0;
				var tickedCount = ZombieTicker.zombiesTicked;
				var allTickedWereLive = tickManager.currentZombiesTicking?.All(zombie => zombie.Spawned && zombie.Dead == false) ?? false;

				var sample = spawned.FirstOrDefault(zombie => zombie.Spawned && zombie.Dead == false);
				FillZombieTickPercent(1f);
				var normalSpeed = sample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				FillZombieTickPercent(targetPercent);
				var throttledSpeed = sample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				var speedRatio = normalSpeed == 0f ? 0f : throttledSpeed / normalSpeed;

				return new
				{
					success = spawned.Count == targetCount
						&& liveCachedBefore == targetCount
						&& expectedTicking > 0
						&& expectedTicking < liveCachedBefore
						&& subsetCount == expectedTicking
						&& tickedCount == subsetCount
						&& allTickedWereLive
						&& speedRatio > 3.5f,
					destroyedExisting,
					spawnedCount = spawned.Count,
					liveCachedBefore,
					targetPercent,
					percentBeforeTicking,
					expectedTicking,
					subsetCount,
					tickedCount,
					allTickedWereLive,
					normalSpeed,
					throttledSpeed,
					speedRatio,
					sample = DescribeZombie(sample)
				};
			}
			finally
			{
				ZombieRuntimeActions.DestroyZombies(map);
				ZombieTicker.percentZombiesTicked = originalPercents;
				ZombieTicker.percentZombiesTickedIndex = originalPercentIndex;
				ZombieTicker.zombiesTicked = originalZombiesTicked;
				ZombieTicker.maxTicking = originalMaxTicking;
				ZombieTicker.currentTicking = originalCurrentTicking;
				tickManager.currentZombiesTicking = Array.Empty<Zombie>();
				tickManager.currentZombiesTickingIndex = 0;
			}
		}

		[Tool("zombieland/zombie_ticking_feedback_contract", Description = "Verify the TickManagerUpdate patch resets, sizes, and feeds back the adaptive zombie tick budget.")]
		public static object ZombieTickingFeedbackContract()
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

			var tickManager = map.GetComponent<TickManager>();
			var gameTickManager = Find.TickManager;
			if (tickManager == null || gameTickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland or RimWorld tick manager is available."
				};
			}

			var patch = typeof(Patches).GetNestedType("Verse_TickManager_TickManagerUpdate_Patch", BindingFlags.NonPublic);
			var prefix = patch?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var postfix = patch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (prefix == null || postfix == null)
			{
				return new
				{
					success = false,
					error = "Could not find TickManagerUpdate prefix/postfix by reflection.",
					prefixFound = prefix != null,
					postfixFound = postfix != null
				};
			}

			var originalTimeSpeed = gameTickManager.CurTimeSpeed;
			var originalRealTimeToTickThrough = gameTickManager.realTimeToTickThrough;
			var originalPercents = (float[])ZombieTicker.percentZombiesTicked.Clone();
			var originalPercentIndex = ZombieTicker.percentZombiesTickedIndex;
			var originalZombiesTicked = ZombieTicker.zombiesTicked;
			var originalMaxTicking = ZombieTicker.maxTicking;
			var originalCurrentTicking = ZombieTicker.currentTicking;
			var originalManagers = ZombieTicker.managers;
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var spawned = new List<Zombie>();

			try
			{
				const int targetCount = 20;
				const float prefixPercent = 0.25f;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				for (var i = 0; i < targetCount; i++)
				{
					var candidateRoot = root + new IntVec3((i % 5) * 3, 0, (i / 5) * 3);
					if (TryFindClearSpawnCell(map, candidateRoot, 16f, out var cell, out var spawnError) == false)
						return spawnError;

					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						return new
						{
							success = false,
							destroyedExisting,
							spawnedCount = spawned.Count,
							error = "ZombieGenerator.SpawnZombie returned no normal zombie."
						};
					}
					spawned.Add(zombie);
				}

				tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
				foreach (var zombie in spawned)
					_ = tickManager.allZombiesCached.Add(zombie);

				FillZombieTickPercent(prefixPercent);
				ZombieTicker.zombiesTicked = 12345;
				gameTickManager.CurTimeSpeed = TimeSpeed.Normal;
				gameTickManager.realTimeToTickThrough = gameTickManager.CurTimePerTick * 2f;
				prefix.Invoke(null, new object[] { gameTickManager });

				var liveCached = tickManager.allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false);
				var prefixPercentRead = ZombieTicker.PercentTicking;
				var prefixMaxTicking = ZombieTicker.maxTicking;
				var prefixCurrentTicking = ZombieTicker.currentTicking;
				var prefixExpectedCurrent = Mathf.FloorToInt(prefixMaxTicking * prefixPercentRead);
				var prefixResetCounter = ZombieTicker.zombiesTicked == 0;
				var prefixSizedBudget = liveCached == targetCount
					&& prefixMaxTicking >= liveCached
					&& prefixCurrentTicking == prefixExpectedCurrent
					&& prefixCurrentTicking > 0
					&& prefixCurrentTicking < prefixMaxTicking;

				FillZombieTickPercent(1f);
				ZombieTicker.currentTicking = 400;
				ZombieTicker.zombiesTicked = 100;
				postfix.Invoke(null, new object[] { gameTickManager });

				var postfixSlot = ZombieTicker.percentZombiesTicked[0];
				var postfixIndex = ZombieTicker.percentZombiesTickedIndex;
				var postfixAverage = ZombieTicker.PercentTicking;
				var expectedPostfixSlot = 0.25f;
				var expectedPostfixAverage = 0.90625f;
				var postfixReducedBudget = Mathf.Abs(postfixSlot - expectedPostfixSlot) < 0.0001f
					&& postfixIndex == 1
					&& Mathf.Abs(postfixAverage - expectedPostfixAverage) < 0.0001f;

				return new
				{
					success = prefixResetCounter
						&& prefixSizedBudget
						&& postfixReducedBudget,
					destroyedExisting,
					spawnedCount = spawned.Count,
					liveCached,
					prefix = new
					{
						prefixPercentRead,
						prefixMaxTicking,
						prefixCurrentTicking,
						prefixExpectedCurrent,
						prefixResetCounter,
						prefixSizedBudget,
						timeSpeed = gameTickManager.CurTimeSpeed.ToString()
					},
					postfix = new
					{
						inputCurrentTicking = 400,
						inputZombiesTicked = 100,
						postfixSlot,
						expectedPostfixSlot,
						postfixIndex,
						postfixAverage,
						expectedPostfixAverage,
						postfixReducedBudget
					}
				};
			}
			finally
			{
				ZombieRuntimeActions.DestroyZombies(map);
				gameTickManager.CurTimeSpeed = originalTimeSpeed;
				gameTickManager.realTimeToTickThrough = originalRealTimeToTickThrough;
				ZombieTicker.percentZombiesTicked = originalPercents;
				ZombieTicker.percentZombiesTickedIndex = originalPercentIndex;
				ZombieTicker.zombiesTicked = originalZombiesTicked;
				ZombieTicker.maxTicking = originalMaxTicking;
				ZombieTicker.currentTicking = originalCurrentTicking;
				ZombieTicker.managers = originalManagers;
				tickManager.currentZombiesTicking = Array.Empty<Zombie>();
				tickManager.currentZombiesTickingIndex = 0;
			}
		}

		[Tool("zombieland/infected_incident_hooks_contract", Description = "Verify Zombieland's incident pawn-group infection and post-incident bite-harmless hooks.")]
		public static object InfectedIncidentHooksContract()
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

			var generatePawnsPatch = typeof(Patches).GetNestedType("IncidentWorker_Patches", BindingFlags.NonPublic);
			var generatePawnsPostfix = generatePawnsPatch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			var incidentTryExecutePatch = typeof(Patches).GetNestedType("IncidentWorker_TryExecute_Patch", BindingFlags.NonPublic);
			var incidentTryExecutePostfix = incidentTryExecutePatch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (generatePawnsPostfix == null || incidentTryExecutePostfix == null)
			{
				return new
				{
					success = false,
					error = "Could not find both infected incident postfixes by reflection.",
					generatePawnsPostfixFound = generatePawnsPostfix != null,
					incidentTryExecutePostfixFound = incidentTryExecutePostfix != null
				};
			}

			var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
			if (animalKind == null)
			{
				return new
				{
					success = false,
					error = "PawnKindDef Muffalo was not found."
				};
			}

			var pawns = new List<Pawn>();
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-8, 0, 12), 18f, out var raidHumanCell, out var raidHumanError) == false)
					return raidHumanError;
				if (TryFindClearSpawnCell(map, raidHumanCell + new IntVec3(3, 0, 0), 10f, out var raidAnimalCell, out var raidAnimalError) == false)
					return raidAnimalError;
				if (TryFindClearSpawnCell(map, raidHumanCell + new IntVec3(6, 0, 0), 12f, out var incidentPawnCell, out var incidentPawnError) == false)
					return incidentPawnError;

				var raidHuman = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				var raidAnimal = PawnGenerator.GeneratePawn(animalKind, null);
				var incidentPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				pawns.AddRange(new[] { raidHuman, raidAnimal, incidentPawn });
				GenSpawn.Spawn(raidHuman, raidHumanCell, map, Rot4.South);
				GenSpawn.Spawn(raidAnimal, raidAnimalCell, map, Rot4.South);
				GenSpawn.Spawn(incidentPawn, incidentPawnCell, map, Rot4.South);
				DisablePawnWork(raidHuman);
				DisablePawnWork(incidentPawn);

				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;
				var generatedPawns = new List<Pawn> { raidHuman, raidAnimal };
				generatePawnsPostfix.Invoke(null, new object[] { generatedPawns });
				var raidHumanInfection = ZombieRuntimeActions.DescribePawnInfection(raidHuman);
				var raidAnimalInfection = ZombieRuntimeActions.DescribePawnInfection(raidAnimal);
				var raidHumanBites = new List<Hediff_Injury_ZombieBite>();
				raidHuman.health.hediffSet.GetHediffs(ref raidHumanBites);
				var raidAnimalBites = new List<Hediff_Injury_ZombieBite>();
				raidAnimal.health.hediffSet.GetHediffs(ref raidAnimalBites);

				if (ZombieRuntimeActions.AddZombieBite(incidentPawn, "final", out var bite, out var biteError) == false)
				{
					return new
					{
						success = false,
						error = biteError
					};
				}

				var incidentBefore = ZombieRuntimeActions.DescribePawnInfection(incidentPawn);
				var parms = new IncidentParms
				{
					pawnGroups = new Dictionary<Pawn, int>
					{
						[incidentPawn] = 0
					}
				};
				Rand.PushState(6300);
				try
				{
					incidentTryExecutePostfix.Invoke(null, new object[] { parms });
				}
				finally
				{
					Rand.PopState();
				}
				var incidentAfter = ZombieRuntimeActions.DescribePawnInfection(incidentPawn);
				var incidentBites = new List<Hediff_Injury_ZombieBite>();
				incidentPawn.health.hediffSet.GetHediffs(ref incidentBites);
				var allIncidentBitesHarmless = incidentBites.Count > 0
					&& incidentBites.All(current => current.mayBecomeZombieWhenDead == false
						&& (current.TendDuration?.GetInfectionState() ?? InfectionState.None) == InfectionState.BittenHarmless);

				return new
				{
					success = raidHumanBites.Count == 1
						&& raidAnimalBites.Count == 0
						&& allIncidentBitesHarmless,
					settings = new
					{
						ZombieSettings.Values.infectedRaidsChance,
						ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					raidGeneration = new
					{
						human = DescribePawn(raidHuman),
						animal = DescribePawn(raidAnimal),
						humanInfection = raidHumanInfection,
						animalInfection = raidAnimalInfection,
						humanBiteCount = raidHumanBites.Count,
						animalBiteCount = raidAnimalBites.Count
					},
					incidentReduction = new
					{
						pawn = DescribePawn(incidentPawn),
						before = incidentBefore,
						after = incidentAfter,
						biteCount = incidentBites.Count,
						allIncidentBitesHarmless
					},
					sourcePath = "PawnGroupKindWorker.GeneratePawns postfix and IncidentWorker.TryExecute postfix"
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
				foreach (var pawn in pawns)
				{
					if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
						pawn.Corpse.Destroy(DestroyMode.Vanish);
					if (pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/zombie_faction_pawn_generation_contract", Description = "Verify Zombie faction PawnGenerator requests route normal zombies through Zombieland while preserving blob/spitter generation.")]
		public static object ZombieFactionPawnGenerationContract()
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

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
				};
			}

			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (zombieFaction == null)
			{
				return new
				{
					success = false,
					error = "No Zombies faction was found."
				};
			}

			Pawn generatedNormal = null;
			Pawn generatedSpitter = null;
			Pawn generatedBlob = null;
			try
			{
				var beforeIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				Rand.PushState(6400);
				try
				{
					generatedNormal = PawnGenerator.GeneratePawn(ZombieDefOf.Zombie, zombieFaction);
					generatedSpitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, zombieFaction);
					generatedBlob = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieBlob, zombieFaction);
				}
				finally
				{
					Rand.PopState();
				}

				var normal = generatedNormal as Zombie;
				if (normal != null)
					_ = tickManager.allZombiesCached.Add(normal);
				var newZombieIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.Where(id => beforeIds.Contains(id) == false)
					.ToArray();

				return new
				{
					success = normal != null
						&& normal.Spawned
						&& normal.Faction == zombieFaction
						&& normal.kindDef == ZombieDefOf.Zombie
						&& newZombieIds.Length == 1
						&& generatedSpitter is ZombieSpitter
						&& generatedSpitter.Spawned == false
						&& generatedBlob is ZombieBlob
						&& generatedBlob.Spawned == false,
					zombieFaction = zombieFaction.def?.defName,
					sourcePath = "PawnGenerator.GenerateNewPawnInternal prefix",
					normal = DescribeZombie(generatedNormal),
					normalSpawnedThroughPatch = normal?.Spawned ?? false,
					newZombieIds,
					spitter = DescribeZombie(generatedSpitter),
					spitterType = generatedSpitter?.GetType().FullName,
					spitterSpawned = generatedSpitter?.Spawned ?? false,
					blob = DescribeZombie(generatedBlob),
					blobType = generatedBlob?.GetType().FullName,
					blobSpawned = generatedBlob?.Spawned ?? false
				};
			}
			finally
			{
				if (generatedNormal is Zombie zombie)
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
				}
				foreach (var pawn in new[] { generatedNormal, generatedSpitter, generatedBlob }.Where(pawn => pawn != null).Distinct())
				{
					if (pawn.Spawned && pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/zombie_active_threat_count_contract", Description = "Verify GenHostility.IsActiveThreatTo excludes all Zombieland pawn types from player hostile counts.")]
		public static object ZombieActiveThreatCountContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(3, 0, 0), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(6, 0, 0), 10f, out var blobCell, out var blobSpawnError) == false)
				return blobSpawnError;

			var normal = SpawnFireFixturePawn(map, normalCell, "normal");
			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			var blob = SpawnFireFixturePawn(map, blobCell, "blob");
			var normalThreat = GenHostility.IsActiveThreatTo(normal, Faction.OfPlayer, false, false);
			var spitterThreat = GenHostility.IsActiveThreatTo(spitter, Faction.OfPlayer, false, false);
			var blobThreat = GenHostility.IsActiveThreatTo(blob, Faction.OfPlayer, false, false);

			return new
			{
				success = normal is Zombie
					&& spitter is ZombieSpitter
					&& blob is ZombieBlob
					&& normalThreat == false
					&& spitterThreat == false
					&& blobThreat == false,
				destroyedZombies,
				normal = DescribePawn(normal),
				spitter = DescribePawn(spitter),
				blob = DescribePawn(blob),
				threatsToPlayer = new
				{
					normal = normalThreat,
					spitter = spitterThreat,
					blob = blobThreat
				}
			};
		}

		[Tool("zombieland/zombie_target_cache_excludes_specials", Description = "Verify AttackTargetsCache.TargetsHostileToColony excludes normal, spitter, and blob zombies.")]
		public static object ZombieTargetCacheExcludesSpecials()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(3, 0, 0), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(6, 0, 0), 10f, out var blobCell, out var blobSpawnError) == false)
				return blobSpawnError;

			var normal = SpawnFireFixturePawn(map, normalCell, "normal");
			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			var blob = SpawnFireFixturePawn(map, blobCell, "blob");
			map.attackTargetsCache.UpdateTarget(normal);
			map.attackTargetsCache.UpdateTarget(spitter);
			map.attackTargetsCache.UpdateTarget(blob);
			var hostileTargets = map.attackTargetsCache.TargetsHostileToColony;
			var containsNormal = hostileTargets.Contains(normal);
			var containsSpitter = hostileTargets.Contains(spitter);
			var containsBlob = hostileTargets.Contains(blob);
			var zTypesInCache = hostileTargets
				.Select(target => target.Thing)
				.Where(thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieBlob)
				.Select(thing => thing.def?.defName)
				.Distinct()
				.OrderBy(defName => defName)
				.ToArray();

			return new
			{
				success = normal is Zombie
					&& spitter is ZombieSpitter
					&& blob is ZombieBlob
					&& containsNormal == false
					&& containsSpitter == false
					&& containsBlob == false,
				destroyedZombies,
				normal = DescribePawn(normal),
				spitter = DescribePawn(spitter),
				blob = DescribePawn(blob),
				hostileCount = hostileTargets.Count,
				contains = new
				{
					normal = containsNormal,
					spitter = containsSpitter,
					blob = containsBlob
				},
				zombielandDefsInCache = zTypesInCache
			};
		}

	}
}
