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
		[Tool("zombieland/spit_zombie_ball", Description = "Put a spitter into its firing state and verify the real job-driver shoot path spawns a ZombieBall projectile.")]
		public static object SpitZombieBall(
			[ToolParameter(Description = "Optional spitter id, ThingID, label, or short name. When omitted, a fresh spitter is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for target selection and projectile launch setup.", Required = false, DefaultValue = 515151)] int seed = 515151)
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

			if (TryFindOrSpawnSpitter(map, target, out var spitter, out var spawnedSpitter, out var error) == false)
				return error;

			var before = DescribeZombie(spitter);
			var zombieBallsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountBefore = CurrentZombies(map).Length;
			_ = ForceSpitterShot(map, spitter, seed);

			var zombieBallsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountAfter = CurrentZombies(map).Length;
			var zombieBalls = map.listerThings.AllThings
				.Where(thing => thing.def == CustomDefs.ZombieBall)
				.Select(thing => new
				{
					thingId = thing.ThingID,
					position = ZombieRuntimeActions.DescribeCell(thing.Position),
					spawned = thing.Spawned
				})
				.ToArray();

			return new
			{
				success = zombieBallsAfter > zombieBallsBefore && spitter.remainingZombies == 0,
				spawnedSpitter,
				seed,
				zombieBallsBefore,
				zombieBallsAfter,
				zombieBallDelta = zombieBallsAfter - zombieBallsBefore,
				zombieCountBefore,
				zombieCountAfter,
				before,
				after = DescribeZombie(spitter),
				zombieBalls
			};
		}

		[Tool("zombieland/impact_zombie_ball", Description = "Launch a real ZombieBall projectile from a spitter and verify its impact path spawns a zombie.")]
		public static object ImpactZombieBall(
			[ToolParameter(Description = "Optional spitter id, ThingID, label, or short name. When omitted, a fresh spitter is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for target selection and projectile launch setup.", Required = false, DefaultValue = 616161)] int seed = 616161)
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

			if (TryFindOrSpawnSpitter(map, target, out var spitter, out var spawnedSpitter, out var error) == false)
				return error;

			var before = DescribeZombie(spitter);
			var zombieCountBefore = CurrentZombies(map).Length;
			var zombieBallsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var projectileTarget = GenRadial.RadialCellsAround(spitter.Position, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(spitter.Position) >= 3f)
				.Where(cell => map.roofGrid.RoofAt(cell)?.isThickRoof != true)
				.DefaultIfEmpty(IntVec3.Invalid)
				.First();
			if (projectileTarget.IsValid == false)
			{
				return new
				{
					success = false,
					spawnedSpitter,
					before,
					after = DescribeZombie(spitter),
					error = "No nearby clear projectile target was found."
				};
			}

			ZombieBall projectile;
			Rand.PushState(seed);
			try
			{
				projectile = GenSpawn.Spawn(CustomDefs.ZombieBall, spitter.Position, map, WipeMode.Vanish) as ZombieBall;
				projectile?.Launch(spitter, spitter.DrawPos + new UnityEngine.Vector3(0, 0, 0.5f), projectileTarget, projectileTarget, ProjectileHitFlags.IntendedTarget);
			}
			finally
			{
				Rand.PopState();
			}

			if (projectile == null)
			{
				return new
				{
					success = false,
					spawnedSpitter,
					before,
					after = DescribeZombie(spitter),
					error = "Spawning ZombieBall did not produce a projectile."
				};
			}

			var projectileStart = projectile.Position;
			var speed = Math.Max(0.001f, projectile.def.projectile.SpeedTilesPerTick);
			var projectileUpdateRateTicks = Math.Max(1, projectile.UpdateRateTicks);
			projectile.Impact(null);

			var zombieCountAfter = CurrentZombies(map).Length;
			var zombieBallsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var spawnedZombies = CurrentZombies(map)
				.Where(pawn => pawn is Zombie)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(projectileTarget))
				.Take(5)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = zombieCountAfter > zombieCountBefore && zombieBallsAfter <= zombieBallsBefore,
				spawnedSpitter,
				seed,
				projectileStart = ZombieRuntimeActions.DescribeCell(projectileStart),
				projectileTarget = ZombieRuntimeActions.DescribeCell(projectileTarget),
				speedTilesPerTick = speed,
				projectileUpdateRateTicks,
				impactCalledDirectly = true,
				zombieBallsBefore,
				zombieBallsAfter,
				zombieCountBefore,
				zombieCountAfter,
				before,
				after = DescribeZombie(spitter),
				nearestSpawnedZombies = spawnedZombies
			};
		}

		[Tool("zombieland/zombie_ball_in_flight", Description = "Launch a real ZombieBall, advance to the source-derived halfway point, verify it is still in flight, then let it impact.")]
		public static object ZombieBallInFlight(
			[ToolParameter(Description = "Deterministic Rand seed for projectile launch setup.", Required = false, DefaultValue = 717171)] int seed = 717171)
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
			if (TryFindClearSpawnCell(map, root, 16f, out var spitterCell, out var spawnError) == false)
				return spawnError;

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					error = "Could not spawn a spitter for ZombieBall travel."
				};
			}

			var targetCell = GenRadial.RadialCellsAround(spitter.Position, 14f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(spitter.Position) >= 8f)
				.Where(cell => map.roofGrid.RoofAt(cell)?.isThickRoof != true)
				.OrderByDescending(cell => cell.DistanceToSquared(spitter.Position))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					error = "No clear distant ZombieBall target was found."
				};
			}

			var zombieCountBefore = CurrentZombies(map).Length;
			var ballsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			ZombieBall projectile;
			Rand.PushState(seed);
			try
			{
				projectile = GenSpawn.Spawn(CustomDefs.ZombieBall, spitter.Position, map, WipeMode.Vanish) as ZombieBall;
				projectile?.Launch(spitter, spitter.DrawPos + new Vector3(0, 0, 0.5f), targetCell, targetCell, ProjectileHitFlags.IntendedTarget);
			}
			finally
			{
				Rand.PopState();
			}

			if (projectile == null)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					error = "Spawning ZombieBall did not produce a projectile."
				};
			}

			var startCell = projectile.Position;
			var startExact = projectile.ExactPosition;
			var startRotation = projectile.ExactRotation.eulerAngles.y;
			var origin = spitter.DrawPos + new Vector3(0, 0, 0.5f);
			var destination = targetCell.ToVector3Shifted();
			var startingTicks = Math.Max(1, Mathf.CeilToInt((origin - destination).magnitude / projectile.def.projectile.SpeedTilesPerTick));
			var halfwayTicks = Math.Max(1, startingTicks / 2);
			AdvanceGameTicks(halfwayTicks);

			var inFlightSpawned = projectile.Spawned && projectile.Destroyed == false;
			var halfwayCell = inFlightSpawned ? projectile.Position : IntVec3.Invalid;
			var halfwayExact = inFlightSpawned ? projectile.ExactPosition : Vector3.zero;
			var halfwayRotation = inFlightSpawned ? projectile.ExactRotation.eulerAngles.y : 0f;
			var movedFromStart = inFlightSpawned && (halfwayExact - startExact).MagnitudeHorizontalSquared() > 0.01f;
			var notYetAtTarget = inFlightSpawned && projectile.Position != targetCell;
			var remainingTicks = startingTicks - halfwayTicks + 5;
			AdvanceGameTicks(remainingTicks);

			var ballsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountAfter = CurrentZombies(map).Length;
			var nearestSpawnedZombies = CurrentZombies(map)
				.Where(pawn => pawn is Zombie)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(targetCell))
				.Take(5)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = inFlightSpawned
					&& movedFromStart
					&& notYetAtTarget
					&& Math.Abs(Mathf.DeltaAngle(startRotation, halfwayRotation)) > 0.1f
					&& ballsAfter <= ballsBefore
					&& zombieCountAfter > zombieCountBefore,
				seed,
				spitter = DescribeZombie(spitter),
				startCell = ZombieRuntimeActions.DescribeCell(startCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				halfwayCell = halfwayCell.IsValid ? ZombieRuntimeActions.DescribeCell(halfwayCell) : null,
				startExact = DescribeVector(startExact),
				halfwayExact = DescribeVector(halfwayExact),
				startRotation,
				halfwayRotation,
				startingTicks,
				halfwayTicks,
				remainingTicks,
				speedTilesPerTick = projectile.def.projectile.SpeedTilesPerTick,
				inFlightSpawned,
				movedFromStart,
				notYetAtTarget,
				ballsBefore,
				ballsAfter,
				zombieCountBefore,
				zombieCountAfter,
				nearestSpawnedZombies
			};
		}

		[Tool("zombieland/spawn_blob", Description = "Spawn a ZombieBlob through its runtime spawn path and verify it enters the map with the blob job.")]
		public static object SpawnBlob(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1)
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

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
				return error;

			var existing = CurrentZombies(map).OfType<ZombieBlob>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieBlob.Spawn(map, cell);
			var blob = CurrentZombies(map).OfType<ZombieBlob>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();

			return new
			{
				success = blob?.Spawned == true && blob.CurJobDef == CustomDefs.Blob,
				requestedCell = ZombieRuntimeActions.DescribeCell(root),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				blob = DescribeZombie(blob),
				assets = new
				{
					Assets.initialized,
					hasMetaballShader = Assets.MetaballShader != null
				}
			};
		}

		[Tool("zombieland/zombie_area_risk_contract", Description = "Verify dangerous-area risk modes classify colonists, normal zombies, spitters, and blobs consistently.")]
		public static object ZombieAreaRiskContract()
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
			if (TryFindClearSpawnCell(map, root + new IntVec3(-6, 0, 0), 16f, out var colonistCell, out var colonistCellError) == false)
				return colonistCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(-2, 0, 0), 16f, out var normalCell, out var normalCellError) == false)
				return normalCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 0), 16f, out var spitterCell, out var spitterCellError) == false)
				return spitterCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(6, 0, 0), 16f, out var blobCell, out var blobCellError) == false)
				return blobCellError;

			var previousDangerousAreas = ZombieSettings.Values.dangerousAreas.ToDictionary(pair => pair.Key, pair => pair.Value);
			var createdAreas = new List<Area>();
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var colonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
				DisablePawnWork(colonist);
				spawnedThings.Add(colonist);

				var normal = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
				if (normal != null)
					spawnedThings.Add(normal);
				ZombieSpitter.Spawn(map, spitterCell);
				var spitter = CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
				if (spitter != null)
					spawnedThings.Add(spitter);
				ZombieBlob.Spawn(map, blobCell);
				var blob = CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(blobCell)).FirstOrDefault();
				if (blob != null)
					spawnedThings.Add(blob);

				if (normal == null || spitter == null || blob == null)
				{
					return new
					{
						success = false,
						colonist = DescribePawn(colonist),
						normal = DescribeZombie(normal),
						spitter = DescribeZombie(spitter),
						blob = DescribeZombie(blob),
						error = "Could not create all dangerous-area pawn fixtures."
					};
				}

				if (map.areaManager.TryMakeNewAllowed(out Area_Allowed area) == false)
				{
					return new
					{
						success = false,
						error = "Could not create a test allowed area."
					};
				}
				createdAreas.Add(area);
				area.labelInt = "ZombielandRiskContract";

				void RunAreaStateUpdater()
				{
					ZombieAreaManager.pawnsInDanger.Clear();
					ZombieAreaManager.lastMap = null;
					var stateUpdater = typeof(ZombieAreaManager)
						.GetMethod("StateUpdater", BindingFlags.Static | BindingFlags.NonPublic)
						.Invoke(null, null) as System.Collections.IEnumerator;
					ZombieAreaManager.stateUpdater = stateUpdater;
					for (var i = 0; i < 64; i++)
					{
						if (ZombieAreaManager.stateUpdater.MoveNext())
							continue;
						ZombieAreaManager.stateUpdater = stateUpdater;
						break;
					}
				}

				object Snapshot(string label, AreaRiskMode mode, params IntVec3[] cells)
				{
					foreach (var cell in area.ActiveCells.ToArray())
						area[cell] = false;
					foreach (var cell in cells)
						area[cell] = true;

					ZombieSettings.Values.dangerousAreas.Clear();
					if (mode != AreaRiskMode.Ignore)
						ZombieSettings.Values.dangerousAreas[area] = mode;
					RunAreaStateUpdater();

					var entries = ZombieAreaManager.pawnsInDanger
						.Select(pair => new
						{
							pawn = DescribePawn(pair.Key),
							kind = DescribeZombieKind(pair.Key as Zombie, pair.Key as ZombieBlob, pair.Key as ZombieSpitter),
							area = pair.Value?.Label
						})
						.ToArray();
					return new
					{
						label,
						mode = mode.ToString(),
						activeCells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						colonist = ZombieAreaManager.pawnsInDanger.ContainsKey(colonist),
						normal = ZombieAreaManager.pawnsInDanger.ContainsKey(normal),
						spitter = ZombieAreaManager.pawnsInDanger.ContainsKey(spitter),
						blob = ZombieAreaManager.pawnsInDanger.ContainsKey(blob),
						entries
					};
				}

				var ignore = Snapshot("ignore", AreaRiskMode.Ignore, colonist.Position, normal.Position, spitter.Position, blob.Position);
				var colonistInside = Snapshot("colonistInside", AreaRiskMode.ColonistInside, colonist.Position, normal.Position, spitter.Position, blob.Position);
				var colonistOutside = Snapshot("colonistOutside", AreaRiskMode.ColonistOutside, normal.Position, spitter.Position, blob.Position);
				var zombieInside = Snapshot("zombieInside", AreaRiskMode.ZombieInside, colonist.Position, normal.Position, spitter.Position, blob.Position);
				var zombieOutside = Snapshot("zombieOutside", AreaRiskMode.ZombieOutside, colonist.Position);

				bool Has(object snapshot, string field)
				{
					return (bool)snapshot.GetType().GetProperty(field).GetValue(snapshot);
				}

				var success = Has(ignore, "colonist") == false
					&& Has(ignore, "normal") == false
					&& Has(ignore, "spitter") == false
					&& Has(ignore, "blob") == false
					&& Has(colonistInside, "colonist")
					&& Has(colonistInside, "normal") == false
					&& Has(colonistInside, "spitter") == false
					&& Has(colonistInside, "blob") == false
					&& Has(colonistOutside, "colonist")
					&& Has(colonistOutside, "normal") == false
					&& Has(colonistOutside, "spitter") == false
					&& Has(colonistOutside, "blob") == false
					&& Has(zombieInside, "colonist") == false
					&& Has(zombieInside, "normal")
					&& Has(zombieInside, "spitter")
					&& Has(zombieInside, "blob")
					&& Has(zombieOutside, "colonist") == false
					&& Has(zombieOutside, "normal")
					&& Has(zombieOutside, "spitter")
					&& Has(zombieOutside, "blob");

				return new
				{
					success,
					area = area.Label,
					colonist = DescribePawn(colonist),
					normal = DescribeZombie(normal),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					snapshots = new[]
					{
						ignore,
						colonistInside,
						colonistOutside,
						zombieInside,
						zombieOutside
					}
				};
			}
			finally
			{
				ZombieSettings.Values.dangerousAreas.Clear();
				foreach (var pair in previousDangerousAreas)
					ZombieSettings.Values.dangerousAreas[pair.Key] = pair.Value;
				foreach (var thing in spawnedThings)
					if (thing != null && thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
				foreach (var area in createdAreas)
					if (area != null && map.areaManager.AllAreas.Contains(area))
						map.areaManager.Remove(area);
				ZombieAreaManager.pawnsInDanger.Clear();
				ZombieAreaManager.lastMap = null;
			}
		}

	}
}
