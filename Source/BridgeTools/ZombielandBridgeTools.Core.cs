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

		[Tool("zombieland/defensive_defaults_contract", Description = "Verify legacy defensive defaults do not throw when old/corrupt enum-style state is encountered.")]
		public static object DefensiveDefaultsContract()
		{
			var brainzStage = new BrainzThought().CurStageIndex;
			var invalidUnitTicks = new SettingsKeyFrame
			{
				amount = 2,
				unit = (SettingsKeyFrame.Unit)999
			}.Ticks;
			var expectedInvalidUnitTicks = 2 * GenDate.TicksPerDay;

			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var faction = Find.FactionManager.AllFactions
				.FirstOrDefault(candidate => candidate != null && candidate != Faction.OfPlayer && candidate.def != null);
			if (faction == null)
			{
				return new
				{
					success = false,
					error = "No non-player faction was available for the invalid attack-mode hostility check."
				};
			}

			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			Zombie zombie = null;
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no defensive-defaults test zombie."
					};
				}

				ZombieSettings.Values.attackMode = (AttackMode)999;
				ZombieSettings.Values.enemiesAttackZombies = true;
				var invalidAttackModeThreat = GenHostility.IsActiveThreatTo(zombie, faction, false, false);

				var success = brainzStage == 0
					&& invalidUnitTicks == expectedInvalidUnitTicks
					&& invalidAttackModeThreat == false;
				return new
				{
					success,
					brainzStage,
					invalidUnitTicks,
					expectedInvalidUnitTicks,
					faction = faction.def.defName,
					invalidAttackModeThreat,
					zombie = DescribeZombie(zombie)
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/brainz_thought_bubble_contract", Description = "Verify the BRRAINZ zombie thought bubble spawns the custom ZombieThought mote with the expected icon material.")]
		public static object BrainzThoughtBubbleContract()
		{
			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var realtimeMotes = RealTime.moteList?.allMotes ?? new List<Mote>();
			var existingThoughts = realtimeMotes
				.Where(thing => thing.def == CustomDefs.ZombieThought)
				.ToHashSet();

			Zombie zombie = null;
			MoteBubble[] spawnedThoughts = Array.Empty<MoteBubble>();
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no thought-bubble test zombie."
					};
				}

				ZombieStateHandler.CastBrainzThought(zombie);
				spawnedThoughts = realtimeMotes
					.OfType<MoteBubble>()
					.Where(mote => mote.def == CustomDefs.ZombieThought && existingThoughts.Contains(mote) == false)
					.ToArray();
				var thought = spawnedThoughts
					.OrderBy(mote => mote.Position.DistanceToSquared(zombie.Position))
					.FirstOrDefault();

				var thoughtPosition = thought?.Position ?? IntVec3.Invalid;
				var success = thought != null
					&& thought.Spawned
					&& thought.Map == map
					&& thoughtPosition == zombie.Position
					&& thought.iconMat == Constants.BRRAINZ;

				return new
				{
					success,
					zombie = DescribeZombie(zombie),
					thoughtCountBefore = existingThoughts.Count,
					spawnedThoughtCount = spawnedThoughts.Length,
					thoughtThingId = thought?.ThingID,
					thoughtSpawned = thought?.Spawned ?? false,
					thoughtPosition = thoughtPosition.IsValid ? ZombieRuntimeActions.DescribeCell(thoughtPosition) : null,
					expectedPosition = ZombieRuntimeActions.DescribeCell(zombie.Position),
					iconMaterial = thought?.iconMat?.name,
					expectedMaterial = Constants.BRRAINZ.name
				};
			}
			finally
			{
				foreach (var thought in spawnedThoughts)
					if (thought.Destroyed == false)
						thought.Destroy(DestroyMode.Vanish);
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_thought_bubble_materials_contract", Description = "Verify every custom zombie thought-bubble material spawns through RimWorld's realtime mote path.")]
		public static object ZombieThoughtBubbleMaterialsContract()
		{
			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var cases = new (string label, Material material, Action<Pawn> cast)[]
			{
				("BRRAINZ", Constants.BRRAINZ, ZombieStateHandler.CastBrainzThought),
				("Eating", Constants.EATING, pawn => Tools.CastThoughtBubble(pawn, Constants.EATING)),
				("Hacking", Constants.HACKING, pawn => Tools.CastThoughtBubble(pawn, Constants.HACKING)),
				("Raging", Constants.RAGING, pawn => Tools.CastThoughtBubble(pawn, Constants.RAGING))
			};

			var realtimeMotes = RealTime.moteList?.allMotes ?? new List<Mote>();
			Zombie zombie = null;
			var spawnedThoughts = new List<MoteBubble>();
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no thought-bubble material test zombie."
					};
				}

				var results = cases.Select(testCase =>
				{
					var before = realtimeMotes
						.Where(mote => mote.def == CustomDefs.ZombieThought)
						.ToHashSet();

					testCase.cast(zombie);
					var newThoughts = realtimeMotes
						.OfType<MoteBubble>()
						.Where(mote => mote.def == CustomDefs.ZombieThought && before.Contains(mote) == false)
						.ToArray();
					spawnedThoughts.AddRange(newThoughts);

					var thought = newThoughts
						.OrderBy(mote => mote.Position.DistanceToSquared(zombie.Position))
						.FirstOrDefault();
					var thoughtPosition = thought?.Position ?? IntVec3.Invalid;
					var materialMatches = thought?.iconMat == testCase.material;
					var positionMatches = thoughtPosition == zombie.Position;
					var ok = thought != null
						&& thought.Spawned
						&& thought.Map == map
						&& positionMatches
						&& materialMatches;

					return new
					{
						label = testCase.label,
						success = ok,
						expectedMaterial = testCase.material.name,
						iconMaterial = thought?.iconMat?.name,
						materialMatches,
						spawnedThoughtCount = newThoughts.Length,
						thoughtThingId = thought?.ThingID,
						thoughtSpawned = thought?.Spawned ?? false,
						thoughtPosition = thoughtPosition.IsValid ? ZombieRuntimeActions.DescribeCell(thoughtPosition) : null,
						expectedPosition = ZombieRuntimeActions.DescribeCell(zombie.Position),
						positionMatches
					};
				}).ToArray();

				return new
				{
					success = results.All(result => result.success),
					zombie = DescribeZombie(zombie),
					results
				};
			}
			finally
			{
				foreach (var thought in spawnedThoughts)
					if (thought.Destroyed == false)
						thought.Destroy(DestroyMode.Vanish);
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
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

		[Tool("zombieland/spawn_spitter_visual_fixture", Description = "Spawn one idle zombie spitter without launching a ZombieBall so its custom three-layer rendering can be inspected.")]
		public static object SpawnSpitterVisualFixture(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Whether to use the aggressive tinted spitter materials.", Required = false, DefaultValue = false)] bool aggressive = false)
		{
			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var existing = CurrentZombies(map).OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, cell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
					spawnCell = ZombieRuntimeActions.DescribeCell(cell),
					error = "ZombieSpitter.Spawn did not produce a spawned spitter."
				};
			}

			spitter.aggressive = aggressive;
			spitter.state = SpitterState.Idle;
			spitter.tickCounter = 0;
			spitter.remainingZombies = 0;
			spitter.spitInterval = 0;
			spitter.Rotation = Rot4.South;

			return new
			{
				success = spitter.Spawned,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				aggressive,
				spitter = DescribeZombie(spitter)
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

	}
}
