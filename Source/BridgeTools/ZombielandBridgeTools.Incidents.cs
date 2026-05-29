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

		[Tool("zombieland/incident_alert_wave_contract", Description = "Verify a multi-zombie incident wave spawns zombies and creates the expected RimWorld threat letter.")]
		public static object IncidentAlertWaveContract()
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

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			if (spawnEventProcess == null)
			{
				return new
				{
					success = false,
					error = "Could not find ZombiesRising.SpawnEventProcess by reflection."
				};
			}

			var oldSpawnHowType = ZombieSettings.Values.spawnHowType;
			var spawnedZombies = new List<Zombie>();
			try
			{
				object RunCase(string name, SpawnHowType spawnHowType, string expectedLabelKey)
				{
					ZombieSettings.Values.spawnHowType = spawnHowType;
					var cellValidator = Tools.ZombieSpawnLocator(map, true);
					var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
					if (spot.IsValid == false)
					{
						return new
						{
							name,
							success = false,
							spawnHowType = spawnHowType.ToString(),
							error = "No valid event spawn spot was found."
						};
					}

					var beforeIds = CurrentZombies(map)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.ToHashSet();
					var iterator = spawnEventProcess.Invoke(null, new object[] { map, 4, spot, cellValidator, true, true, ZombieType.Normal }) as System.Collections.IEnumerator;
					if (iterator == null)
					{
						return new
						{
							name,
							success = false,
							spawnHowType = spawnHowType.ToString(),
							spot = ZombieRuntimeActions.DescribeCell(spot),
							error = "SpawnEventProcess did not return an IEnumerator."
						};
					}

					var steps = 0;
					while (steps < 4096 && iterator.MoveNext())
						steps++;

					var after = CurrentZombies(map)
						.OfType<Zombie>()
						.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
						.ToArray();
					spawnedZombies.AddRange(after);
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeLetters.Contains(letter) == false)
						.ToArray();
					var expectedLabel = expectedLabelKey.Translate().ToString();
					var matchingLetters = newLetters
						.Where(letter => letter?.def == LetterDefOf.ThreatSmall && letter.Label == expectedLabel)
						.Select(letter => new
						{
							label = letter.Label,
							defName = letter.def?.defName,
							letter.arrivalTick
						})
						.ToArray();

					return new
					{
						name,
						success = steps < 4096
							&& after.Length == 4
							&& after.All(zombie => MatchesRequestedZombieType(zombie, ZombieType.Normal))
							&& newLetters.Length == 1
							&& matchingLetters.Length == 1,
						spawnHowType = spawnHowType.ToString(),
						expectedLabel,
						spot = ZombieRuntimeActions.DescribeCell(spot),
						steps,
						spawnedCount = after.Length,
						zombies = after.Select(DescribeZombie).ToArray(),
						newLetterCount = newLetters.Length,
						letters = newLetters.Select(letter => new
						{
							label = letter.Label,
							defName = letter.def?.defName,
							letter.arrivalTick
						}).ToArray(),
						matchingLetterCount = matchingLetters.Length
					};
				}

				var edgeCase = RunCase("from_edges_threat_letter", SpawnHowType.FromTheEdges, "LetterLabelZombiesRising");
				var allOverCase = RunCase("all_over_map_threat_letter", SpawnHowType.AllOverTheMap, "LetterLabelZombiesRisingNearYourBase");
				var cases = new[] { edgeCase, allOverCase };
				return new
				{
					success = cases.All(sample => sample.GetType().GetProperty("success")?.GetValue(sample) is true),
					sourcePath = "ZombiesRising.SpawnEventProcess -> zombiesSpawning > 3 -> Find.LetterStack.ReceiveLetter",
					cases
				};
			}
			finally
			{
				ZombieSettings.Values.spawnHowType = oldSpawnHowType;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/random_zombie_type_weights_contract", Description = "Verify ZombieType.Random honors each special-zombie settings weight and the normal fallback weight.")]
		public static object RandomZombieTypeWeightsContract()
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

			var oldSuicideBomberChance = ZombieSettings.Values.suicideBomberChance;
			var oldToxicSplasherChance = ZombieSettings.Values.toxicSplasherChance;
			var oldTankyOperatorChance = ZombieSettings.Values.tankyOperatorChance;
			var oldMinerChance = ZombieSettings.Values.minerChance;
			var oldElectrifierChance = ZombieSettings.Values.electrifierChance;
			var oldAlbinoChance = ZombieSettings.Values.albinoChance;
			var oldDarkSlimerChance = ZombieSettings.Values.darkSlimerChance;
			var oldHealerChance = ZombieSettings.Values.healerChance;
			var spawnedZombies = new List<Zombie>();

			void ClearChances()
			{
				ZombieSettings.Values.suicideBomberChance = 0f;
				ZombieSettings.Values.toxicSplasherChance = 0f;
				ZombieSettings.Values.tankyOperatorChance = 0f;
				ZombieSettings.Values.minerChance = 0f;
				ZombieSettings.Values.electrifierChance = 0f;
				ZombieSettings.Values.albinoChance = 0f;
				ZombieSettings.Values.darkSlimerChance = 0f;
				ZombieSettings.Values.healerChance = 0f;
			}

			void SelectOnly(ZombieType type)
			{
				ClearChances();
				switch (type)
				{
					case ZombieType.SuicideBomber:
						ZombieSettings.Values.suicideBomberChance = 1f;
						break;
					case ZombieType.ToxicSplasher:
						ZombieSettings.Values.toxicSplasherChance = 1f;
						break;
					case ZombieType.TankyOperator:
						ZombieSettings.Values.tankyOperatorChance = 1f;
						break;
					case ZombieType.Miner:
						ZombieSettings.Values.minerChance = 1f;
						break;
					case ZombieType.Electrifier:
						ZombieSettings.Values.electrifierChance = 1f;
						break;
					case ZombieType.Albino:
						ZombieSettings.Values.albinoChance = 1f;
						break;
					case ZombieType.DarkSlimer:
						ZombieSettings.Values.darkSlimerChance = 1f;
						break;
					case ZombieType.Healer:
						ZombieSettings.Values.healerChance = 1f;
						break;
				}
			}

			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
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
				var samples = new List<object>();
				var success = true;
				for (var i = 0; i < types.Length; i++)
				{
					var expectedType = types[i];
					SelectOnly(expectedType);
					var cellRoot = root + new IntVec3((i % 3 - 1) * 4, 0, (i / 3 - 1) * 4);
					if (TryFindClearSpawnCell(map, cellRoot, 20f, out var cell, out var cellError) == false)
					{
						success = false;
						samples.Add(new
						{
							expectedType = expectedType.ToString(),
							success = false,
							cellError
						});
						continue;
					}

					Rand.PushState(6100 + i);
					Zombie zombie;
					try
					{
						zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Random, true);
					}
					finally
					{
						Rand.PopState();
					}

					if (zombie != null)
						spawnedZombies.Add(zombie);
					var matched = MatchesRequestedZombieType(zombie, expectedType);
					success &= matched;
					samples.Add(new
					{
						expectedType = expectedType.ToString(),
						success = matched,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						zombie = DescribeZombie(zombie),
						bodyType = zombie?.story?.bodyType?.defName,
						chances = new
						{
							ZombieSettings.Values.suicideBomberChance,
							ZombieSettings.Values.toxicSplasherChance,
							ZombieSettings.Values.tankyOperatorChance,
							ZombieSettings.Values.minerChance,
							ZombieSettings.Values.electrifierChance,
							ZombieSettings.Values.albinoChance,
							ZombieSettings.Values.darkSlimerChance,
							ZombieSettings.Values.healerChance
						}
					});
				}

				return new
				{
					success,
					sourcePath = "ZombieGenerator.PrepareZombieType -> TryRandomElementByWeight(zombieTypeInitializers)",
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.suicideBomberChance = oldSuicideBomberChance;
				ZombieSettings.Values.toxicSplasherChance = oldToxicSplasherChance;
				ZombieSettings.Values.tankyOperatorChance = oldTankyOperatorChance;
				ZombieSettings.Values.minerChance = oldMinerChance;
				ZombieSettings.Values.electrifierChance = oldElectrifierChance;
				ZombieSettings.Values.albinoChance = oldAlbinoChance;
				ZombieSettings.Values.darkSlimerChance = oldDarkSlimerChance;
				ZombieSettings.Values.healerChance = oldHealerChance;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/child_zombie_generation_contract", Description = "Verify child chance creates child normal zombies without overriding suicide bomber or tanky body rules.")]
		public static object ChildZombieGenerationContract()
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

			if (BodyTypeDefOf.Child == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "BodyTypeDefOf.Child is unavailable in this RimWorld build."
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

			var oldChildChance = ZombieSettings.Values.childChance;
			var spawnedZombies = new List<Zombie>();
			try
			{
				ZombieSettings.Values.childChance = 1f;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				var cases = new[]
				{
					new { name = "normal_child", type = ZombieType.Normal, expectedBody = BodyTypeDefOf.Child, expectedChild = true },
					new { name = "suicide_bomber_adult", type = ZombieType.SuicideBomber, expectedBody = BodyTypeDefOf.Hulk, expectedChild = false },
					new { name = "tanky_adult", type = ZombieType.TankyOperator, expectedBody = BodyTypeDefOf.Fat, expectedChild = false }
				};
				var samples = new List<object>();
				var success = true;
				for (var i = 0; i < cases.Length; i++)
				{
					var entry = cases[i];
					var cellRoot = root + new IntVec3((i - 1) * 4, 0, 8);
					if (TryFindClearSpawnCell(map, cellRoot, 20f, out var cell, out var cellError) == false)
					{
						success = false;
						samples.Add(new
						{
							entry.name,
							success = false,
							cellError
						});
						continue;
					}

					Rand.PushState(6200 + i);
					Zombie zombie;
					try
					{
						zombie = ZombieRuntimeActions.SpawnZombie(cell, map, entry.type, true);
					}
					finally
					{
						Rand.PopState();
					}

					if (zombie != null)
						spawnedZombies.Add(zombie);
					var bodyType = zombie?.story?.bodyType;
					var isChild = bodyType == BodyTypeDefOf.Child;
					var age = zombie?.ageTracker?.AgeBiologicalYearsFloat ?? -1f;
					var ageMatches = entry.expectedChild
						? age >= 4.5f && age <= 15.6f
						: age >= 16.4f;
					var matched = zombie != null
						&& bodyType == entry.expectedBody
						&& isChild == entry.expectedChild
						&& MatchesRequestedZombieType(zombie, entry.type)
						&& ageMatches;
					success &= matched;
					samples.Add(new
					{
						entry.name,
						success = matched,
						requestedType = entry.type.ToString(),
						expectedBody = entry.expectedBody.defName,
						bodyType = bodyType?.defName,
						expectedChild = entry.expectedChild,
						isChild,
						age,
						ageMatches,
						zombie = DescribeZombie(zombie)
					});
				}

				return new
				{
					success,
					childChance = ZombieSettings.Values.childChance,
					sourcePath = "ZombieGenerator.SpawnZombieIterativ -> isChild excludes SuicideBomber and Tanky",
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.childChance = oldChildChance;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/incident_scheduling_contract", Description = "Verify zombie incident scheduler skip reasons and positive incident-size calculation.")]
		public static object IncidentSchedulingContract()
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

			var lastIncidentField = typeof(IncidentInfo).GetField("lastIncident", BindingFlags.Instance | BindingFlags.NonPublic);
			if (lastIncidentField == null)
			{
				return new
				{
					success = false,
					error = "Could not find IncidentInfo.lastIncident by reflection."
				};
			}

			var originalInfo = tickManager.incidentInfo;
			var oldDaysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			var oldSpawnWhenType = ZombieSettings.Values.spawnWhenType;
			var oldMaximumZombies = ZombieSettings.Values.maximumNumberOfZombies;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			var oldBaseNumberOfZombies = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			var oldColonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var oldExtraDaysBetweenEvents = ZombieSettings.Values.extraDaysBetweenEvents;
			var temporaryColonists = new List<Pawn>();

			IncidentInfo NewIncidentInfo()
			{
				var info = new IncidentInfo
				{
					parameters = new IncidentParameters
					{
						daysStretched = -10f,
						scaleFactor = 1f
					}
				};
				lastIncidentField.SetValue(info, -GenDate.TicksPerDay * 100);
				return info;
			}

			object RunWithSeed(int seed, Func<object> action)
			{
				Rand.PushState(seed);
				try
				{
					return action();
				}
				finally
				{
					Rand.PopState();
				}
			}

			bool HasEnoughCapableColonists()
			{
				var colonists = Tools.ColonistsInfo(map);
				var total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
				var minimumCapable = (total + 1) / 3;
				return colonists.Item1 >= minimumCapable;
			}

			bool EnsureCapableColonistFixture(out object error)
			{
				error = null;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				while (HasEnoughCapableColonists() == false && temporaryColonists.Count < 8)
				{
					var candidateRoot = root + new IntVec3(temporaryColonists.Count * 2, 0, 0);
					if (TryFindClearSpawnCell(map, candidateRoot, 32f, out var cell, out error) == false)
						return false;

					var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(pawn, cell, map, Rot4.South);
					pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
					var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
						?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
					var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
					if (weapon == null)
					{
						error = new
						{
							success = false,
							error = "No test ranged weapon def was available for the incident scheduler fixture."
						};
						return false;
					}
					pawn.equipment?.AddEquipment(weapon);
					temporaryColonists.Add(pawn);
				}

				if (HasEnoughCapableColonists())
					return true;

				var colonists = Tools.ColonistsInfo(map);
				var total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
				error = new
				{
					success = false,
					error = "Could not create enough temporary capable colonists for the incident scheduler fixture.",
					capable = colonists.Item1,
					incapable = colonists.Item2,
					total,
					minimumCapable = (total + 1) / 3,
					temporaryColonists = temporaryColonists.Count
				};
				return false;
			}

			try
			{
				if (EnsureCapableColonistFixture(out var fixtureError) == false)
					return fixtureError;

				ZombieSettings.Values.spawnWhenType = SpawnWhenType.AllTheTime;
				ZombieSettings.Values.useDynamicThreatLevel = false;
				ZombieSettings.Values.extraDaysBetweenEvents = 0;
				ZombieSettings.Values.colonyMultiplier = 1f;

				var waiting = RunWithSeed(1101, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = Mathf.CeilToInt(GenDate.DaysPassedFloat) + 10;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 20;
					ZombieSettings.Values.maximumNumberOfZombies = Math.Max(500, tickManager.ZombieCount() + 100);
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "waiting_for_zombies",
						success = result == false && parameters.skipReason == "waiting for zombies",
						result,
						expectedResult = false,
						expectedSkipReason = "waiting for zombies",
						lastIncident,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var empty = RunWithSeed(1102, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = 0;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 0;
					ZombieSettings.Values.maximumNumberOfZombies = 0;
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "empty_incident",
						success = result == false && parameters.skipReason == "empty incident" && parameters.incidentSize == 0,
						result,
						expectedResult = false,
						expectedSkipReason = "empty incident",
						lastIncident,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var positive = RunWithSeed(1103, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = 0;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 20;
					ZombieSettings.Values.maximumNumberOfZombies = Math.Max(500, tickManager.ZombieCount() + 100);
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "positive_incident_size",
						success = result
							&& parameters.skipReason == "-"
							&& parameters.incidentSize > 0
							&& parameters.calculatedZombies > 0
							&& parameters.maxAdditionalZombies > 0
							&& parameters.deltaDays > 0
							&& lastIncident == GenTicks.TicksAbs,
						result,
						expectedResult = true,
						expectedSkipReason = "-",
						lastIncident,
						currentTicks = GenTicks.TicksAbs,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var colonists = Tools.ColonistsInfo(map);
				var cases = new[] { waiting, empty, positive };
				return new
				{
					success = cases.All(sample => sample.GetType().GetProperty("success")?.GetValue(sample) is true),
					map = map.uniqueID,
					threatLevel = ZombieWeather.GetThreatLevel(map),
					colonists = new
					{
						capable = colonists.Item1,
						incapable = colonists.Item2,
						total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count()
					},
					cases
				};
			}
			finally
			{
				tickManager.incidentInfo = originalInfo;
				ZombieSettings.Values.daysBeforeZombiesCome = oldDaysBeforeZombies;
				ZombieSettings.Values.spawnWhenType = oldSpawnWhenType;
				ZombieSettings.Values.maximumNumberOfZombies = oldMaximumZombies;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
				ZombieSettings.Values.baseNumberOfZombiesinEvent = oldBaseNumberOfZombies;
				ZombieSettings.Values.colonyMultiplier = oldColonyMultiplier;
				ZombieSettings.Values.extraDaysBetweenEvents = oldExtraDaysBetweenEvents;
				foreach (var pawn in temporaryColonists)
				{
					if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
						pawn.Corpse.Destroy(DestroyMode.Vanish);
					if (pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

	}
}
