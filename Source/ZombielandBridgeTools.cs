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

		struct FogRoomFixture
		{
			public IntVec3 doorCell;
			public IntVec3 targetWallCell;
			public CellRect interiorRect;
			public Building_Door door;
			public Building targetWall;
		}

		sealed class NeedSnapshot
		{
			public bool hasTracker;
			public int allCount;
			public int internalCount;
			public int miscCount;
			public string[] allDefs;
			public string[] internalDefs;
			public string[] miscDefs;
			public bool hasFoodField;
			public bool hasMoodField;
			public float? foodLevel;
		}

		sealed class RecordSnapshot
		{
			public float rawValue;
			public float publicValue;
			public int publicInt;
		}

		sealed class BloodFilthSnapshot
		{
			public object pawn;
			public string bloodDef;
			public object cell;
			public int before;
			public int after;
			public int delta;
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

		static object DescribeColor(Color color)
		{
			return new
			{
				r = color.r,
				g = color.g,
				b = color.b,
				a = color.a
			};
		}

		static bool ColorsApproximatelyEqual(Color a, Color b, float tolerance = 0.01f)
		{
			return Mathf.Abs(a.r - b.r) <= tolerance
				&& Mathf.Abs(a.g - b.g) <= tolerance
				&& Mathf.Abs(a.b - b.b) <= tolerance
				&& Mathf.Abs(a.a - b.a) <= tolerance;
		}

		static bool SelectsThroughSelector(object obj)
		{
			var selector = Find.Selector;
			selector.ClearSelection();
			selector.Select(obj, false, false);
			var selected = selector.IsSelected(obj);
			selector.ClearSelection();
			return selected;
		}

		static bool TryHasAnySocialMemoryWith(Pawn pawn, Pawn otherPawn, out bool result, out string error)
		{
			result = false;
			error = null;
			var method = typeof(RelationsUtility).GetMethod("HasAnySocialMemoryWith", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find RelationsUtility.HasAnySocialMemoryWith by reflection.";
				return false;
			}

			result = (bool)method.Invoke(null, new object[] { pawn, otherPawn });
			return true;
		}

		static Dictionary<string, int> MemoryDefCounts(Pawn pawn)
		{
			return pawn?.needs?.mood?.thoughts?.memories?.Memories?
				.GroupBy(memory => memory.def?.defName ?? "<null>")
				.ToDictionary(group => group.Key, group => group.Count())
				?? new Dictionary<string, int>();
		}

		static int TotalMemoryCount(Pawn pawn)
		{
			return pawn?.needs?.mood?.thoughts?.memories?.Memories?.Count ?? 0;
		}

		static readonly FieldInfo needsTrackerNeedsField = typeof(Pawn_NeedsTracker).GetField("needs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly FieldInfo needsTrackerMiscNeedsField = typeof(Pawn_NeedsTracker).GetField("needsMisc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo needsTrackerAddNeedMethod = typeof(Pawn_NeedsTracker).GetMethod("AddNeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		static List<Need> InternalNeeds(Pawn_NeedsTracker needsTracker, FieldInfo field)
		{
			return field?.GetValue(needsTracker) as List<Need> ?? new List<Need>();
		}

		static string[] NeedDefNames(IEnumerable<Need> needs)
		{
			return needs?
				.Select(need => need?.def?.defName ?? "<null>")
				.OrderBy(name => name)
				.ToArray() ?? Array.Empty<string>();
		}

		static NeedSnapshot DescribeNeeds(Pawn pawn)
		{
			var needsTracker = pawn?.needs;
			var allNeeds = needsTracker?.AllNeeds ?? new List<Need>();
			var internalNeeds = InternalNeeds(needsTracker, needsTrackerNeedsField);
			var miscNeeds = InternalNeeds(needsTracker, needsTrackerMiscNeedsField);
			return new NeedSnapshot
			{
				hasTracker = needsTracker != null,
				allCount = allNeeds.Count,
				internalCount = internalNeeds.Count,
				miscCount = miscNeeds.Count,
				allDefs = NeedDefNames(allNeeds),
				internalDefs = NeedDefNames(internalNeeds),
				miscDefs = NeedDefNames(miscNeeds),
				hasFoodField = needsTracker?.food != null,
				hasMoodField = needsTracker?.mood != null,
				foodLevel = needsTracker?.food?.CurLevel
			};
		}

		static bool TryForceAddNeed(Pawn pawn, NeedDef needDef, out string error)
		{
			error = null;
			if (pawn?.needs == null)
			{
				error = "Pawn has no needs tracker.";
				return false;
			}
			if (needDef == null)
			{
				error = "NeedDef is null.";
				return false;
			}
			if (needsTrackerAddNeedMethod == null)
			{
				error = "Could not find Pawn_NeedsTracker.AddNeed by reflection.";
				return false;
			}

			if (pawn.needs.TryGetNeed(needDef) == null)
				needsTrackerAddNeedMethod.Invoke(pawn.needs, new object[] { needDef });
			return true;
		}

		static float ImmunityFor(Pawn pawn, HediffDef hediffDef)
		{
			return pawn?.health?.immunity?.GetImmunity(hediffDef, true) ?? 0f;
		}

		static int ImmunityRecordCount(Pawn pawn)
		{
			return pawn?.health?.immunity?.ImmunityListForReading?.Count ?? 0;
		}

		static readonly FieldInfo recordsTrackerRecordsField = typeof(Pawn_RecordsTracker).GetField("records", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		static System.Collections.IList RawRecordValues(Pawn pawn)
		{
			var records = recordsTrackerRecordsField?.GetValue(pawn?.records);
			var valuesField = records?.GetType().GetField("values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return valuesField?.GetValue(records) as System.Collections.IList;
		}

		static bool TryFindRawRecordDef(Pawn pawn, RecordType recordType, out RecordDef recordDef, out string error)
		{
			recordDef = null;
			error = null;
			var values = RawRecordValues(pawn);
			if (values == null)
			{
				error = "Could not read Pawn_RecordsTracker.records values.";
				return false;
			}

			recordDef = DefDatabase<RecordDef>.AllDefsListForReading
				.Where(def => def.type == recordType && def.index >= 0 && def.index < values.Count)
				.OrderBy(def => def.index)
				.FirstOrDefault();
			if (recordDef != null)
				return true;

			error = $"No {recordType} RecordDef has an index inside the pawn's raw record map of size {values.Count}.";
			return false;
		}

		static float RawRecordValue(Pawn pawn, RecordDef recordDef)
		{
			var values = RawRecordValues(pawn);
			var value = values != null && recordDef != null && recordDef.index >= 0 && recordDef.index < values.Count
				? values[recordDef.index]
				: null;
			return value == null ? 0f : Convert.ToSingle(value);
		}

		static RecordSnapshot DescribeRecord(Pawn pawn, RecordDef recordDef)
		{
			return new RecordSnapshot
			{
				rawValue = RawRecordValue(pawn, recordDef),
				publicValue = pawn?.records?.GetValue(recordDef) ?? 0f,
				publicInt = pawn?.records?.GetAsInt(recordDef) ?? 0
			};
		}

		static readonly MethodInfo pathFollowerCostToMoveIntoCellMethod = typeof(Pawn_PathFollower).GetMethod("CostToMoveIntoCell", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Pawn), typeof(IntVec3) }, null);
		static readonly MethodInfo fireDoFireDamageMethod = typeof(Fire).GetMethod("DoFireDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Thing) }, null);

		static bool TryCostToMoveIntoCell(Pawn pawn, IntVec3 cell, out float cost, out string error)
		{
			cost = 0f;
			error = null;
			if (pathFollowerCostToMoveIntoCellMethod == null)
			{
				error = "Could not find Pawn_PathFollower.CostToMoveIntoCell(Pawn, IntVec3).";
				return false;
			}
			if (pawn == null)
			{
				error = "Pawn is null.";
				return false;
			}

			cost = Convert.ToSingle(pathFollowerCostToMoveIntoCellMethod.Invoke(null, new object[] { pawn, cell }));
			return true;
		}

		static bool TryDoFireDamage(Fire fire, Thing target, out string error)
		{
			error = null;
			if (fireDoFireDamageMethod == null)
			{
				error = "Could not find Fire.DoFireDamage(Thing).";
				return false;
			}
			if (fire == null || target == null)
			{
				error = "Fire and target are required.";
				return false;
			}

			fireDoFireDamageMethod.Invoke(fire, new object[] { target });
			return true;
		}

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
				wasMapPawnBefore = zombie?.wasMapPawnBefore ?? false,
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

		static void DisablePawnWork(Pawn pawn)
		{
			if (pawn?.workSettings == null)
				return;

			pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
			pawn.workSettings.DisableAll();
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

		static int CountThingsAt(Map map, IntVec3 cell, ThingDef thingDef)
		{
			if (map == null || cell.IsValid == false || thingDef == null)
				return 0;

			return cell.GetThingList(map).Count(thing => thing.def == thingDef);
		}

		static void ClearFilthAt(Map map, IntVec3 cell)
		{
			if (map == null || cell.IsValid == false)
				return;

			foreach (var filth in cell.GetThingList(map).OfType<Filth>().ToArray())
				filth.Destroy();
		}

		static int CountZombieZapMotesNear(Map map, IntVec3 center, float radius)
		{
			return CountThingsNear(map, center, CustomDefs.ZombieZapA, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapB, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapC, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapD, radius);
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

		static AvoidGrid BuildAvoidGridForZombie(Map map, Zombie zombie)
		{
			var tickManager = map.GetComponent<TickManager>();
			var specs = new List<ZombieCostSpecs>
			{
				new()
				{
					position = zombie.Position,
					radius = Tools.ZombieAvoidRadius(zombie),
					maxCosts = TickManager.ZombieMaxCosts(zombie)
				}
			};
			tickManager.avoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			return tickManager.avoidGrid;
		}

		static int AvoidCost(AvoidGrid avoidGrid, Map map, IntVec3 cell)
		{
			return avoidGrid.GetCosts()[cell.x + cell.z * map.Size.x];
		}

		static IntVec3[] DescribePathCells(PawnPath path)
		{
			if (path?.Found != true)
				return Array.Empty<IntVec3>();
			return Enumerable.Range(0, path.NodesLeftCount).Select(path.Peek).ToArray();
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

		static bool TryFindShockerFixtureCell(Map map, IntVec3 root, float radius, out IntVec3 shockerCell, out object error)
		{
			shockerCell = IntVec3.Invalid;
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

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var clear = true;
				for (var dx = -2; dx <= 2 && clear; dx++)
				{
					for (var dz = -3; dz <= 4 && clear; dz++)
					{
						var cell = candidate + new IntVec3(dx, 0, dz);
						if (cell.InBounds(map) == false || cell.Fogged(map) || cell.Standable(map) == false)
						{
							clear = false;
							break;
						}
						if (cell.GetEdifice(map) != null || cell.GetFirstThing<Mineable>(map) != null)
						{
							clear = false;
							break;
						}
						if (cell.GetThingList(map).Any(thing => thing is Pawn))
						{
							clear = false;
							break;
						}
					}
				}

				if (clear)
				{
					shockerCell = candidate;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear zombie shocker fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryFindFogRoomFixtureDoorCell(Map map, IntVec3 root, float radius, out IntVec3 doorCell, out object error)
		{
			doorCell = IntVec3.Invalid;
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

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var fixtureRect = CellRect.FromLimits(candidate.x - 3, candidate.z - 1, candidate.x + 3, candidate.z + 6);
				if (fixtureRect.InBounds(map) == false)
					continue;

				var clear = true;
				foreach (var cell in fixtureRect.Cells)
				{
					if (cell.Fogged(map)
						|| cell.Standable(map) == false
						|| cell.GetEdifice(map) != null
						|| cell.GetFirstThing<Mineable>(map) != null
						|| cell.GetThingList(map).Any(thing => thing is Pawn))
					{
						clear = false;
						break;
					}
				}

				if (clear)
				{
					doorCell = candidate;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear fog-room fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryBuildFogRoomFixture(Map map, IntVec3 root, float radius, out FogRoomFixture fixture, out object error)
		{
			fixture = default;
			error = null;
			if (TryFindFogRoomFixtureDoorCell(map, root, radius, out var doorCell, out error) == false)
				return false;

			var wallDef = ThingDefOf.Wall;
			var doorDef = ThingDefOf.Door;
			var stuffDef = ThingDefOf.WoodLog;
			var interiorRect = CellRect.FromLimits(doorCell.x - 2, doorCell.z + 1, doorCell.x + 2, doorCell.z + 5);
			var fixtureRect = CellRect.FromLimits(doorCell.x - 3, doorCell.z, doorCell.x + 3, doorCell.z + 6);
			var targetWallCell = doorCell + IntVec3.West;
			Building targetWall = null;
			foreach (var cell in fixtureRect.EdgeCells)
			{
				if (cell == doorCell)
					continue;

				var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
				if (wall == null)
					continue;
				GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
				wall.SetFaction(Faction.OfPlayer);
				if (cell == targetWallCell)
					targetWall = wall;
			}

			var door = ThingMaker.MakeThing(doorDef, stuffDef) as Building_Door;
			if (door == null || targetWall == null)
			{
				error = new
				{
					success = false,
					targetWallCell = ZombieRuntimeActions.DescribeCell(targetWallCell),
					error = "Could not create the fog-room fixture door or target wall."
				};
				return false;
			}

			GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
			door.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			fixture = new FogRoomFixture
			{
				doorCell = doorCell,
				targetWallCell = targetWallCell,
				interiorRect = interiorRect,
				door = door,
				targetWall = targetWall
			};
			return true;
		}

		static bool TryFindClearBuildingFootprint(Map map, ThingDef thingDef, IntVec3 root, float radius, out IntVec3 cell, out object error)
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

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;

				var occupied = false;
				foreach (var footprintCell in GenAdj.OccupiedRect(candidate, Rot4.North, thingDef.size))
				{
					if (footprintCell.InBounds(map) == false
						|| footprintCell.Fogged(map)
						|| footprintCell.Standable(map) == false
						|| footprintCell.GetEdifice(map) != null
						|| footprintCell.GetFirstThing<Mineable>(map) != null
						|| footprintCell.GetThingList(map).Any(thing => thing is Pawn))
					{
						occupied = true;
						break;
					}
				}

				if (occupied)
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear footprint for {thingDef?.defName ?? "building"} was found near ({root.x}, {root.z})."
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

		[Tool("zombieland/zombie_selection_respects_former_colonist", Description = "Verify map-click and selector behavior distinguishes ordinary zombies from former-colonist zombies and corpses.")]
		public static object ZombieSelectionRespectsFormerColonist()
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

			Find.Selector.ClearSelection();
			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(4, 0, 0), 10f, out var formerPawnCell, out var formerSpawnError) == false)
				return formerSpawnError;

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
			if (normalZombie == null)
			{
				return new
				{
					success = false,
					normalCell = ZombieRuntimeActions.DescribeCell(normalCell),
					error = "ZombieGenerator.SpawnZombie returned no ordinary zombie."
				};
			}

			var beforeConversionIds = new HashSet<string>(CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId));
			var formerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(formerPawn, formerPawnCell, map, Rot4.South);
			DisablePawnWork(formerPawn);
			var formerPawnBeforeConversion = DescribePawn(formerPawn);
			ZombieRuntimeActions.ConvertPawnToZombie(formerPawn, map, true);
			var formerZombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeConversionIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.OrderBy(zombie => zombie.Position.DistanceToSquared(formerPawnCell))
				.FirstOrDefault();
			if (formerZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					formerPawn = formerPawnBeforeConversion,
					formerPawnCell = ZombieRuntimeActions.DescribeCell(formerPawnCell),
					error = "Converting the pawn did not produce a new zombie."
				};
			}

			var expectedFormerColor = new Color(0.7f, 1f, 0.7f);
			var normalLiveSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(normalZombie);
			var formerLiveSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(formerZombie);
			var normalLiveLabelColor = PawnNameColorUtility.PawnNameColorOf(normalZombie);
			var formerLiveLabelColor = PawnNameColorUtility.PawnNameColorOf(formerZombie);
			var normalLiveHasFormerColor = ColorsApproximatelyEqual(normalLiveLabelColor, expectedFormerColor);
			var formerLiveHasFormerColor = ColorsApproximatelyEqual(formerLiveLabelColor, expectedFormerColor);

			normalZombie.Kill(null);
			formerZombie.Kill(null);
			var normalCorpse = normalZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(normalCell)).FirstOrDefault();
			var formerCorpse = formerZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(formerPawnCell)).FirstOrDefault();
			if (normalCorpse == null || formerCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					normalZombie = DescribeZombie(normalZombie),
					formerZombie = DescribeZombie(formerZombie),
					normalCorpse = DescribeCorpse(normalCorpse),
					formerCorpse = DescribeCorpse(formerCorpse),
					error = "Killing the test zombies did not leave both zombie corpses."
				};
			}

			var normalCorpseSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(normalCorpse);
			var formerCorpseSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(formerCorpse);
			var normalCorpseSelectedBySelector = SelectsThroughSelector(normalCorpse);
			var formerCorpseSelectedBySelector = SelectsThroughSelector(formerCorpse);
			Find.Selector.ClearSelection();

			return new
			{
				success = normalLiveSelectableByMapClick == false
					&& formerLiveSelectableByMapClick
					&& normalCorpseSelectableByMapClick == false
					&& formerCorpseSelectableByMapClick
					&& normalCorpseSelectedBySelector == false
					&& formerCorpseSelectedBySelector
					&& normalLiveHasFormerColor == false
					&& formerLiveHasFormerColor,
				destroyedZombies,
				destroyedZombieCorpses,
				normalZombie = DescribeZombie(normalZombie),
				formerZombie = DescribeZombie(formerZombie),
				formerPawnBeforeConversion,
				normalCorpse = DescribeCorpse(normalCorpse),
				formerCorpse = DescribeCorpse(formerCorpse),
				normalLiveSelectableByMapClick,
				formerLiveSelectableByMapClick,
				normalCorpseSelectableByMapClick,
				formerCorpseSelectableByMapClick,
				normalCorpseSelectedBySelector,
				formerCorpseSelectedBySelector,
				normalLiveLabelColor = DescribeColor(normalLiveLabelColor),
				formerLiveLabelColor = DescribeColor(formerLiveLabelColor),
				normalLiveHasFormerColor,
				formerLiveHasFormerColor
			};
		}

		[Tool("zombieland/zombie_social_thought_suppression", Description = "Verify zombie pawns and zombie corpses are ignored by RimWorld social-memory, interaction, and observed-corpse thought APIs.")]
		public static object ZombieSocialThoughtSuppression()
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;
			if (TryFindClearSpawnCell(map, actorCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no social/thought test zombie."
				};
			}

			if (TryHasAnySocialMemoryWith(actor, zombie, out var hasAnySocialMemoryWithZombie, out var socialMemoryError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = socialMemoryError
				};
			}

			var thoughtDef = ThoughtDefOf.DebugBad;
			var actorCanGetDebugThought = thoughtDef != null && ThoughtUtility.CanGetThought(actor, thoughtDef);
			var zombieCanGetDebugThought = thoughtDef != null && ThoughtUtility.CanGetThought(zombie, thoughtDef);
			var pawnsKnowEachOther = RelationsUtility.PawnsKnowEachOther(actor, zombie);
			var pawnsKnowEachOtherReverse = RelationsUtility.PawnsKnowEachOther(zombie, actor);
			var actorOpinionOfZombie = actor.relations?.OpinionOf(zombie) ?? int.MinValue;
			var zombieOpinionOfActor = zombie.relations?.OpinionOf(actor) ?? int.MinValue;
			var socialThoughtsAboutZombie = new List<ISocialThought>();
			actor.needs?.mood?.thoughts?.GetSocialThoughts(zombie, socialThoughtsAboutZombie);
			var actorInteractWithZombie = actor.interactions?.TryInteractWith(zombie, InteractionDefOf.Chitchat) ?? false;
			var zombieInteractWithActor = zombie.interactions?.TryInteractWith(actor, InteractionDefOf.Chitchat) ?? false;

			zombie.Kill(null);
			var zombieCorpse = zombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
			if (zombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "Killing the social/thought test zombie did not leave a ZombieCorpse."
				};
			}

			var observedZombieCorpseThought = zombieCorpse.GiveObservedThought(actor);
			var observedZombieCorpseHistoryEvent = zombieCorpse.GiveObservedHistoryEvent(actor);

			return new
			{
				success = thoughtDef != null
					&& actorCanGetDebugThought
					&& zombieCanGetDebugThought == false
					&& pawnsKnowEachOther == false
					&& pawnsKnowEachOtherReverse == false
					&& hasAnySocialMemoryWithZombie == false
					&& actorOpinionOfZombie == 0
					&& zombieOpinionOfActor == 0
					&& socialThoughtsAboutZombie.Count == 0
					&& actorInteractWithZombie == false
					&& zombieInteractWithActor == false
					&& observedZombieCorpseThought == null
					&& observedZombieCorpseHistoryEvent == null,
				destroyedZombies,
				destroyedZombieCorpses,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				zombieCorpse = DescribeCorpse(zombieCorpse),
				thoughtDef = thoughtDef?.defName,
				actorCanGetDebugThought,
				zombieCanGetDebugThought,
				pawnsKnowEachOther,
				pawnsKnowEachOtherReverse,
				hasAnySocialMemoryWithZombie,
				actorOpinionOfZombie,
				zombieOpinionOfActor,
				socialThoughtCountAboutZombie = socialThoughtsAboutZombie.Count,
				actorInteractWithZombie,
				zombieInteractWithActor,
				observedZombieCorpseThoughtDef = observedZombieCorpseThought?.def?.defName,
				observedZombieCorpseHistoryEventDef = observedZombieCorpseHistoryEvent?.defName
			};
		}

		[Tool("zombieland/zombie_corpse_alert_forbid_contract", Description = "Verify normal and former-colonist zombie corpses stay out of vanilla colonist-corpse alerts and outside-home forbidding.")]
		public static object ZombieCorpseAlertForbidContract()
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(3, 0, 0), 10f, out var normalZombieCell, out var normalZombieSpawnError) == false)
				return normalZombieSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(6, 0, 0), 12f, out var formerPawnCell, out var formerPawnSpawnError) == false)
				return formerPawnSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(0, 0, 3), 10f, out var humanCorpseCell, out var humanCorpseSpawnError) == false)
				return humanCorpseSpawnError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
			DisablePawnWork(worker);

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalZombieCell, map, ZombieType.Normal, true);
			if (normalZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					error = "ZombieGenerator.SpawnZombie returned no ordinary corpse test zombie."
				};
			}

			var beforeConversionIds = new HashSet<string>(CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId));
			var formerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(formerPawn, formerPawnCell, map, Rot4.South);
			DisablePawnWork(formerPawn);
			var formerPawnBeforeConversion = DescribePawn(formerPawn);
			ZombieRuntimeActions.ConvertPawnToZombie(formerPawn, map, true);
			var formerZombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeConversionIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.OrderBy(zombie => zombie.Position.DistanceToSquared(formerPawnCell))
				.FirstOrDefault();
			if (formerZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					formerPawn = formerPawnBeforeConversion,
					error = "Converting the former-colonist corpse test pawn did not produce a zombie."
				};
			}

			var humanPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(humanPawn, humanCorpseCell, map, Rot4.South);
			DisablePawnWork(humanPawn);
			var humanPawnBeforeDeath = DescribePawn(humanPawn);
			if (ZombieRuntimeActions.KillPawnToCorpse(humanPawn, out var humanCorpse, out var killError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					humanPawn = humanPawnBeforeDeath,
					error = killError
				};
			}

			normalZombie.Kill(null);
			formerZombie.Kill(null);
			var normalZombieCorpse = normalZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(normalZombieCell)).FirstOrDefault();
			var formerZombieCorpse = formerZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(formerPawnCell)).FirstOrDefault();
			if (normalZombieCorpse == null || formerZombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					normalZombie = DescribeZombie(normalZombie),
					formerZombie = DescribeZombie(formerZombie),
					normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
					formerZombieCorpse = DescribeCorpse(formerZombieCorpse),
					error = "Killing the corpse test zombies did not leave both ZombieCorpse instances."
				};
			}

			var humanCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(humanCorpse);
			var normalZombieCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(normalZombieCorpse);
			var formerZombieCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(formerZombieCorpse);

			foreach (var corpse in new Corpse[] { humanCorpse, normalZombieCorpse, formerZombieCorpse })
			{
				corpse.SetForbidden(false, false);
				map.areaManager.Home[corpse.Position] = false;
				ForbidUtility.SetForbiddenIfOutsideHomeArea(corpse);
			}
			var humanCorpseForbiddenAfterOutsideHome = humanCorpse.IsForbidden(worker);
			var normalZombieCorpseForbiddenAfterOutsideHome = normalZombieCorpse.IsForbidden(worker);
			var formerZombieCorpseForbiddenAfterOutsideHome = formerZombieCorpse.IsForbidden(worker);

			var extractWorkGiver = new WorkGiver_ExtractZombieSerum();
			var doubleTapWorkGiver = new WorkGiver_DoubleTap();
			var normalZombieCorpseHasExtractJob = extractWorkGiver.HasJobOnThing(worker, normalZombieCorpse, true);
			var formerZombieCorpseHasExtractJob = extractWorkGiver.HasJobOnThing(worker, formerZombieCorpse, true);
			var normalZombieCorpseHasDoubleTapJob = doubleTapWorkGiver.HasJobOnThing(worker, normalZombieCorpse, true);
			var formerZombieCorpseHasDoubleTapJob = doubleTapWorkGiver.HasJobOnThing(worker, formerZombieCorpse, true);

			return new
			{
				success = humanCorpseIsColonist
					&& normalZombieCorpseIsColonist == false
					&& formerZombieCorpseIsColonist == false
					&& humanCorpseForbiddenAfterOutsideHome
					&& normalZombieCorpseForbiddenAfterOutsideHome == false
					&& formerZombieCorpseForbiddenAfterOutsideHome == false
					&& normalZombieCorpseHasExtractJob
					&& formerZombieCorpseHasExtractJob
					&& normalZombieCorpseHasDoubleTapJob == false
					&& formerZombieCorpseHasDoubleTapJob == false,
				destroyedZombies,
				destroyedZombieCorpses,
				worker = DescribePawn(worker),
				humanPawnBeforeDeath,
				formerPawnBeforeConversion,
				normalZombie = DescribeZombie(normalZombie),
				formerZombie = DescribeZombie(formerZombie),
				humanCorpse = DescribeCorpse(humanCorpse),
				normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
				formerZombieCorpse = DescribeCorpse(formerZombieCorpse),
				humanCorpseIsColonist,
				normalZombieCorpseIsColonist,
				formerZombieCorpseIsColonist,
				humanCorpseForbiddenAfterOutsideHome,
				normalZombieCorpseForbiddenAfterOutsideHome,
				formerZombieCorpseForbiddenAfterOutsideHome,
				normalZombieCorpseHasExtractJob,
				formerZombieCorpseHasExtractJob,
				normalZombieCorpseHasDoubleTapJob,
				formerZombieCorpseHasDoubleTapJob
			};
		}

		[Tool("zombieland/zombie_death_thought_suppression", Description = "Verify RimWorld death-thought delivery gives colonist death memories but suppresses adult zombie death memories.")]
		public static object ZombieDeathThoughtSuppression()
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var observerCell, out var observerSpawnError) == false)
				return observerSpawnError;
			if (TryFindClearSpawnCell(map, observerCell + new IntVec3(3, 0, 0), 10f, out var humanVictimCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, observerCell + new IntVec3(6, 0, 0), 12f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var observer = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(observer, observerCell, map, Rot4.South);
			DisablePawnWork(observer);
			var humanVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(humanVictim, humanVictimCell, map, Rot4.South);
			DisablePawnWork(humanVictim);
			var zombieVictim = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombieVictim == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					error = "ZombieGenerator.SpawnZombie returned no death-thought test zombie."
				};
			}

			var memoriesBefore = TotalMemoryCount(observer);
			var memoryDefsBefore = MemoryDefCounts(observer);
			if (ZombieRuntimeActions.KillPawnToCorpse(humanVictim, out var humanCorpse, out var killHumanError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					humanVictim = DescribePawn(humanVictim),
					error = killHumanError
				};
			}
			var memoriesAfterHumanKill = TotalMemoryCount(observer);
			PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(humanVictim, null, PawnDiedOrDownedThoughtsKind.Died);
			var memoriesAfterHumanTryGive = TotalMemoryCount(observer);
			var humanThoughtDelta = memoriesAfterHumanTryGive - memoriesAfterHumanKill;
			var humanTotalDelta = memoriesAfterHumanTryGive - memoriesBefore;
			var memoryDefsAfterHuman = MemoryDefCounts(observer);

			zombieVictim.Kill(null);
			var zombieCorpse = zombieVictim.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
			if (zombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					zombieVictim = DescribeZombie(zombieVictim),
					error = "Killing the death-thought test zombie did not leave a ZombieCorpse."
				};
			}

			PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(zombieVictim, null, PawnDiedOrDownedThoughtsKind.Died);
			var memoriesAfterZombieTryGive = TotalMemoryCount(observer);
			var zombieThoughtDelta = memoriesAfterZombieTryGive - memoriesAfterHumanTryGive;
			var memoryDefsAfterZombie = MemoryDefCounts(observer);

			return new
			{
				success = humanThoughtDelta > 0
					&& humanTotalDelta > 0
					&& zombieThoughtDelta == 0,
				destroyedZombies,
				destroyedZombieCorpses,
				observer = DescribePawn(observer),
				humanVictim = DescribePawn(humanVictim),
				humanCorpse = DescribeCorpse(humanCorpse),
				zombieVictim = DescribeZombie(zombieVictim),
				zombieCorpse = DescribeCorpse(zombieCorpse),
				memoriesBefore,
				memoriesAfterHumanKill,
				memoriesAfterHumanTryGive,
				memoriesAfterZombieTryGive,
				humanThoughtDelta,
				humanTotalDelta,
				zombieThoughtDelta,
				memoryDefsBefore,
				memoryDefsAfterHuman,
				memoryDefsAfterZombie
			};
		}

		[Tool("zombieland/zombie_damage_memory_suppression", Description = "Verify normal pawn damage can create harm memories while zombie-instigated damage does not create social memories about zombies.")]
		public static object ZombieDamageMemorySuppression()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanAttackerCell, out var humanAttackerSpawnError) == false)
				return humanAttackerSpawnError;
			if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(3, 0, 0), 10f, out var humanVictimCell, out var humanVictimSpawnError) == false)
				return humanVictimSpawnError;
			if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(6, 0, 0), 12f, out var zombieAttackerCell, out var zombieAttackerSpawnError) == false)
				return zombieAttackerSpawnError;
			if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(9, 0, 0), 14f, out var zombieVictimCell, out var zombieVictimSpawnError) == false)
				return zombieVictimSpawnError;

			var humanAttacker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var humanVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var zombieVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(humanAttacker, humanAttackerCell, map, Rot4.South);
			GenSpawn.Spawn(humanVictim, humanVictimCell, map, Rot4.South);
			GenSpawn.Spawn(zombieVictim, zombieVictimCell, map, Rot4.South);
			DisablePawnWork(humanAttacker);
			DisablePawnWork(humanVictim);
			DisablePawnWork(zombieVictim);
			var zombieAttacker = ZombieRuntimeActions.SpawnZombie(zombieAttackerCell, map, ZombieType.Normal, true);
			if (zombieAttacker == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					humanAttacker = DescribePawn(humanAttacker),
					humanVictim = DescribePawn(humanVictim),
					zombieVictim = DescribePawn(zombieVictim),
					error = "ZombieGenerator.SpawnZombie returned no damage-memory test zombie."
				};
			}

			var humanVictimMemoriesBefore = TotalMemoryCount(humanVictim);
			var humanVictimDefsBefore = MemoryDefCounts(humanVictim);
			var humanDamage = humanVictim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 2f, 0f, -1f, humanAttacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown, humanVictim, true, true));
			var humanVictimMemoriesAfter = TotalMemoryCount(humanVictim);
			var humanVictimDefsAfter = MemoryDefCounts(humanVictim);
			var humanDamageMemoryDelta = humanVictimMemoriesAfter - humanVictimMemoriesBefore;

			var zombieVictimMemoriesBefore = TotalMemoryCount(zombieVictim);
			var zombieVictimDefsBefore = MemoryDefCounts(zombieVictim);
			var zombieDamage = zombieVictim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 2f, 0f, -1f, zombieAttacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown, zombieVictim, true, true));
			var zombieVictimMemoriesAfter = TotalMemoryCount(zombieVictim);
			var zombieVictimDefsAfter = MemoryDefCounts(zombieVictim);
			var zombieDamageMemoryDelta = zombieVictimMemoriesAfter - zombieVictimMemoriesBefore;

			return new
			{
				success = humanDamageMemoryDelta > 0
					&& zombieDamageMemoryDelta == 0,
				destroyedZombies,
				humanAttacker = DescribePawn(humanAttacker),
				humanVictim = DescribePawn(humanVictim),
				zombieAttacker = DescribeZombie(zombieAttacker),
				zombieVictim = DescribePawn(zombieVictim),
				humanDamageTotal = humanDamage.totalDamageDealt,
				zombieDamageTotal = zombieDamage.totalDamageDealt,
				humanVictimMemoriesBefore,
				humanVictimMemoriesAfter,
				humanDamageMemoryDelta,
				zombieVictimMemoriesBefore,
				zombieVictimMemoriesAfter,
				zombieDamageMemoryDelta,
				humanVictimDefsBefore,
				humanVictimDefsAfter,
				zombieVictimDefsBefore,
				zombieVictimDefsAfter
			};
		}

		[Tool("zombieland/zombie_health_needs_upkeep_suppression", Description = "Verify zombie needs reconciliation, needs ticking, and immunity ticking are suppressed while normal pawns still use the vanilla upkeep paths.")]
		public static object ZombieHealthNeedsUpkeepSuppression()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no health/needs test zombie."
				};
			}

			var needDef = NeedDefOf.Food;
			var humanNeedsBefore = DescribeNeeds(human);
			human.needs.AddOrRemoveNeedsAsAppropriate();
			var humanNeedsAfterReconcile = DescribeNeeds(human);

			var zombieNeedsBefore = DescribeNeeds(zombie);
			if (TryForceAddNeed(zombie, needDef, out var needError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					zombieNeedsBefore,
					error = needError
				};
			}
			var zombieNeedsAfterForcedNeed = DescribeNeeds(zombie);
			zombie.needs.AddOrRemoveNeedsAsAppropriate();
			var zombieNeedsAfterReconcile = DescribeNeeds(zombie);

			if (TryForceAddNeed(zombie, needDef, out needError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					zombieNeedsAfterReconcile,
					error = needError
				};
			}
			var zombieFoodBeforeTick = zombie.needs.TryGetNeed(needDef)?.CurLevel ?? -1f;
			zombie.needs.NeedsTrackerTickInterval(150);
			var zombieFoodAfterTick = zombie.needs.TryGetNeed(needDef)?.CurLevel ?? -1f;
			var zombieNeedsAfterTick = DescribeNeeds(zombie);
			zombie.needs.AddOrRemoveNeedsAsAppropriate();
			var zombieNeedsAfterFinalReconcile = DescribeNeeds(zombie);

			var diseaseDef = HediffDefOf.Plague;
			human.health.AddHediff(HediffMaker.MakeHediff(diseaseDef, human));
			zombie.health.AddHediff(HediffMaker.MakeHediff(diseaseDef, zombie));
			var humanImmunityBefore = ImmunityFor(human, diseaseDef);
			var zombieImmunityBefore = ImmunityFor(zombie, diseaseDef);
			var humanImmunityRecordCountBefore = ImmunityRecordCount(human);
			var zombieImmunityRecordCountBefore = ImmunityRecordCount(zombie);
			const int oneDayTicks = 60000;
			human.health.immunity.ImmunityHandlerTickInterval(oneDayTicks);
			zombie.health.immunity.ImmunityHandlerTickInterval(oneDayTicks);
			var humanImmunityAfter = ImmunityFor(human, diseaseDef);
			var zombieImmunityAfter = ImmunityFor(zombie, diseaseDef);
			var humanImmunityRecordCountAfter = ImmunityRecordCount(human);
			var zombieImmunityRecordCountAfter = ImmunityRecordCount(zombie);

			var humanNeedsPopulated = humanNeedsAfterReconcile.internalCount > 0;
			var zombieForcedNeedVisibleInternally = zombieNeedsAfterForcedNeed.internalCount > 0;
			var zombieNeedsClearedByReconcile = zombieNeedsAfterReconcile.internalCount == 0
				&& zombieNeedsAfterReconcile.allCount == 0
				&& zombieNeedsAfterReconcile.hasFoodField == false;
			var zombieNeedTickSkipped = zombieFoodAfterTick == zombieFoodBeforeTick;
			var zombieNeedsClearedAfterTick = zombieNeedsAfterFinalReconcile.internalCount == 0
				&& zombieNeedsAfterFinalReconcile.allCount == 0;
			var humanImmunityAdvanced = humanImmunityAfter > humanImmunityBefore && humanImmunityRecordCountAfter > humanImmunityRecordCountBefore;
			var zombieImmunitySuppressed = zombieImmunityAfter == zombieImmunityBefore && zombieImmunityRecordCountAfter == zombieImmunityRecordCountBefore;

			return new
			{
				success = humanNeedsPopulated
					&& zombieForcedNeedVisibleInternally
					&& zombieNeedsClearedByReconcile
					&& zombieNeedTickSkipped
					&& zombieNeedsClearedAfterTick
					&& humanImmunityAdvanced
					&& zombieImmunitySuppressed,
				destroyedZombies,
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				needDef = needDef.defName,
				diseaseDef = diseaseDef.defName,
				immunityTickWindow = oneDayTicks,
				humanNeedsBefore,
				humanNeedsAfterReconcile,
				zombieNeedsBefore,
				zombieNeedsAfterForcedNeed,
				zombieNeedsAfterReconcile,
				zombieFoodBeforeTick,
				zombieFoodAfterTick,
				zombieNeedsAfterTick,
				zombieNeedsAfterFinalReconcile,
				humanNeedsPopulated,
				zombieForcedNeedVisibleInternally,
				zombieNeedsClearedByReconcile,
				zombieNeedTickSkipped,
				zombieNeedsClearedAfterTick,
				humanImmunityBefore,
				humanImmunityAfter,
				zombieImmunityBefore,
				zombieImmunityAfter,
				humanImmunityRecordCountBefore,
				humanImmunityRecordCountAfter,
				zombieImmunityRecordCountBefore,
				zombieImmunityRecordCountAfter,
				humanImmunityAdvanced,
				zombieImmunitySuppressed
			};
		}

		[Tool("zombieland/zombie_records_suppression", Description = "Verify zombies cannot mutate or report vanilla pawn records while ordinary pawns still can.")]
		public static object ZombieRecordsSuppression()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no records test zombie."
				};
			}

			if (TryFindRawRecordDef(human, RecordType.Int, out var recordDef, out var recordError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					error = recordError
				};
			}

			var humanBefore = DescribeRecord(human, recordDef);
			var zombieBefore = DescribeRecord(zombie, recordDef);
			human.records.Increment(recordDef);
			human.records.AddTo(recordDef, 2f);
			zombie.records.Increment(recordDef);
			zombie.records.AddTo(recordDef, 2f);
			var humanAfter = DescribeRecord(human, recordDef);
			var zombieAfter = DescribeRecord(zombie, recordDef);

			var humanRecordsMutated = humanAfter.rawValue == humanBefore.rawValue + 3f
				&& humanAfter.publicValue == humanBefore.publicValue + 3f
				&& humanAfter.publicInt == humanBefore.publicInt + 3;
			var zombieRecordsNotMutated = zombieAfter.rawValue == zombieBefore.rawValue;
			var zombieRecordsHidden = zombieAfter.publicValue == 0f && zombieAfter.publicInt == 0;

			return new
			{
				success = humanRecordsMutated
					&& zombieRecordsNotMutated
					&& zombieRecordsHidden,
				destroyedZombies,
				recordDef = recordDef.defName,
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				humanBefore,
				humanAfter,
				zombieBefore,
				zombieAfter,
				humanRecordsMutated,
				zombieRecordsNotMutated,
				zombieRecordsHidden
			};
		}

		[Tool("zombieland/zombie_clamor_suppression", Description = "Verify all GenClamor overloads suppress zombie-originated clamors while ordinary pawn clamors still reach nearby hearers.")]
		public static object ZombieClamorSuppression()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var listenerCell, out var listenerSpawnError) == false)
				return listenerSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(3, 0, 0), 8f, out var humanSourceCell, out var humanSourceSpawnError) == false)
				return humanSourceSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(6, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(3, 0, 3), 10f, out var blobCell, out var blobSpawnError) == false)
				return blobSpawnError;

			var listener = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var humanSource = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(listener, listenerCell, map, Rot4.South);
			GenSpawn.Spawn(humanSource, humanSourceCell, map, Rot4.South);
			DisablePawnWork(listener);
			DisablePawnWork(humanSource);

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					error = "ZombieGenerator.SpawnZombie returned no clamor test zombie."
				};
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					zombie = DescribeZombie(zombie),
					error = "ZombieSpitter.Spawn returned no clamor test spitter."
				};
			}

			var existingBlobs = CurrentZombies(map).OfType<ZombieBlob>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieBlob.Spawn(map, blobCell);
			var blob = CurrentZombies(map).OfType<ZombieBlob>()
				.FirstOrDefault(candidate => existingBlobs.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(blobCell)).FirstOrDefault();
			if (blob == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					error = "ZombieBlob.Spawn returned no clamor test blob."
				};
			}

			int ListenerEffectCount(Thing source)
			{
				var count = 0;
				GenClamor.DoClamor(source, source.Position, 20f, (clamorSource, hearer) =>
				{
					if (hearer == listener)
						count++;
				});
				return count;
			}

			var humanEffectCount = ListenerEffectCount(humanSource);
			var zombieEffectCount = ListenerEffectCount(zombie);
			var spitterEffectCount = ListenerEffectCount(spitter);
			var blobEffectCount = ListenerEffectCount(blob);

			return new
			{
				success = humanEffectCount > 0
					&& zombieEffectCount == 0
					&& spitterEffectCount == 0
					&& blobEffectCount == 0,
				destroyedZombies,
				listener = DescribePawn(listener),
				humanSource = DescribePawn(humanSource),
				zombie = DescribeZombie(zombie),
				spitter = DescribeZombie(spitter),
				blob = DescribeZombie(blob),
				humanEffectCount,
				zombieEffectCount,
				spitterEffectCount,
				blobEffectCount
			};
		}

		[Tool("zombieland/tar_slime_move_cost_contract", Description = "Verify TarSlime applies the Zombieland movement-cost formula to zombies and spitters while ordinary pawns use the non-zombie formula.")]
		public static object TarSlimeMoveCostContract()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 10f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var clearCell, out var clearSpawnError) == false)
				return clearSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 3), 10f, out var tarCell, out var tarSpawnError) == false)
				return tarSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no tar-cost test zombie."
				};
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					error = "ZombieSpitter.Spawn returned no tar-cost test spitter."
				};
			}

			FilthMaker.TryMakeFilth(tarCell, map, CustomDefs.TarSlime);
			var tarSlime = map.thingGrid.ThingAt<TarSlime>(tarCell);
			if (tarSlime == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					tarCell = ZombieRuntimeActions.DescribeCell(tarCell),
					error = "Could not create TarSlime in the tar-cost test cell."
				};
			}

			if (TryCostToMoveIntoCell(human, clearCell, out var humanClearCost, out var error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(human, tarCell, out var humanTarCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(zombie, clearCell, out var zombieClearCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(zombie, tarCell, out var zombieTarCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(spitter, clearCell, out var spitterClearCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(spitter, tarCell, out var spitterTarCost, out error) == false)
				return new { success = false, error };

			var difficulty = Tools.Difficulty();
			var expectedZombieTarCost = (float)GenMath.LerpDouble(0, 5, 150, 14, difficulty);
			var expectedHumanTarCost = (float)GenMath.LerpDouble(0, 5, 14, 400, difficulty);
			var humanMatchesTarFormula = Mathf.Abs(humanTarCost - expectedHumanTarCost) < 0.001f;
			var zombieMatchesTarFormula = Mathf.Abs(zombieTarCost - expectedZombieTarCost) < 0.001f;
			var spitterMatchesTarFormula = Mathf.Abs(spitterTarCost - expectedZombieTarCost) < 0.001f;
			var clearCostsDifferFromTar = Mathf.Abs(humanClearCost - humanTarCost) > 0.001f
				&& Mathf.Abs(zombieClearCost - zombieTarCost) > 0.001f
				&& Mathf.Abs(spitterClearCost - spitterTarCost) > 0.001f;

			return new
			{
				success = humanMatchesTarFormula
					&& zombieMatchesTarFormula
					&& spitterMatchesTarFormula
					&& clearCostsDifferFromTar,
				destroyedZombies,
				difficulty,
				clearCell = ZombieRuntimeActions.DescribeCell(clearCell),
				tarCell = ZombieRuntimeActions.DescribeCell(tarCell),
				tarSlimeId = ZombieRuntimeActions.StableThingId(tarSlime),
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				spitter = DescribeZombie(spitter),
				humanClearCost,
				humanTarCost,
				zombieClearCost,
				zombieTarCost,
				spitterClearCost,
				spitterTarCost,
				expectedHumanTarCost,
				expectedZombieTarCost,
				humanMatchesTarFormula,
				zombieMatchesTarFormula,
				spitterMatchesTarFormula,
				clearCostsDifferFromTar
			};
		}

		[Tool("zombieland/zombie_blood_filth_contract", Description = "Verify zombie blood filth follows the Zombieland setting and tanky armor suppression while humans still use vanilla blood drops.")]
		public static object ZombieBloodFilthContract()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 10f, out var tankyCell, out var tankySpawnError) == false)
				return tankySpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var tanky = ZombieRuntimeActions.SpawnZombie(tankyCell, map, ZombieType.TankyOperator, true);
			if (zombie == null || tanky == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					tanky = DescribeZombie(tanky),
					error = "ZombieGenerator.SpawnZombie returned no normal or tanky blood-filth test zombie."
				};
			}

			var tankyArmorForced = false;
			if (tanky.hasTankyShield <= 0f && tanky.hasTankySuit <= 0f)
			{
				tanky.hasTankyShield = 1f;
				tankyArmorForced = true;
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					tanky = DescribeZombie(tanky),
					error = "ZombieSpitter.Spawn returned no blood-filth test spitter."
				};
			}

			BloodFilthSnapshot DropBloodSample(Pawn pawn)
			{
				var bloodDef = pawn?.RaceProps?.BloodDef;
				var cell = pawn?.Position ?? IntVec3.Invalid;
				ClearFilthAt(map, cell);
				var before = CountThingsAt(map, cell, bloodDef);
				pawn.health.DropBloodFilth();
				var after = CountThingsAt(map, cell, bloodDef);
				return new BloodFilthSnapshot
					{
						pawn = DescribePawn(pawn),
						bloodDef = bloodDef?.defName,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						before = before,
						after = after,
						delta = after - before
					};
			}

			var originalZombiesDropBlood = ZombieSettings.Values.zombiesDropBlood;
			BloodFilthSnapshot humanEnabled;
			BloodFilthSnapshot zombieEnabled;
			BloodFilthSnapshot tankyEnabled;
			BloodFilthSnapshot spitterEnabled;
			BloodFilthSnapshot humanDisabled;
			BloodFilthSnapshot zombieDisabled;
			BloodFilthSnapshot spitterDisabled;
			try
			{
				ZombieSettings.Values.zombiesDropBlood = true;
				humanEnabled = DropBloodSample(human);
				zombieEnabled = DropBloodSample(zombie);
				tankyEnabled = DropBloodSample(tanky);
				spitterEnabled = DropBloodSample(spitter);

				ZombieSettings.Values.zombiesDropBlood = false;
				humanDisabled = DropBloodSample(human);
				zombieDisabled = DropBloodSample(zombie);
				spitterDisabled = DropBloodSample(spitter);
			}
			finally
			{
				ZombieSettings.Values.zombiesDropBlood = originalZombiesDropBlood;
			}

			var humanEnabledDropsBlood = humanEnabled.delta > 0;
			var zombieEnabledDropsBlood = zombieEnabled.delta > 0;
			var tankyEnabledDropsNoBlood = tankyEnabled.delta == 0;
			var spitterEnabledDropsBlood = spitterEnabled.delta > 0;
			var humanDisabledStillDropsBlood = humanDisabled.delta > 0;
			var zombieDisabledDropsNoBlood = zombieDisabled.delta == 0;
			var spitterDisabledDropsNoBlood = spitterDisabled.delta == 0;

			return new
			{
				success = humanEnabledDropsBlood
					&& zombieEnabledDropsBlood
					&& tankyEnabledDropsNoBlood
					&& spitterEnabledDropsBlood
					&& humanDisabledStillDropsBlood
					&& zombieDisabledDropsNoBlood
					&& spitterDisabledDropsNoBlood,
				destroyedZombies,
				originalZombiesDropBlood,
				tankyArmorForced,
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				tanky = DescribeZombie(tanky),
				tankyArmor = DescribeTankyArmor(tanky),
				spitter = DescribeZombie(spitter),
				humanEnabled,
				zombieEnabled,
				tankyEnabled,
				spitterEnabled,
				humanDisabled,
				zombieDisabled,
				spitterDisabled,
				humanEnabledDropsBlood,
				zombieEnabledDropsBlood,
				tankyEnabledDropsNoBlood,
				spitterEnabledDropsBlood,
				humanDisabledStillDropsBlood,
				zombieDisabledDropsNoBlood,
				spitterDisabledDropsNoBlood
			};
		}

		[Tool("zombieland/tar_slime_fire_spread_contract", Description = "Verify real Fire.DoFireDamage on burning TarSlime raises fire size and ignites adjacent TarSlime.")]
		public static object TarSlimeFireSpreadContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var sourceCell, out var sourceError) == false)
				return sourceError;

			var adjacentCell = GenAdj.AdjacentCellsAround
				.Select(offset => sourceCell + offset)
				.FirstOrDefault(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetThingList(map).Any() == false);
			if (adjacentCell.IsValid == false)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					error = "No clear adjacent TarSlime spread cell was found."
				};
			}

			ClearFilthAt(map, sourceCell);
			ClearFilthAt(map, adjacentCell);
			foreach (var fire in sourceCell.GetThingList(map).OfType<Fire>().Concat(adjacentCell.GetThingList(map).OfType<Fire>()).ToArray())
				fire.Destroy();

			FilthMaker.TryMakeFilth(sourceCell, map, CustomDefs.TarSlime);
			FilthMaker.TryMakeFilth(adjacentCell, map, CustomDefs.TarSlime);
			var sourceTar = map.thingGrid.ThingAt<TarSlime>(sourceCell);
			var adjacentTar = map.thingGrid.ThingAt<TarSlime>(adjacentCell);
			if (sourceTar == null || adjacentTar == null)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
					sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
					adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
					error = "Could not create both TarSlime fixtures."
				};
			}

			FireUtility.TryStartFireIn(sourceCell, map, 0.1f, null);
			var sourceFire = sourceCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (sourceFire == null)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
					sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
					adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
					error = "Could not start a real fire on the source TarSlime cell."
				};
			}

			var fireSizeBefore = sourceFire.fireSize;
			var adjacentBurningBefore = adjacentTar.IsBurning();
			if (TryDoFireDamage(sourceFire, sourceTar, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			var fireSizeAfter = sourceFire.fireSize;
			var adjacentBurningAfter = adjacentTar.IsBurning();
			var adjacentFiresAfter = adjacentCell.GetThingList(map).OfType<Fire>().ToArray();

			return new
			{
				success = adjacentBurningBefore == false
					&& adjacentBurningAfter
					&& fireSizeAfter >= 0.5f
					&& adjacentFiresAfter.Length > 0,
				sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
				adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
				sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
				adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
				sourceFire = ZombieRuntimeActions.StableThingId(sourceFire),
				adjacentFireIds = adjacentFiresAfter.Select(ZombieRuntimeActions.StableThingId).ToArray(),
				fireSizeBefore,
				fireSizeAfter,
				adjacentBurningBefore,
				adjacentBurningAfter,
				adjacentFireCountAfter = adjacentFiresAfter.Length
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

		[Tool("zombieland/double_tap_infected_corpse", Description = "Run the real DoubleTap job on an infected corpse and verify the missing brain prevents corpse conversion.")]
		public static object DoubleTapInfectedCorpse()
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
			var zombieCorpses = map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray();
			foreach (var zombieCorpse in zombieCorpses)
				zombieCorpse.Destroy();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			if (TryFindAdjacentClearCell(actor, out var victimCell) == false
				&& TryFindClearSpawnCell(map, actor.Position, 8f, out victimCell, out var spawnError) == false)
				return spawnError;

			var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(victim, victimCell, map, WipeMode.Vanish);
			if (ZombieRuntimeActions.AddZombieBite(victim, "final", out var bite, out var error) == false)
			{
				return new
				{
					success = false,
					victim = DescribePawn(victim),
					error
				};
			}

			if (ZombieRuntimeActions.KillPawnToCorpse(victim, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					victim = DescribePawn(victim),
					biteLabel = bite.LabelCap,
					error
				};
			}

			var oldHours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, oldHours);
			try
			{
				actor.pather?.StopDead();
				actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);

				var workGiver = new WorkGiver_DoubleTap();
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var job = workGiver.JobOnThing(actor, corpse, true);
				if (hasForcedJob == false || job == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						hasForcedJob,
						jobDef = job?.def?.defName,
						error = "WorkGiver_DoubleTap did not create a forced DoubleTap job."
					};
				}

				var meleeDps = Math.Max(0.1f, actor.GetStatValue(StatDefOf.MeleeDPS, true));
				var maxHitWindows = (int)Math.Ceiling(100f / (meleeDps * 4f)) + 1;
				var maxTicks = 2 + maxHitWindows * 80;
				var samples = new List<object>();
				var brainBefore = corpse.InnerPawn?.health?.hediffSet?.GetBrain()?.def?.defName;
				job.playerForced = true;
				var jobDefName = job.def?.defName;
				actor.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
				var startedJob = actor.CurJobDef?.defName;

				var tickHit = -1;
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var brainMissing = corpse.InnerPawn?.health?.hediffSet?.GetBrain() == null;
					if (tick == 1 || tick == maxTicks || tick % 80 == 0 || brainMissing)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							brainMissing,
							corpseSpawned = corpse.Spawned,
							corpseDestroyed = corpse.Destroyed
						});
					}

					if (brainMissing)
					{
						tickHit = tick;
						break;
					}
				}

				var brainMissingAfter = corpse.InnerPawn?.health?.hediffSet?.GetBrain() == null;
				var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
				var queueCountBeforeRot = queue?.Count ?? -1;
				var queuedBeforeRot = queue?.Contains(corpse) ?? false;
				var rotTriggered = ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error);
				var queueCountAfterRot = queue?.Count ?? -1;
				var queuedAfterRot = queue?.Contains(corpse) ?? false;

				return new
				{
					success = brainBefore != null
						&& brainMissingAfter
						&& tickHit > 0
						&& rotTriggered
						&& queuedBeforeRot == false
						&& queuedAfterRot == false,
					destroyedZombies,
					destroyedZombieCorpses = zombieCorpses.Length,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					corpse = DescribeCorpse(corpse),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					biteLabel = bite.LabelCap,
					restoredHoursAfterDeathToBecomeZombie = oldHours,
					hasForcedJob,
					jobDef = jobDefName,
					startedJob,
					meleeDps,
					maxHitWindows,
					maxTicks,
					tickHit,
					brainBefore,
					brainMissingAfter,
					rotTriggered,
					rotStageBefore = rotStageBefore.ToString(),
					rotStageAfter = rotStageAfter.ToString(),
					rotError = error,
					queueCountBeforeRot,
					queueCountAfterRot,
					queuedBeforeRot,
					queuedAfterRot,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHours;
			}
		}

		[Tool("zombieland/extract_serum_from_zombie_corpse", Description = "Kill a real zombie into a ZombieCorpse, run the ExtractZombieSerum job, and verify extract is produced.")]
		public static object ExtractSerumFromZombieCorpse()
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

			var oldAmount = ZombieSettings.Values.corpsesExtractAmount;
			ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, oldAmount);
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var existingCorpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					existingCorpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
				DisablePawnWork(actor);

				if (TryFindAdjacentClearCell(actor, out var zombieCell) == false
					&& TryFindClearSpawnCell(map, actor.Position, 8f, out zombieCell, out var zombieSpawnError) == false)
					return zombieSpawnError;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.Kill(null);
				var corpse = zombie.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
				if (corpse == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "Killing the zombie did not leave a ZombieCorpse."
					};
				}

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.allZombieCorpses?.Contains(corpse) == false)
					tickManager.allZombieCorpses.Add(corpse);

				var workGiver = new WorkGiver_ExtractZombieSerum();
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var job = workGiver.JobOnThing(actor, corpse, true);
				if (hasForcedJob == false || job == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						hasForcedJob,
						jobDef = job?.def?.defName,
						error = "WorkGiver_ExtractZombieSerum did not create a forced extract job."
					};
				}

				var extractBefore = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
				var tendSpeed = Math.Max(0.1f, actor.GetStatValue(StatDefOf.MedicalTendSpeed, true));
				var maxTicks = 120 + (int)Math.Ceiling(100f / (tendSpeed / 2f));
				var samples = new List<object>();
				job.playerForced = true;
				var jobDefName = job.def?.defName;
				actor.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
				var startedJob = actor.CurJobDef?.defName;
				var tickHit = -1;

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var extractNow = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
					var corpseGone = corpse.Destroyed || corpse.Spawned == false;
					if (tick == 1 || tick == maxTicks || tick % 80 == 0 || corpseGone || extractNow > extractBefore)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							corpseGone,
							extractNow
						});
					}

					if (corpseGone && extractNow > extractBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var extractAfter = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
				var corpseDestroyed = corpse.Destroyed || corpse.Spawned == false;

				return new
				{
					success = corpseDestroyed && extractAfter > extractBefore && tickHit > 0,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					corpse = DescribeCorpse(corpse),
					restoredCorpsesExtractAmount = oldAmount,
					hasForcedJob,
					jobDef = jobDefName,
					startedJob,
					tendSpeed,
					maxTicks,
					tickHit,
					extractBefore,
					extractAfter,
					extractDelta = extractAfter - extractBefore,
					expectedExtractPerZombie = Tools.ExtractPerZombie(),
					corpseDestroyed,
					trackedCorpseCount = tickManager?.allZombieCorpses?.Count ?? -1,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.corpsesExtractAmount = oldAmount;
			}
		}

		[Tool("zombieland/zombie_extract_filter_visibility", Description = "Verify the broad zombie ThingFilter patch still allows zombie extract and serum defs while blocking actual zombie defs.")]
		public static object ZombieExtractFilterVisibility()
		{
			var serumDef = DefDatabase<ThingDef>.GetNamed("ZombieSerumSimple", false);
			if (serumDef == null)
			{
				return new
				{
					success = false,
					error = "ZombieSerumSimple def was not loaded."
				};
			}

			var filter = new ThingFilter();
			filter.SetAllow(CustomDefs.ZombieExtract, true);
			filter.SetAllow(serumDef, true);
			filter.SetAllow(CustomDefs.Corpse_Zombie, true);
			filter.SetAllow(CustomDefs.Zombie, true);
			var allowedDefs = filter.AllowedThingDefs.ToHashSet();
			var extractAllowed = allowedDefs.Contains(CustomDefs.ZombieExtract);
			var serumAllowed = allowedDefs.Contains(serumDef);
			var zombieCorpseAllowed = allowedDefs.Contains(CustomDefs.Corpse_Zombie);
			var zombiePawnAllowed = allowedDefs.Contains(CustomDefs.Zombie);

			var extractThing = ThingMaker.MakeThing(CustomDefs.ZombieExtract);
			var serumFilterWorker = new ZombieSerumFilterWorker();
			var extractExcludedBySerumFilter = serumFilterWorker.Matches(extractThing);

			return new
			{
				success = extractAllowed
					&& serumAllowed
					&& zombieCorpseAllowed == false
					&& zombiePawnAllowed == false
					&& extractExcludedBySerumFilter == false,
				extract = new
				{
					defName = CustomDefs.ZombieExtract.defName,
					allowed = extractAllowed,
					excludedBySerumFilter = extractExcludedBySerumFilter
				},
				serum = new
				{
					defName = serumDef.defName,
					allowed = serumAllowed
				},
				blockedZombieDefs = new
				{
					corpse = new
					{
						defName = CustomDefs.Corpse_Zombie.defName,
						allowed = zombieCorpseAllowed
					},
					pawn = new
					{
						defName = CustomDefs.Zombie.defName,
						allowed = zombiePawnAllowed
					}
				}
			};
		}

		[Tool("zombieland/rope_zombie_job", Description = "Run the real RopeZombie job from a colonist to a live zombie and verify the zombie becomes roped.")]
		public static object RopeZombieJob()
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

			if (TryFindAdjacentClearCell(actor, out var zombieCell) == false
				&& TryFindClearSpawnCell(map, actor.Position, 8f, out zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}

			var job = JobMaker.MakeJob(CustomDefs.RopeZombie, zombie);
			job.playerForced = true;
			var canReserveAndReach = actor.CanReach(zombie, PathEndMode.Touch, Danger.Deadly)
				&& zombie.ropedBy == null;
			actor.drafter.Drafted = true;
			_ = actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var startedJob = actor.CurJobDef?.defName;
			var maxTicks = 180;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var roped = ReferenceEquals(zombie.ropedBy, actor);
				if (tick == 1 || tick == maxTicks || tick % 30 == 0 || roped)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						zombieRopedBy = zombie.ropedBy?.ThingID,
						zombie.IsRopedOrConfused
					});
				}

				if (roped)
				{
					tickHit = tick;
					break;
				}
			}

			return new
			{
				success = canReserveAndReach && tickHit > 0 && ReferenceEquals(zombie.ropedBy, actor) && zombie.IsRopedOrConfused,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				canReserveAndReach,
				startedJob,
				maxTicks,
				tickHit,
				ropedBy = zombie.ropedBy?.ThingID,
				isRopedOrConfused = zombie.IsRopedOrConfused,
				samples
			};
		}

		[Tool("zombieland/flee_ignores_harmless_zombies", Description = "Call RimWorld FleeUtility.ShouldFleeFrom for real zombies and verify roped/confused/electrical/albino zombies are not flee threats.")]
		public static object FleeIgnoresHarmlessZombies()
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
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);

			var zombieCells = GenRadial.RadialCellsAround(actorCell, 7f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(actorCell) <= 7.5f)
				.Where(cell => cell != actorCell)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Take(5)
				.ToArray();
			if (zombieCells.Length < 5)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "Could not find enough nearby cells for flee-threat zombies."
				};
			}

			var normal = ZombieRuntimeActions.SpawnZombie(zombieCells[0], map, ZombieType.Normal, true);
			var roped = ZombieRuntimeActions.SpawnZombie(zombieCells[1], map, ZombieType.Normal, true);
			var confused = ZombieRuntimeActions.SpawnZombie(zombieCells[2], map, ZombieType.Normal, true);
			var electrifier = ZombieRuntimeActions.SpawnZombie(zombieCells[3], map, ZombieType.Electrifier, true);
			var albino = ZombieRuntimeActions.SpawnZombie(zombieCells[4], map, ZombieType.Albino, true);

			if (normal == null || roped == null || confused == null || electrifier == null || albino == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no zombie for one or more flee-threat cases."
				};
			}

			roped.ropedBy = actor;
			confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
			electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;

			var normalThreat = FleeUtility.ShouldFleeFrom(normal, actor, true, false);
			var ropedThreat = FleeUtility.ShouldFleeFrom(roped, actor, true, false);
			var confusedThreat = FleeUtility.ShouldFleeFrom(confused, actor, true, false);
			var electrifierThreat = FleeUtility.ShouldFleeFrom(electrifier, actor, true, false);
			var albinoThreat = FleeUtility.ShouldFleeFrom(albino, actor, true, false);

			return new
			{
				success = normalThreat
					&& ropedThreat == false
					&& confusedThreat == false
					&& electrifierThreat == false
					&& albinoThreat == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				normal = DescribeZombie(normal),
				roped = DescribeZombie(roped),
				confused = DescribeZombie(confused),
				electrifier = DescribeZombie(electrifier),
				albino = DescribeZombie(albino),
				threats = new
				{
					normal = normalThreat,
					roped = ropedThreat,
					confused = confusedThreat,
					electrifier = electrifierThreat,
					albino = albinoThreat
				},
				seesAsThreat = new
				{
					normal = actor.SeesZombieAsThreat(normal),
					roped = actor.SeesZombieAsThreat(roped),
					confused = actor.SeesZombieAsThreat(confused),
					electrifier = actor.SeesZombieAsThreat(electrifier),
					albino = actor.SeesZombieAsThreat(albino)
				}
			};
		}

		[Tool("zombieland/colonist_avoidance_interrupts_job", Description = "Build a real avoid grid around a zombie and verify a non-forced colonist job is interrupted into a Flee job.")]
		public static object ColonistAvoidanceInterruptsJob()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var zombieCell = GenRadial.RadialCellsAround(actorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No nearby clear zombie cell was found."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var actorAvoidCost = AvoidCost(avoidGrid, map, actor.Position);
				var inAvoidDangerBefore = avoidGrid.InAvoidDanger(actor);
				var safeCells = GenRadial.RadialCellsAround(actor.Position, 8f, true)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => avoidGrid.ShouldAvoid(map, cell) == false)
					.Take(8)
					.Select(ZombieRuntimeActions.DescribeCell)
					.ToArray();

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = actor.CurJobDef?.defName;
				var samples = new List<object>();
				var tickHit = -1;
				const int maxTicks = 30;

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var currentJob = actor.CurJob;
					if (tick == 1 || tick == maxTicks || currentJob?.def == JobDefOf.Flee)
					{
						samples.Add(new
						{
							tick,
							job = actor.CurJobDef?.defName,
							currentJob?.playerForced,
							target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
						});
					}

					if (currentJob?.def == JobDefOf.Flee)
					{
						tickHit = tick;
						break;
					}
				}

				var fleeJob = actor.CurJob;
				var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
				var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;

				return new
				{
					success = inAvoidDangerBefore
						&& startedJob == JobDefOf.Wait_Combat.defName
						&& tickHit > 0
						&& fleeJob?.playerForced == true
						&& fleeDestinationAvoids,
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					startedJob,
					inAvoidDangerBefore,
					actorAvoidCost,
					safeCells,
					tickHit,
					maxTicks,
					fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
					fleeDestinationAvoids,
					finalJob = actor.CurJobDef?.defName,
					finalJobPlayerForced = actor.CurJob?.playerForced,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/workgiver_respects_avoid_grid", Description = "Verify a non-forced DoubleTap workgiver rejects an infected corpse in avoid danger while a forced command still creates the job.")]
		public static object WorkgiverRespectsAvoidGrid()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldHours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ZombieSettings.Values.betterZombieAvoidance = true;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, oldHours);
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var zombieCorpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					zombieCorpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoDoubleTap = true;

				var victimCell = GenRadial.RadialCellsAround(actor.Position, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.DistanceTo(actor.Position) >= 10f)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (victimCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No distant victim cell was found for the avoid-grid workgiver fixture."
					};
				}

				var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(victim, victimCell, map, WipeMode.Vanish);
				if (ZombieRuntimeActions.AddZombieBite(victim, "final", out var bite, out var error) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						error
					};
				}

				if (ZombieRuntimeActions.KillPawnToCorpse(victim, out var corpse, out error) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						biteLabel = bite.LabelCap,
						error
					};
				}

				var zombieCell = GenRadial.RadialCellsAround(corpse.Position, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(corpse.Position))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						error = "No nearby zombie cell was found for the avoid-grid workgiver fixture."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}

				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var targetAvoidCost = AvoidCost(avoidGrid, map, corpse.Position);
				var targetShouldAvoid = avoidGrid.ShouldAvoid(map, corpse.Position);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);

				var workGiver = new WorkGiver_DoubleTap();
				var hasUnforcedJob = workGiver.HasJobOnThing(actor, corpse, false);
				var unforcedJob = hasUnforcedJob ? workGiver.JobOnThing(actor, corpse, false) : null;
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var forcedJob = workGiver.JobOnThing(actor, corpse, true);

				return new
				{
					success = targetShouldAvoid
						&& actorShouldAvoid == false
						&& hasUnforcedJob == false
						&& unforcedJob == null
						&& hasForcedJob
						&& forcedJob?.def == CustomDefs.DoubleTap,
					destroyedZombies,
					actor = DescribePawn(actor),
					corpse = DescribeCorpse(corpse),
					zombie = DescribeZombie(zombie),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					targetAvoidCost,
					targetShouldAvoid,
					actorShouldAvoid,
					hasUnforcedJob,
					unforcedJobDef = unforcedJob?.def?.defName,
					hasForcedJob,
					forcedJobDef = forcedJob?.def?.defName
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHours;
			}
		}

		[Tool("zombieland/avoid_grid_blocks_door_and_danger", Description = "Verify avoid-grid danger affects vanilla door and danger checks for normal colonist behavior but not drafted or player-forced commands.")]
		public static object AvoidGridBlocksDoorAndDanger()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var doorCell = GenRadial.RadialCellsAround(actor.Position, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetEdifice(map) == null)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 10f)
					.OrderBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (doorCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No distant clear door cell was found for the avoid-grid fixture."
					};
				}

				var zombieCell = GenRadial.RadialCellsAround(doorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell != doorCell)
					.OrderBy(cell => cell.DistanceToSquared(doorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "No nearby zombie cell was found for the avoid-grid door fixture."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}
				zombie.state = ZombieState.Tracking;

				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var doorAvoidCost = AvoidCost(avoidGrid, map, doorCell);
				var doorShouldAvoid = avoidGrid.ShouldAvoid(map, doorCell);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "Could not create test door."
					};
				}
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				actor.drafter.Drafted = false;
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
				var normalDoorCanOpen = door.PawnCanOpen(actor);
				var normalDanger = doorCell.GetDangerFor(actor, map);

				actor.drafter.Drafted = true;
				var draftedDoorCanOpen = door.PawnCanOpen(actor);
				actor.drafter.Drafted = false;

				var forcedWait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				forcedWait.playerForced = true;
				actor.jobs.StartJob(forcedWait, JobCondition.InterruptForced, null, false, true);
				var forcedDoorCanOpen = door.PawnCanOpen(actor);
				var forcedDanger = doorCell.GetDangerFor(actor, map);

				return new
				{
					success = doorShouldAvoid
						&& actorShouldAvoid == false
						&& normalDoorCanOpen == false
						&& normalDanger == Danger.Deadly
						&& draftedDoorCanOpen
						&& forcedDoorCanOpen
						&& forcedDanger != Danger.Deadly,
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						defName = door.def?.defName,
						faction = door.Faction?.Name,
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						freePassage = door.FreePassage
					},
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					doorAvoidCost,
					doorShouldAvoid,
					actorShouldAvoid,
					normalDoorCanOpen,
					normalDanger = normalDanger.ToString(),
					draftedDoorCanOpen,
					forcedDoorCanOpen,
					forcedDanger = forcedDanger.ToString(),
					forcedJob = actor.CurJobDef?.defName,
					forcedJobPlayerForced = actor.CurJob?.playerForced
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/avoid_grid_interrupts_existing_path", Description = "Verify an already-started colonist path asks for a new path when its source-derived lookahead cell becomes zombie avoid danger.")]
		public static object AvoidGridInterruptsExistingPath()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var destination = GenRadial.RadialCellsAround(actor.Position, 18f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 14f)
					.Where(cell => actor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
					.OrderByDescending(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (destination.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No reachable distant destination was found for the avoid-grid path fixture."
					};
				}

				var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, destination);
				gotoJob.playerForced = false;
				var startedJob = actor.jobs.TryTakeOrderedJob(gotoJob, JobTag.Misc, false);
				if (startedJob == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						error = "Could not start the real Goto job for the avoid-grid path fixture."
					};
				}

				const int maxPathTicks = 60;
				var pathReadyTick = -1;
				for (var tick = 0; tick <= maxPathTicks; tick++)
				{
					if (actor.pather.curPath?.Found == true && actor.pather.curPath.NodesLeftCount >= 6)
					{
						pathReadyTick = tick;
						break;
					}
					AdvanceGameTicks(1);
				}

				var path = actor.pather.curPath;
				if (path?.Found != true || path.NodesLeftCount < 6)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						pathReadyTick,
						nodesLeft = path?.NodesLeftCount ?? 0,
						error = "Pawn path did not become available with enough nodes for the lookahead fixture."
					};
				}

				var lookAhead = path.Peek(4);
				var lastNode = path.LastNode;
				if ((lookAhead - lastNode).LengthHorizontalSquared < 25)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						lastNode = ZombieRuntimeActions.DescribeCell(lastNode),
						nodesLeft = path.NodesLeftCount,
						error = "Source-derived lookahead cell was too close to destination for the NeedNewPath patch."
					};
				}

				var needNewPathBefore = actor.pather.NeedNewPath();
				var pathCells = Enumerable.Range(0, path.NodesLeftCount)
					.Select(path.Peek)
					.ToHashSet();
				var zombieCell = GenRadial.RadialCellsAround(lookAhead, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => pathCells.Contains(cell) == false)
					.OrderBy(cell => cell.DistanceToSquared(lookAhead))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						nodesLeft = path.NodesLeftCount,
						needNewPathBefore,
						error = "No off-path zombie cell was found near the lookahead cell."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}
				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var lookAheadAvoidCost = AvoidCost(avoidGrid, map, lookAhead);
				var lookAheadShouldAvoid = avoidGrid.ShouldAvoid(map, lookAhead);
				var needNewPathAfter = actor.pather.NeedNewPath();

				return new
				{
					success = needNewPathBefore == false
						&& lookAheadShouldAvoid
						&& needNewPathAfter,
					destroyedZombies,
					startedJob,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					destination = ZombieRuntimeActions.DescribeCell(destination),
					lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					lastNode = ZombieRuntimeActions.DescribeCell(lastNode),
					pathReadyTick,
					nodesLeft = path.NodesLeftCount,
					lookAheadAvoidCost,
					lookAheadShouldAvoid,
					needNewPathBefore,
					needNewPathAfter
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/avoid_grid_costs_route_new_path", Description = "Verify a new RimWorld 1.6 path request uses Zombieland avoid-grid costs and routes through fewer zombie-danger cells.")]
		public static object AvoidGridCostsRouteNewPath()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = false;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = false;

				var destination = GenRadial.RadialCellsAround(actor.Position, 22f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 16f)
					.Where(cell => actor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
					.OrderByDescending(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (destination.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No reachable distant destination was found for the avoid-grid route fixture."
					};
				}

				var baselinePath = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
				var baselineCells = DescribePathCells(baselinePath);
				if (baselinePath?.Found != true || baselineCells.Length < 10)
				{
					baselinePath?.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						baselinePathFound = baselinePath?.Found ?? false,
						baselineCells = baselineCells.Length,
						error = "Baseline path did not become available with enough cells for the avoid-grid route fixture."
					};
				}

				var zombieCell = baselineCells
					.Skip(Math.Max(2, baselineCells.Length / 3))
					.Take(Math.Max(1, baselineCells.Length / 3))
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 6f)
					.Where(cell => cell.DistanceTo(destination) >= 6f)
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					baselinePath.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						baselineCells = baselineCells.Length,
						error = "No usable zombie cell was found on the baseline path."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					baselinePath.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid route zombie."
					};
				}
				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var baselineAvoidCells = baselineCells.Count(cell => avoidGrid.ShouldAvoid(map, cell));
				var baselineAvoidCost = baselineCells.Sum(cell => AvoidCost(avoidGrid, map, cell));

				ZombieSettings.Values.betterZombieAvoidance = true;
				if (config != null)
					config.autoAvoidZombies = true;

				var avoidedPath = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
				var avoidedCells = DescribePathCells(avoidedPath);
				var avoidedAvoidCells = avoidedCells.Count(cell => avoidGrid.ShouldAvoid(map, cell));
				var avoidedAvoidCost = avoidedCells.Sum(cell => AvoidCost(avoidGrid, map, cell));
				var avoidedPathFound = avoidedPath?.Found == true;
				baselinePath.ReleaseToPool();
				avoidedPath?.ReleaseToPool();

				return new
				{
					success = avoidedPathFound
						&& baselineAvoidCells > 0
						&& avoidedAvoidCells < baselineAvoidCells
						&& avoidedAvoidCost < baselineAvoidCost,
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					destination = ZombieRuntimeActions.DescribeCell(destination),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					baseline = new
					{
						pathFound = true,
						cellCount = baselineCells.Length,
						avoidCells = baselineAvoidCells,
						avoidCost = baselineAvoidCost
					},
					avoided = new
					{
						pathFound = avoidedPathFound,
						cellCount = avoidedCells.Length,
						avoidCells = avoidedAvoidCells,
						avoidCost = avoidedAvoidCost
					}
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/zombie_manual_door_close_ignored", Description = "Verify a zombie cannot manually schedule a door to close while a normal colonist still can.")]
		public static object ZombieManualDoorCloseIgnored()
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

			var doorCell = GenRadial.RadialCellsAround(actorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (doorCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear door cell was found for the zombie manual-close fixture."
				};
			}

			var zombieCell = GenRadial.RadialCellsAround(doorCell, 3f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell != doorCell)
				.OrderBy(cell => cell.DistanceToSquared(doorCell))
				.FirstOrDefault();
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					error = "No nearby zombie cell was found for the zombie manual-close fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no door-close zombie."
				};
			}

			var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			if (door == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "Could not create test door."
				};
			}
			GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
			door.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			door.StartManualOpenBy(actor);

			var ticksUntilCloseField = typeof(Building_Door).GetField("ticksUntilClose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (ticksUntilCloseField == null)
			{
				return new
				{
					success = false,
					door = ZombieRuntimeActions.StableThingId(door),
					error = "Could not access Building_Door.ticksUntilClose."
				};
			}

			const int sentinelTicksUntilClose = 12345;
			ticksUntilCloseField.SetValue(door, sentinelTicksUntilClose);
			door.StartManualCloseBy(zombie);
			var ticksAfterZombie = (int)ticksUntilCloseField.GetValue(door);
			door.StartManualCloseBy(actor);
			var ticksAfterActor = (int)ticksUntilCloseField.GetValue(door);

			return new
			{
				success = door.Open
					&& ticksAfterZombie == sentinelTicksUntilClose
					&& ticksAfterActor != sentinelTicksUntilClose,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				door = new
				{
					id = ZombieRuntimeActions.StableThingId(door),
					defName = door.def?.defName,
					faction = door.Faction?.Name,
					position = ZombieRuntimeActions.DescribeCell(door.Position),
					door.Open
				},
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				sentinelTicksUntilClose,
				ticksAfterZombie,
				ticksAfterActor
			};
		}

		[Tool("zombieland/albino_does_not_hold_door_open", Description = "Verify an albino zombie in an open door does not reset the auto-close delay while a normal zombie still does.")]
		public static object AlbinoDoesNotHoldDoorOpen()
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

			var ticksUntilCloseField = typeof(Building_Door).GetField("ticksUntilClose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (ticksUntilCloseField == null)
			{
				return new
				{
					success = false,
					error = "Could not access Building_Door.ticksUntilClose."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalDoorCell, out var spawnError) == false)
				return spawnError;

			var albinoDoorCell = GenRadial.RadialCellsAround(normalDoorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(normalDoorCell) >= 2f)
				.OrderBy(cell => cell.DistanceToSquared(normalDoorCell))
				.FirstOrDefault();
			if (albinoDoorCell.IsValid == false)
			{
				return new
				{
					success = false,
					normalDoorCell = ZombieRuntimeActions.DescribeCell(normalDoorCell),
					error = "No second clear door cell was found for the albino door fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var actorCell = GenRadial.RadialCellsAround(normalDoorCell, 4f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderByDescending(cell => cell.DistanceToSquared(normalDoorCell))
				.FirstOrDefault();
			if (actorCell.IsValid == false)
				actorCell = normalDoorCell;
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);

			var normalDoor = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			var albinoDoor = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			if (normalDoor == null || albinoDoor == null)
			{
				return new
				{
					success = false,
					error = "Could not create one or both test doors."
				};
			}
			GenSpawn.Spawn(normalDoor, normalDoorCell, map, WipeMode.Vanish);
			GenSpawn.Spawn(albinoDoor, albinoDoorCell, map, WipeMode.Vanish);
			normalDoor.SetFaction(Faction.OfPlayer);
			albinoDoor.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			normalDoor.StartManualOpenBy(actor);
			albinoDoor.StartManualOpenBy(actor);

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalDoorCell, map, ZombieType.Normal, true);
			var albinoZombie = ZombieRuntimeActions.SpawnZombie(albinoDoorCell, map, ZombieType.Albino, true);
			if (normalZombie == null || albinoZombie == null)
			{
				return new
				{
					success = false,
					normalDoorCell = ZombieRuntimeActions.DescribeCell(normalDoorCell),
					albinoDoorCell = ZombieRuntimeActions.DescribeCell(albinoDoorCell),
					error = "ZombieGenerator.SpawnZombie returned no normal or albino test zombie."
				};
			}

			const int initialTicksUntilClose = 10;
			ticksUntilCloseField.SetValue(normalDoor, initialTicksUntilClose);
			ticksUntilCloseField.SetValue(albinoDoor, initialTicksUntilClose);
			AdvanceGameTicks(1);
			var normalTicksAfter = (int)ticksUntilCloseField.GetValue(normalDoor);
			var albinoTicksAfter = (int)ticksUntilCloseField.GetValue(albinoDoor);

			return new
			{
				success = normalDoor.Open
					&& albinoDoor.Open
					&& normalTicksAfter > initialTicksUntilClose
					&& albinoTicksAfter == initialTicksUntilClose - 1,
				destroyedZombies,
				actor = DescribePawn(actor),
				normalZombie = DescribeZombie(normalZombie),
				albinoZombie = DescribeZombie(albinoZombie),
				normalDoor = new
				{
					id = ZombieRuntimeActions.StableThingId(normalDoor),
					position = ZombieRuntimeActions.DescribeCell(normalDoor.Position),
					normalDoor.Open
				},
				albinoDoor = new
				{
					id = ZombieRuntimeActions.StableThingId(albinoDoor),
					position = ZombieRuntimeActions.DescribeCell(albinoDoor.Position),
					albinoDoor.Open
				},
				initialTicksUntilClose,
				normalTicksAfter,
				albinoTicksAfter
			};
		}

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

		static object DescribeVector(Vector3 vector)
		{
			return new
			{
				x = vector.x,
				y = vector.y,
				z = vector.z
			};
		}

	}
}
