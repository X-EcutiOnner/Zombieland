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

		sealed class DamageLogSnapshot
		{
			public object pawn;
			public float damageAmount;
			public int beforeHediffCount;
			public int afterHediffCount;
			public float damageTotal;
			public int resultPartCount;
			public int resultHediffCount;
			public int combatTextBefore;
			public int combatTextAfter;
			public int combatTextDelta;
		}

		sealed class ContaminationSnapshot
		{
			public float stored;
			public bool hasNeed;
			public float? needLevel;
			public bool hasHediff;
			public float? hediffSeverity;
			public float effectiveness;
			public int hediffCount;
		}

		sealed class FireDamageSample
		{
			public string kind;
			public int seed;
			public bool burnLonger;
			public float injuryBefore;
			public float injuryAfter;
			public float injuryDelta;
			public bool deadAfter;
			public object pawn;
			public string error;
		}

		sealed class FireDamageComparison
		{
			public string kind;
			public float disabledTotal;
			public float enabledTotal;
			public float delta;
			public float[] disabledDeltas;
			public float[] enabledDeltas;
			public int disabledDeadCount;
			public int enabledDeadCount;
			public string[] errors;
		}

		sealed class WallPushFixture
		{
			public Zombie zombie;
			public Building wall;
			public IntVec3 zombieCell;
			public IntVec3 wallCell;
			public IntVec3 destinationCell;
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
		static readonly MethodInfo fireDoComplexCalcsMethod = typeof(Fire).GetMethod("DoComplexCalcs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo fireVulnerableToRainMethod = typeof(Fire).GetMethod("VulnerableToRain", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo fireWatcherUpdateObservationsMethod = typeof(FireWatcher).GetMethod("UpdateObservations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo meleeDamageInfosToApplyMethod = typeof(Verb_MeleeAttackDamage).GetMethod("DamageInfosToApply", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

		static bool TryDoFireComplexCalcs(Fire fire, out string error)
		{
			error = null;
			if (fireDoComplexCalcsMethod == null)
			{
				error = "Could not find Fire.DoComplexCalcs().";
				return false;
			}
			if (fire == null)
			{
				error = "Fire is required.";
				return false;
			}

			fireDoComplexCalcsMethod.Invoke(fire, null);
			return true;
		}

		static bool TryVulnerableToRain(Fire fire, out bool vulnerable, out string error)
		{
			vulnerable = false;
			error = null;
			if (fireVulnerableToRainMethod == null)
			{
				error = "Could not find Fire.VulnerableToRain().";
				return false;
			}
			if (fire == null)
			{
				error = "Fire is required.";
				return false;
			}

			vulnerable = (bool)fireVulnerableToRainMethod.Invoke(fire, null);
			return true;
		}

		static bool TryMeleeDamageInfosToApply(Verb verb, LocalTargetInfo target, out DamageInfo[] damageInfos, out string error)
		{
			damageInfos = Array.Empty<DamageInfo>();
			error = null;
			if (meleeDamageInfosToApplyMethod == null)
			{
				error = "Could not find Verb_MeleeAttackDamage.DamageInfosToApply(LocalTargetInfo).";
				return false;
			}
			if (verb is not Verb_MeleeAttackDamage)
			{
				error = $"Verb is not Verb_MeleeAttackDamage: {verb?.GetType().Name ?? "null"}.";
				return false;
			}

			try
			{
				damageInfos = ((IEnumerable<DamageInfo>)meleeDamageInfosToApplyMethod.Invoke(verb, new object[] { target })).ToArray();
				return true;
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
		}

		static bool TrySampleRainVulnerability(Fire fire, int samples, int seed, out int trueCount, out int falseCount, out string error)
		{
			trueCount = 0;
			falseCount = 0;
			error = null;

			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < samples; i++)
				{
					if (TryVulnerableToRain(fire, out var vulnerable, out error) == false)
						return false;
					if (vulnerable)
						trueCount++;
					else
						falseCount++;
				}
			}
			finally
			{
				Rand.PopState();
			}

			return true;
		}

		static float TotalInjurySeverity(Pawn pawn)
		{
			return pawn?.health?.hediffSet?.hediffs?
				.OfType<Hediff_Injury>()
				.Sum(hediff => hediff.Severity) ?? 0f;
		}

		static Pawn SpawnFireFixturePawn(Map map, IntVec3 cell, string kind)
		{
			if (kind == "human")
			{
				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, cell, map, Rot4.South);
				DisablePawnWork(human);
				return human;
			}

			if (kind == "normal")
				return ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);

			if (kind == "spitter")
			{
				var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
				GenSpawn.Spawn(spitter, cell, map, Rot4.South, WipeMode.Vanish, false);
				return spitter;
			}

			if (kind == "blob")
			{
				var blob = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieBlob, Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
				GenSpawn.Spawn(blob, cell, map, Rot4.South, WipeMode.Vanish, false);
				return blob;
			}

			return null;
		}

		static void NormalizeFireDamagePawn(Pawn pawn)
		{
			if (pawn == null)
				return;
			pawn.apparel?.DestroyAll();
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			foreach (var hediff in pawn.health?.hediffSet?.hediffs?.ToArray() ?? Array.Empty<Hediff>())
				pawn.health.RemoveHediff(hediff);
		}

		static FireDamageSample SampleFireDamage(Map map, Fire fire, IntVec3 cell, string kind, bool burnLonger, int seed)
		{
			Pawn pawn = null;
			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			try
			{
				foreach (var existingPawn in cell.GetThingList(map).OfType<Pawn>().ToArray())
					existingPawn.Destroy(DestroyMode.Vanish);

				pawn = SpawnFireFixturePawn(map, cell, kind);
				if (pawn == null)
				{
					return new FireDamageSample
					{
						kind = kind,
						seed = seed,
						burnLonger = burnLonger,
						error = $"Could not spawn {kind} fire-damage fixture pawn."
					};
				}

				NormalizeFireDamagePawn(pawn);
				var before = TotalInjurySeverity(pawn);
				ZombieSettings.Values.zombiesBurnLonger = burnLonger;
				Rand.PushState(seed);
				try
				{
					if (TryDoFireDamage(fire, pawn, out var error) == false)
					{
						return new FireDamageSample
						{
							kind = kind,
							seed = seed,
							burnLonger = burnLonger,
							injuryBefore = before,
							injuryAfter = TotalInjurySeverity(pawn),
							injuryDelta = TotalInjurySeverity(pawn) - before,
							deadAfter = pawn.Dead,
							pawn = DescribePawn(pawn),
							error = error
						};
					}
				}
				finally
				{
					Rand.PopState();
				}

				var after = TotalInjurySeverity(pawn);
				return new FireDamageSample
				{
					kind = kind,
					seed = seed,
					burnLonger = burnLonger,
					injuryBefore = before,
					injuryAfter = after,
					injuryDelta = after - before,
					deadAfter = pawn.Dead,
					pawn = DescribePawn(pawn)
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
				if (pawn != null && pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static FireDamageComparison CompareFireDamage(Map map, Fire fire, IntVec3 cell, string kind, int samples, int seed)
		{
			var disabled = new FireDamageSample[samples];
			var enabled = new FireDamageSample[samples];
			for (var i = 0; i < samples; i++)
			{
				var sampleSeed = seed + i;
				disabled[i] = SampleFireDamage(map, fire, cell, kind, false, sampleSeed);
				enabled[i] = SampleFireDamage(map, fire, cell, kind, true, sampleSeed);
			}

			var disabledTotal = disabled.Sum(sample => sample.injuryDelta);
			var enabledTotal = enabled.Sum(sample => sample.injuryDelta);
			return new FireDamageComparison
			{
				kind = kind,
				disabledTotal = disabledTotal,
				enabledTotal = enabledTotal,
				delta = disabledTotal - enabledTotal,
				disabledDeltas = disabled.Select(sample => sample.injuryDelta).ToArray(),
				enabledDeltas = enabled.Select(sample => sample.injuryDelta).ToArray(),
				disabledDeadCount = disabled.Count(sample => sample.deadAfter),
				enabledDeadCount = enabled.Count(sample => sample.deadAfter),
				errors = disabled.Concat(enabled)
					.Where(sample => sample.error != null)
					.Select(sample => $"{sample.kind}:{sample.seed}:{sample.burnLonger}:{sample.error}")
					.ToArray()
			};
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
				bombWillGoOff = zombie?.bombWillGoOff,
				bombTickingInterval = zombie?.bombTickingInterval,
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

		static object DescribeVerb(Verb verb)
		{
			var damageDef = verb?.GetDamageDef();
			return new
			{
				isNull = verb == null,
				label = verb?.verbProps?.label,
				verbClass = verb?.GetType().Name,
				isMelee = verb?.IsMeleeAttack ?? false,
				damageDef = damageDef?.defName,
				damageIsRanged = damageDef?.isRanged,
				canHarmElectric = verb.CanHarmElectricZombies()
			};
		}

		static object[] DescribeDamageInfos(IEnumerable<DamageInfo> damageInfos)
		{
			return damageInfos
				.Select(info => new
				{
					def = info.Def?.defName,
					amount = info.Amount,
					weapon = info.Weapon?.defName,
					instigator = ZombieRuntimeActions.StableThingId(info.Instigator as Thing)
				})
				.Cast<object>()
				.ToArray();
		}

		static (bool value, string error) TryHostileTo(Thing a, Thing b)
		{
			try
			{
				return (GenHostility.HostileTo(a, b), null);
			}
			catch (Exception ex)
			{
				return (false, $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		static (bool value, string error) TryHostileTo(Thing thing, Faction faction)
		{
			try
			{
				return (GenHostility.HostileTo(thing, faction), null);
			}
			catch (Exception ex)
			{
				return (false, $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		static object DescribeHostility((bool value, string error) sample)
		{
			return new
			{
				value = sample.value,
				error = sample.error
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

		static DamageLogSnapshot DamageAndAssociateWithLog(Pawn pawn, float amount)
		{
			var beforeHediffCount = pawn?.health?.hediffSet?.hediffs?.Count ?? 0;
			var part = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(record => record.def.alive)
				?? pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
			var hediff = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
			hediff.Severity = amount;
			pawn.health.AddHediff(hediff, part);

			var result = new DamageWorker.DamageResult
			{
				hitThing = pawn,
				totalDamageDealt = amount
			};
			result.AddPart(pawn, part);
			result.AddHediff(hediff);

			var combatTextBefore = string.IsNullOrEmpty(hediff.combatLogText) ? 0 : 1;
			var log = new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_Fire);
			result.AssociateWithLog(log);
			var combatTextAfter = string.IsNullOrEmpty(hediff.combatLogText) ? 0 : 1;

			return new DamageLogSnapshot
			{
				pawn = DescribePawn(pawn),
				damageAmount = amount,
				beforeHediffCount = beforeHediffCount,
				afterHediffCount = pawn?.health?.hediffSet?.hediffs?.Count ?? 0,
				damageTotal = result.totalDamageDealt,
				resultPartCount = result.parts?.Count ?? 0,
				resultHediffCount = result.hediffs?.Count ?? 0,
				combatTextBefore = combatTextBefore,
				combatTextAfter = combatTextAfter,
					combatTextDelta = combatTextAfter - combatTextBefore
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

		static ContaminationSnapshot DescribeContamination(Pawn pawn)
		{
			var need = pawn?.needs?.TryGetNeed<ContaminationNeed>();
			var hediffs = pawn?.health?.hediffSet?.hediffs?
				.OfType<Hediff_Contamination>()
				.ToArray() ?? Array.Empty<Hediff_Contamination>();
			var hediff = hediffs.FirstOrDefault();
			return new ContaminationSnapshot
			{
				stored = pawn?.GetContamination() ?? 0f,
				hasNeed = need != null,
				needLevel = need?.CurLevel,
				hasHediff = hediff != null,
				hediffSeverity = hediff?.Severity,
				effectiveness = pawn?.GetEffectiveness() ?? 0f,
				hediffCount = hediffs.Length
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

		static void ClearGasAt(Map map, IntVec3 cell)
		{
			if (map == null || cell.IsValid == false)
				return;

			foreach (var gas in cell.GetThingList(map).Where(thing => thing.def.category == ThingCategory.Gas).ToArray())
				gas.Destroy();
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

		static bool TryFindWallPushFixtureCells(Map map, IntVec3 root, float radius, out IntVec3 zombieCell, out IntVec3 wallCell, out IntVec3 destinationCell, out object error)
		{
			zombieCell = IntVec3.Invalid;
			wallCell = IntVec3.Invalid;
			destinationCell = IntVec3.Invalid;
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

			var directions = new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South };
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map) || candidate.Standable(map) == false)
					continue;
				if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				foreach (var direction in directions)
				{
					var wall = candidate + direction;
					var destination = wall + direction;
					if (wall.InBounds(map) == false || destination.InBounds(map) == false)
						continue;
					if (wall.Fogged(map) || destination.Fogged(map))
						continue;
					if (wall.GetEdifice(map) != null || wall.GetFirstThing<Mineable>(map) != null)
						continue;
					if (wall.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					if (destination.Standable(map) == false)
						continue;
					if (destination.GetEdifice(map) != null || destination.GetFirstThing<Mineable>(map) != null)
						continue;
					if (destination.GetThingList(map).Any(thing => thing is Pawn))
						continue;

					var otherWallCount = 0;
					foreach (var adjacentDirection in directions)
					{
						var adjacent = candidate + adjacentDirection;
						if (adjacent == wall)
							continue;
						if (adjacent.InBounds(map) && adjacent.GetThingList(map).Any(thing => thing is Pawn))
						{
							otherWallCount = int.MaxValue;
							break;
						}
						if (adjacent.InBounds(map) && adjacent.IsWallOrDoor(map))
							otherWallCount++;
					}
					if (otherWallCount > 0)
						continue;

					zombieCell = candidate;
					wallCell = wall;
					destinationCell = destination;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear wall-push fixture triplet was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static Building SpawnWoodWall(Map map, IntVec3 cell)
		{
			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			if (wall == null)
				return null;
			GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);
			return wall;
		}

		static bool TryCreateWallPushFixture(Map map, IntVec3 root, float radius, out WallPushFixture fixture, out object error)
		{
			fixture = null;
			if (TryFindWallPushFixtureCells(map, root, radius, out var zombieCell, out var wallCell, out var destinationCell, out error) == false)
				return false;

			var wall = SpawnWoodWall(map, wallCell);
			if (wall == null)
			{
				error = new
				{
					success = false,
					error = "Could not create test wall."
				};
				return false;
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				error = new
				{
					success = false,
					error = "Could not spawn wall-push test zombie.",
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell)
				};
				return false;
			}

			fixture = new WallPushFixture
			{
				zombie = zombie,
				wall = wall,
				zombieCell = zombieCell,
				wallCell = wallCell,
				destinationCell = destinationCell
			};
			return true;
		}

		static void ClearWallPushGridNeighborhood(Map map, IntVec3 zombieCell)
		{
			var grid = map.GetGrid();
			var nearbyCells = new[] { zombieCell, zombieCell + IntVec3.East, zombieCell + IntVec3.West, zombieCell + IntVec3.North, zombieCell + IntVec3.South };
			foreach (var cell in nearbyCells)
			{
				if (cell.InBounds(map) == false)
					continue;
				var current = grid.GetZombieCount(cell);
				if (current != 0)
					grid.ChangeZombieCount(cell, -current);
			}
		}

		static void PrepareWallPushZombie(Map map, Zombie zombie, IntVec3 zombieCell)
		{
			var tickManager = map.GetComponent<TickManager>();
			tickManager?.allZombiesCached?.RemoveWhere(cached => cached == null || cached.Destroyed || cached.Spawned == false || cached.Dead);
			_ = tickManager?.allZombiesCached?.Add(zombie);

			zombie.pather?.StopDead();
			zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			zombie.state = ZombieState.Wandering;
			zombie.wallPushProgress = -1f;
			zombie.wallPushStart = Vector3.zero;
			zombie.wallPushDestination = Vector3.zero;
			zombie.wallPushCooldown = 0;
			zombie.lastGotoPosition = zombieCell;
			zombie.Rotation = Rot4.South;
		}

		static void ClearThrottleKey(string key)
		{
			var field = typeof(Tools).GetField("nextExecutions", BindingFlags.Static | BindingFlags.NonPublic);
			if (field?.GetValue(null) is Dictionary<string, float> nextExecutions)
				_ = nextExecutions.Remove(key);
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

		static bool MatchesRequestedZombieType(Zombie zombie, ZombieType type)
		{
			if (zombie == null)
				return false;

			return type switch
			{
				ZombieType.SuicideBomber => zombie.IsSuicideBomber,
				ZombieType.ToxicSplasher => zombie.isToxicSplasher,
				ZombieType.TankyOperator => zombie.IsTanky,
				ZombieType.Miner => zombie.isMiner,
				ZombieType.Electrifier => zombie.isElectrifier,
				ZombieType.Albino => zombie.isAlbino,
				ZombieType.DarkSlimer => zombie.isDarkSlimer,
				ZombieType.Healer => zombie.isHealer,
				ZombieType.Normal => zombie.IsSuicideBomber == false
					&& zombie.isToxicSplasher == false
					&& zombie.IsTanky == false
					&& zombie.isMiner == false
					&& zombie.isElectrifier == false
					&& zombie.isAlbino == false
					&& zombie.isDarkSlimer == false
					&& zombie.isHealer == false,
				_ => false,
			};
		}

		[Tool("zombieland/incident_special_type_spawn_contract", Description = "Verify the ZombiesRising event spawn core preserves explicit special zombie type requests.")]
		public static object IncidentSpecialTypeSpawnContract()
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

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			if (spawnEventProcess == null)
			{
				return new
				{
					success = false,
					error = "Could not find ZombiesRising.SpawnEventProcess by reflection."
				};
			}

			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
			if (spot.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No valid event spawn spot was found."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var initialIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var spawnedZombies = new List<Zombie>();
			var samples = new List<object>();
			var types = new[]
			{
				ZombieType.SuicideBomber,
				ZombieType.ToxicSplasher,
				ZombieType.TankyOperator,
				ZombieType.Miner,
				ZombieType.Electrifier,
				ZombieType.Albino,
				ZombieType.DarkSlimer,
				ZombieType.Healer,
				ZombieType.Normal
			};

			try
			{
				var success = true;
				foreach (var type in types)
				{
					var beforeIds = CurrentZombies(map)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					var iterator = spawnEventProcess.Invoke(null, new object[] { map, 1, spot, cellValidator, false, true, type }) as System.Collections.IEnumerator;
					if (iterator == null)
					{
						success = false;
						samples.Add(new
						{
							type = type.ToString(),
							success = false,
							error = "SpawnEventProcess did not return an IEnumerator."
						});
						continue;
					}

					var steps = 0;
					while (steps < 2048 && iterator.MoveNext())
						steps++;

					var after = CurrentZombies(map)
						.OfType<Zombie>()
						.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
						.ToArray();
					spawnedZombies.AddRange(after);
					var best = after
						.OrderBy(zombie => zombie.Position.DistanceToSquared(spot))
						.FirstOrDefault();
					var matched = MatchesRequestedZombieType(best, type);
					success &= matched && steps < 2048 && after.Length == 1;
					samples.Add(new
					{
						type = type.ToString(),
						success = matched && steps < 2048 && after.Length == 1,
						matched,
						steps,
						spawnedCount = after.Length,
						spawned = DescribeZombie(best)
					});
				}

				var currentIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				var totalNewZombies = currentIds.Count(id => initialIds.Contains(id) == false);
				return new
				{
					success,
					spot = ZombieRuntimeActions.DescribeCell(spot),
					requestedTypes = types.Select(type => type.ToString()).ToArray(),
					totalNewZombies,
					samples
				};
			}
			finally
			{
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager?.allZombiesCached?.Remove(zombie);
					_ = tickManager?.hummingZombies?.Remove(zombie);
					_ = tickManager?.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
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

		[Tool("zombieland/contamination_core_contract", Description = "Verify core contamination storage, pawn need/hediff sync, clearing, clamping, and stack split propagation.")]
		public static object ContaminationCoreContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var itemCell, out var itemSpawnError) == false)
				return itemSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();
			var initial = DescribeContamination(human);

			const float addValue = 0.4f;
			human.AddContamination(addValue);
			var afterAdd = DescribeContamination(human);

			const float setValue = 0.25f;
			human.SetContamination(setValue);
			var afterSet = DescribeContamination(human);

			human.ClearContamination();
			var afterClear = DescribeContamination(human);

			const float highNonLethalValue = 0.75f;
			human.AddContamination(highNonLethalValue);
			var afterHighNonLethalAdd = DescribeContamination(human);
			human.ClearContamination();
			var afterSecondClear = DescribeContamination(human);

			var clampedComponent = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			const float clampInput = 1.5f;
			clampedComponent.AddContamination(clampInput, (sbyte)map.Index);
			var clampedComponentContamination = clampedComponent.GetContamination();

			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			component.stackCount = 10;
			GenSpawn.Spawn(component, itemCell, map, WipeMode.Vanish);
			const float stackContamination = 0.6f;
			component.AddContamination(stackContamination);
			var componentBeforeSplitCount = component.stackCount;
			var componentBeforeSplitContamination = component.GetContamination();
			var split = component.SplitOff(4);
			var componentAfterSplitCount = component.stackCount;
			var splitCount = split?.stackCount ?? 0;
			var componentAfterSplitContamination = component.GetContamination();
			var splitContamination = split?.GetContamination() ?? 0f;

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var expectedEffectivenessAfterAdd = Mathf.Max(0.05f, 1f - addValue * ZombieSettings.Values.contamination.contaminationEffectivenessPercentage);
			var expectedEffectivenessAfterSet = Mathf.Max(0.05f, 1f - setValue * ZombieSettings.Values.contamination.contaminationEffectivenessPercentage);

			var initialClean = CloseFloat(initial.stored, 0f) && initial.hasHediff == false;
			var addSynced = CloseFloat(afterAdd.stored, addValue)
				&& afterAdd.hasNeed
				&& Close(afterAdd.needLevel, addValue)
				&& afterAdd.hasHediff
				&& Close(afterAdd.hediffSeverity, addValue)
				&& CloseFloat(afterAdd.effectiveness, expectedEffectivenessAfterAdd);
			var setSynced = CloseFloat(afterSet.stored, setValue)
				&& Close(afterSet.needLevel, setValue)
				&& afterSet.hasHediff
				&& Close(afterSet.hediffSeverity, setValue)
				&& CloseFloat(afterSet.effectiveness, expectedEffectivenessAfterSet);
			var clearSynced = CloseFloat(afterClear.stored, 0f)
				&& Close(afterClear.needLevel, 0f)
				&& afterClear.hasHediff == false;
			var highNonLethalAddSynced = CloseFloat(afterHighNonLethalAdd.stored, highNonLethalValue)
				&& Close(afterHighNonLethalAdd.needLevel, highNonLethalValue)
				&& afterHighNonLethalAdd.hasHediff
				&& Close(afterHighNonLethalAdd.hediffSeverity, highNonLethalValue);
			var clampSynced = CloseFloat(clampedComponentContamination, 1f);
			var secondClearSynced = CloseFloat(afterSecondClear.stored, 0f)
				&& Close(afterSecondClear.needLevel, 0f)
				&& afterSecondClear.hasHediff == false
				&& human.Dead == false;
			var splitPropagated = componentBeforeSplitCount == 10
				&& componentAfterSplitCount == 6
				&& splitCount == 4
				&& CloseFloat(componentBeforeSplitContamination, stackContamination)
				&& CloseFloat(componentAfterSplitContamination, stackContamination)
				&& CloseFloat(splitContamination, stackContamination);

			return new
			{
				success = initialClean
					&& addSynced
					&& setSynced
					&& clearSynced
					&& highNonLethalAddSynced
					&& clampSynced
					&& secondClearSynced
					&& splitPropagated,
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
				initial,
				afterAdd,
				afterSet,
				afterClear,
				afterHighNonLethalAdd,
				afterSecondClear,
				expectedEffectivenessAfterAdd,
				expectedEffectivenessAfterSet,
				clampedComponent = ZombieRuntimeActions.StableThingId(clampedComponent),
				clampedComponentContamination,
				component = ZombieRuntimeActions.StableThingId(component),
				split = ZombieRuntimeActions.StableThingId(split),
				componentBeforeSplitCount,
				componentAfterSplitCount,
				splitCount,
				componentBeforeSplitContamination,
				componentAfterSplitContamination,
				splitContamination,
				initialClean,
				addSynced,
				setSynced,
				clearSynced,
				highNonLethalAddSynced,
				clampSynced,
				secondClearSynced,
				splitPropagated
			};
		}

		[Tool("zombieland/contamination_cell_fire_contract", Description = "Verify contaminated ground affects pawns on cell entry and real Fire.DoComplexCalcs burns contamination down.")]
		public static object ContaminationCellFireContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var entryCell, out var entrySpawnError) == false)
				return entrySpawnError;
			if (TryFindClearSpawnCell(map, entryCell + new IntVec3(4, 0, 0), 10f, out var fireCell, out var fireSpawnError) == false)
				return fireSpawnError;

			foreach (var existingFire in fireCell.GetThingList(map).OfType<Fire>().ToArray())
				existingFire.Destroy();
			map.SetContamination(entryCell, 0f);
			map.SetContamination(fireCell, 0f);

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, entryCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();

			const float entryCellContamination = 0.8f;
			map.SetContamination(entryCell, entryCellContamination);
			var humanBeforeEntry = DescribeContamination(human);
			var entryGroundBefore = map.GetContamination(entryCell);
			human.filth.Notify_EnteredNewCell();
			var humanAfterEntry = DescribeContamination(human);
			var entryGroundAfter = map.GetContamination(entryCell);
			var expectedEntryGain = Mathf.Max(0f, entryGroundBefore * ZombieSettings.Values.contamination.cellFactor - humanBeforeEntry.stored)
				* ZombieSettings.Values.contamination.enterCellAdd;

			const float fireContaminationBefore = 0.4f;
			map.SetContamination(fireCell, fireContaminationBefore);
			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			GenSpawn.Spawn(component, fireCell, map, WipeMode.Vanish);
			component.SetContamination(fireContaminationBefore);
			var componentBeforeFire = component.GetContamination();
			var groundBeforeFire = map.GetContamination(fireCell);

			FireUtility.TryStartFireIn(fireCell, map, 0.5f, null);
			var fire = fireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (fire == null)
			{
				return new
				{
					success = false,
					entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					error = "Could not start a real fire for the contamination cleanup fixture."
				};
			}
			if (TryDoFireComplexCalcs(fire, out var fireError) == false)
			{
				return new
				{
					success = false,
					entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					fire = ZombieRuntimeActions.StableThingId(fire),
					error = fireError
				};
			}

			var componentAfterFire = component.GetContamination();
			var groundAfterFire = map.GetContamination(fireCell);
			var expectedFireReduction = ZombieSettings.Values.contamination.fireReduction;
			var expectedComponentAfterFire = Mathf.Max(0f, componentBeforeFire - expectedFireReduction);
			var expectedGroundAfterFire = Mathf.Max(0f, groundBeforeFire - expectedFireReduction);

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var entryApplied = CloseFloat(humanBeforeEntry.stored, 0f)
				&& CloseFloat(entryGroundBefore, entryCellContamination)
				&& CloseFloat(entryGroundAfter, entryGroundBefore)
				&& CloseFloat(humanAfterEntry.stored, expectedEntryGain)
				&& Close(humanAfterEntry.needLevel, expectedEntryGain)
				&& humanAfterEntry.hasHediff
				&& Close(humanAfterEntry.hediffSeverity, expectedEntryGain);
			var fireReduced = CloseFloat(componentAfterFire, expectedComponentAfterFire)
				&& CloseFloat(groundAfterFire, expectedGroundAfterFire);

			return new
			{
				success = entryApplied && fireReduced,
				human = DescribePawn(human),
				entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
				fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
				fire = ZombieRuntimeActions.StableThingId(fire),
				component = ZombieRuntimeActions.StableThingId(component),
				humanBeforeEntry,
				humanAfterEntry,
				entryGroundBefore,
				entryGroundAfter,
				expectedEntryGain,
				fireReduction = expectedFireReduction,
				componentBeforeFire,
				componentAfterFire,
				expectedComponentAfterFire,
				groundBeforeFire,
				groundAfterFire,
				expectedGroundAfterFire,
				entryApplied,
				fireReduced
			};
		}

		[Tool("zombieland/contamination_zombie_death_contract", Description = "Verify killing a real zombie contaminates its death cell while an ordinary pawn death does not.")]
		public static object ContaminationZombieDeathContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(4, 0, 0), 10f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			map.SetContamination(zombieCell, 0f);
			map.SetContamination(humanCell, 0f);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no death-contamination test zombie."
				};
			}

			var zombieGroundBefore = map.GetContamination(zombieCell);
			var humanGroundBefore = map.GetContamination(humanCell);
			zombie.Kill(null);
			human.Kill(null);
			var zombieGroundAfter = map.GetContamination(zombieCell);
			var humanGroundAfter = map.GetContamination(humanCell);
			var expectedZombieGroundAfter = Mathf.Clamp01(zombieGroundBefore + ZombieSettings.Values.contamination.zombieDeathAdd);

			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
			var zombieDeathContaminated = CloseFloat(zombieGroundAfter, expectedZombieGroundAfter);
			var humanDeathIgnored = CloseFloat(humanGroundAfter, humanGroundBefore);

			return new
			{
				success = zombieDeathContaminated && humanDeathIgnored,
				zombie = DescribeZombie(zombie),
				human = DescribePawn(human),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				zombieDeathAdd = ZombieSettings.Values.contamination.zombieDeathAdd,
				zombieGroundBefore,
				zombieGroundAfter,
				expectedZombieGroundAfter,
				humanGroundBefore,
				humanGroundAfter,
				zombieDeathContaminated,
				humanDeathIgnored
			};
		}

		[Tool("zombieland/contamination_effect_manager_contract", Description = "Verify contaminated pawns register with the effect manager and can trigger the first source-derived contamination job.")]
		public static object ContaminationEffectManagerContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var effects = tickManager?.contaminationEffects;
			if (effects == null)
			{
				return new
				{
					success = false,
					error = "Current map has no contamination effect manager."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();
			effects.Remove(human);
			human.mindState?.mentalStateHandler?.Reset();

			var trackedBefore = effects.pawns.ContainsKey(human);
			const float forceRestContamination = 0.24f;
			human.AddContamination(forceRestContamination);
			var trackedAfterAdd = effects.pawns.TryGetValue(human, out var effect);
			var nextEffectTickBeforeForce = effect?.nextEffectTick ?? -1;
			if (effect != null)
				effect.nextEffectTick = Find.TickManager.TicksGame;
			effects.Tick();

			var mentalStateAfterTick = human.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterTick = human.CurJobDef?.defName;
			var reportAfterTick = human.CurJob?.GetReport(human);
			var forceRecoverAfterTicks = human.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;
			var trackedAfterTick = effects.pawns.ContainsKey(human);
			var contaminationAfterTick = DescribeContamination(human);

			human.ClearContamination();
			var trackedAfterClear = effects.pawns.ContainsKey(human);
			var contaminationAfterClear = DescribeContamination(human);

			const float forceRestMin = 0.15f;
			const float forceRestMax = 0.40f;
			var expectedForceRestFactor = Mathf.InverseLerp(forceRestMin, forceRestMax, forceRestContamination);
			var expectedForceRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + expectedForceRestFactor * 7);
			var registered = trackedBefore == false && trackedAfterAdd;
			var forceRestStarted = mentalStateAfterTick == EffectDefs.ContaminationStateForceRest.defName
				&& jobAfterTick == EffectDefs.ContaminationJobForceRest.defName
				&& forceRecoverAfterTicks == expectedForceRecoverAfterTicks;
			var unregistered = trackedAfterClear == false
				&& contaminationAfterClear.hasHediff == false
				&& contaminationAfterClear.stored == 0f;

			return new
			{
				success = registered && forceRestStarted && trackedAfterTick && unregistered,
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				contamination = forceRestContamination,
				expectedForceRestFactor,
				trackedBefore,
				trackedAfterAdd,
				trackedAfterTick,
				trackedAfterClear,
				nextEffectTickBeforeForce,
				forcedEffectTick = Find.TickManager.TicksGame,
				mentalStateAfterTick,
				expectedMentalState = EffectDefs.ContaminationStateForceRest.defName,
				jobAfterTick,
				expectedJob = EffectDefs.ContaminationJobForceRest.defName,
				reportAfterTick,
				forceRecoverAfterTicks,
				expectedForceRecoverAfterTicks,
				contaminationAfterTick,
				contaminationAfterClear,
				registered,
				forceRestStarted,
				unregistered
			};
		}

		[Tool("zombieland/contamination_hallucination_contract", Description = "Verify the contamination hallucination effect starts the real job, ghost mote, and source-derived 30-tick movement loop.")]
		public static object ContaminationHallucinationContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();
			human.mindState?.mentalStateHandler?.Reset();

			const float hallucinationMin = 0.25f;
			const float hallucinationMax = 0.50f;
			const float hallucinationContamination = 0.40f;
			var factor = Mathf.InverseLerp(hallucinationMin, hallucinationMax, hallucinationContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Hallucination(human, factor);
			var mentalStateAfterApply = human.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = human.CurJobDef?.defName;
			var recoverAfterApply = human.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = human.jobs?.curDriver as JobDriver_ContaminationHallucination;
			var destinationAfterInit = driverAfterInit?.destination ?? IntVec3.Invalid;
			var ghostAfterInit = driverAfterInit?.ghost;
			var ghostVecAfterInit = driverAfterInit?.ghostVec ?? Vector3.zero;
			var movingAfterInit = human.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? human.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedUpdateTicks = 30;
			AdvanceGameTicks(sourceDerivedUpdateTicks);
			var driverAfterUpdate = human.jobs?.curDriver as JobDriver_ContaminationHallucination;
			var destinationAfterUpdate = driverAfterUpdate?.destination ?? IntVec3.Invalid;
			var ghostAfterUpdate = driverAfterUpdate?.ghost;
			var ghostVecAfterUpdate = driverAfterUpdate?.ghostVec ?? Vector3.zero;
			var ghostMoved = (ghostVecAfterUpdate - ghostVecAfterInit).sqrMagnitude > 0.0001f;
			var jobAfterUpdate = human.CurJobDef?.defName;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateHallucination.defName
				&& jobAfterApply == EffectDefs.ContaminationJobHallucination.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& destinationAfterInit.IsValid
				&& ghostAfterInit != null
				&& movingAfterInit
				&& pathDestinationAfterInit == destinationAfterInit;
			var periodicLoopRan = driverAfterUpdate != null
				&& destinationAfterUpdate.IsValid
				&& ghostAfterUpdate != null
				&& ghostMoved
				&& jobAfterUpdate == EffectDefs.ContaminationJobHallucination.defName;

			return new
			{
				success = stateStarted && jobInitialized && periodicLoopRan,
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				hallucinationContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateHallucination.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobHallucination.defName,
				recoverAfterApply,
				destinationAfterInit = destinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(destinationAfterInit) : null,
				ghostAfterInit = ghostAfterInit != null,
				ghostVecAfterInit = DescribeVector(ghostVecAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedUpdateTicks,
				destinationAfterUpdate = destinationAfterUpdate.IsValid ? ZombieRuntimeActions.DescribeCell(destinationAfterUpdate) : null,
				ghostAfterUpdate = ghostAfterUpdate != null,
				ghostVecAfterUpdate = DescribeVector(ghostVecAfterUpdate),
				ghostMoved,
				jobAfterUpdate,
				stateStarted,
				jobInitialized,
				periodicLoopRan
			};
		}

		[Tool("zombieland/contamination_mimic_contract", Description = "Verify the contamination mimic effect survives RimWorld's 30-tick think-tree pass, tracks a victim, scares them, and starts their flee job.")]
		public static object ContaminationMimicContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingColonists = map.mapPawns.FreeColonists.ToArray();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var offsets = new[]
			{
				new IntVec3(2, 0, 0),
				new IntVec3(-2, 0, 0),
				new IntVec3(0, 0, 2),
				new IntVec3(0, 0, -2)
			};
			bool IsClearCell(IntVec3 candidate)
			{
				return candidate.InBounds(map)
					&& candidate.Standable(map)
					&& candidate.Fogged(map) == false
					&& candidate.GetThingList(map).Any(thing => thing is Pawn) == false;
			}
			Pawn GenerateMobileColonist()
			{
				for (var i = 0; i < 10; i++)
				{
					var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					if (pawn.Downed == false && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) == true)
						return pawn;
				}
				return PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			}

			var mimicCell = IntVec3.Invalid;
			var victimCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (IsClearCell(candidate) == false)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 100))
					continue;

				foreach (var offset in offsets)
				{
					var candidateVictimCell = candidate + offset;
					if (IsClearCell(candidateVictimCell) == false)
						continue;
					var victimDistance = candidate.DistanceToSquared(candidateVictimCell);
					if (existingColonists.Any(colonist => colonist.Position.DistanceToSquared(candidate) <= victimDistance))
						continue;
					if (map.reachability.CanReach(candidate, candidateVictimCell, PathEndMode.ClosestTouch, TraverseMode.PassDoors, Danger.Deadly) == false)
						continue;

					mimicCell = candidate;
					victimCell = candidateVictimCell;
					break;
				}
				if (mimicCell.IsValid)
					break;
			}
			if (mimicCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated clear mimic/victim fixture cells were found."
				};
			}

			var mimic = GenerateMobileColonist();
			var victim = GenerateMobileColonist();
			GenSpawn.Spawn(mimic, mimicCell, map, Rot4.South);
			GenSpawn.Spawn(victim, victimCell, map, Rot4.South);
			DisablePawnWork(mimic);
			DisablePawnWork(victim);
			mimic.needs?.AddOrRemoveNeedsAsAppropriate();
			victim.needs?.AddOrRemoveNeedsAsAppropriate();
			victim.drafter.Drafted = true;
			victim.jobs.StopAll(false, false);
			mimic.ClearContamination();
			victim.ClearContamination();
			mimic.mindState?.mentalStateHandler?.Reset();
			victim.mindState?.mentalStateHandler?.Reset();

			const float mimicMin = 0.50f;
			const float mimicMax = 1.00f;
			const float mimicContamination = 0.80f;
			var factor = Mathf.InverseLerp(mimicMin, mimicMax, mimicContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var victimMemoriesBefore = MemoryDefCounts(victim);
			var applied = ContaminationEffect.Mimicing(mimic, factor);
			var mentalStateAfterApply = mimic.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = mimic.CurJobDef?.defName;
			var recoverAfterApply = mimic.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var trackedVictimAfterInit = driverAfterInit?.victim;
			var movingAfterInit = mimic.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? mimic.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var jobAfterThinkTreeWindow = mimic.CurJobDef?.defName;
			var trackedVictimAfterThinkTreeWindow = driverAfterThinkTreeWindow?.victim;

			var previousVictim = driverAfterThinkTreeWindow?.previousVictims;
			var escapeJobStarted = victim.CurJobDef == JobDefOf.Flee || victim.CurJobDef == JobDefOf.FleeAndCower;
			var victimMovedFromSpawn = victim.Position != victimCell;
			var victimJobDuringEscape = victim.CurJobDef?.defName;
			var ticksUntilScare = 0;
			var maxArrivalTicks = Math.Max(1, expectedRecoverAfterTicks - sourceDerivedThinkTreeWindow - 1);
			while (ticksUntilScare < maxArrivalTicks && previousVictim != victim && escapeJobStarted == false)
			{
				AdvanceGameTicks(1);
				ticksUntilScare++;
				if (mimic.jobs?.curDriver is JobDriver_ContaminationMimic currentDriver)
					previousVictim = currentDriver.previousVictims;
				else
					victimMovedFromSpawn = victim.Position != victimCell;
				escapeJobStarted = victim.CurJobDef == JobDefOf.Flee || victim.CurJobDef == JobDefOf.FleeAndCower;
				if (escapeJobStarted)
					victimJobDuringEscape = victim.CurJobDef?.defName;
			}

			var driverAfterScare = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var victimMemoriesAfter = MemoryDefCounts(victim);
			victimMemoriesBefore.TryGetValue(CustomDefs.ZombieScare.defName, out var zombieScareBefore);
			victimMemoriesAfter.TryGetValue(CustomDefs.ZombieScare.defName, out var zombieScareAfter);
			var zombieScareMemoryGained = zombieScareAfter > zombieScareBefore;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateMimicing.defName
				&& jobAfterApply == EffectDefs.ContaminationJobMimic.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& trackedVictimAfterInit == victim
				&& movingAfterInit
				&& pathDestinationAfterInit == victimCell;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobMimic.defName
				&& trackedVictimAfterThinkTreeWindow == victim;
			var victimScared = previousVictim == victim
				&& zombieScareMemoryGained
				&& escapeJobStarted;

			return new
			{
				success = stateStarted && jobInitialized && survivedThinkTreeWindow && victimScared,
				mimic = DescribePawn(mimic),
				victim = DescribePawn(victim),
				mimicCell = ZombieRuntimeActions.DescribeCell(mimicCell),
				victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
				mimicContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateMimicing.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobMimic.defName,
				recoverAfterApply,
				trackedVictimAfterInit = ZombieRuntimeActions.StableThingId(trackedVictimAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				trackedVictimAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(trackedVictimAfterThinkTreeWindow),
				ticksUntilScare,
				maxArrivalTicks,
				previousVictim = ZombieRuntimeActions.StableThingId(previousVictim),
				driverAfterScareStillMimic = driverAfterScare != null,
				escapeJobStarted,
				victimMovedFromSpawn,
				victimJobDuringEscape,
				victimJobAfterScare = victim.CurJobDef?.defName,
				victimMemoriesBefore,
				victimMemoriesAfter,
				zombieScareBefore,
				zombieScareAfter,
				zombieScareMemoryGained,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow,
				victimScared
			};
		}

		[Tool("zombieland/contamination_sleepwalk_contract", Description = "Verify the sleepwalk contamination effect starts from a real sleeping pawn, reaches an occupied bed, wakes the occupant, and starts their flee job.")]
		public static object ContaminationSleepwalkContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var avoidGrid = map.GetComponent<TickManager>()?.avoidGrid;
			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingZombies = CurrentZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var sleepwalkerCell = IntVec3.Invalid;
			var bedCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidate) == true)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 400))
					continue;
				if (existingZombies.Any(zombie => zombie.Position.DistanceToSquared(candidate) < 900))
					continue;
				if (TryFindClearBuildingFootprint(map, ThingDefOf.Bed, candidate + new IntVec3(8, 0, 0), 12f, out var candidateBedCell, out _) == false)
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidateBedCell) == true)
					continue;

				sleepwalkerCell = candidate;
				bedCell = candidateBedCell;
				break;
			}
			if (sleepwalkerCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated sleepwalk fixture cells were found away from existing pawns and zombies."
				};
			}

			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the sleepwalk fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var occupantCell = bed.GetSleepingSlotPos(0);
			var sleepwalker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var occupant = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(sleepwalker, sleepwalkerCell, map, Rot4.South);
			GenSpawn.Spawn(occupant, occupantCell, map, Rot4.South);
			DisablePawnWork(sleepwalker);
			DisablePawnWork(occupant);
			sleepwalker.needs?.AddOrRemoveNeedsAsAppropriate();
			occupant.needs?.AddOrRemoveNeedsAsAppropriate();
			sleepwalker.ClearContamination();
			occupant.ClearContamination();
			sleepwalker.mindState?.mentalStateHandler?.Reset();
			occupant.mindState?.mentalStateHandler?.Reset();

			var occupantSleepJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
			occupantSleepJob.forceSleep = true;
			occupant.jobs.ClearQueuedJobs();
			occupant.jobs.StartJob(occupantSleepJob, JobCondition.InterruptForced, null);
			var sleepwalkerSleepJob = JobMaker.MakeJob(JobDefOf.LayDown, sleepwalkerCell);
			sleepwalkerSleepJob.forceSleep = true;
			sleepwalker.jobs.ClearQueuedJobs();
			sleepwalker.jobs.StartJob(sleepwalkerSleepJob, JobCondition.InterruptForced, null);

			var sleepPrepTicks = 0;
			const int maxSleepPrepTicks = 180;
			while (sleepPrepTicks < maxSleepPrepTicks
				&& (sleepwalker.jobs?.curDriver?.asleep != true || occupant.jobs?.curDriver?.asleep != true || bed.CurOccupants.Contains(occupant) == false))
			{
				AdvanceGameTicks(1);
				sleepPrepTicks++;
			}
			var sleepwalkerAsleepBeforeApply = sleepwalker.jobs?.curDriver?.asleep == true;
			var occupantAsleepBeforeApply = occupant.jobs?.curDriver?.asleep == true;
			var bedOccupiedBeforeApply = bed.CurOccupants.Contains(occupant);

			const float sleepwalkMin = 0.35f;
			const float sleepwalkMax = 0.50f;
			const float sleepwalkContamination = 0.44f;
			var factor = Mathf.InverseLerp(sleepwalkMin, sleepwalkMax, sleepwalkContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Sleepwalk(sleepwalker, factor);
			var mentalStateAfterApply = sleepwalker.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = sleepwalker.CurJobDef?.defName;
			var recoverAfterApply = sleepwalker.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var trackedBedAfterInit = driverAfterInit?.bed;
			var movingAfterInit = sleepwalker.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? sleepwalker.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var jobAfterThinkTreeWindow = sleepwalker.CurJobDef?.defName;
			var trackedBedAfterThinkTreeWindow = driverAfterThinkTreeWindow?.bed;

			var occupantFleeStarted = occupant.CurJobDef == JobDefOf.Flee || occupant.CurJobDef == JobDefOf.FleeAndCower;
			var occupantAwakeAfterApply = RestUtility.Awake(occupant);
			var waitUntilAfterArrival = driverAfterThinkTreeWindow?.waitUntil ?? -1;
			var ticksUntilWake = 0;
			var maxArrivalTicks = Math.Max(1, expectedRecoverAfterTicks - sourceDerivedThinkTreeWindow - 1);
			while (ticksUntilWake < maxArrivalTicks && occupantFleeStarted == false)
			{
				AdvanceGameTicks(1);
				ticksUntilWake++;
				occupantFleeStarted = occupant.CurJobDef == JobDefOf.Flee || occupant.CurJobDef == JobDefOf.FleeAndCower;
				occupantAwakeAfterApply |= RestUtility.Awake(occupant);
				if (sleepwalker.jobs?.curDriver is JobDriver_ContaminationSleepwalk currentDriver)
					waitUntilAfterArrival = currentDriver.waitUntil;
				else
					break;
			}

			var driverAfterWake = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateSleepwalking.defName
				&& jobAfterApply == EffectDefs.ContaminationJobSleepwalk.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& trackedBedAfterInit == bed
				&& movingAfterInit
				&& pathDestinationAfterInit == bed.Position;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobSleepwalk.defName
				&& trackedBedAfterThinkTreeWindow == bed;
			var occupantWokenAndFleeing = occupantAwakeAfterApply
				&& occupantFleeStarted
				&& waitUntilAfterArrival > Find.TickManager.TicksGame;

			return new
			{
				success = sleepwalkerAsleepBeforeApply && occupantAsleepBeforeApply && bedOccupiedBeforeApply && stateStarted && jobInitialized && survivedThinkTreeWindow && occupantWokenAndFleeing,
				sleepwalker = DescribePawn(sleepwalker),
				occupant = DescribePawn(occupant),
				bed = new
				{
					id = ZombieRuntimeActions.StableThingId(bed),
					cell = ZombieRuntimeActions.DescribeCell(bed.Position),
					occupantCell = ZombieRuntimeActions.DescribeCell(occupantCell),
					occupants = bed.CurOccupants.Select(ZombieRuntimeActions.StableThingId).ToArray()
				},
				sleepwalkerCell = ZombieRuntimeActions.DescribeCell(sleepwalkerCell),
				sleepPrepTicks,
				maxSleepPrepTicks,
				sleepwalkerAsleepBeforeApply,
				occupantAsleepBeforeApply,
				bedOccupiedBeforeApply,
				sleepwalkContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateSleepwalking.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobSleepwalk.defName,
				recoverAfterApply,
				trackedBedAfterInit = ZombieRuntimeActions.StableThingId(trackedBedAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				trackedBedAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(trackedBedAfterThinkTreeWindow),
				ticksUntilWake,
				maxArrivalTicks,
				driverAfterWakeStillSleepwalk = driverAfterWake != null,
				waitUntilAfterArrival,
				currentTicks = Find.TickManager.TicksGame,
				occupantAwakeAfterApply,
				occupantFleeStarted,
				occupantJobAfterWake = occupant.CurJobDef?.defName,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow,
				occupantWokenAndFleeing
			};
		}

		[Tool("zombieland/contamination_hoard_pather_failure_contract", Description = "Verify the hoarding contamination job survives a pather failure without ending as ErroredPather.")]
		public static object ContaminationHoardPatherFailureContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;
			var bedCell = fixture.interiorRect.CenterCell;

			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the hoarding pather-failure fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var hoarderCell = fixture.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hoarderCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear hoarder cell in the fixture room."
				};
			}

			var hoarder = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(hoarder, hoarderCell, map, Rot4.South);
			DisablePawnWork(hoarder);
			hoarder.needs?.AddOrRemoveNeedsAsAppropriate();
			hoarder.ClearContamination();
			hoarder.mindState?.mentalStateHandler?.Reset();
			bed.CompAssignableToPawn?.TryAssignPawn(hoarder);
			bed.NotifyRoomAssignedPawnsChanged();

			const float hoardingContamination = 0.54f;
			var factor = Mathf.InverseLerp(0.45f, 0.60f, hoardingContamination);
			var applied = ContaminationEffect.Hoarding(hoarder, factor);
			var driver = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			if (applied == false || driver == null)
			{
				return new
				{
					success = false,
					applied,
					hoarder = DescribePawn(hoarder),
					job = hoarder.CurJobDef?.defName,
					error = "The hoarding contamination job did not start."
				};
			}

			var unreachableThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			unreachableThing.stackCount = 1;
			driver.state = JobDriver_ContaminationHoard.State.moveToThing;
			driver.thing = unreachableThing;
			driver.rejectedThings.Clear();
			driver.Notify_PatherFailed();
			var driverAfterFailure = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var rejected = driverAfterFailure?.rejectedThings.Contains(unreachableThing) ?? false;
			var survived = driverAfterFailure != null
				&& hoarder.CurJobDef == EffectDefs.ContaminationJobHoard
				&& driverAfterFailure.state == JobDriver_ContaminationHoard.State.findThing
				&& driverAfterFailure.thing == null
				&& rejected;

			return new
			{
				success = survived,
				hoarder = DescribePawn(hoarder),
				hoardingContamination,
				applied,
				jobAfterFailure = hoarder.CurJobDef?.defName,
				driverStateAfterFailure = driverAfterFailure?.state.ToString(),
				thingCleared = driverAfterFailure?.thing == null,
				rejected,
				survived
			};
		}

		[Tool("zombieland/contamination_hoard_driver_flow_contract", Description = "Verify the hoarding contamination job initializes from a real assigned room and runs its pickup/drop arrival callbacks.")]
		public static object ContaminationHoardDriverFlowContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var bedroomFixture, out var bedroomError) == false)
				return bedroomError;
			if (TryBuildFogRoomFixture(map, bedroomFixture.doorCell + new IntVec3(10, 0, 0), 32f, out var sourceFixture, out var sourceError) == false)
				return sourceError;

			var bedCell = bedroomFixture.interiorRect.CenterCell;
			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the hoarding flow fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var sourceThingCell = sourceFixture.interiorRect.CenterCell;
			var sourceThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			sourceThing.stackCount = ThingDefOf.Silver.stackLimit;
			GenSpawn.Spawn(sourceThing, sourceThingCell, map, WipeMode.Vanish);

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var bedroom = bed.GetRoom(RegionType.Set_All);
			var sourceRoom = sourceThingCell.GetRoom(map);
			if (bedroom == null || sourceRoom == null || bedroom == sourceRoom || bedroom.IsHuge || sourceRoom.IsHuge)
			{
				return new
				{
					success = false,
					bedroomExists = bedroom != null,
					sourceRoomExists = sourceRoom != null,
					sameRoom = bedroom != null && bedroom == sourceRoom,
					bedroomHuge = bedroom?.IsHuge,
					sourceRoomHuge = sourceRoom?.IsHuge,
					error = "The hoarding flow fixture did not produce two distinct non-huge rooms."
				};
			}

			var hoarderCell = bedroomFixture.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hoarderCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear hoarder cell in the bedroom fixture."
				};
			}

			var hoarder = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(hoarder, hoarderCell, map, Rot4.South);
			DisablePawnWork(hoarder);
			hoarder.needs?.AddOrRemoveNeedsAsAppropriate();
			hoarder.ClearContamination();
			hoarder.mindState?.mentalStateHandler?.Reset();
			bed.CompAssignableToPawn?.TryAssignPawn(hoarder);
			bed.NotifyRoomAssignedPawnsChanged();

			const float hoardingContamination = 0.54f;
			var factor = Mathf.InverseLerp(0.45f, 0.60f, hoardingContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);
			var applied = ContaminationEffect.Hoarding(hoarder, factor);
			var jobAfterApply = hoarder.CurJobDef?.defName;
			var recoverAfterApply = hoarder.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var selectedThing = driverAfterInit?.thing;
			var storageCell = driverAfterInit?.cell ?? IntVec3.Invalid;
			var initialized = applied
				&& jobAfterApply == EffectDefs.ContaminationJobHoard.defName
				&& recoverAfterApply == expectedRecoverAfterTicks
				&& driverAfterInit != null
				&& driverAfterInit.room == bedroom
				&& driverAfterInit.state == JobDriver_ContaminationHoard.State.moveToThing
				&& selectedThing != null
				&& selectedThing.def == ThingDefOf.Silver
				&& storageCell.IsValid;
			if (initialized == false)
			{
				return new
				{
					success = false,
					hoarder = DescribePawn(hoarder),
					hoardingContamination,
					applied,
					jobAfterApply,
					currentJob = hoarder.CurJobDef?.defName,
					recoverAfterApply,
					expectedRecoverAfterTicks,
					driverExists = driverAfterInit != null,
					driverState = driverAfterInit?.state.ToString(),
					selectedThing = ZombieRuntimeActions.StableThingId(selectedThing),
					selectedThingDef = selectedThing?.def?.defName,
					storageCell = storageCell.IsValid ? ZombieRuntimeActions.DescribeCell(storageCell) : null,
					error = "The hoarding driver did not initialize into a move-to-thing state."
				};
			}

			const int sourceDerivedThinkTreeWindow = 30;
			var positionBeforeThinkTreeWindow = hoarder.Position;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var selectedThingAfterThinkTreeWindow = driverAfterThinkTreeWindow?.thing;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& hoarder.CurJobDef == EffectDefs.ContaminationJobHoard
				&& driverAfterThinkTreeWindow.state == JobDriver_ContaminationHoard.State.moveToThing
				&& selectedThingAfterThinkTreeWindow != null
				&& selectedThingAfterThinkTreeWindow.def == ThingDefOf.Silver;
			if (survivedThinkTreeWindow == false)
			{
				return new
				{
					success = false,
					hoarder = DescribePawn(hoarder),
					hoardingContamination,
					initialized,
					sourceDerivedThinkTreeWindow,
					jobAfterThinkTreeWindow = hoarder.CurJobDef?.defName,
					driverAfterThinkTreeWindowExists = driverAfterThinkTreeWindow != null,
					driverStateAfterThinkTreeWindow = driverAfterThinkTreeWindow?.state.ToString(),
					selectedThingAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(selectedThingAfterThinkTreeWindow),
					selectedThingDefAfterThinkTreeWindow = selectedThingAfterThinkTreeWindow?.def?.defName,
					error = "The hoarding job did not survive the source-derived think-tree window."
				};
			}

			hoarder.pather.StopDead();
			driverAfterThinkTreeWindow.Notify_PatherArrived();
			var carriedAfterPickup = hoarder.carryTracker.CarriedThing;
			var stateAfterPickup = driverAfterThinkTreeWindow.state;
			var pickedUp = carriedAfterPickup != null
				&& carriedAfterPickup.def == ThingDefOf.Silver
				&& stateAfterPickup == JobDriver_ContaminationHoard.State.moveToStorage;

			hoarder.pather.StopDead();
			driverAfterThinkTreeWindow.Notify_PatherArrived();
			var carriedAfterDrop = hoarder.carryTracker.CarriedThing;
			var droppedThing = storageCell.GetThingList(map).FirstOrDefault(thing => thing.def == ThingDefOf.Silver);
			var droppedInBedroom = droppedThing != null && bedroom.Cells.Contains(droppedThing.Position);
			var dropped = pickedUp
				&& carriedAfterDrop == null
				&& droppedInBedroom
				&& driverAfterThinkTreeWindow.state == JobDriver_ContaminationHoard.State.findThing;

			return new
			{
				success = initialized && survivedThinkTreeWindow && pickedUp && dropped,
				hoarder = DescribePawn(hoarder),
				hoardingContamination,
				applied,
				expectedRecoverAfterTicks,
				recoverAfterApply,
				sourceDerivedThinkTreeWindow,
				positionBeforeThinkTreeWindow = ZombieRuntimeActions.DescribeCell(positionBeforeThinkTreeWindow),
				positionAfterThinkTreeWindow = ZombieRuntimeActions.DescribeCell(hoarder.Position),
				bedroom = new
				{
					center = ZombieRuntimeActions.DescribeCell(bedroomFixture.interiorRect.CenterCell),
					cellCount = bedroom.CellCount
				},
				sourceRoom = new
				{
					center = ZombieRuntimeActions.DescribeCell(sourceThingCell),
					cellCount = sourceRoom.CellCount
				},
				selectedThing = ZombieRuntimeActions.StableThingId(selectedThing),
				selectedThingDef = selectedThing?.def?.defName,
				storageCell = ZombieRuntimeActions.DescribeCell(storageCell),
				carriedAfterPickup = ZombieRuntimeActions.StableThingId(carriedAfterPickup),
				carriedAfterPickupDef = carriedAfterPickup?.def?.defName,
				stateAfterPickup = stateAfterPickup.ToString(),
				carriedAfterDrop = ZombieRuntimeActions.StableThingId(carriedAfterDrop),
				droppedThing = ZombieRuntimeActions.StableThingId(droppedThing),
				droppedThingCell = droppedThing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(droppedThing.Position) : null,
				driverStateAfterDrop = driverAfterThinkTreeWindow.state.ToString(),
				initialized,
				survivedThinkTreeWindow,
				pickedUp,
				dropped
			};
		}

		[Tool("zombieland/contamination_breakdown_contract", Description = "Verify the breakdown contamination effect starts the real job, immediately picks a flee path, and survives RimWorld's 30-tick think-tree pass.")]
		public static object ContaminationBreakdownContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var avoidGrid = map.GetComponent<TickManager>()?.avoidGrid;
			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingZombies = CurrentZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var pawnCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidate) == true)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 400))
					continue;
				if (existingZombies.Any(zombie => zombie.Position.DistanceToSquared(candidate) < 900))
					continue;

				pawnCell = candidate;
				break;
			}
			if (pawnCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated breakdown fixture cell was found away from existing pawns and zombies."
				};
			}

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.needs?.AddOrRemoveNeedsAsAppropriate();
			pawn.ClearContamination();
			pawn.mindState?.mentalStateHandler?.Reset();

			const float breakdownMin = 0.60f;
			const float breakdownMax = 0.80f;
			const float breakdownContamination = 0.72f;
			var factor = Mathf.InverseLerp(breakdownMin, breakdownMax, breakdownContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Breakdown(pawn, factor);
			var mentalStateAfterApply = pawn.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = pawn.CurJobDef?.defName;
			var recoverAfterApply = pawn.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = pawn.jobs?.curDriver as JobDriver_ContaminationBreakdown;
			var movingAfterInit = pawn.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? pawn.pather.Destination.Cell : IntVec3.Invalid;
			var movedAfterInit = pawn.Position != pawnCell;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = pawn.jobs?.curDriver as JobDriver_ContaminationBreakdown;
			var jobAfterThinkTreeWindow = pawn.CurJobDef?.defName;
			var movingAfterThinkTreeWindow = pawn.pather?.Moving ?? false;
			var pathDestinationAfterThinkTreeWindow = movingAfterThinkTreeWindow ? pawn.pather.Destination.Cell : IntVec3.Invalid;
			var movedAfterThinkTreeWindow = pawn.Position != pawnCell;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateBreakdown.defName
				&& jobAfterApply == EffectDefs.ContaminationJobBreakdown.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& (movingAfterInit || movedAfterInit)
				&& (pathDestinationAfterInit.IsValid || movedAfterInit);
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobBreakdown.defName
				&& (movingAfterThinkTreeWindow || movedAfterThinkTreeWindow);

			return new
			{
				success = stateStarted && jobInitialized && survivedThinkTreeWindow,
				pawn = DescribePawn(pawn),
				pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
				breakdownContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateBreakdown.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobBreakdown.defName,
				recoverAfterApply,
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				movedAfterInit,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				movingAfterThinkTreeWindow,
				pathDestinationAfterThinkTreeWindow = pathDestinationAfterThinkTreeWindow.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterThinkTreeWindow) : null,
				movedAfterThinkTreeWindow,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow
			};
		}

		[Tool("zombieland/contamination_ingestion_contract", Description = "Verify ingesting contaminated stack food transfers the source-derived partial-stack contamination to the eater.")]
		public static object ContaminationIngestionContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var eaterCell, out var eaterSpawnError) == false)
				return eaterSpawnError;
			if (TryFindClearSpawnCell(map, eaterCell + new IntVec3(3, 0, 0), 8f, out var mealCell, out var mealSpawnError) == false)
				return mealSpawnError;

			var mealDef = ThingDefOf.MealSurvivalPack;
			var mealStack = Math.Min(5, mealDef.stackLimit);
			if (mealStack < 2)
			{
				return new
				{
					success = false,
					mealDef = mealDef.defName,
					stackLimit = mealDef.stackLimit,
					error = "Packaged survival meal is not stackable in this runtime."
				};
			}

			var eater = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(eater, eaterCell, map, Rot4.South);
			DisablePawnWork(eater);
			eater.needs?.AddOrRemoveNeedsAsAppropriate();
			eater.ClearContamination();

			var meal = ThingMaker.MakeThing(mealDef);
			meal.stackCount = mealStack;
			GenSpawn.Spawn(meal, mealCell, map, WipeMode.Vanish);
			const float mealContamination = 0.5f;
			meal.SetContamination(mealContamination);

			var eaterBefore = DescribeContamination(eater);
			var mealBefore = meal.GetContamination();
			var mealStackBefore = meal.stackCount;
			var nutritionWanted = meal.GetStatValue(StatDefOf.Nutrition);
			var nutritionIngested = meal.Ingested(eater, nutritionWanted);
			var eaterAfter = DescribeContamination(eater);
			var mealDestroyed = meal.Destroyed;
			var mealStackAfter = mealDestroyed ? 0 : meal.stackCount;
			var mealAfter = mealDestroyed ? 0f : meal.GetContamination();
			var numTaken = mealStackBefore - mealStackAfter;
			var expectedFactor = numTaken == 0 ? 0f : numTaken / (float)mealStackBefore;
			var requestedTransfer = mealBefore * ZombieSettings.Values.contamination.ingestTransfer * expectedFactor;
			var expectedTransfer = Mathf.Min(mealBefore, requestedTransfer);
			var expectedMealAfter = Mathf.Max(0f, mealBefore - expectedTransfer);
			var expectedEaterAfter = eaterBefore.stored + expectedTransfer;

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var atePartialStack = numTaken > 0 && mealStackAfter > 0 && nutritionIngested > 0f;
			var contaminationTransferred = CloseFloat(eaterAfter.stored, expectedEaterAfter)
				&& Close(eaterAfter.needLevel, expectedEaterAfter)
				&& eaterAfter.hasHediff
				&& Close(eaterAfter.hediffSeverity, expectedEaterAfter)
				&& CloseFloat(mealAfter, expectedMealAfter);

			return new
			{
				success = atePartialStack && contaminationTransferred,
				eater = DescribePawn(eater),
				eaterCell = ZombieRuntimeActions.DescribeCell(eaterCell),
				meal = ZombieRuntimeActions.StableThingId(meal),
				mealDef = mealDef.defName,
				mealCell = ZombieRuntimeActions.DescribeCell(mealCell),
				mealStackBefore,
				mealStackAfter,
				mealDestroyed,
				numTaken,
				nutritionWanted,
				nutritionIngested,
				ingestTransfer = ZombieSettings.Values.contamination.ingestTransfer,
				expectedFactor,
				requestedTransfer,
				expectedTransfer,
				eaterBefore,
				eaterAfter,
				expectedEaterAfter,
				mealBefore,
				mealAfter,
				expectedMealAfter,
				atePartialStack,
				contaminationTransferred
			};
		}

		[Tool("zombieland/contamination_tending_contract", Description = "Verify real TendUtility.DoTend transfers contamination from medicine and equalizes doctor/patient contamination.")]
		public static object ContaminationTendingContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var doctorCell, out var doctorSpawnError) == false)
				return doctorSpawnError;
			if (TryFindClearSpawnCell(map, doctorCell + new IntVec3(2, 0, 0), 8f, out var patientCell, out var patientSpawnError) == false)
				return patientSpawnError;
			if (TryFindClearSpawnCell(map, doctorCell + new IntVec3(0, 0, 2), 8f, out var medicineCell, out var medicineSpawnError) == false)
				return medicineSpawnError;

			var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(doctor, doctorCell, map, Rot4.South);
			GenSpawn.Spawn(patient, patientCell, map, Rot4.South);
			DisablePawnWork(doctor);
			DisablePawnWork(patient);
			doctor.needs?.AddOrRemoveNeedsAsAppropriate();
			patient.needs?.AddOrRemoveNeedsAsAppropriate();
			doctor.jobs?.StopAll(false, false);
			patient.jobs?.StopAll(false, false);
			doctor.ClearContamination();
			patient.ClearContamination();
			doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 0;

			var part = patient.health.hediffSet.GetNotMissingParts()
				.FirstOrDefault(record => record.def == BodyPartDefOf.Torso)
				?? patient.health.hediffSet.GetNotMissingParts().FirstOrDefault(record => record.def.alive)
				?? patient.health.hediffSet.GetNotMissingParts().FirstOrDefault();
			var wound = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, part);
			wound.Severity = 5f;
			patient.health.AddHediff(wound, part);

			var medicine = (Medicine)ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
			medicine.stackCount = 2;
			GenSpawn.Spawn(medicine, medicineCell, map, WipeMode.Vanish);
			medicine.ClearContamination();

			const float medicineInitial = 0.50f;
			const float doctorInitial = 0.10f;
			const float patientInitial = 0.00f;
			medicine.AddContamination(medicineInitial);
			doctor.AddContamination(doctorInitial);

			var medicineBefore = medicine.GetContamination();
			var doctorBefore = DescribeContamination(doctor);
			var patientBefore = DescribeContamination(patient);
			var woundNeededTendBefore = patient.health.HasHediffsNeedingTend();

			TendUtility.DoTend(doctor, patient, medicine);

			var medicineAfter = medicine.GetContamination();
			var doctorAfter = DescribeContamination(doctor);
			var patientAfter = DescribeContamination(patient);
			var woundTended = wound.IsTended();

			var medicineTransfer = ZombieSettings.Values.contamination.medicineTransfer;
			var medicineAfterPatient = medicineInitial * (1f - medicineTransfer);
			var expectedPatientAfterMedicine = patientInitial + medicineInitial * medicineTransfer;
			var medicineDoctorTransfer = medicineAfterPatient * medicineTransfer;
			var expectedMedicineAfter = medicineAfterPatient - medicineDoctorTransfer;
			var expectedDoctorAfterMedicine = doctorInitial + medicineDoctorTransfer;
			var equalizeWeight = GenMath.LerpDoubleClamped(
				0,
				20,
				ZombieSettings.Values.contamination.tendEqualizeWorst,
				ZombieSettings.Values.contamination.tendEqualizeBest,
				doctor.skills.GetSkill(SkillDefOf.Medicine).Level
			);
			var high = Mathf.Max(expectedDoctorAfterMedicine, expectedPatientAfterMedicine);
			var low = Mathf.Min(expectedDoctorAfterMedicine, expectedPatientAfterMedicine);
			var highAfterEqualize = high * (1f - equalizeWeight) + low * equalizeWeight;
			var lowAfterEqualize = low + high - highAfterEqualize;
			var expectedDoctorAfter = expectedDoctorAfterMedicine >= expectedPatientAfterMedicine ? highAfterEqualize : lowAfterEqualize;
			var expectedPatientAfter = expectedPatientAfterMedicine >= expectedDoctorAfterMedicine ? highAfterEqualize : lowAfterEqualize;
			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var medicineTransferred = CloseFloat(medicineAfter, expectedMedicineAfter)
				&& medicine.stackCount == 1
				&& medicine.Destroyed == false;
			var doctorPatientEqualized = CloseFloat(doctorAfter.stored, expectedDoctorAfter)
				&& Close(doctorAfter.needLevel, expectedDoctorAfter)
				&& Close(doctorAfter.hediffSeverity, expectedDoctorAfter)
				&& CloseFloat(patientAfter.stored, expectedPatientAfter)
				&& Close(patientAfter.needLevel, expectedPatientAfter)
				&& Close(patientAfter.hediffSeverity, expectedPatientAfter);

			return new
			{
				success = woundNeededTendBefore && woundTended && medicineTransferred && doctorPatientEqualized,
				doctor = DescribePawn(doctor),
				patient = DescribePawn(patient),
				medicine = new
				{
					id = ZombieRuntimeActions.StableThingId(medicine),
					thingId = medicine.ThingID,
					defName = medicine.def?.defName,
					spawned = medicine.Spawned,
					destroyed = medicine.Destroyed,
					stackCount = medicine.stackCount
				},
				doctorCell = ZombieRuntimeActions.DescribeCell(doctorCell),
				patientCell = ZombieRuntimeActions.DescribeCell(patientCell),
				medicineCell = ZombieRuntimeActions.DescribeCell(medicineCell),
				woundNeededTendBefore,
				woundTended,
				medicineBefore,
				medicineAfter,
				expectedMedicineAfter,
				medicineStackAfter = medicine.stackCount,
				medicineTransfer,
				equalizeWeight,
				doctorBefore,
				doctorAfter,
				expectedDoctorAfter,
				patientBefore,
				patientAfter,
				expectedPatientAfter,
				medicineTransferred,
				doctorPatientEqualized
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

		[Tool("zombieland/tar_slime_fire_smoke_contract", Description = "Verify a real burning TarSlime fire tick emits Zombieland TarSmoke instead of only vanilla fire smoke.")]
		public static object TarSlimeFireSmokeContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var spawnError) == false)
				return spawnError;

			ClearFilthAt(map, cell);
			ClearGasAt(map, cell);
			foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
				existingFire.Destroy();

			FilthMaker.TryMakeFilth(cell, map, CustomDefs.TarSlime);
			var tar = map.thingGrid.ThingAt<TarSlime>(cell);
			if (tar == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not create TarSlime fixture."
				};
			}

			var tarSmokeBefore = CountThingsAt(map, cell, CustomDefs.TarSmoke);
			var gasAtCellBefore = cell.GetGas(map)?.def?.defName;
			FireUtility.TryStartFireIn(cell, map, 0.8f, null);
			var sourceFire = cell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (sourceFire == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					tar = ZombieRuntimeActions.StableThingId(tar),
					error = "Could not start a real fire on the TarSlime cell."
				};
			}

			const int sourceDerivedFireUpdateTicks = 15;
			var fireSizeBefore = sourceFire.fireSize;
			AdvanceGameTicks(sourceDerivedFireUpdateTicks);
			var tarSmokeAfter = CountThingsAt(map, cell, CustomDefs.TarSmoke);
			var gasAtCellAfter = cell.GetGas(map)?.def?.defName;

			return new
			{
				success = tarSmokeAfter > tarSmokeBefore && gasAtCellAfter == CustomDefs.TarSmoke.defName,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				tar = ZombieRuntimeActions.StableThingId(tar),
				fire = ZombieRuntimeActions.StableThingId(sourceFire),
				fireSizeBefore,
				fireSizeAfter = sourceFire.Destroyed ? 0f : sourceFire.fireSize,
				ticksToRun = sourceDerivedFireUpdateTicks,
				gasAtCellBefore,
				gasAtCellAfter,
				tarSmokeBefore,
				tarSmokeAfter,
				tarSmokeDelta = tarSmokeAfter - tarSmokeBefore
			};
		}

		[Tool("zombieland/zombie_fire_watcher_excludes_attached_fires", Description = "Verify FireWatcher counts normal fires but excludes fires attached to Zombieland pawns.")]
		public static object ZombieFireWatcherExcludesAttachedFires()
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
			if (fireWatcherUpdateObservationsMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find FireWatcher.UpdateObservations."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var mapFireCell, out var mapFireError) == false)
				return mapFireError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(3, 0, 0), 10f, out var humanCell, out var humanError) == false)
				return humanError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(-3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
				return zombieError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(0, 0, 3), 10f, out var spitterCell, out var spitterError) == false)
				return spitterError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(0, 0, -3), 10f, out var blobCell, out var blobError) == false)
				return blobError;

			foreach (var cell in new[] { mapFireCell, humanCell, zombieCell, spitterCell, blobCell })
			{
				ClearGasAt(map, cell);
				foreach (var fire in cell.GetThingList(map).OfType<Fire>().ToArray())
					fire.Destroy();
			}

			FireUtility.TryStartFireIn(mapFireCell, map, 1.25f, null);
			var mapFire = mapFireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (mapFire == null)
			{
				return new
				{
					success = false,
					mapFireCell = ZombieRuntimeActions.DescribeCell(mapFireCell),
					error = "Could not start a normal map fire."
				};
			}
			mapFire.fireSize = 1.25f;

			var human = SpawnFireFixturePawn(map, humanCell, "human");
			FireUtility.TryAttachFire(human, 2f, null);
			var humanFire = human.GetAttachment(ThingDefOf.Fire) as Fire;

			var zombie = SpawnFireFixturePawn(map, zombieCell, "normal");
			FireUtility.TryAttachFire(zombie, 3f, null);
			var zombieFire = zombie?.GetAttachment(ThingDefOf.Fire) as Fire;

			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			FireUtility.TryAttachFire(spitter, 1.5f, null);
			var spitterFire = spitter?.GetAttachment(ThingDefOf.Fire) as Fire;

			var blob = SpawnFireFixturePawn(map, blobCell, "blob");
			FireUtility.TryAttachFire(blob, 1.75f, null);
			var blobFire = blob?.GetAttachment(ThingDefOf.Fire) as Fire;

			if (humanFire == null || zombieFire == null || spitterFire == null || blobFire == null)
			{
				return new
				{
					success = false,
					mapFire = ZombieRuntimeActions.StableThingId(mapFire),
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					blobFire = ZombieRuntimeActions.StableThingId(blobFire),
					error = "Could not attach human, normal zombie, spitter, and blob fires."
				};
			}

			fireWatcherUpdateObservationsMethod.Invoke(map.fireWatcher, null);
			var fireDangerAfter = map.fireWatcher.FireDanger;
			var expectedExcludingZombie = 0.5f + mapFire.fireSize + 0.5f + humanFire.fireSize;
			var expectedIncludingZombie = expectedExcludingZombie
				+ 0.5f + zombieFire.fireSize
				+ 0.5f + spitterFire.fireSize
				+ 0.5f + blobFire.fireSize;
			var tolerance = 0.001f;

			return new
			{
				success = Math.Abs(fireDangerAfter - expectedExcludingZombie) <= tolerance
					&& Math.Abs(fireDangerAfter - expectedIncludingZombie) > 0.1f,
				mapFireCell = ZombieRuntimeActions.DescribeCell(mapFireCell),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
				blobCell = ZombieRuntimeActions.DescribeCell(blobCell),
				mapFire = ZombieRuntimeActions.StableThingId(mapFire),
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				spitter = DescribeZombie(spitter),
				blob = DescribeZombie(blob),
				humanFire = ZombieRuntimeActions.StableThingId(humanFire),
				zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
				spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
				blobFire = ZombieRuntimeActions.StableThingId(blobFire),
				humanFireParent = ZombieRuntimeActions.StableThingId(humanFire.parent),
				zombieFireParent = ZombieRuntimeActions.StableThingId(zombieFire.parent),
				spitterFireParent = ZombieRuntimeActions.StableThingId(spitterFire.parent),
				blobFireParent = ZombieRuntimeActions.StableThingId(blobFire.parent),
				mapFireSize = mapFire.fireSize,
				humanFireSize = humanFire.fireSize,
				zombieFireSize = zombieFire.fireSize,
				spitterFireSize = spitterFire.fireSize,
				blobFireSize = blobFire.fireSize,
				fireDangerAfter,
				expectedExcludingZombie,
				expectedIncludingZombie,
				tolerance
			};
		}

		[Tool("zombieland/zombie_fire_rain_vulnerability_contract", Description = "Verify zombiesBurnLonger lets attached zombie fires sometimes ignore rain while human fires remain vanilla.")]
		public static object ZombieFireRainVulnerabilityContract(
			[ToolParameter(Description = "Deterministic Rand seed used for the enabled zombie-fire sample.", Required = false, DefaultValue = 737373)] int seed = 737373,
			[ToolParameter(Description = "Number of Fire.VulnerableToRain samples to take while zombiesBurnLonger is enabled.", Required = false, DefaultValue = 100)] int samples = 100)
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

			var cappedSamples = Math.Max(20, Math.Min(samples, 500));
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var fixtureCells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => map.roofGrid.RoofAt(cell) == null)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Take(4)
				.ToArray();
			if (fixtureCells.Length < 4)
			{
				return new
				{
					success = false,
					error = "Could not find four clear unroofed fixture cells for rain-vulnerability sampling."
				};
			}

			var humanCell = fixtureCells[0];
			var zombieCell = fixtureCells[1];
			var spitterCell = fixtureCells[2];
			var blobCell = fixtureCells[3];
			foreach (var cell in fixtureCells)
			{
				ClearGasAt(map, cell);
				foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
					existingFire.Destroy();
			}

			var human = SpawnFireFixturePawn(map, humanCell, "human");
			FireUtility.TryAttachFire(human, 1f, null);
			var humanFire = human.GetAttachment(ThingDefOf.Fire) as Fire;

			var zombie = SpawnFireFixturePawn(map, zombieCell, "normal");
			FireUtility.TryAttachFire(zombie, 1f, null);
			var zombieFire = zombie?.GetAttachment(ThingDefOf.Fire) as Fire;

			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			FireUtility.TryAttachFire(spitter, 1f, null);
			var spitterFire = spitter?.GetAttachment(ThingDefOf.Fire) as Fire;

			var blob = SpawnFireFixturePawn(map, blobCell, "blob");
			FireUtility.TryAttachFire(blob, 1f, null);
			var blobFire = blob?.GetAttachment(ThingDefOf.Fire) as Fire;

			if (humanFire == null || zombieFire == null || spitterFire == null || blobFire == null)
			{
				return new
				{
					success = false,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					blobFire = ZombieRuntimeActions.StableThingId(blobFire),
					error = "Could not attach human, normal zombie, spitter, and blob fires."
				};
			}

			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			try
			{
				ZombieSettings.Values.zombiesBurnLonger = true;
				if (TrySampleRainVulnerability(humanFire, cappedSamples, seed + 1, out var humanTrueEnabled, out var humanFalseEnabled, out var humanError) == false)
				{
					return new
					{
						success = false,
						human = DescribePawn(human),
						humanFire = ZombieRuntimeActions.StableThingId(humanFire),
						error = humanError
					};
				}
				if (TrySampleRainVulnerability(zombieFire, cappedSamples, seed, out var zombieTrueEnabled, out var zombieFalseEnabled, out var zombieError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
						error = zombieError
					};
				}
				if (TrySampleRainVulnerability(spitterFire, cappedSamples, seed + 3, out var spitterTrueEnabled, out var spitterFalseEnabled, out var spitterError) == false)
				{
					return new
					{
						success = false,
						spitter = DescribeZombie(spitter),
						spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
						error = spitterError
					};
				}
				if (TrySampleRainVulnerability(blobFire, cappedSamples, seed + 4, out var blobTrueEnabled, out var blobFalseEnabled, out var blobError) == false)
				{
					return new
					{
						success = false,
						blob = DescribeZombie(blob),
						blobFire = ZombieRuntimeActions.StableThingId(blobFire),
						error = blobError
					};
				}

				ZombieSettings.Values.zombiesBurnLonger = false;
				if (TrySampleRainVulnerability(zombieFire, cappedSamples, seed + 2, out var zombieTrueDisabled, out var zombieFalseDisabled, out var disabledError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
						error = disabledError
					};
				}
				if (TrySampleRainVulnerability(spitterFire, cappedSamples, seed + 5, out var spitterTrueDisabled, out var spitterFalseDisabled, out var spitterDisabledError) == false)
				{
					return new
					{
						success = false,
						spitter = DescribeZombie(spitter),
						spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
						error = spitterDisabledError
					};
				}
				if (TrySampleRainVulnerability(blobFire, cappedSamples, seed + 6, out var blobTrueDisabled, out var blobFalseDisabled, out var blobDisabledError) == false)
				{
					return new
					{
						success = false,
						blob = DescribeZombie(blob),
						blobFire = ZombieRuntimeActions.StableThingId(blobFire),
						error = blobDisabledError
					};
				}

				var humanVanilla = humanTrueEnabled == cappedSamples && humanFalseEnabled == 0;
				var zombieSometimesProtected = zombieTrueEnabled > 0 && zombieFalseEnabled > 0;
				var spitterSometimesProtected = spitterTrueEnabled > 0 && spitterFalseEnabled > 0;
				var blobSometimesProtected = blobTrueEnabled > 0 && blobFalseEnabled > 0;
				var zombieVanillaWhenDisabled = zombieTrueDisabled == cappedSamples && zombieFalseDisabled == 0;
				var spitterVanillaWhenDisabled = spitterTrueDisabled == cappedSamples && spitterFalseDisabled == 0;
				var blobVanillaWhenDisabled = blobTrueDisabled == cappedSamples && blobFalseDisabled == 0;
				return new
				{
					success = humanVanilla
						&& zombieSometimesProtected
						&& spitterSometimesProtected
						&& blobSometimesProtected
						&& zombieVanillaWhenDisabled
						&& spitterVanillaWhenDisabled
						&& blobVanillaWhenDisabled,
					seed,
					samples = cappedSamples,
					humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					blobCell = ZombieRuntimeActions.DescribeCell(blobCell),
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					blobFire = ZombieRuntimeActions.StableThingId(blobFire),
					humanVanilla,
					zombieSometimesProtected,
					spitterSometimesProtected,
					blobSometimesProtected,
					zombieVanillaWhenDisabled,
					spitterVanillaWhenDisabled,
					blobVanillaWhenDisabled,
					humanTrueEnabled,
					humanFalseEnabled,
					zombieTrueEnabled,
					zombieFalseEnabled,
					spitterTrueEnabled,
					spitterFalseEnabled,
					blobTrueEnabled,
					blobFalseEnabled,
					zombieTrueDisabled,
					zombieFalseDisabled,
					spitterTrueDisabled,
					spitterFalseDisabled,
					blobTrueDisabled,
					blobFalseDisabled,
					originalBurnLonger,
					restoredBurnLonger = originalBurnLonger
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
			}
		}

		[Tool("zombieland/zombie_fire_damage_reduction_contract", Description = "Verify zombiesBurnLonger reduces real Fire.DoFireDamage for all Zombieland pawn fire fixtures while humans still take ordinary fire damage.")]
		public static object ZombieFireDamageReductionContract(
			[ToolParameter(Description = "Deterministic Rand seed used for paired Fire.DoFireDamage samples.", Required = false, DefaultValue = 747474)] int seed = 747474,
			[ToolParameter(Description = "Number of paired damage samples per pawn kind.", Required = false, DefaultValue = 12)] int samples = 12)
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
			if (fireDoFireDamageMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find Fire.DoFireDamage(Thing)."
				};
			}

			var cappedSamples = Math.Max(4, Math.Min(samples, 30));
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var fixtureCells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Take(5)
				.ToArray();
			if (fixtureCells.Length < 5)
			{
				return new
				{
					success = false,
					error = "Could not find five clear fixture cells for fire-damage sampling."
				};
			}

			var fireCell = fixtureCells[0];
			foreach (var cell in fixtureCells)
			{
				ClearGasAt(map, cell);
				foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
					existingFire.Destroy();
			}

			FireUtility.TryStartFireIn(fireCell, map, Fire.MaxFireSize, null);
			var fire = fireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (fire == null)
			{
				return new
				{
					success = false,
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					error = "Could not start a real fire for fire-damage sampling."
				};
			}
			fire.fireSize = Fire.MaxFireSize;

			var human = CompareFireDamage(map, fire, fixtureCells[1], "human", cappedSamples, seed);
			var normal = CompareFireDamage(map, fire, fixtureCells[2], "normal", cappedSamples, seed + 1000);
			var spitter = CompareFireDamage(map, fire, fixtureCells[3], "spitter", cappedSamples, seed + 2000);
			var blob = CompareFireDamage(map, fire, fixtureCells[4], "blob", cappedSamples, seed + 3000);

			var tolerance = 0.001f;
			var humanFireDamageControl = human.disabledTotal > tolerance && human.enabledTotal > tolerance;
			var normalReduced = normal.enabledTotal < normal.disabledTotal - tolerance;
			var spitterReduced = spitter.enabledTotal < spitter.disabledTotal - tolerance;
			var blobReduced = blob.enabledTotal < blob.disabledTotal - tolerance;
			var noErrors = new[] { human, normal, spitter, blob }.Any(comparison => comparison.errors.Length > 0) == false;

			return new
			{
				success = noErrors && humanFireDamageControl && normalReduced && spitterReduced && blobReduced,
				seed,
				samples = cappedSamples,
				fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
				fire = ZombieRuntimeActions.StableThingId(fire),
				fireSize = fire.fireSize,
				humanFireDamageControl,
				normalReduced,
				spitterReduced,
				blobReduced,
				noErrors,
				human,
				normal,
				spitter,
				blob,
				tolerance
			};
		}

		[Tool("zombieland/zombie_damage_log_association_suppression", Description = "Verify RimWorld DamageResult combat-log association fills human hediff logs but skips all Zombieland pawn types.")]
		public static object ZombieDamageLogAssociationSuppression()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
				return humanError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
				return zombieError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(-3, 0, 0), 10f, out var spitterCell, out var spitterError) == false)
				return spitterError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 10f, out var blobCell, out var blobError) == false)
				return blobError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();

			var existingBlobs = CurrentZombies(map).OfType<ZombieBlob>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieBlob.Spawn(map, blobCell);
			var blob = CurrentZombies(map).OfType<ZombieBlob>()
				.FirstOrDefault(candidate => existingBlobs.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(blobCell)).FirstOrDefault();

			if (zombie == null || spitter == null || blob == null)
			{
				return new
				{
					success = false,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					error = "Could not create all damage-log fixture pawns."
				};
			}

			var humanResult = DamageAndAssociateWithLog(human, 2f);
			var zombieResult = DamageAndAssociateWithLog(zombie, 2f);
			var spitterResult = DamageAndAssociateWithLog(spitter, 0.5f);
			var blobResult = DamageAndAssociateWithLog(blob, 0.5f);

			var humanAssociated = humanResult.resultHediffCount > 0 && humanResult.combatTextDelta > 0;
			var zombieSuppressed = zombieResult.resultHediffCount > 0 && zombieResult.combatTextDelta == 0;
			var spitterSuppressed = spitterResult.resultHediffCount > 0 && spitterResult.combatTextDelta == 0;
			var blobSuppressed = blobResult.resultHediffCount > 0 && blobResult.combatTextDelta == 0;

			return new
			{
				success = humanAssociated && zombieSuppressed && spitterSuppressed && blobSuppressed,
				humanAssociated,
				zombieSuppressed,
				spitterSuppressed,
				blobSuppressed,
				human = humanResult,
				zombie = zombieResult,
				spitter = spitterResult,
				blob = blobResult
			};
		}

		[Tool("zombieland/zombie_fire_attachment_state_contract", Description = "Verify real fire attachment add/remove keeps Zombie.isOnFire synchronized.")]
		public static object ZombieFireAttachmentStateContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var spawnError) == false)
				return spawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not spawn a normal zombie for fire attachment state."
				};
			}

			var initialHasFire = zombie.HasAttachment(ThingDefOf.Fire);
			var initialIsOnFire = zombie.isOnFire;
			FireUtility.TryAttachFire(zombie, 1f, null);
			var fire = zombie.GetAttachment(ThingDefOf.Fire) as Fire;
			var afterAttachHasFire = zombie.HasAttachment(ThingDefOf.Fire);
			var afterAttachIsOnFire = zombie.isOnFire;

			var comp = zombie.TryGetComp<CompAttachBase>();
			if (fire != null)
				comp?.RemoveAttachment(fire);
			var afterRemoveHasFire = zombie.HasAttachment(ThingDefOf.Fire);
			var afterRemoveIsOnFire = zombie.isOnFire;
			if (fire != null && fire.Destroyed == false)
				fire.Destroy(DestroyMode.Vanish);

			return new
			{
				success = initialHasFire == false
					&& initialIsOnFire == false
					&& fire != null
					&& afterAttachHasFire
					&& afterAttachIsOnFire
					&& afterRemoveHasFire == false
					&& afterRemoveIsOnFire == false,
				zombie = DescribeZombie(zombie),
				fire = ZombieRuntimeActions.StableThingId(fire),
				initialHasFire,
				initialIsOnFire,
				afterAttachHasFire,
				afterAttachIsOnFire,
				afterRemoveHasFire,
				afterRemoveIsOnFire
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

		[Tool("zombieland/wall_push_over_wall_contract", Description = "Build a real single-wall fixture, run the real Stumble job, and verify a zombie is pushed over the wall across the source-derived progress window.")]
		public static object WallPushOverWallContract(
			[ToolParameter(Description = "Optional x cell near which the fixture should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Optional z cell near which the fixture should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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
			if (root.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
			}

			if (TryCreateWallPushFixture(map, root, 20f, out var fixture, out var error) == false)
				return error;

			var zombie = fixture.zombie;
			var wall = fixture.wall;
			var zombieCell = fixture.zombieCell;
			var wallCell = fixture.wallCell;
			var destinationCell = fixture.destinationCell;

			var grid = map.GetGrid();
			ClearWallPushGridNeighborhood(map, zombieCell);

			var sourceDerivedProgressDelta = 0.01f;
			var expectedLandingTicks = 102;
			var maxTicks = 110;
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var primedGridCount = Math.Max(0, effectiveMinimum - 4);
			grid.ChangeZombieCount(zombieCell, primedGridCount);

			var roofBefore = map.roofGrid.RoofAt(destinationCell);
			map.roofGrid.SetRoof(destinationCell, RoofDefOf.RoofConstructed);
			var roofAfterSetup = map.roofGrid.RoofAt(destinationCell);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;

			PrepareWallPushZombie(map, zombie, zombieCell);

			object CaptureSample(int tick)
			{
				return new
				{
					tick,
					gameTick = Find.TickManager.TicksGame,
					absoluteTick = GenTicks.TicksAbs,
					position = ZombieRuntimeActions.DescribeCell(zombie.Position),
					drawPos = DescribeVector(zombie.DrawPos),
					progress = zombie.wallPushProgress,
					pushStart = DescribeVector(zombie.wallPushStart),
					pushDestination = DescribeVector(zombie.wallPushDestination),
					wallPushCooldown = zombie.wallPushCooldown,
					rotation = zombie.Rotation.AsInt,
					currentJob = zombie.CurJobDef?.defName,
					spawned = zombie.Spawned,
					dead = zombie.Dead
				};
			}

			var samples = new List<object>();
			var startedPush = false;
			var landed = false;
			var landingTick = -1;
			try
			{
				ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
				ZombieSettings.Values.dangerousSituationMessage = false;
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
				samples.Add(CaptureSample(0));

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					if (zombie.wallPushProgress >= 0f)
						startedPush = true;
					if (tick == 1 || tick % 10 == 0 || zombie.Position == destinationCell || zombie.wallPushProgress < 0f && startedPush)
						samples.Add(CaptureSample(tick));
					if (zombie.Position == destinationCell && zombie.wallPushProgress < 0f)
					{
						landed = true;
						landingTick = tick;
						break;
					}
				}
			}
			finally
			{
				ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
				ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
			}

			var roofAfterLanding = map.roofGrid.RoofAt(destinationCell);
			var wallDestroyed = wall.Destroyed;
			var wallAtCellAfter = wallCell.GetEdifice(map);
			var gridCountAtZombieCell = grid.GetZombieCount(zombieCell);
			var gridCountAtDestinationCell = grid.GetZombieCount(destinationCell);
			var wallStillPresent = wallDestroyed == false && wallAtCellAfter == wall;
			var roofCleared = roofAfterLanding == null;

			return new
			{
				success = startedPush
					&& landed
					&& zombie.Position == destinationCell
					&& wallStillPresent
					&& roofCleared,
				sourceDerivedProgressDelta,
				expectedLandingTicks,
				maxTicks,
				startedPush,
				landed,
				landingTick,
				effectiveMinimum,
				primedGridCount,
				gridCountAtZombieCell,
				gridCountAtDestinationCell,
				root = ZombieRuntimeActions.DescribeCell(root),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				wallCell = ZombieRuntimeActions.DescribeCell(wallCell),
				destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell),
				wallId = ZombieRuntimeActions.StableThingId(wall),
				wallDestroyed,
				wallStillPresent,
				wallAtCellAfter = wallAtCellAfter?.def?.defName,
				roofBefore = roofBefore?.defName,
				roofAfterSetup = roofAfterSetup?.defName,
				roofAfterLanding = roofAfterLanding?.defName,
				roofCleared,
				zombie = DescribeZombie(zombie),
				samples
			};
		}

		[Tool("zombieland/wall_push_gate_contract", Description = "Verify the source-derived wall-push rejection gates with real one-tick Stumble job samples.")]
		public static object WallPushGateContract(
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var enoughWithSingleWall = Math.Max(0, effectiveMinimum - 4);
			var belowThreshold = Math.Max(0, enoughWithSingleWall - 1);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;
			var caseIndex = 0;
			var allCasesSucceeded = true;

			object RunCase(string name, int settingMinimum, int primedGridCount, Action<WallPushFixture> mutate)
			{
				var caseRoot = root + new IntVec3((caseIndex % 4 - 1) * 12, 0, (caseIndex / 4 - 1) * 12);
				caseIndex++;
				if (caseRoot.InBounds(map) == false)
					caseRoot = root;

				if (TryCreateWallPushFixture(map, caseRoot, 10f, out var fixture, out var setupError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						setupError
					};
				}

				var zombie = fixture.zombie;
				var grid = map.GetGrid();
				ClearWallPushGridNeighborhood(map, fixture.zombieCell);
				grid.ChangeZombieCount(fixture.zombieCell, Math.Max(0, primedGridCount));
				map.roofGrid.SetRoof(fixture.destinationCell, null);
				PrepareWallPushZombie(map, zombie, fixture.zombieCell);

				mutate?.Invoke(fixture);

				var before = new
				{
					position = ZombieRuntimeActions.DescribeCell(zombie.Position),
					progress = zombie.wallPushProgress,
					cooldown = zombie.wallPushCooldown,
					gridAtZombie = grid.GetZombieCount(fixture.zombieCell),
					gridAtDestination = grid.GetZombieCount(fixture.destinationCell),
					roofAtDestination = map.roofGrid.RoofAt(fixture.destinationCell)?.defName,
					wallCount = new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South }
						.Count(direction =>
						{
							var adjacent = fixture.zombieCell + direction;
							return adjacent.InBounds(map) && adjacent.IsWallOrDoor(map);
						}),
					destinationWalkable = fixture.destinationCell.WalkableBy(map, zombie),
					destinationCachedZombie = map.GetComponent<TickManager>()?.allZombiesCached?.Any(cached => cached.Position == fixture.destinationCell) ?? false
				};

				try
				{
					ZombieSettings.Values.minimumZombiesForWallPushing = settingMinimum;
					ZombieSettings.Values.dangerousSituationMessage = false;
					zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
					AdvanceGameTicks(1);
				}
				finally
				{
					ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
					ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
				}

				var stayedOutOfPush = zombie.wallPushProgress < 0f;
				allCasesSucceeded &= stayedOutOfPush;
				return new
				{
					name,
					success = stayedOutOfPush,
					settingMinimum,
					primedGridCount,
					stayedOutOfPush,
					before,
					after = new
					{
						position = ZombieRuntimeActions.DescribeCell(zombie.Position),
						drawPos = DescribeVector(zombie.DrawPos),
						progress = zombie.wallPushProgress,
						pushStart = DescribeVector(zombie.wallPushStart),
						pushDestination = DescribeVector(zombie.wallPushDestination),
						cooldown = zombie.wallPushCooldown,
						currentJob = zombie.CurJobDef?.defName,
						roofAtDestination = map.roofGrid.RoofAt(fixture.destinationCell)?.defName
					},
					zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell)
				};
			}

			IntVec3 ExtraAdjacentCell(WallPushFixture fixture)
			{
				foreach (var direction in new[] { IntVec3.North, IntVec3.South, IntVec3.East, IntVec3.West })
				{
					var cell = fixture.zombieCell + direction;
					if (cell == fixture.wallCell || cell.InBounds(map) == false || cell.Fogged(map))
						continue;
					if (cell.GetEdifice(map) != null || cell.GetFirstThing<Mineable>(map) != null)
						continue;
					if (cell.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					return cell;
				}
				return IntVec3.Invalid;
			}

			var cases = new[]
			{
				RunCase("settingDisabled", 0, effectiveMinimum + 8, null),
				RunCase("belowThreshold", effectiveMinimum, belowThreshold, null),
				RunCase("noAdjacentWall", effectiveMinimum, effectiveMinimum, fixture => fixture.wall.Destroy(DestroyMode.Vanish)),
				RunCase("multipleAdjacentWalls", effectiveMinimum, effectiveMinimum, fixture =>
				{
					var extraWallCell = ExtraAdjacentCell(fixture);
					if (extraWallCell.IsValid)
						_ = SpawnWoodWall(map, extraWallCell);
				}),
				RunCase("blockedDestination", effectiveMinimum, enoughWithSingleWall, fixture => _ = SpawnWoodWall(map, fixture.destinationCell)),
				RunCase("rockRoofDestination", effectiveMinimum, enoughWithSingleWall, fixture => map.roofGrid.SetRoof(fixture.destinationCell, RoofDefOf.RoofRockThick)),
				RunCase("occupiedDestination", effectiveMinimum, enoughWithSingleWall, fixture =>
				{
					var blocker = ZombieRuntimeActions.SpawnZombie(fixture.destinationCell, map, ZombieType.Normal, true);
					if (blocker != null)
						_ = map.GetComponent<TickManager>()?.allZombiesCached?.Add(blocker);
				}),
				RunCase("cooldownActive", effectiveMinimum, enoughWithSingleWall, fixture => fixture.zombie.wallPushCooldown = GenTicks.TicksAbs + 100)
			};

			return new
			{
				success = allCasesSucceeded,
				effectiveMinimum,
				enoughWithSingleWall,
				belowThreshold,
				sourceGates = new[]
				{
					"minimumZombiesForWallPushing == 0",
					"totalZombies < minimum",
					"wallCount != 1",
					"destination.WalkableBy == false",
					"rock roof at destination",
					"cached zombie at destination",
					"wallPushCooldown active"
				},
				cases
			};
		}

		[Tool("zombieland/wall_push_warning_letter_contract", Description = "Verify wall-push warning letters fire only for home-area walls after a real Stumble wall-push start.")]
		public static object WallPushWarningLetterContract(
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var primedGridCount = Math.Max(0, effectiveMinimum - 4);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;
			var allCasesSucceeded = true;

			int DangerousLetterCount()
			{
				return Find.LetterStack?.LettersListForReading?
					.Count(letter => letter?.def == CustomDefs.DangerousSituation) ?? 0;
			}

			object[] DangerousLetters()
			{
				return Find.LetterStack?.LettersListForReading?
					.Where(letter => letter?.def == CustomDefs.DangerousSituation)
					.Select(letter => new
					{
						label = letter.Label,
						defName = letter.def?.defName,
						arrivalTick = letter.arrivalTick
					})
					.Cast<object>()
					.ToArray() ?? Array.Empty<object>();
			}

			object RunCase(string name, IntVec3 caseRoot, bool homeWall)
			{
				if (TryCreateWallPushFixture(map, caseRoot, 10f, out var fixture, out var setupError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						setupError
					};
				}

				var zombie = fixture.zombie;
				var grid = map.GetGrid();
				ClearWallPushGridNeighborhood(map, fixture.zombieCell);
				grid.ChangeZombieCount(fixture.zombieCell, primedGridCount);
				map.roofGrid.SetRoof(fixture.destinationCell, null);
				PrepareWallPushZombie(map, zombie, fixture.zombieCell);

				var originalHome = map.areaManager.Home[fixture.wallCell];
				var beforeLetters = DangerousLetterCount();
				var startedPush = false;
				try
				{
					map.areaManager.Home[fixture.wallCell] = homeWall;
					ClearThrottleKey("DangerousSituation");
					ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
					ZombieSettings.Values.dangerousSituationMessage = true;
					zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
					AdvanceGameTicks(1);
					startedPush = zombie.wallPushProgress >= 0f;
				}
				finally
				{
					map.areaManager.Home[fixture.wallCell] = originalHome;
					ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
					ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
				}

				var afterLetters = DangerousLetterCount();
				var letterDelta = afterLetters - beforeLetters;
				var expectedDelta = homeWall ? 1 : 0;
				var success = startedPush && letterDelta == expectedDelta;
				allCasesSucceeded &= success;
				return new
				{
					name,
					success,
					homeWall,
					startedPush,
					beforeLetters,
					afterLetters,
					letterDelta,
					expectedDelta,
					zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell),
					progress = zombie.wallPushProgress,
					letters = DangerousLetters()
				};
			}

			var outsideHome = RunCase("outsideHomeWall", root + new IntVec3(-12, 0, -12), false);
			var insideHome = RunCase("insideHomeWall", root + new IntVec3(12, 0, -12), true);
			var caseResults = new[] { outsideHome, insideHome };

			return new
			{
				success = allCasesSucceeded,
				effectiveMinimum,
				primedGridCount,
				sourcePath = "ZombieStateHandler.CheckWallPushing -> dangerousSituationMessage && Home[wallCell] -> DangerousSituation letter",
				cases = caseResults
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
