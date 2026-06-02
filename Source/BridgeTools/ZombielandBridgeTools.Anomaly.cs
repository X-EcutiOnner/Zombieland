using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class AnomalyMatrixCase
		{
			public string id;
			public string kindDef;
			public string faction;
			public string activityState;
			public string dormancyState;
		}

		sealed class AnomalyMatrixRuntimeRow
		{
			public AnomalyMatrixCase testCase;
			public Pawn pawn;
			public Zombie zombie;
		}

		sealed class AnomalyEngagementState
		{
			public string testCase;
			public string mode;
			public object pawn;
			public object zombie;
			public object activity;
			public object dormancy;
			public object vanillaThreat;
			public bool attracts;
			public bool attackable;
			public object reverseHostility;
			public object grid;
			public long targetTimestamp;
			public long zombieTimestamp;
			public bool anyAdjacentTimestamp;
			public bool anyAdjacentZombieCount;
			public object stumbleDriver;
			public bool zombieTracking;
			public bool zombiePatherMoving;
		}

		static readonly AnomalyMatrixCase[] anomalyMatrixCases =
		{
			// Nociosphere skip/onslaught AI is not safe in this static zombie-pair matrix.
			new() { id = "FleshmassNucleus-active", kindDef = "FleshmassNucleus", faction = "entities", activityState = "active" },
			new() { id = "FleshmassNucleus-passive", kindDef = "FleshmassNucleus", faction = "entities", activityState = "passive" },
			new() { id = "Metalhorror-awake", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake" },
			new() { id = "Metalhorror-dormant", kindDef = "Metalhorror", faction = "entities", dormancyState = "dormant" },
			new() { id = "Revenant", kindDef = "Revenant", faction = "entities" },
			new() { id = "Sightstealer", kindDef = "Sightstealer", faction = "entities" },
			new() { id = "Noctol", kindDef = "Noctol", faction = "entities" },
			new() { id = "Gorehulk", kindDef = "Gorehulk", faction = "entities" },
			new() { id = "Devourer", kindDef = "Devourer", faction = "entities" },
			new() { id = "Chimera", kindDef = "Chimera", faction = "entities" },
			new() { id = "Bulbfreak", kindDef = "Bulbfreak", faction = "entities" },
			new() { id = "Fingerspike-awake", kindDef = "Fingerspike", faction = "entities", dormancyState = "awake" },
			new() { id = "Fingerspike-dormant", kindDef = "Fingerspike", faction = "entities", dormancyState = "dormant" },
			new() { id = "Toughspike", kindDef = "Toughspike", faction = "entities" },
			new() { id = "Trispike", kindDef = "Trispike", faction = "entities" },
			new() { id = "Dreadmeld", kindDef = "Dreadmeld", faction = "entities" },
			new() { id = "ShamblerSwarmer-awake", kindDef = "ShamblerSwarmer", faction = "entities", dormancyState = "awake" },
			new() { id = "ShamblerSoldier-awake", kindDef = "ShamblerSoldier", faction = "entities", dormancyState = "awake" },
			new() { id = "ShamblerGorehulk-awake", kindDef = "ShamblerGorehulk", faction = "entities", dormancyState = "awake" },
			new() { id = "Ghoul-player", kindDef = "Ghoul", faction = "player" },
			new() { id = "Ghoul-entities", kindDef = "Ghoul", faction = "entities" },
			new() { id = "Horaxian_Underthrall", kindDef = "Horaxian_Underthrall", faction = "horax" },
			new() { id = "Horaxian_Gunner", kindDef = "Horaxian_Gunner", faction = "horax" },
			new() { id = "Horaxian_Highthrall", kindDef = "Horaxian_Highthrall", faction = "horax" },
		};

		[Tool("zombieland/anomaly_zombie_matrix", Description = "Stage Anomaly pawn-kind/state rows and report Zombieland zombie attraction, attack-mode, hostility, infection, and short tick outcomes.")]
		public static object AnomalyZombieMatrix(
			[ToolParameter(Description = "Comma-separated case ids or PawnKindDef names. Empty runs the built-in first Anomaly matrix.", Required = false, DefaultValue = "")] string cases = "",
			[ToolParameter(Description = "Ticks to step after staging all rows. Use 0 for a pure static matrix.", Required = false, DefaultValue = 60)] int ticks = 60,
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var selectedCases = SelectAnomalyMatrixCases(cases);
			var missingKinds = selectedCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (missingKinds.Length > 0 || zombieFaction == null)
			{
				return new
				{
					success = false,
					missingKinds,
					zombieFactionPresent = zombieFaction != null,
					error = missingKinds.Length > 0
						? "One or more Anomaly PawnKindDefs were not loaded."
						: "Zombie faction was not loaded."
				};
			}

			var settings = ZombieSettings.Values;
			var originalAttackMode = settings.attackMode;
			var originalEnemiesAttackZombies = settings.enemiesAttackZombies;
			var originalAnimalsAttackZombies = settings.animalsAttackZombies;
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var runtimeRows = new List<AnomalyMatrixRuntimeRow>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			ticks = Mathf.Clamp(ticks, 0, 600);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				for (var i = 0; i < selectedCases.Length; i++)
				{
					var testCase = selectedCases[i];
					var rowRoot = root + new IntVec3((i % 4) * 10 - 15, 0, (i / 4) * 7 - 18);
					if (rowRoot.InBounds(map) == false)
						rowRoot = root;
					if (TryFindClearSpawnCell(map, rowRoot, 18f, out var targetCell, out var targetCellError) == false)
						return targetCellError;
					if (TryFindClearSpawnCell(map, targetCell + new IntVec3(2, 0, 0), 8f, out var zombieCell, out var zombieCellError) == false)
						return zombieCellError;

					var kindDef = DefDatabase<PawnKindDef>.GetNamed(testCase.kindDef);
					var faction = ResolveAnomalyMatrixFaction(testCase.faction);
					var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
					GenSpawn.Spawn(pawn, targetCell, map, Rot4.South);
					DisablePawnWork(pawn);
					ApplyAnomalyState(pawn, testCase.activityState, testCase.dormancyState);
					var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, testCase.id, error = "ZombieGenerator.SpawnZombie returned no normal zombie." };
					spawned.Add(pawn);
					spawned.Add(zombie);
					runtimeRows.Add(new AnomalyMatrixRuntimeRow { testCase = testCase, pawn = pawn, zombie = zombie });

					rows.Add(DescribeAnomalyMatrixRow(testCase, pawn, zombie, zombieFaction, settings));
				}

				if (ticks > 0)
					AdvanceGameTicks(ticks);

				var afterTickRows = runtimeRows
					.Select(row =>
					{
						var pawn = row.pawn;
						return pawn == null ? null : new
						{
							row.testCase.id,
							pawn = DescribePawn(pawn),
							zombie = DescribeZombie(row.zombie),
							activity = DescribeAnomalyActivity(pawn),
							dormancy = DescribeAnomalyDormancy(pawn),
							vanillaThreat = DescribeAnomalyVanillaThreat(pawn, row.zombie),
							infectionState = pawn.InfectionState().ToString(),
							attractsCurrentSettings = Customization.DoesAttractsZombies(pawn)
						};
					})
					.Where(row => row != null)
					.ToArray();

				return new
				{
					success = true,
					caseCount = selectedCases.Length,
					ticks,
					matrix = rows.ToArray(),
					afterTicks = afterTickRows,
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				settings.attackMode = originalAttackMode;
				settings.enemiesAttackZombies = originalEnemiesAttackZombies;
				settings.animalsAttackZombies = originalAnimalsAttackZombies;
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/anomaly_policy_edges", Description = "Verify narrow Anomaly zombie policy edges that do not belong in the broad pawn matrix: active Nociosphere and FleshmassHeart.")]
		public static object AnomalyPolicyEdges(
			[ToolParameter(Description = "Optional short tick window after staging. Default 0 keeps the probe static and avoids Nociosphere onslaught AI.", Required = false, DefaultValue = 0)] int ticks = 0,
			[ToolParameter(Description = "Destroy staged Anomaly things and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var nociosphereKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Nociosphere");
			var heartDef = DefDatabase<ThingDef>.GetNamedSilentFail("FleshmassHeart");
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var entitiesFaction = ResolveAnomalyMatrixFaction("entities");
			if (nociosphereKind == null || heartDef == null || zombieFaction == null)
			{
				return new
				{
					success = false,
					nociosphereKindPresent = nociosphereKind != null,
					fleshmassHeartPresent = heartDef != null,
					zombieFactionPresent = zombieFaction != null,
					error = "One or more required Anomaly/Zombieland defs were not loaded."
				};
			}

			var settings = ZombieSettings.Values;
			var spawned = new List<Thing>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var clampedTicks = Mathf.Clamp(ticks, 0, 30);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);

				if (TryFindClearSpawnCell(map, root + new IntVec3(-12, 0, -8), 18f, out var nociosphereCell, out var nociosphereCellError) == false)
					return nociosphereCellError;
				if (TryFindClearSpawnCell(map, nociosphereCell + new IntVec3(2, 0, 0), 8f, out var nociosphereZombieCell, out var nociosphereZombieCellError) == false)
					return nociosphereZombieCellError;
				if (TryFindClearSpawnCell(map, root + new IntVec3(12, 0, -8), 18f, out var heartCell, out var heartCellError) == false)
					return heartCellError;
				if (TryFindClearSpawnCell(map, heartCell + new IntVec3(2, 0, 0), 8f, out var heartZombieCell, out var heartZombieCellError) == false)
					return heartZombieCellError;

				var nociosphere = PawnGenerator.GeneratePawn(nociosphereKind, entitiesFaction);
				GenSpawn.Spawn(nociosphere, nociosphereCell, map, Rot4.South);
				DisablePawnWork(nociosphere);
				var nociosphereZombie = ZombieRuntimeActions.SpawnZombie(nociosphereZombieCell, map, ZombieType.Normal, true);
				if (nociosphereZombie == null)
					return new { success = false, error = "Could not spawn Nociosphere policy zombie." };
				spawned.Add(nociosphere);
				spawned.Add(nociosphereZombie);
				ApplyAnomalyState(nociosphere, "active", null);

				var heartThing = ThingMaker.MakeThing(heartDef);
				if (heartThing is not Building_FleshmassHeart heart)
					return new { success = false, defName = heartThing?.def?.defName, type = heartThing?.GetType().FullName, error = "FleshmassHeart did not create the expected building type." };
				heart.SetFaction(entitiesFaction);
				GenSpawn.Spawn(heart, heartCell, map, Rot4.South);
				var heartZombie = ZombieRuntimeActions.SpawnZombie(heartZombieCell, map, ZombieType.Normal, true);
				if (heartZombie == null)
					return new { success = false, error = "Could not spawn FleshmassHeart policy zombie." };
				spawned.Add(heart);
				spawned.Add(heartZombie);

				var beforeTicks = new
				{
					nociosphere = DescribeNociospherePolicy(nociosphere, nociosphereZombie, zombieFaction, settings),
					fleshmassHeart = DescribeThingPolicy("FleshmassHeart", heart, heartZombie, zombieFaction, settings)
				};

				if (clampedTicks > 0)
				{
					AdvanceGameTicks(clampedTicks);
					settings = ZombieSettings.Values;
				}

				var afterTicks = new
				{
					nociosphere = DescribeNociospherePolicy(nociosphere, nociosphereZombie, zombieFaction, settings),
					fleshmassHeart = DescribeThingPolicy("FleshmassHeart", heart, heartZombie, zombieFaction, settings)
				};

				return new
				{
					success = true,
					ticks = clampedTicks,
					beforeTicks,
					afterTicks,
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/anomaly_engagement_matrix", Description = "Stage representative Anomaly pawn rows under each zombie attack mode and verify real pheromone/tracking engagement signals.")]
		public static object AnomalyEngagementMatrix(
			[ToolParameter(Description = "Comma-separated case ids or PawnKindDef names. Empty runs a small representative engagement set.", Required = false, DefaultValue = "")] string cases = "",
			[ToolParameter(Description = "Ticks to step after each row. Clamped to 1..180.", Required = false, DefaultValue = 90)] int ticks = 90,
			[ToolParameter(Description = "Whether enemy/entity pawns should be allowed to attack zombies during reverse-hostility probes.", Required = false, DefaultValue = true)] bool enemiesAttackZombies = true,
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true,
			[ToolParameter(Description = "Include full before/after row details. Default false keeps the runtime evidence compact.", Required = false, DefaultValue = false)] bool includeDetails = false)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var selectedCases = SelectAnomalyEngagementCases(cases);
			var missingKinds = selectedCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (missingKinds.Length > 0 || zombieFaction == null)
			{
				return new
				{
					success = false,
					missingKinds,
					zombieFactionPresent = zombieFaction != null,
					error = missingKinds.Length > 0
						? "One or more Anomaly PawnKindDefs were not loaded."
						: "Zombie faction was not loaded."
				};
			}

			var clampedTicks = Mathf.Clamp(ticks, 1, 180);
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var summaries = new List<object>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var settingsSnapshot = SnapshotZombieSettings();

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				var modes = new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything };
				for (var caseIndex = 0; caseIndex < selectedCases.Length; caseIndex++)
				{
					var testCase = selectedCases[caseIndex];
					for (var modeIndex = 0; modeIndex < modes.Length; modeIndex++)
					{
						var mode = modes[modeIndex];
						var rowRoot = root + new IntVec3((caseIndex % 3) * 14 - 14, 0, (caseIndex / 3) * 18 + modeIndex * 5 - 22);
						if (rowRoot.InBounds(map) == false)
							rowRoot = root;
						if (TryFindClearSpawnCell(map, rowRoot, 22f, out var targetCell, out var targetCellError) == false)
							return targetCellError;
						if (TryFindClearSpawnCell(map, targetCell + new IntVec3(-4, 0, 0), 12f, out var zombieCell, out var zombieCellError) == false)
							return zombieCellError;

						ApplyZombieSettingsOverride(values =>
						{
							values.attackMode = mode;
							values.enemiesAttackZombies = enemiesAttackZombies;
							values.animalsAttackZombies = enemiesAttackZombies;
						});

						var kindDef = DefDatabase<PawnKindDef>.GetNamed(testCase.kindDef);
						var faction = ResolveAnomalyMatrixFaction(testCase.faction);
						var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
						GenSpawn.Spawn(pawn, targetCell, map, Rot4.South);
						DisablePawnWork(pawn);
						ApplyAnomalyState(pawn, testCase.activityState, testCase.dormancyState);
						var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
						if (zombie == null)
							return new { success = false, testCase.id, mode = mode.ToString(), error = "ZombieGenerator.SpawnZombie returned no normal zombie." };
						spawned.Add(pawn);
						spawned.Add(zombie);
						PrepareWallPushZombie(map, zombie, zombieCell);

						ClearAnomalyEngagementGrid(map, pawn.Position, zombie.Position);
						var before = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						var moved = TryFindAdjacentMoveCell(pawn, out var moveCell);
						if (moved)
						{
							pawn.Position = moveCell;
							pawn.Notify_Teleported(false, false);
						}
						var afterMove = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						AdvanceGameTicks(clampedTicks);
						var afterTicks = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						var expectedEngagement = Customization.DoesAttractsZombies(pawn) && Tools.Attackable(zombie, mode, pawn);
						var classification = ClassifyAnomalyEngagement(expectedEngagement, moved, before, afterMove, afterTicks);
						summaries.Add(new
						{
							testCase.id,
							testCase.kindDef,
							mode = mode.ToString(),
							expectedEngagement,
							classification,
							attractsAfterMove = afterMove.attracts,
							attackableAfterMove = afterMove.attackable,
							seededAfterMove = afterMove.targetTimestamp > 0 || afterMove.zombieTimestamp > 0 || afterMove.anyAdjacentTimestamp,
							zombieTrackingAfterTicks = afterTicks.zombieTracking,
							zombieMovingAfterTicks = afterTicks.zombiePatherMoving,
							targetTimestampAfterMove = afterMove.targetTimestamp,
							zombieTimestampAfterTicks = afterTicks.zombieTimestamp
						});
						if (includeDetails)
							rows.Add(new
						{
							testCase.id,
							testCase.kindDef,
							mode = mode.ToString(),
							ticks = clampedTicks,
							enemiesAttackZombies,
							expectedEngagement,
							moved,
							moveCell = moved ? ZombieRuntimeActions.DescribeCell(moveCell) : null,
							before,
							afterMove,
							afterTicks,
							classification
						});

						if (cleanup)
						{
							CleanupAnomalyThing(zombie);
							CleanupAnomalyThing(pawn);
							ClearAnomalyEngagementGrid(map);
							_ = spawned.Remove(zombie);
							_ = spawned.Remove(pawn);
						}
					}
				}

				return new
				{
					success = true,
					caseCount = selectedCases.Length,
					rowCount = summaries.Count,
					ticks = clampedTicks,
					classificationCounts = summaries
						.GroupBy(row => row.GetType().GetProperty("classification")?.GetValue(row)?.ToString() ?? "unknown")
						.ToDictionary(group => group.Key, group => group.Count()),
					summaries = summaries.ToArray(),
					rows = rows.ToArray(),
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
					ClearAnomalyEngagementGrid(map);
				}
			}
		}

		static AnomalyMatrixCase[] SelectAnomalyMatrixCases(string cases)
		{
			if (string.IsNullOrWhiteSpace(cases))
				return anomalyMatrixCases;

			var requested = cases.Split(',')
				.Select(item => item.Trim())
				.Where(item => item.Length > 0)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var selected = anomalyMatrixCases
				.Where(testCase => requested.Contains(testCase.id) || requested.Contains(testCase.kindDef))
				.ToArray();
			if (selected.Length > 0)
				return selected;

			return requested
				.Select(kind => new AnomalyMatrixCase { id = kind, kindDef = kind, faction = "entities" })
				.ToArray();
		}

		static AnomalyMatrixCase[] SelectAnomalyEngagementCases(string cases)
		{
			if (string.IsNullOrWhiteSpace(cases))
			{
				var ids = new HashSet<string>(new[]
				{
					"FleshmassNucleus-passive",
					"Metalhorror-awake",
					"Metalhorror-dormant",
					"ShamblerSoldier-awake",
					"Ghoul-player",
					"Horaxian_Gunner"
				}, StringComparer.OrdinalIgnoreCase);
				return anomalyMatrixCases.Where(testCase => ids.Contains(testCase.id)).ToArray();
			}
			return SelectAnomalyMatrixCases(cases);
		}

		static object DescribeAnomalyMatrixRow(AnomalyMatrixCase testCase, Pawn pawn, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			var oldAttackMode = settings.attackMode;
			var oldEnemiesAttackZombies = settings.enemiesAttackZombies;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			try
			{
				var attackModes = new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything }
					.Select(mode =>
					{
						settings.attackMode = mode;
						return new
						{
							mode = mode.ToString(),
							attracts = Customization.DoesAttractsZombies(pawn),
							attackable = Tools.Attackable(zombie, mode, pawn)
						};
					})
					.ToArray();

				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				var reverseDisabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				var reverseEnabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				return new
				{
					testCase.id,
					testCase.kindDef,
					requestedFaction = testCase.faction,
					requestedActivityState = testCase.activityState,
					requestedDormancyState = testCase.dormancyState,
					pawn = DescribePawn(pawn),
					zombie = DescribeZombie(zombie),
					race = new
					{
						pawn.RaceProps.Humanlike,
						pawn.RaceProps.Animal,
						pawn.RaceProps.IsFlesh,
						pawn.RaceProps.IsMechanoid,
						pawn.IsShambler
					},
					activity = DescribeAnomalyActivity(pawn),
					dormancy = DescribeAnomalyDormancy(pawn),
					vanillaThreat = DescribeAnomalyVanillaThreat(pawn, zombie),
					cannotBecomeZombie = Customization.CannotBecomeZombie(pawn),
					infectionState = pawn.InfectionState().ToString(),
					attackModes,
					reverseHostility = new
					{
						enemiesAndAnimalsDisabled = reverseDisabled,
						enemiesAndAnimalsEnabled = reverseEnabled
					}
				};
			}
			finally
			{
				settings.attackMode = oldAttackMode;
				settings.enemiesAttackZombies = oldEnemiesAttackZombies;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
			}
		}

		static void ClearAnomalyEngagementGrid(Map map, IntVec3 pawnCell, IntVec3 zombieCell)
		{
			if (map == null)
				return;
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(pawnCell, 24f, true)
				.Concat(GenRadial.RadialCellsAround(zombieCell, 24f, true))
				.Where(cell => cell.InBounds(map))
				.Distinct())
			{
				grid.SetTimestamp(cell, 0);
				var count = grid.GetZombieCount(cell);
				if (count != 0)
					grid.ChangeZombieCount(cell, -count);
			}
		}

		static void ClearAnomalyEngagementGrid(Map map)
		{
			map?.GetGrid()?.IterateCellsQuick(cell =>
			{
				cell.timestamp = 0;
				cell.zombieCount = 0;
			});
		}

		static AnomalyEngagementState DescribeAnomalyEngagementState(AnomalyMatrixCase testCase, AttackMode mode, Pawn pawn, Zombie zombie, Faction zombieFaction)
		{
			var grid = pawn?.Map?.GetGrid() ?? zombie?.Map?.GetGrid();
			var targetTimestamp = pawn?.Spawned == true && grid != null ? grid.GetTimestamp(pawn.Position) : 0;
			var zombieTimestamp = zombie?.Spawned == true && grid != null ? grid.GetTimestamp(zombie.Position) : 0;
			var nextToZombie = zombie?.Spawned == true && grid != null
				? GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Select(cell => new
					{
						cell = ZombieRuntimeActions.DescribeCell(cell),
						timestamp = grid.GetTimestamp(cell),
						zombieCount = grid.GetZombieCount(cell)
					})
					.Where(sample => sample.timestamp > 0 || sample.zombieCount != 0)
					.Cast<object>()
					.ToArray()
				: Array.Empty<object>();
			var driver = zombie?.jobs?.curDriver as JobDriver_Stumble;
			var anyAdjacentTimestamp = zombie?.Spawned == true && grid != null
				&& GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Any(cell => grid.GetTimestamp(cell) > 0);
			var anyAdjacentZombieCount = zombie?.Spawned == true && grid != null
				&& GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Any(cell => grid.GetZombieCount(cell) != 0);
			return new AnomalyEngagementState
			{
				testCase = testCase.id,
				mode = mode.ToString(),
				pawn = DescribePawn(pawn),
				zombie = DescribeZombie(zombie),
				activity = DescribeAnomalyActivity(pawn),
				dormancy = DescribeAnomalyDormancy(pawn),
				vanillaThreat = DescribeAnomalyVanillaThreat(pawn, zombie),
				attracts = pawn != null && Customization.DoesAttractsZombies(pawn),
				attackable = pawn != null && zombie != null && Tools.Attackable(zombie, mode, pawn),
				reverseHostility = DescribeAnomalyReverseHostility(pawn, zombie, zombieFaction, ZombieSettings.Values),
				targetTimestamp = targetTimestamp,
				zombieTimestamp = zombieTimestamp,
				anyAdjacentTimestamp = anyAdjacentTimestamp,
				anyAdjacentZombieCount = anyAdjacentZombieCount,
				zombieTracking = zombie?.state == ZombieState.Tracking,
				zombiePatherMoving = zombie?.pather?.Moving ?? false,
				grid = new
				{
					targetTimestamp,
					zombieTimestamp,
					targetZombieCount = pawn?.Spawned == true && grid != null ? grid.GetZombieCount(pawn.Position) : 0,
					zombieCellZombieCount = zombie?.Spawned == true && grid != null ? grid.GetZombieCount(zombie.Position) : 0,
					nextToZombie
				},
				stumbleDriver = new
				{
					present = driver != null,
					destination = driver?.destination.IsValid == true ? ZombieRuntimeActions.DescribeCell(driver.destination) : null
				}
			};
		}

		static string ClassifyAnomalyEngagement(bool expectedEngagement, bool moved, AnomalyEngagementState before, AnomalyEngagementState afterMove, AnomalyEngagementState afterTicks)
		{
			var seeded = afterMove.targetTimestamp > 0 || afterMove.zombieTimestamp > 0 || afterMove.anyAdjacentTimestamp;
			var seededWithinWindow = seeded || afterTicks.targetTimestamp > 0 || afterTicks.zombieTimestamp > 0 || afterTicks.anyAdjacentTimestamp;
			var tracking = afterTicks.zombieTracking;
			var quietBefore = before.targetTimestamp == 0 && before.zombieTimestamp == 0 && before.anyAdjacentTimestamp == false;
			if (expectedEngagement)
				return moved && quietBefore && seededWithinWindow && tracking ? "nominal" : "unclear";
			return quietBefore && seeded == false && tracking == false ? "nominal" : "wrong";
		}

		static Faction ResolveAnomalyMatrixFaction(string key)
		{
			var normalized = (key ?? "entities").Trim().ToLowerInvariant();
			if (normalized == "player")
				return Faction.OfPlayer;
			if (normalized == "hostile")
				return Faction.OfAncientsHostile;
			if (normalized == "none")
				return null;
			if (normalized == "horax")
			{
				var horax = DefDatabase<FactionDef>.GetNamedSilentFail("HoraxCult");
				return horax == null ? Faction.OfAncientsHostile : Find.FactionManager.FirstFactionOfDef(horax) ?? Faction.OfAncientsHostile;
			}
			var entities = DefDatabase<FactionDef>.GetNamedSilentFail("Entities");
			if (entities != null)
				return Find.FactionManager.FirstFactionOfDef(entities);
			return null;
		}

		static void ApplyAnomalyState(Pawn pawn, string activityState, string dormancyState)
		{
			if (pawn == null)
				return;

			if (pawn.activity != null)
			{
				var normalized = (activityState ?? "").Trim().ToLowerInvariant();
				if (normalized == "active")
					pawn.activity.EnterActiveState();
				else if (normalized == "passive")
					pawn.activity.EnterPassiveState();
				else if (normalized == "deactivated")
					pawn.activity.Deactivate();
			}

			if (pawn.canBeDormant != null)
			{
				var normalized = (dormancyState ?? "").Trim().ToLowerInvariant();
				if (normalized == "awake")
					pawn.canBeDormant.WakeUp();
				else if (normalized == "dormant" || normalized == "sleep")
					pawn.canBeDormant.ToSleep();
			}
		}

		static object DescribeAnomalyActivity(Pawn pawn)
		{
			var activity = pawn?.activity;
			if (activity == null)
				return new { present = false };
			return new
			{
				present = true,
				state = activity.State.ToString(),
				activityLevel = activity.ActivityLevel,
				deactivated = activity.Deactivated,
				canBeSuppressed = activity.CanBeSuppressed,
				isDormant = activity.IsDormant
			};
		}

		static object DescribeAnomalyDormancy(Pawn pawn)
		{
			var dormancy = pawn?.canBeDormant;
			if (dormancy == null)
				return new { present = false };
			return new
			{
				present = true,
				awake = dormancy.Awake,
				waitingToWakeUp = dormancy.WaitingToWakeUp,
				showZs = dormancy.ShowZs,
				sleepJob = dormancy.SleepJob?.defName
			};
		}

		static object DescribeAnomalyVanillaThreat(Pawn pawn, Zombie zombie)
		{
			var zombieFaction = zombie?.Faction;
			var attackTarget = pawn as IAttackTarget;
			var canQueryThreat = attackTarget != null && pawn?.Spawned == true && pawn.Map != null;
			return new
			{
				implementsAttackTarget = attackTarget != null,
				hostileToZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
				hostileToZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
				hostileToPlayerFaction = DescribeHostility(TryHostileTo(pawn, Faction.OfPlayer)),
				threatQuerySkipped = canQueryThreat == false,
				potentialThreat = canQueryThreat == false ? (bool?)null : GenHostility.IsPotentialThreat(attackTarget),
				activeThreatToPlayer = canQueryThreat == false ? (bool?)null : GenHostility.IsActiveThreatTo(attackTarget, Faction.OfPlayer, false, false),
				activeThreatToZombies = canQueryThreat == false || zombieFaction == null ? (bool?)null : GenHostility.IsActiveThreatTo(attackTarget, zombieFaction, false, false)
			};
		}

		static object DescribeNociospherePolicy(Pawn nociosphere, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			return new
			{
				pawn = DescribePawn(nociosphere),
				zombie = DescribeZombie(zombie),
				activity = DescribeAnomalyActivity(nociosphere),
				vanillaThreat = DescribeAnomalyVanillaThreat(nociosphere, zombie),
				attackModes = DescribeAnomalyThingAttackModes(zombie, nociosphere, settings),
				reverseHostility = DescribeAnomalyReverseHostility(nociosphere, zombie, zombieFaction, settings)
			};
		}

		static object DescribeThingPolicy(string id, Thing thing, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			return new
			{
				id,
				thing = DescribeAnomalyThing(thing),
				zombie = DescribeZombie(zombie),
				attackModes = DescribeAnomalyThingAttackModes(zombie, thing, settings),
				reverseHostility = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(thing, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(thing, zombieFaction))
				}
			};
		}

		static object[] DescribeAnomalyThingAttackModes(Zombie zombie, Thing thing, SettingsGroup settings)
		{
			var oldAttackMode = settings.attackMode;
			try
			{
				return new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything }
					.Select(mode =>
					{
						settings.attackMode = mode;
						return new
						{
							mode = mode.ToString(),
							attackable = Tools.Attackable(zombie, mode, thing),
							attracts = thing is Pawn pawn ? Customization.DoesAttractsZombies(pawn) : (bool?)null
						};
					})
					.Cast<object>()
					.ToArray();
			}
			finally
			{
				settings.attackMode = oldAttackMode;
			}
		}

		static object DescribeAnomalyReverseHostility(Pawn pawn, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			var oldEnemiesAttackZombies = settings.enemiesAttackZombies;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			try
			{
				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				var disabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				var enabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				return new
				{
					enemiesAndAnimalsDisabled = disabled,
					enemiesAndAnimalsEnabled = enabled
				};
			}
			finally
			{
				settings.enemiesAttackZombies = oldEnemiesAttackZombies;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
			}
		}

		static object DescribeAnomalyThing(Thing thing)
		{
			return thing == null ? null : new
			{
				id = ZombieRuntimeActions.StableThingId(thing),
				thingId = thing.ThingID,
				defName = thing.def?.defName,
				type = thing.GetType().FullName,
				label = thing.LabelCap.ToString(),
				faction = thing.Faction?.Name,
				spawned = thing.Spawned,
				destroyed = thing.Destroyed,
				position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
				hitPoints = thing.HitPoints,
				maxHitPoints = thing.MaxHitPoints
			};
		}

		static void CleanupAnomalyThing(Thing thing)
		{
			if (thing == null || thing.Destroyed)
				return;
			if (thing.Spawned && thing.def?.destroyable == false)
				thing.DeSpawn(DestroyMode.Vanish);
			else
				thing.Destroy(DestroyMode.Vanish);
		}
	}
}
