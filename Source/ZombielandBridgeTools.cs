using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed class ZombielandBridgeTools
	{
		struct LineupEntry
		{
			public readonly ZombieType type;
			public readonly int dx;
			public readonly int dz;

			public LineupEntry(ZombieType type, int dx, int dz)
			{
				this.type = type;
				this.dx = dx;
				this.dz = dz;
			}
		}

		static readonly LineupEntry[] referenceLineup =
		{
			new(ZombieType.Electrifier, 0, 0),
			new(ZombieType.SuicideBomber, 2, 0),
			new(ZombieType.Healer, 4, 0),
			new(ZombieType.DarkSlimer, 6, 0),
			new(ZombieType.Albino, 2, 2),
			new(ZombieType.TankyOperator, 0, 4),
			new(ZombieType.ToxicSplasher, 4, 4),
			new(ZombieType.Miner, 6, 4)
		};

		static Map CurrentMap => Find.CurrentMap;

		static object DescribeZombie(Pawn pawn)
		{
			var zombie = pawn as Zombie;
			var blob = pawn as ZombieBlob;
			var spitter = pawn as ZombieSpitter;

			return new
			{
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				thingId = pawn?.ThingID,
				defName = pawn?.def?.defName,
				kindDef = pawn?.kindDef?.defName,
				label = pawn?.LabelCap,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				faction = pawn?.Faction?.Name,
				position = pawn == null ? null : ZombieRuntimeActions.DescribeCell(pawn.Position),
				state = zombie?.state.ToString() ?? spitter?.state.ToString(),
				raging = zombie?.raging ?? 0,
				kind = DescribeZombieKind(zombie, blob, spitter),
				isSuicideBomber = zombie?.IsSuicideBomber ?? false,
				isToxicSplasher = zombie?.isToxicSplasher ?? false,
				isTanky = zombie?.IsTanky ?? false,
				isMiner = zombie?.isMiner ?? false,
				isElectrifier = zombie?.isElectrifier ?? false,
				isAlbino = zombie?.isAlbino ?? false,
				isDarkSlimer = zombie?.isDarkSlimer ?? false,
				isHealer = zombie?.isHealer ?? false,
				spitterAggressive = spitter?.aggressive,
				spitterWaves = spitter?.waves,
				spitterRemainingZombies = spitter?.remainingZombies,
				spitterSpitInterval = spitter?.spitInterval,
				spitterTickCounter = spitter?.tickCounter,
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = pawn?.CurJob?.GetReport(pawn)
			};
		}

		static object DescribePawn(Pawn pawn)
		{
			return new
			{
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				thingId = pawn?.ThingID,
				defName = pawn?.def?.defName,
				kindDef = pawn?.kindDef?.defName,
				label = pawn?.LabelCap,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				faction = pawn?.Faction?.Name,
				position = pawn == null ? null : ZombieRuntimeActions.DescribeCell(pawn.Position),
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = pawn?.CurJob?.GetReport(pawn),
				stunned = pawn?.stances?.stunner?.Stunned
			};
		}

		static object DescribeTankyArmor(Zombie zombie)
		{
			return zombie == null ? null : new
			{
				shield = zombie.hasTankyShield,
				helmet = zombie.hasTankyHelmet,
				suit = zombie.hasTankySuit,
				isTanky = zombie.IsTanky
			};
		}

		static object DescribeCorpse(Corpse corpse)
		{
			var compRottable = corpse?.TryGetComp<CompRottable>();
			var innerPawn = corpse?.InnerPawn;
			return new
			{
				corpseId = ZombieRuntimeActions.StableThingId(corpse),
				thingId = corpse?.ThingID,
				label = corpse?.LabelCap,
				spawned = corpse?.Spawned ?? false,
				destroyed = corpse?.Destroyed ?? true,
				position = corpse == null || corpse.Spawned == false ? null : ZombieRuntimeActions.DescribeCell(corpse.Position),
				rotStage = corpse == null || corpse.Destroyed ? null : corpse.GetRotStage().ToString(),
				rotProgress = compRottable?.RotProgress ?? 0f,
				innerPawnId = ZombieRuntimeActions.StableThingId(innerPawn),
				innerPawnThingId = innerPawn?.ThingID,
				innerPawnLabel = innerPawn?.LabelCap
			};
		}

		static string DescribeZombieKind(Zombie zombie, ZombieBlob blob, ZombieSpitter spitter)
		{
			if (blob != null)
				return "Blob";
			if (spitter != null)
				return "Spitter";
			if (zombie == null)
				return null;
			if (zombie.IsSuicideBomber)
				return "SuicideBomber";
			if (zombie.isToxicSplasher)
				return "ToxicSplasher";
			if (zombie.IsTanky)
				return "TankyOperator";
			if (zombie.isMiner)
				return "Miner";
			if (zombie.isElectrifier)
				return "Electrifier";
			if (zombie.isAlbino)
				return "Albino";
			if (zombie.isDarkSlimer)
				return "DarkSlimer";
			if (zombie.isHealer)
				return "Healer";
			return "Normal";
		}

		static bool TryParseZombieType(string value, out ZombieType zombieType, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(value))
			{
				zombieType = ZombieType.Normal;
				return true;
			}

			if (Enum.TryParse(value.Trim(), true, out zombieType))
				return true;

			var names = string.Join(", ", Enum.GetNames(typeof(ZombieType)));
			error = $"Unknown zombie type '{value}'. Valid values: {names}.";
			return false;
		}

		static bool TryFindSpawnCell(int x, int z, out Map map, out IntVec3 cell, out object error)
		{
			map = CurrentMap;
			cell = IntVec3.Invalid;
			error = null;

			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (root.InBounds(map) == false)
			{
				error = new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, 12f, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No standable unfogged cell was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static Pawn[] CurrentZombies(Map map)
		{
			if (map?.mapPawns?.AllPawnsSpawned == null)
				return Array.Empty<Pawn>();

			return map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn is Zombie || pawn is ZombieBlob || pawn is ZombieSpitter)
				.Cast<Pawn>()
				.ToArray();
		}

		static bool TryFindZombie(Map map, string target, out Pawn pawn, out string error)
		{
			pawn = null;
			error = null;
			if (map == null)
			{
				error = "No current map is loaded.";
				return false;
			}
			if (string.IsNullOrWhiteSpace(target))
			{
				error = "A zombie id, ThingID, label, or short name is required.";
				return false;
			}

			var query = target.Trim();
			pawn = CurrentZombies(map).FirstOrDefault(candidate =>
				string.Equals(ZombieRuntimeActions.StableThingId(candidate), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.ThingID, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.LabelShortCap, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringShort, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringFull, query, StringComparison.OrdinalIgnoreCase));
			if (pawn != null)
				return true;

			error = $"No spawned Zombieland pawn matched '{target}'.";
			return false;
		}

		static bool TryFindOrSpawnSpitter(Map map, string target, out ZombieSpitter spitter, out bool spawnedSpitter, out object error)
		{
			spitter = null;
			spawnedSpitter = false;
			error = null;

			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out error) == false)
					return false;

				var existing = CurrentZombies(map).OfType<ZombieSpitter>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSpitter.Spawn(map, cell);
				spitter = CurrentZombies(map).OfType<ZombieSpitter>()
					.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
				spawnedSpitter = spitter != null;
			}
			else if (TryFindZombie(map, target, out var pawn, out var findError) == false)
			{
				error = new
				{
					success = false,
					error = findError
				};
				return false;
			}
			else
			{
				spitter = pawn as ZombieSpitter;
			}

			if (spitter != null)
				return true;

			error = new
			{
				success = false,
				error = "Target is not a zombie spitter."
			};
			return false;
		}

		static ZombieBall ForceSpitterShot(Map map, ZombieSpitter spitter, int seed)
		{
			if (spitter.CurJobDef != CustomDefs.Spitter)
				spitter.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Spitter));

			var existing = map.listerThings.AllThings
				.Where(thing => thing.def == CustomDefs.ZombieBall)
				.Select(thing => thing.ThingID)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			Rand.PushState(seed);
			try
			{
				spitter.aggressive = true;
				spitter.waves = Math.Max(1, spitter.waves);
				spitter.remainingZombies = 1;
				spitter.spitInterval = 4;
				spitter.tickCounter = spitter.spitInterval;
				spitter.state = SpitterState.Spitting;
				AdvanceGameTicks(1);
			}
			finally
			{
				Rand.PopState();
			}

			return map.listerThings.AllThings
				.OfType<ZombieBall>()
				.FirstOrDefault(projectile => existing.Contains(projectile.ThingID) == false)
				?? map.listerThings.AllThings.OfType<ZombieBall>().FirstOrDefault();
		}

		static int CountThingsNear(Map map, IntVec3 center, ThingDef thingDef, float radius)
		{
			if (map == null || center.IsValid == false || thingDef == null)
				return 0;

			return GenRadial.RadialCellsAround(center, radius, true)
				.Where(cell => cell.InBounds(map))
				.SelectMany(cell => cell.GetThingList(map))
				.Count(thing => thing.def == thingDef);
		}

		static Dictionary<IntVec3, long> SnapshotPheromones(Map map, IntVec3 center, float radius)
		{
			var grid = map.GetGrid();
			return GenRadial.RadialCellsAround(center, radius, true)
				.Where(cell => cell.InBounds(map))
				.ToDictionary(cell => cell, cell => grid.GetTimestamp(cell));
		}

		static void ClearPheromones(Map map, IntVec3 center, float radius)
		{
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
				if (cell.InBounds(map))
					grid.SetTimestamp(cell, 0);
		}

		static object DescribePheromoneChange(Map map, Dictionary<IntVec3, long> before, out int changedCount)
		{
			var grid = map.GetGrid();
			var changed = before
				.Select(pair => new
				{
					cell = pair.Key,
					before = pair.Value,
					after = grid.GetTimestamp(pair.Key)
				})
				.Where(item => item.after > item.before)
				.ToArray();

			changedCount = changed.Length;
			return new
			{
				changedCount = changed.Length,
				maxDelta = changed.Length == 0 ? 0 : changed.Max(item => item.after - item.before),
				changedCells = changed
					.OrderByDescending(item => item.after - item.before)
					.Take(12)
					.Select(item => new
					{
						cell = ZombieRuntimeActions.DescribeCell(item.cell),
						item.before,
						item.after,
						delta = item.after - item.before
					})
					.ToArray()
			};
		}

		static void AdvanceGameTicks(int ticks)
		{
			var tickManager = Find.TickManager;
			ZombieTicker.managers = Find.Maps.Select(map => map.GetComponent<TickManager>()).OfType<TickManager>().ToArray();
			for (var i = 0; i < ticks; i++)
				tickManager.DoSingleTick();
		}

		static bool TryFindAdjacentMoveCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.AdjacentCells)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (pawn.HasValidDestination(candidate) == false)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindAdjacentClearCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.AdjacentCells)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindAdjacentBuildingCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.CardinalDirections)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindClearSpawnCell(Map map, IntVec3 root, float radius, out IntVec3 cell, out object error)
		{
			cell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			if (root.InBounds(map) == false)
			{
				error = new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear standable unfogged cell was found near ({root.x}, {root.z})."
			};
			return false;
		}

		[Tool("zombieland/get_status", Description = "Read a compact live Zombieland status summary for the current RimWorld session.")]
		public static object GetStatus()
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			var zombies = CurrentZombies(map);
			var gameTickManager = Current.Game?.tickManager;

			return new
			{
				success = true,
				hasCurrentMap = map != null,
				mapId = map?.uniqueID ?? -1,
				mapSize = map == null ? null : new
				{
					x = map.Size.x,
					z = map.Size.z
				},
				defsLoaded = new
				{
					zombieFaction = ZombieDefOf.Zombies != null,
					zombieKind = ZombieDefOf.Zombie != null,
					zombieRace = ZombieDefOf.Zombie?.race != null,
					zombieBlobKind = ZombieDefOf.ZombieBlob != null,
					zombieSpitterKind = ZombieDefOf.ZombieSpitter != null
				},
				tickManager = tickManager == null ? null : new
				{
					initialized = tickManager.isInitialized,
					cachedZombieCount = tickManager.allZombiesCached?.Count ?? 0,
					liveZombieCount = tickManager.ZombieCount(),
					currentColonyPoints = tickManager.currentColonyPoints,
					spawningInProgress = ZombieGenerator.ZombiesSpawning
				},
				spawnedZombieCount = zombies.Length,
				ordinaryZombies = zombies.OfType<Zombie>().Count(),
				blobs = zombies.OfType<ZombieBlob>().Count(),
				spitters = zombies.OfType<ZombieSpitter>().Count(),
				timeSpeed = gameTickManager == null ? null : gameTickManager.CurTimeSpeed.ToString()
			};
		}

		[Tool("zombieland/list_zombies", Description = "List spawned Zombieland pawns on the current map with stable ids and compact state.")]
		public static object ListZombies([ToolParameter(Description = "Maximum number of zombies to return.", Required = false, DefaultValue = 100)] int limit = 100)
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

			var cappedLimit = Math.Max(1, Math.Min(limit, 500));
			var zombies = CurrentZombies(map)
				.OrderBy(pawn => pawn.ThingID, StringComparer.Ordinal)
				.Take(cappedLimit)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = true,
				count = zombies.Length,
				limit = cappedLimit,
				zombies
			};
		}

		[Tool("zombieland/spawn_zombie", Description = "Spawn one Zombieland zombie near a map cell for runtime smoke tests.")]
		public static object SpawnZombie(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Zombie type name, for example Normal, Random, SuicideBomber, ToxicSplasher, TankyOperator, Miner, Electrifier, Albino, DarkSlimer, or Healer.", Required = false, DefaultValue = "Normal")] string type = "Normal",
			[ToolParameter(Description = "When true, skip the underground dig-out state and spawn the zombie standing.", Required = false, DefaultValue = true)] bool appearDirectly = true)
		{
			if (TryParseZombieType(type, out var zombieType, out var parseError) == false)
			{
				return new
				{
					success = false,
					error = parseError
				};
			}

			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, zombieType, appearDirectly);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			return new
			{
				success = zombie.Spawned,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				appearDirectly,
				zombie = DescribeZombie(zombie),
				tickManagerCached = tickManager?.allZombiesCached?.Contains(zombie) ?? false
			};
		}

		[Tool("zombieland/spawn_reference_lineup", Description = "Clear and spawn the eight common special zombie types in a compact visual comparison pattern.")]
		public static object SpawnReferenceLineup(
			[ToolParameter(Description = "Origin x coordinate for the top-left zombie. Use -1 with z -1 to start near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Origin z coordinate for the top-left zombie. Use -1 with x -1 to start near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Destroy existing Zombieland pawns on the current map before spawning the lineup.", Required = false, DefaultValue = true)] bool clearExisting = true,
			[ToolParameter(Description = "When true, skip the underground dig-out state and spawn each zombie standing.", Required = false, DefaultValue = true)] bool appearDirectly = true)
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

			var origin = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (origin.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({origin.x}, {origin.z}) is outside the current map."
				};
			}

			var destroyed = clearExisting ? ZombieRuntimeActions.DestroyZombies(map) : 0;
			var success = true;
			var results = referenceLineup.Select<LineupEntry, object>(entry =>
			{
				var requestedCell = new IntVec3(origin.x + entry.dx, 0, origin.z + entry.dz);
				if (TryFindSpawnCell(requestedCell.x, requestedCell.z, out var spawnMap, out var cell, out var error) == false)
				{
					success = false;
					return new
					{
						success = false,
						type = entry.type.ToString(),
						requestedCell = ZombieRuntimeActions.DescribeCell(requestedCell),
						spawnCell = (object)null,
						error,
						zombie = (object)null
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(cell, spawnMap, entry.type, appearDirectly);
				success &= zombie?.Spawned ?? false;
				return new
				{
					success = zombie?.Spawned ?? false,
					type = entry.type.ToString(),
					requestedCell = ZombieRuntimeActions.DescribeCell(requestedCell),
					spawnCell = ZombieRuntimeActions.DescribeCell(cell),
					error = zombie == null ? "ZombieGenerator.SpawnZombie returned no zombie." : null,
					zombie = DescribeZombie(zombie)
				};
			}).ToArray();

			return new
			{
				success,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destroyed,
				appearDirectly,
				count = results.Length,
				results
			};
		}

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

		[Tool("zombieland/convert_infected_corpse_to_zombie", Description = "Create an infected rotting corpse from a spawned pawn, verify Corpse.RotStageChanged queued it, then run that queued conversion.")]
		public static object ConvertInfectedCorpseToZombie(
			[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
			[ToolParameter(Description = "Bite state to apply before death: harmful, final, or harmless.", Required = false, DefaultValue = "final")] string stage = "final")
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

			if (ZombieRuntimeActions.AddZombieBite(pawn, stage, out var bite, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					error
				};
			}

			if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					biteLabel = bite.LabelCap,
					error
				};
			}

			var corpseBeforeRot = DescribeCorpse(corpse);
			if (ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					biteLabel = bite.LabelCap,
					corpse = corpseBeforeRot,
					error
				};
			}

			var corpseAfterRot = DescribeCorpse(corpse);
			var convertedQueuedCorpse = ZombieRuntimeActions.RunQueuedConversion(map, corpse, out var queueCountBeforeRun, out var queueCountAfterRun, out error);
			var after = CurrentZombies(map);
			var newZombies = after
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = convertedQueuedCorpse && newZombies.Length > 0,
				targetId,
				targetThingId,
				targetLabel,
				stage = stage ?? "final",
				biteLabel = bite.LabelCap,
				rotStageBefore = rotStageBefore.ToString(),
				rotStageAfter = rotStageAfter.ToString(),
				corpseBeforeRot,
				corpseAfterRot,
				queuedConversionFound = convertedQueuedCorpse,
				queueCountBeforeRun,
				queueCountAfterRun,
				error,
				beforeCount = before.Length,
				afterCount = after.Length,
				newZombieCount = newZombies.Length,
				newZombies
			};
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
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
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
			var bulletDamageTotals = new float[cappedAttempts];
			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < cappedAttempts; i++)
				{
					var dinfo = new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
					bulletDamageTotals[i] = albino.TakeDamage(dinfo).totalDamageDealt;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var explosiveInfo = new DamageInfo(DamageDefOf.Bomb, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var explosiveDamage = albino.TakeDamage(explosiveInfo).totalDamageDealt;
			var bulletHits = bulletDamageTotals.Count(total => total > 0f);
			var bulletBlocked = bulletDamageTotals.Count(total => total <= 0f);

			return new
			{
				success = bulletHits > 0 && bulletBlocked > 0 && explosiveDamage > 0f,
				spawnedAlbino,
				seed,
				bulletAttempts = cappedAttempts,
				bulletHits,
				bulletBlocked,
				bulletDamageTotal = bulletDamageTotals.Sum(),
				bulletDamageTotals,
				explosiveDamage,
				before,
				after = DescribeZombie(albino)
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

	}
}
