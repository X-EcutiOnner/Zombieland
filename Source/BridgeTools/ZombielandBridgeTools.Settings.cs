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
		const string SettingsScenarioPawnName = "ZL_Settings_Colonist";

		[Tool("zombieland/settings_state", Description = "Read, prepare, or verify a reusable Zombieland settings/keyframe/colonist-toggle persistence fixture.")]
		public static object SettingsState(
			[ToolParameter(Description = "Action mode: read, prepare, verify, modal, behavior, or gizmos.", Required = false, DefaultValue = "read")] string actionMode = "read",
			[ToolParameter(Description = "Open RimWorld's real Zombieland game-settings page/dialog while reading or preparing.", Required = false, DefaultValue = false)] bool openSettingsDialog = false)
		{
			var normalizedMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			if (normalizedMode == "prepare")
			{
				var prepared = PrepareSettingsPersistenceFixture(openSettingsDialog);
				return new
				{
					success = ObjectSuccess(prepared),
					actionMode = normalizedMode,
					state = ReadSettingsPersistenceFixture(openSettingsDialog),
					prepared
				};
			}
			if (normalizedMode == "verify")
			{
				var state = ReadSettingsPersistenceFixture(openSettingsDialog);
				var verification = VerifySettingsPersistenceFixture(state);
				return new
				{
					success = ObjectSuccess(verification),
					actionMode = normalizedMode,
					state,
					verification
				};
			}
			if (normalizedMode == "modal")
			{
				var modalVerification = VerifySettingsModalFixtures(openSettingsDialog);
				return new
				{
					success = ObjectSuccess(modalVerification),
					actionMode = normalizedMode,
					state = ReadSettingsPersistenceFixture(openSettingsDialog),
					modalVerification
				};
			}
			if (normalizedMode == "behavior")
			{
				var behaviorVerification = VerifySettingsBehaviorFixtures();
				return new
				{
					success = ObjectSuccess(behaviorVerification),
					actionMode = normalizedMode,
					state = ReadSettingsPersistenceFixture(openSettingsDialog),
					behaviorVerification
				};
			}
			if (normalizedMode == "gizmos")
			{
				var gizmoVerification = VerifySettingsGizmoFixtures();
				return new
				{
					success = ObjectSuccess(gizmoVerification),
					actionMode = normalizedMode,
					state = ReadSettingsPersistenceFixture(openSettingsDialog),
					gizmoVerification
				};
			}
			if (normalizedMode == "read")
			{
				var state = ReadSettingsPersistenceFixture(openSettingsDialog);
				return new
				{
					success = true,
					actionMode = normalizedMode,
					state
				};
			}
			return new
			{
				success = false,
				actionMode,
				error = "Unsupported settings actionMode. Use read, prepare, verify, modal, behavior, or gizmos."
			};
		}

		static object PrepareSettingsPersistenceFixture(bool openSettingsDialog)
		{
			var map = CurrentMap;
			var settings = ZombieSettings.GetGameSettings();
			if (map == null || settings == null)
			{
				return new
				{
					success = false,
					hasMap = map != null,
					hasGameSettings = settings != null,
					error = "Preparing the settings fixture requires a loaded playable map and game settings world component."
				};
			}

			DestroySettingsFixturePawns(map);

			ZombieSettings.ValuesOverTime = CreateSettingsKeyframes();
			ZombieSettings.Values = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, 0);
			ContaminationFactors.ApplyBaseFactor(ZombieSettings.Values.contamination, ZombieSettings.Values.contaminationBaseFactor);
			SettingsDialog.scrollPosition = Vector2.zero;
			DialogTimeHeader.Reset();

			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 16f, out var cell, out var cellError) == false)
				return cellError;

			var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
			pawn.Name = new NameSingle(SettingsScenarioPawnName);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config != null)
			{
				config.autoAvoidZombies = false;
				config.autoDoubleTap = false;
				config.autoExtractZombieSerum = true;
			}

			var dialogState = MaybeOpenSettingsDialog(openSettingsDialog);
			return new
			{
				success = ZombieSettings.ValuesOverTime.Count == 3
					&& ZombieSettings.Values.attackMode == AttackMode.Everything
					&& pawn.Spawned
					&& config != null
					&& config.autoAvoidZombies == false
					&& config.autoDoubleTap == false
					&& config.autoExtractZombieSerum,
				keyframes = DescribeSettingsKeyframes(ZombieSettings.ValuesOverTime),
				currentValues = DescribeSettingsGroup(ZombieSettings.Values),
				colonist = DescribePawn(pawn),
				colonistConfig = DescribeColonistConfig(config),
				dialogState
			};
		}

		static object ReadSettingsPersistenceFixture(bool openSettingsDialog)
		{
			var settings = ZombieSettings.GetGameSettings();
			var values = ZombieSettings.Values;
			var valuesOverTime = ZombieSettings.ValuesOverTime;
			var defaultValues = ZombieSettingsDefaults.group;
			var defaultValuesOverTime = ZombieSettingsDefaults.groupOverTime;
			var pawn = FindSettingsScenarioPawn(CurrentMap);
			var config = pawn == null ? null : ColonistSettings.Values.ConfigFor(pawn);
			var dialogState = MaybeOpenSettingsDialog(openSettingsDialog);
			var samples = valuesOverTime == null ? null : new
			{
				day0 = DescribeSettingsGroup(ZombieSettings.CalculateInterpolation(valuesOverTime, 0)),
				day1 = DescribeSettingsGroup(ZombieSettings.CalculateInterpolation(valuesOverTime, GenDate.TicksPerDay)),
				day2 = DescribeSettingsGroup(ZombieSettings.CalculateInterpolation(valuesOverTime, 2 * GenDate.TicksPerDay)),
				day3 = DescribeSettingsGroup(ZombieSettings.CalculateInterpolation(valuesOverTime, 3 * GenDate.TicksPerDay)),
				day6 = DescribeSettingsGroup(ZombieSettings.CalculateInterpolation(valuesOverTime, 6 * GenDate.TicksPerDay))
			};

			return new
			{
				hasGameSettings = settings != null,
				settingsCategory = LoadedModManager.GetMod<ZombielandMod>()?.SettingsCategory(),
				values = DescribeSettingsGroup(values),
				keyframes = DescribeSettingsKeyframes(valuesOverTime),
				defaultValues = DescribeSettingsGroup(defaultValues),
				defaultKeyframeCount = defaultValuesOverTime?.Count ?? 0,
				interpolationSamples = samples,
				colonist = DescribePawn(pawn),
				colonistConfig = DescribeColonistConfig(config),
				colonistSettingsCount = ColonistSettings.colonists?.Count ?? 0,
				dialogState
			};
		}

		static object VerifySettingsPersistenceFixture(object state)
		{
			var valuesOverTime = ZombieSettings.ValuesOverTime;
			var day0 = valuesOverTime == null ? null : ZombieSettings.CalculateInterpolation(valuesOverTime, 0);
			var day1 = valuesOverTime == null ? null : ZombieSettings.CalculateInterpolation(valuesOverTime, GenDate.TicksPerDay);
			var day2 = valuesOverTime == null ? null : ZombieSettings.CalculateInterpolation(valuesOverTime, 2 * GenDate.TicksPerDay);
			var day3 = valuesOverTime == null ? null : ZombieSettings.CalculateInterpolation(valuesOverTime, 3 * GenDate.TicksPerDay);
			var day6 = valuesOverTime == null ? null : ZombieSettings.CalculateInterpolation(valuesOverTime, 6 * GenDate.TicksPerDay);
			var pawn = FindSettingsScenarioPawn(CurrentMap);
			var config = pawn == null ? null : ColonistSettings.Values.ConfigFor(pawn);

			var keyframesValid = valuesOverTime?.Count == 3
				&& valuesOverTime[0].Ticks == 0
				&& valuesOverTime[1].Ticks == 2 * GenDate.TicksPerDay
				&& valuesOverTime[2].Ticks == 5 * GenDate.TicksPerDay;
			var interpolationValid = day0 != null
				&& Approximately(day0.threatScale, 0.5f)
				&& day0.maximumNumberOfZombies == 100
				&& day0.attackMode == AttackMode.Everything
				&& Approximately(day1.threatScale, 1.0f)
				&& day1.maximumNumberOfZombies == 200
				&& day1.attackMode == AttackMode.Everything
				&& Approximately(day2.threatScale, 1.5f)
				&& day2.maximumNumberOfZombies == 300
				&& day2.attackMode == AttackMode.OnlyColonists
				&& Approximately(day3.threatScale, 2.0f)
				&& day3.maximumNumberOfZombies == 400
				&& day3.attackMode == AttackMode.OnlyColonists
				&& Approximately(day6.threatScale, 3.0f)
				&& day6.maximumNumberOfZombies == 600
				&& day6.attackMode == AttackMode.OnlyHumans;
			var colonistValid = pawn != null
				&& pawn.Spawned
				&& config != null
				&& config.autoAvoidZombies == false
				&& config.autoDoubleTap == false
				&& config.autoExtractZombieSerum;

			return new
			{
				success = ZombieSettings.GetGameSettings() != null
					&& keyframesValid
					&& interpolationValid
					&& colonistValid,
				hasGameSettings = ZombieSettings.GetGameSettings() != null,
				keyframesValid,
				interpolationValid,
				colonistValid,
				state,
				samples = new
				{
					day0 = DescribeSettingsGroup(day0),
					day1 = DescribeSettingsGroup(day1),
					day2 = DescribeSettingsGroup(day2),
					day3 = DescribeSettingsGroup(day3),
					day6 = DescribeSettingsGroup(day6)
				},
				colonist = DescribePawn(pawn),
				colonistConfig = DescribeColonistConfig(config)
			};
		}

		static object VerifySettingsModalFixtures(bool openSettingsDialog)
		{
			var map = CurrentMap;
			var settings = ZombieSettings.ValuesOverTime?.FirstOrDefault()?.values ?? ZombieSettings.Values;
			if (settings == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland settings group is available for modal verification."
				};
			}

			var apparel = VerifyApparelBlacklistModal(settings);
			var biome = VerifyBiomeListModal(settings);
			var advanced = VerifyAdvancedSettingsModal();
			var thumper = VerifyThumperSettingsModal(map);
			var saveThenUninstall = new
			{
				success = true,
				coveredHere = false,
				reason = "Dialog_SaveThenUninstall is intentionally left to the destructive save-hygiene scenario."
			};
			var dialogState = MaybeOpenSettingsDialog(openSettingsDialog);

			return new
			{
				success = ObjectSuccess(apparel)
					&& ObjectSuccess(biome)
					&& ObjectSuccess(advanced)
					&& ObjectSuccess(thumper),
				apparel,
				biome,
				advanced,
				thumper,
				saveThenUninstall,
				dialogState,
				values = DescribeSettingsGroup(settings)
			};
		}

		static object VerifySettingsBehaviorFixtures()
		{
			var map = CurrentMap;
			var pawn = FindSettingsScenarioPawn(map);
			var config = pawn == null ? null : ColonistSettings.Values.ConfigFor(pawn);
			if (map == null || pawn == null || config == null)
			{
				return new
				{
					success = false,
					hasMap = map != null,
					pawn = DescribePawn(pawn),
					colonistConfig = DescribeColonistConfig(config),
					error = "The settings behavior fixture requires the persisted ZL_Settings_Colonist from settings_state prepare."
				};
			}

			var spawnedThings = new List<Thing>();
			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			try
			{
				var extract = VerifySettingsExtractWorkgiver(map, pawn, spawnedThings);
				var doubleTap = VerifySettingsDoubleTapWorkgiver(map, pawn, spawnedThings);
				var avoidance = VerifySettingsAutoAvoidBehavior(map, pawn, spawnedThings);

				return new
				{
					success = config.autoExtractZombieSerum
						&& config.autoDoubleTap == false
						&& config.autoAvoidZombies == false
						&& ObjectSuccess(extract)
						&& ObjectSuccess(doubleTap)
						&& ObjectSuccess(avoidance),
					pawn = DescribePawn(pawn),
					colonistConfig = DescribeColonistConfig(config),
					extract,
					doubleTap,
					avoidance
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				if (pawn.Destroyed == false)
					pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifySettingsExtractWorkgiver(Map map, Pawn pawn, List<Thing> spawnedThings)
		{
			var oldAmount = ZombieSettings.Values.corpsesExtractAmount;
			ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, oldAmount);
			try
			{
				if (TryFindAdjacentClearCell(pawn, out var zombieCell) == false
					&& TryFindClearSpawnCell(map, pawn.Position + new IntVec3(2, 0, 0), 8f, out zombieCell, out var zombieCellError) == false)
					return zombieCellError;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no zombie for extract workgiver verification."
					};
				}
				spawnedThings.Add(zombie);
				zombie.Kill(null);
				var corpse = zombie.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
				if (corpse != null)
					spawnedThings.Add(corpse);
				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.allZombieCorpses?.Contains(corpse) == false)
					tickManager.allZombieCorpses.Add(corpse);

				var workGiver = new WorkGiver_ExtractZombieSerum();
				var nonForced = corpse != null && workGiver.HasJobOnThing(pawn, corpse, false);
				var forced = corpse != null && workGiver.HasJobOnThing(pawn, corpse, true);
				var job = corpse == null ? null : workGiver.JobOnThing(pawn, corpse, false);
				return new
				{
					success = corpse != null
						&& nonForced
						&& forced
						&& job?.def == CustomDefs.ExtractZombieSerum,
					corpse = DescribeCorpse(corpse),
					nonForced,
					forced,
					jobDef = job?.def?.defName,
					corpsesExtractAmount = ZombieSettings.Values.corpsesExtractAmount
				};
			}
			finally
			{
				ZombieSettings.Values.corpsesExtractAmount = oldAmount;
			}
		}

		static object VerifySettingsDoubleTapWorkgiver(Map map, Pawn pawn, List<Thing> spawnedThings)
		{
			var oldHours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, oldHours);
			try
			{
				if (TryFindAdjacentClearCell(pawn, out var victimCell) == false
					&& TryFindClearSpawnCell(map, pawn.Position + new IntVec3(-2, 0, 0), 8f, out victimCell, out var victimCellError) == false)
					return victimCellError;

				var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(victim, victimCell, map, WipeMode.Vanish);
				spawnedThings.Add(victim);
				if (ZombieRuntimeActions.AddZombieBite(victim, "final", out _, out var biteError) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						error = biteError
					};
				}
				if (ZombieRuntimeActions.KillPawnToCorpse(victim, out var corpse, out var corpseError) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						error = corpseError
					};
				}
				if (corpse != null)
					spawnedThings.Add(corpse);

				var workGiver = new WorkGiver_DoubleTap();
				var nonForced = corpse != null && workGiver.HasJobOnThing(pawn, corpse, false);
				var forced = corpse != null && workGiver.HasJobOnThing(pawn, corpse, true);
				var job = corpse == null ? null : workGiver.JobOnThing(pawn, corpse, true);
				return new
				{
					success = corpse != null
						&& nonForced == false
						&& forced
						&& job?.def == CustomDefs.DoubleTap,
					corpse = DescribeCorpse(corpse),
					nonForced,
					forced,
					jobDef = job?.def?.defName,
					hoursAfterDeathToBecomeZombie = ZombieSettings.Values.hoursAfterDeathToBecomeZombie
				};
			}
			finally
			{
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHours;
			}
		}

		static object VerifySettingsAutoAvoidBehavior(Map map, Pawn pawn, List<Thing> spawnedThings)
		{
			ZombieSettings.Values.betterZombieAvoidance = true;
			if (pawn.Destroyed == false)
				pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);

			if (TryFindAdjacentClearCell(pawn, out var zombieCell) == false
				&& TryFindClearSpawnCell(map, pawn.Position + new IntVec3(0, 0, 2), 8f, out zombieCell, out var zombieCellError) == false)
				return zombieCellError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no zombie for auto-avoid verification."
				};
			}
			spawnedThings.Add(zombie);
			zombie.state = ZombieState.Tracking;
			var avoidGrid = BuildAvoidGridForZombie(map, zombie);
			var avoidCost = AvoidCost(avoidGrid, map, pawn.Position);
			var shouldAvoid = avoidGrid.ShouldAvoid(map, pawn.Position);

			var destination = GenRadial.RadialCellsAround(pawn.Position, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(pawn.Position) >= 8f)
				.FirstOrDefault();
			if (destination.IsValid == false)
			{
				return new
				{
					success = false,
					pawn = DescribePawn(pawn),
					error = "No valid Goto destination was found for auto-avoid verification."
				};
			}

			var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, destination);
			gotoJob.playerForced = false;
			pawn.jobs.StartJob(gotoJob, JobCondition.InterruptForced, null, false, true);
			var startedJob = pawn.CurJobDef?.defName;
			var samples = new List<object>();
			const int maxTicks = 10;
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var currentJob = pawn.CurJob;
				if (tick == 1 || tick == maxTicks || currentJob?.def == JobDefOf.Flee)
				{
					samples.Add(new
					{
						tick,
						job = pawn.CurJobDef?.defName,
						currentJob?.playerForced
					});
				}
				if (currentJob?.def == JobDefOf.Flee)
					break;
			}

			var finalJob = pawn.CurJob;
			var result = new
			{
				success = shouldAvoid
					&& avoidCost > 0
					&& startedJob == JobDefOf.Goto.defName
					&& finalJob?.def != JobDefOf.Flee,
				avoidCost,
				shouldAvoid,
				destination = ZombieRuntimeActions.DescribeCell(destination),
				startedJob,
				finalJob = finalJob?.def?.defName,
				finalJobPlayerForced = finalJob?.playerForced,
				samples
			};
			if (pawn.Destroyed == false)
				pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			return result;
		}

		static object VerifySettingsGizmoFixtures()
		{
			var map = CurrentMap;
			var anchor = FindSettingsScenarioPawn(map);
			if (map == null || anchor == null)
			{
				return new
				{
					success = false,
					hasMap = map != null,
					anchor = DescribePawn(anchor),
					error = "The settings gizmo fixture requires the persisted ZL_Settings_Colonist from settings_state prepare."
				};
			}

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldExtractAmount = ZombieSettings.Values.corpsesExtractAmount;
			var oldHoursAfterDeath = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			Pawn pawn = null;

			try
			{
				ZombieSettings.Values.betterZombieAvoidance = true;
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, ZombieSettings.Values.corpsesExtractAmount);
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, ZombieSettings.Values.hoursAfterDeathToBecomeZombie);

				pawn = CreateSettingsGizmoPawn(map, anchor);
				var config = pawn == null ? null : ColonistSettings.Values.ConfigFor(pawn);
				if (pawn == null || config == null)
				{
					return new
					{
						success = false,
						anchor = DescribePawn(anchor),
						pawn = DescribePawn(pawn),
						error = "Could not create a capable temporary colonist for settings gizmo verification."
					};
				}

				config.autoAvoidZombies = false;
				config.autoDoubleTap = false;
				config.autoExtractZombieSerum = false;

				var priorityWork = pawn.mindState?.priorityWork;
				var priorityWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("ExtractZombieSerum", false);
				if (priorityWork != null && priorityWorkGiver != null)
					priorityWork.Set(pawn.Position, priorityWorkGiver);

				var canDoctor = pawn.CanDoctor();
				var canHunt = pawn.CanHunt();
				var doesAttract = Customization.DoesAttractsZombies(pawn);
				var beforeGizmos = priorityWork?.GetGizmos().ToArray() ?? Array.Empty<Gizmo>();
				var beforeCommands = beforeGizmos.OfType<Command_Action>().ToArray();
				var beforeAvoid = FindSettingsCommand(beforeCommands, "Ignore zombies", "Automatically avoid zombies");
				var beforeExtract = FindSettingsCommand(beforeCommands, "zombie serum");
				var beforeDoubleTap = FindSettingsCommand(beforeCommands, "double tap");
				var vanillaPriorityCommandCount = beforeGizmos.OfType<Command>().Count(command => IsSettingsCommand(command) == false);

				beforeAvoid?.action?.Invoke();
				beforeExtract?.action?.Invoke();
				beforeDoubleTap?.action?.Invoke();

				var afterConfig = new
				{
					config.autoAvoidZombies,
					config.autoDoubleTap,
					config.autoExtractZombieSerum
				};
				var afterGizmos = priorityWork?.GetGizmos().ToArray() ?? Array.Empty<Gizmo>();
				var afterCommands = afterGizmos.OfType<Command_Action>().ToArray();
				var afterAvoid = FindSettingsCommand(afterCommands, "Ignore zombies", "Automatically avoid zombies");
				var afterExtract = FindSettingsCommand(afterCommands, "zombie serum");
				var afterDoubleTap = FindSettingsCommand(afterCommands, "double tap");

				var beforeHasCommands = beforeAvoid?.disabled == false
					&& beforeExtract?.disabled == false
					&& beforeDoubleTap?.disabled == false;
				var actionsFlippedConfig = config.autoAvoidZombies
					&& config.autoDoubleTap
					&& config.autoExtractZombieSerum;
				var afterDescriptionsUpdated = (afterAvoid?.defaultDesc?.Contains("Automatically avoid zombies") ?? false)
					&& (afterExtract?.defaultDesc?.Contains("Automatically extract zombie serum") ?? false)
					&& (afterDoubleTap?.defaultDesc?.Contains("Automatically double tap corpses") ?? false);

				return new
				{
					success = canDoctor
						&& canHunt
						&& doesAttract
						&& beforeHasCommands
						&& actionsFlippedConfig
						&& afterDescriptionsUpdated
						&& vanillaPriorityCommandCount > 0,
					pawn = DescribePawn(pawn),
					anchor = DescribePawn(anchor),
					canDoctor,
					canHunt,
					doesAttract,
					priorityWorkSeeded = priorityWorkGiver != null,
					vanillaPriorityCommandCount,
					beforeConfig = new
					{
						autoAvoidZombies = false,
						autoDoubleTap = false,
						autoExtractZombieSerum = false
					},
					afterConfig,
					beforeGizmos = DescribeSettingsGizmos(beforeGizmos),
					afterGizmos = DescribeSettingsGizmos(afterGizmos),
					beforeHasCommands,
					actionsFlippedConfig,
					afterDescriptionsUpdated
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.corpsesExtractAmount = oldExtractAmount;
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHoursAfterDeath;
				if (pawn != null)
					ColonistSettings.Values.RemoveColonist(pawn);
				if (pawn?.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static Pawn CreateSettingsGizmoPawn(Map map, Pawn anchor)
		{
			for (var attempt = 0; attempt < 60; attempt++)
			{
				if (TryFindClearSpawnCell(map, anchor.Position + new IntVec3(2 + attempt % 5, 0, attempt / 5), 10f, out var cell, out _) == false)
					continue;

				var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				pawn.Name = new NameSingle("ZL_Settings_Gizmo_Colonist");
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				pawn.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
				pawn.workSettings?.SetPriority(WorkTypeDefOf.Doctor, 3);
				pawn.workSettings?.SetPriority(WorkTypeDefOf.Hunting, 3);
				if (pawn.CanDoctor() && pawn.CanHunt() && Customization.DoesAttractsZombies(pawn))
					return pawn;

				ColonistSettings.Values.RemoveColonist(pawn);
				if (pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
			return null;
		}

		static object VerifyApparelBlacklistModal(SettingsGroup settings)
		{
			var candidate = ZombieGenerator.AllApparel.TryGetValue(false, out var apparelByBody)
				? apparelByBody.SelectMany(pair => pair.Value).Select(pair => pair.thing).Distinct().FirstOrDefault()
				: null;
			if (candidate == null)
			{
				return new
				{
					success = false,
					error = "No non-miner zombie apparel candidate exists for the apparel blacklist modal."
				};
			}

			const string invalidDefName = "ZL_Settings_Invalid_Apparel";
			settings.blacklistedApparel = new List<string> { invalidDefName, candidate.defName };
			var dialog = new Dialog_ApparelBlacklist(settings);
			if (Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_ApparelBlacklist), false);
				Find.WindowStack.Add(dialog);
			}
			var opened = Find.WindowStack?.IsOpen(typeof(Dialog_ApparelBlacklist)) == true;
			dialog.PreClose();
			_ = Find.WindowStack?.TryRemove(typeof(Dialog_ApparelBlacklist), false);

			return new
			{
				success = settings.blacklistedApparel.Contains(candidate.defName)
					&& settings.blacklistedApparel.Contains(invalidDefName) == false
					&& opened,
				candidate = candidate.defName,
				opened,
				invalidRemoved = settings.blacklistedApparel.Contains(invalidDefName) == false,
				candidateKept = settings.blacklistedApparel.Contains(candidate.defName),
				blacklistedApparel = settings.blacklistedApparel.ToArray()
			};
		}

		static object VerifyBiomeListModal(SettingsGroup settings)
		{
			var candidate = DefDatabase<BiomeDef>.AllDefsListForReading
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (candidate == null)
			{
				return new
				{
					success = false,
					error = "No biome defs exist for the biome-list modal."
				};
			}

			settings.biomesWithoutZombies = new HashSet<string> { candidate.defName };
			var dialog = new Dialog_BiomeList(settings);
			if (Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_BiomeList), false);
				Find.WindowStack.Add(dialog);
			}
			var opened = Find.WindowStack?.IsOpen(typeof(Dialog_BiomeList)) == true;
			dialog.PreClose();
			_ = Find.WindowStack?.TryRemove(typeof(Dialog_BiomeList), false);

			return new
			{
				success = opened
					&& dialog.allBiomes.Count > 0
					&& settings.biomesWithoutZombies.Contains(candidate.defName),
				candidate = candidate.defName,
				opened,
				biomeCount = dialog.allBiomes.Count,
				candidateKept = settings.biomesWithoutZombies.Contains(candidate.defName),
				biomesWithoutZombies = settings.biomesWithoutZombies.ToArray()
			};
		}

		static object VerifyAdvancedSettingsModal()
		{
			var oldValue = Constants.DEBUG_MAX_ZOMBIE_COUNT;
			var testValue = oldValue == 17 ? 18 : 17;
			Constants.DEBUG_MAX_ZOMBIE_COUNT = testValue;
			var dialog = new Dialog_AdvancedSettings();
			if (Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_AdvancedSettings), false);
				Find.WindowStack.Add(dialog);
			}
			var opened = Find.WindowStack?.IsOpen(typeof(Dialog_AdvancedSettings)) == true;
			dialog.PreClose();
			var saved = Constants.Load();
			var savedValue = saved.TryGetValue(nameof(Constants.DEBUG_MAX_ZOMBIE_COUNT), out var savedEntry)
				? Convert.ToInt32(savedEntry.value)
				: int.MinValue;
			_ = Find.WindowStack?.TryRemove(typeof(Dialog_AdvancedSettings), false);
			Constants.DEBUG_MAX_ZOMBIE_COUNT = oldValue;
			Constants.Save(Constants.Current());

			return new
			{
				success = opened
					&& savedValue == testValue
					&& Constants.DEBUG_MAX_ZOMBIE_COUNT == oldValue,
				opened,
				oldValue,
				testValue,
				savedValue,
				restoredValue = Constants.DEBUG_MAX_ZOMBIE_COUNT
			};
		}

		static object VerifyThumperSettingsModal(Map map)
		{
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map exists for the thumper settings modal."
				};
			}
			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, new IntVec3(map.Size.x / 2 + 8, 0, map.Size.z / 2), 18f, out var thumperCell, out var cellError) == false)
				return cellError;

			var thumper = ThingMaker.MakeThing(CustomDefs.Thumper) as ZombieThumper;
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "Could not create a ZombieThumper for the thumper settings modal."
				};
			}

			thumper.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			thumper.intensity = 0.42f;
			thumper.intervalTicks = GenDate.TicksPerHour / 7;
			var dialog = new Dialog_ThumperSettings(thumper);
			if (Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_ThumperSettings), false);
				Find.WindowStack.Add(dialog);
			}
			var opened = Find.WindowStack?.IsOpen(typeof(Dialog_ThumperSettings)) == true;
			_ = Find.WindowStack?.TryRemove(typeof(Dialog_ThumperSettings), false);
			var result = new
			{
				success = opened
					&& thumper.Spawned
					&& Approximately(thumper.intensity, 0.42f)
					&& thumper.intervalTicks == GenDate.TicksPerHour / 7,
				opened,
				thumper = new
				{
					id = ZombieRuntimeActions.StableThingId(thumper),
					defName = thumper.def?.defName,
					label = thumper.LabelCap,
					position = thumper.Spawned ? ZombieRuntimeActions.DescribeCell(thumper.Position) : null
				},
				thumper.intensity,
				thumper.intervalTicks
			};
			if (thumper.Destroyed == false)
				thumper.Destroy(DestroyMode.Vanish);
			return result;
		}

		static List<SettingsKeyFrame> CreateSettingsKeyframes()
		{
			return new List<SettingsKeyFrame>
			{
				new()
				{
					amount = 0,
					unit = SettingsKeyFrame.Unit.Days,
					values = CreateSettingsGroup(0.5f, 100, AttackMode.Everything, SpawnWhenType.AllTheTime, SpawnHowType.FromTheEdges, SmashMode.DoorsOnly, true, false, 0.25f, 0.75f)
				},
				new()
				{
					amount = 2,
					unit = SettingsKeyFrame.Unit.Days,
					values = CreateSettingsGroup(1.5f, 300, AttackMode.OnlyColonists, SpawnWhenType.WhenDark, SpawnHowType.AllOverTheMap, SmashMode.AnyBuilding, false, true, 0.5f, 1.25f)
				},
				new()
				{
					amount = 5,
					unit = SettingsKeyFrame.Unit.Days,
					values = CreateSettingsGroup(3.0f, 600, AttackMode.OnlyHumans, SpawnWhenType.InEventsOnly, SpawnHowType.FromTheEdges, SmashMode.Nothing, true, true, 0.9f, 2.0f)
				}
			};
		}

		static SettingsGroup CreateSettingsGroup(float threatScale, int maximumNumberOfZombies, AttackMode attackMode, SpawnWhenType spawnWhenType, SpawnHowType spawnHowType, SmashMode smashMode, bool doubleTapRequired, bool betterAvoidance, float infectionChance, float contaminationBaseFactor)
		{
			var group = new SettingsGroup
			{
				threatScale = threatScale,
				maximumNumberOfZombies = maximumNumberOfZombies,
				attackMode = attackMode,
				spawnWhenType = spawnWhenType,
				spawnHowType = spawnHowType,
				smashMode = smashMode,
				doubleTapRequired = doubleTapRequired,
				betterZombieAvoidance = betterAvoidance,
				zombieBiteInfectionChance = infectionChance,
				contaminationBaseFactor = contaminationBaseFactor,
				enemiesAttackZombies = attackMode != AttackMode.OnlyColonists,
				animalsAttackZombies = attackMode == AttackMode.Everything,
				zombiesEatDowned = attackMode != AttackMode.OnlyColonists,
				zombiesEatCorpses = true,
				useCustomTextures = threatScale < 3f,
				healthFactor = Mathf.Max(0.5f, threatScale),
				childChance = Mathf.Min(0.25f, infectionChance / 2f),
				spitterThreat = threatScale,
				minimumZombiesForWallPushing = Mathf.RoundToInt(threatScale * 10f)
			};
			group.blacklistedApparel = new List<string> { "ZL_Settings_Apparel_" + maximumNumberOfZombies };
			group.biomesWithoutZombies = new HashSet<string> { "ZL_Settings_Biome_" + maximumNumberOfZombies };
			return group;
		}

		static Pawn FindSettingsScenarioPawn(Map map)
		{
			return map?.mapPawns?.AllPawnsSpawned
				.FirstOrDefault(pawn => pawn.Name?.ToStringShort == SettingsScenarioPawnName);
		}

		static void DestroySettingsFixturePawns(Map map)
		{
			foreach (var pawn in map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn.Name?.ToStringShort == SettingsScenarioPawnName)
				.ToArray())
			{
				pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static object MaybeOpenSettingsDialog(bool openSettingsDialog)
		{
			var mod = LoadedModManager.GetMod<ZombielandMod>();
			if (openSettingsDialog == false || mod == null || Find.WindowStack == null)
			{
				return new
				{
					requested = openSettingsDialog,
					hasMod = mod != null,
					opened = false
				};
			}

			_ = Find.WindowStack.TryRemove(typeof(Dialog_ModSettings), false);
			var dialog = new Dialog_ModSettings(mod);
			Find.WindowStack.Add(dialog);
			return new
			{
				requested = true,
				hasMod = true,
				opened = Find.WindowStack.IsOpen(typeof(Dialog_ModSettings)),
				settingsCategory = mod.SettingsCategory()
			};
		}

		static object[] DescribeSettingsKeyframes(List<SettingsKeyFrame> keyframes)
		{
			return keyframes?
				.Select(keyframe => new
				{
					keyframe.amount,
					unit = keyframe.unit.ToString(),
					keyframe.Ticks,
					values = DescribeSettingsGroup(keyframe.values)
				})
				.Cast<object>()
				.ToArray() ?? Array.Empty<object>();
		}

		static object DescribeSettingsGroup(SettingsGroup group)
		{
			if (group == null)
				return null;
			return new
			{
				group.threatScale,
				group.maximumNumberOfZombies,
				attackMode = group.attackMode.ToString(),
				spawnWhenType = group.spawnWhenType.ToString(),
				spawnHowType = group.spawnHowType.ToString(),
				smashMode = group.smashMode.ToString(),
				group.enemiesAttackZombies,
				group.animalsAttackZombies,
				group.doubleTapRequired,
				group.betterZombieAvoidance,
				group.zombiesEatDowned,
				group.zombieBiteInfectionChance,
				group.contaminationBaseFactor,
				group.healthFactor,
				group.childChance,
				group.spitterThreat,
				group.minimumZombiesForWallPushing,
				blacklistedApparel = group.blacklistedApparel?.ToArray() ?? Array.Empty<string>(),
				biomesWithoutZombies = group.biomesWithoutZombies?.ToArray() ?? Array.Empty<string>()
			};
		}

		static object DescribeColonistConfig(ColonistConfig config)
		{
			if (config == null)
				return null;
			return new
			{
				config.autoAvoidZombies,
				config.autoDoubleTap,
				config.autoExtractZombieSerum
			};
		}

		static Command_Action FindSettingsCommand(IEnumerable<Command_Action> commands, params string[] fragments)
		{
			return commands.FirstOrDefault(command =>
			{
				var description = command.defaultDesc ?? string.Empty;
				return fragments.Any(fragment => description.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
			});
		}

		static bool IsSettingsCommand(Command command)
		{
			var description = command.defaultDesc ?? string.Empty;
			return description.IndexOf("zombies", StringComparison.OrdinalIgnoreCase) >= 0
				|| description.IndexOf("zombie serum", StringComparison.OrdinalIgnoreCase) >= 0
				|| description.IndexOf("double tap", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static object[] DescribeSettingsGizmos(IEnumerable<Gizmo> gizmos)
		{
			return gizmos.Select((gizmo, index) =>
			{
				var command = gizmo as Command;
				var action = gizmo as Command_Action;
				return new
				{
					index,
					type = gizmo.GetType().FullName,
					label = command?.defaultLabel,
					desc = command?.defaultDesc,
					disabled = command?.disabled ?? false,
					hotKey = command?.hotKey?.defName,
					hasAction = action?.action != null,
					isZombielandSettingsCommand = command != null && IsSettingsCommand(command)
				};
			}).Cast<object>().ToArray();
		}

		static bool Approximately(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;
	}
}
