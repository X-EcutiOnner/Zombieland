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

			[Tool("zombieland/ambient_temperature_contract", Description = "Verify Zombieland pawns and zombie corpses report normal ambient temperature while ordinary spawned things keep vanilla map temperature.")]
			public static object AmbientTemperatureContract()
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

				var spawnedThings = new List<Thing>();
				try
				{
					_ = ZombieRuntimeActions.DestroyZombies(map);
					foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
						corpse.Destroy();

					var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
						return humanError;
					if (TryFindClearSpawnCell(map, humanCell + new IntVec3(2, 0, 0), 8f, out var humanCorpseCell, out var humanCorpseError) == false)
						return humanCorpseError;
					if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
						return zombieError;
					if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 12f, out var zombieCorpseCell, out var zombieCorpseError) == false)
						return zombieCorpseError;
					if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterError) == false)
						return spitterError;
					if (TryFindClearSpawnCell(map, humanCell + new IntVec3(2, 0, 3), 8f, out var blobCell, out var blobError) == false)
						return blobError;

					var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(human, humanCell, map, Rot4.South);
					DisablePawnWork(human);
					spawnedThings.Add(human);

					var humanCorpsePawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(humanCorpsePawn, humanCorpseCell, map, Rot4.South);
					DisablePawnWork(humanCorpsePawn);
					spawnedThings.Add(humanCorpsePawn);
					if (ZombieRuntimeActions.KillPawnToCorpse(humanCorpsePawn, out var humanCorpse, out var corpseError) == false)
					{
						return new
						{
							success = false,
							error = corpseError,
							humanCorpsePawn = DescribePawn(humanCorpsePawn)
						};
					}
					if (humanCorpse != null)
						spawnedThings.Add(humanCorpse);

					var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						return new
						{
							success = false,
							error = "ZombieGenerator.SpawnZombie returned no ambient-temperature test zombie."
						};
					}
					spawnedThings.Add(zombie);

					var zombieForCorpse = ZombieRuntimeActions.SpawnZombie(zombieCorpseCell, map, ZombieType.Normal, true);
					if (zombieForCorpse == null)
					{
						return new
						{
							success = false,
							error = "ZombieGenerator.SpawnZombie returned no ambient-temperature corpse zombie."
						};
					}
					spawnedThings.Add(zombieForCorpse);
					zombieForCorpse.Kill(null);
					var zombieCorpse = zombieForCorpse.Corpse as ZombieCorpse
						?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCorpseCell)).FirstOrDefault();
					if (zombieCorpse != null)
						spawnedThings.Add(zombieCorpse);

					var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					ZombieSpitter.Spawn(map, spitterCell);
					var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
						.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
						?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
					if (spitter != null)
						spawnedThings.Add(spitter);

					var existingBlobs = CurrentZombies(map).OfType<ZombieBlob>()
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					ZombieBlob.Spawn(map, blobCell);
					var blob = CurrentZombies(map).OfType<ZombieBlob>()
						.FirstOrDefault(candidate => existingBlobs.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
						?? CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(blobCell)).FirstOrDefault();
					if (blob != null)
						spawnedThings.Add(blob);

					var ordinaryCases = new[]
					{
						DescribeAmbientTemperature("human", human, map, false),
						DescribeAmbientTemperature("humanCorpse", humanCorpse, map, false)
					};
					var zombielandCases = new[]
					{
						DescribeAmbientTemperature("zombie", zombie, map, true),
						DescribeAmbientTemperature("zombieCorpse", zombieCorpse, map, true),
						DescribeAmbientTemperature("spitter", spitter, map, true),
						DescribeAmbientTemperature("blob", blob, map, true)
					};

					return new
					{
						success = ordinaryCases.All(c => c.success) && zombielandCases.All(c => c.success),
						mapTemperature = new
						{
							outdoorTemp = map.mapTemperature.OutdoorTemp
						},
						ordinaryCases,
						zombielandCases
					};
				}
				finally
				{
					foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
						thing.Destroy(DestroyMode.Vanish);
				}
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

		sealed class AmbientTemperatureCase
		{
			public string name { get; set; }
			public string thingId { get; set; }
			public string thingType { get; set; }
			public string defName { get; set; }
			public object position { get; set; }
			public float ambientTemperature { get; set; }
			public float cellTemperature { get; set; }
			public bool expectNormal { get; set; }
			public bool success { get; set; }
		}

		static AmbientTemperatureCase DescribeAmbientTemperature(string name, Thing thing, Map map, bool expectNormal)
		{
			var ambientTemperature = thing?.AmbientTemperature ?? float.NaN;
			var cellTemperature = thing != null && thing.Spawned
				? GenTemperature.GetTemperatureForCell(thing.Position, map)
				: float.NaN;
			var success = thing != null
				&& (expectNormal
					? Mathf.Abs(ambientTemperature - 21f) <= 0.001f
					: Mathf.Abs(ambientTemperature - cellTemperature) <= 0.001f);
			return new AmbientTemperatureCase
			{
				name = name,
				thingId = ZombieRuntimeActions.StableThingId(thing),
				thingType = thing?.GetType().FullName,
				defName = thing?.def?.defName,
				position = thing == null || thing.Spawned == false ? null : ZombieRuntimeActions.DescribeCell(thing.Position),
				ambientTemperature = ambientTemperature,
				cellTemperature = cellTemperature,
				expectNormal = expectNormal,
				success = success
			};
		}

	}
}
