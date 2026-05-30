using HarmonyLib;
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
		const string AreaWorkflowPrefix = "ZL_Area_";

		[Tool("zombieland/area_workflow_state", Description = "Set up and inspect a reusable dangerous-area workflow fixture, including the real manage-areas dialog.")]
		public static object AreaWorkflowState(
			[ToolParameter(Description = "Create or refresh reusable allowed areas for every Zombieland risk mode.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Open RimWorld's real Manage Areas dialog after preparing the selected Zombieland area.", Required = false, DefaultValue = false)] bool openManageDialog = false,
			[ToolParameter(Description = "Optional scenario action: read or behavior.", Required = false, DefaultValue = "read")] string actionMode = "read")
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

			object setup = null;
			if (setupFixture)
				setup = EnsureAreaWorkflowFixture(map);

			var fixtureAreas = FindAreaWorkflowAreas(map);
			var selectedArea = fixtureAreas.FirstOrDefault(area => area.mode == AreaRiskMode.ZombieInside).area
				?? fixtureAreas.FirstOrDefault().area;
			if (selectedArea == null)
			{
				return new
				{
					success = false,
					setupFixture,
					setup,
					error = "No Zombieland area workflow fixture exists; rerun with setupFixture=true."
				};
			}

			var normalizedActionMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			object action = null;
			if (normalizedActionMode == "behavior")
				action = RunAreaWorkflowBehavior(map, fixtureAreas);
			else if (normalizedActionMode == "targeting")
				action = RunAreaWorkflowTargeting(map);
			else if (normalizedActionMode == "ranged-projectiles")
				action = RunAreaWorkflowRangedProjectiles(map);
			else if (normalizedActionMode == "danger-flee")
				action = RunAreaWorkflowDangerFlee(map);
			else if (normalizedActionMode == "smart-melee")
				action = RunAreaWorkflowSmartMelee(map);
			else if (normalizedActionMode == "job-gate")
				action = RunAreaWorkflowJobGate(map);
			else if (normalizedActionMode == "special-melee-verbs")
				action = RunAreaWorkflowSpecialMeleeVerbs(map);
			else if (normalizedActionMode == "electrifier-melee-damage")
				action = RunAreaWorkflowElectrifierMeleeDamage(map);
			else if (normalizedActionMode == "animal-response")
				action = RunAreaWorkflowAnimalResponse(map);
			else if (normalizedActionMode != "read")
			{
				return new
				{
					success = false,
					actionMode,
					error = "Unsupported area workflow actionMode. Use read, behavior, targeting, ranged-projectiles, danger-flee, smart-melee, job-gate, special-melee-verbs, electrifier-melee-damage, or animal-response."
				};
			}
			var actionSucceeded = normalizedActionMode == "read"
				|| (bool)(action?.GetType().GetProperty("success")?.GetValue(action) ?? false);

			var canMakeNewAllowed = map.areaManager.CanMakeNewAllowed();
			var tryMakeNewAllowed = map.areaManager.TryMakeNewAllowed(out Area_Allowed throwawayArea);
			if (throwawayArea != null && map.areaManager.AllAreas.Contains(throwawayArea))
				map.areaManager.Remove(throwawayArea);

			var sortBefore = map.areaManager.AllAreas.Select(area => area.Label).ToArray();
			map.areaManager.SortAreas();
			var sortAfter = map.areaManager.AllAreas.Select(area => area.Label).ToArray();
			var sortPreserved = sortBefore.SequenceEqual(sortAfter);

			Dialog_ManageAreas_Patches.selected = selectedArea;
			Dialog_ManageAreas_Patches.selectedIndex = 999;
			Dialog_ManageAreas_Patches.scrollPosition = new Vector2(11f, 17f);
			_ = new Dialog_ManageAreas(map);
			var constructorReset = Dialog_ManageAreas_Patches.selected == null
				&& Dialog_ManageAreas_Patches.selectedIndex == -1
				&& Dialog_ManageAreas_Patches.scrollPosition == Vector2.zero;

			Dialog_ManageAreas_Patches.selected = selectedArea;
			Dialog_ManageAreas_Patches.selectedIndex = map.areaManager.AllAreas.IndexOf(selectedArea);
			Dialog_ManageAreas_Patches.scrollPosition = Vector2.zero;

			var selectedMode = Dialog_ManageAreas_Patches.GetMode(selectedArea);
			var selectedModeText = selectedMode.ToStringHuman();
			var selectedLabelColor = Dialog_ManageAreas_Patches.AreaLabelColor(selectedArea);
			var expectedSelectedColor = ExpectedAreaLabelColor(selectedMode);
			var selectedColorMatchesMode = ColorsApproximatelyEqual(selectedLabelColor, expectedSelectedColor);

			if (openManageDialog && Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_ManageAreas), false);
				var dialog = new Dialog_ManageAreas(map);
				Dialog_ManageAreas_Patches.selected = selectedArea;
				Dialog_ManageAreas_Patches.selectedIndex = map.areaManager.AllAreas.IndexOf(selectedArea);
				Find.WindowStack.Add(dialog);
			}
			var manageDialogOpened = Find.WindowStack?.IsOpen(typeof(Dialog_ManageAreas)) == true;

			var areas = fixtureAreas
				.Select(pair => new
				{
					label = pair.area.Label,
					mode = pair.mode.ToString(),
					modeText = pair.mode.ToStringHuman(),
					labelColor = DescribeColor(Dialog_ManageAreas_Patches.AreaLabelColor(pair.area)),
					expectedLabelColor = DescribeColor(ExpectedAreaLabelColor(pair.mode)),
					colorMatchesMode = ColorsApproximatelyEqual(Dialog_ManageAreas_Patches.AreaLabelColor(pair.area), ExpectedAreaLabelColor(pair.mode)),
					activeCellCount = pair.area.ActiveCells.Count(),
					index = map.areaManager.AllAreas.IndexOf(pair.area)
				})
				.ToArray();

			return new
			{
				success = canMakeNewAllowed
					&& tryMakeNewAllowed
					&& sortPreserved
					&& constructorReset
					&& areas.Length >= 5
					&& areas.All(area => area.colorMatchesMode)
					&& selectedColorMatchesMode
					&& (openManageDialog == false || manageDialogOpened)
					&& actionSucceeded,
				setupFixture,
				setup,
				openManageDialog,
				manageDialogOpened,
				actionMode = normalizedActionMode,
				actionSucceeded,
				action,
				selected = new
				{
					label = selectedArea.Label,
					mode = selectedMode.ToString(),
					modeText = selectedModeText,
					labelColor = DescribeColor(selectedLabelColor),
					expectedLabelColor = DescribeColor(expectedSelectedColor),
					selectedColorMatchesMode,
					selectedIndex = Dialog_ManageAreas_Patches.selectedIndex
				},
				canMakeNewAllowed,
				tryMakeNewAllowed,
				sortPreserved,
				constructorReset,
				areas
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
				var colonist = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
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

		static object EnsureAreaWorkflowFixture(Map map)
		{
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var cells = GenRadial.RadialCellsAround(root, 4f, true)
				.Where(cell => cell.InBounds(map))
				.Take(20)
				.ToArray();

			var ignore = EnsureAreaWorkflowArea(map, "Ignore", AreaRiskMode.Ignore, new Color(0.45f, 0.45f, 0.45f), cells.Take(4));
			var colonistInside = EnsureAreaWorkflowArea(map, "ColonistInside", AreaRiskMode.ColonistInside, new Color(0.75f, 0.1f, 0.1f), cells.Skip(4).Take(4));
			var colonistOutside = EnsureAreaWorkflowArea(map, "ColonistOutside", AreaRiskMode.ColonistOutside, new Color(0.1f, 0.7f, 0.1f), cells.Skip(8).Take(4));
			var zombieInside = EnsureAreaWorkflowArea(map, "ZombieInside", AreaRiskMode.ZombieInside, new Color(0.9f, 0.45f, 0f), cells.Skip(12).Take(4));
			var zombieOutside = EnsureAreaWorkflowArea(map, "ZombieOutside", AreaRiskMode.ZombieOutside, new Color(1f, 0.1f, 0.55f), cells.Skip(16).Take(4));

			var fixtureAreas = new[] { ignore, colonistInside, colonistOutside, zombieInside, zombieOutside };
			Dialog_ManageAreas_Patches.selected = zombieInside;
			Dialog_ManageAreas_Patches.selectedIndex = map.areaManager.AllAreas.IndexOf(zombieInside);
			Dialog_ManageAreas_Patches.scrollPosition = Vector2.zero;

			return new
			{
				root = ZombieRuntimeActions.DescribeCell(root),
				labels = fixtureAreas.Select(area => area.Label).ToArray(),
				activeCellCounts = fixtureAreas.Select(area => area.ActiveCells.Count()).ToArray()
			};
		}

		static object RunAreaWorkflowBehavior(Map map, (Area_Allowed area, AreaRiskMode mode)[] fixtureAreas)
		{
			var colonistInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ColonistInside).area;
			var zombieInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ZombieInside).area;
			if (colonistInsideArea == null || zombieInsideArea == null)
			{
				return new
				{
					success = false,
					error = "Area workflow behavior needs ColonistInside and ZombieInside fixture areas."
				};
			}

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldHighlightDangerousAreas = ZombieSettings.Values.highlightDangerousAreas;
			try
			{
				ZombieSettings.Values.betterZombieAvoidance = true;
				ZombieSettings.Values.highlightDangerousAreas = true;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-4, 0, 0), 12f, out var actorCell, out var actorError) == false)
					return actorError;
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
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						error = "No nearby clear zombie cell was found for the area workflow behavior fixture."
					};
				}

				var actor = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				actor.Name = new NameSingle("ZL_Area_Worker");
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				actor.playerSettings.AreaRestrictionInPawnCurrentMap = colonistInsideArea;
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no area workflow zombie."
					};
				}
				zombie.Name = new NameSingle("ZL_Area_Zombie");
				zombie.state = ZombieState.Tracking;

				SetAreaCells(map, colonistInsideArea, actor.Position);
				SetAreaCells(map, zombieInsideArea, zombie.Position);
				ZombieSettings.Values.dangerousAreas[colonistInsideArea] = AreaRiskMode.ColonistInside;
				ZombieSettings.Values.dangerousAreas[zombieInsideArea] = AreaRiskMode.ZombieInside;
				RunZombieAreaStateUpdater();

				var colonistInDangerArea = ZombieAreaManager.pawnsInDanger.TryGetValue(actor, out var actorDangerArea)
					&& actorDangerArea == colonistInsideArea;
				var zombieInDangerArea = ZombieAreaManager.pawnsInDanger.TryGetValue(zombie, out var zombieDangerArea)
					&& zombieDangerArea == zombieInsideArea;
				var warningEntries = ZombieAreaManager.pawnsInDanger
					.Select(pair => new
					{
						pawn = DescribePawn(pair.Key),
						area = pair.Value?.Label
					})
					.ToArray();

				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var actorAvoidCost = AvoidCost(avoidGrid, map, actor.Position);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);
				var normalDanger = DangerUtility.GetDangerFor(actor.Position, actor, map);

				var forcedWait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				forcedWait.playerForced = true;
				actor.jobs.StartJob(forcedWait, JobCondition.InterruptForced, null, false, true);
				var forcedDanger = DangerUtility.GetDangerFor(actor.Position, actor, map);
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);

				actor.drafter.Drafted = true;
				var draftedWait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				draftedWait.playerForced = false;
				actor.jobs.StartJob(draftedWait, JobCondition.InterruptForced, null, false, true);
				for (var tick = 0; tick < 5; tick++)
					AdvanceGameTicks(1);
				var draftedJob = actor.CurJobDef?.defName;
				var draftedInterruptedToFlee = actor.CurJobDef == JobDefOf.Flee;
				actor.drafter.Drafted = false;
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = actor.CurJobDef?.defName;
				var tickHit = -1;
				var samples = new List<object>();
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
				var meleeInterruption = VerifyAreaWorkflowMeleeInterruption(map, actorCell, zombie, avoidGrid);

				return new
				{
					success = colonistInDangerArea
						&& zombieInDangerArea
						&& actor.playerSettings.AreaRestrictionInPawnCurrentMap == colonistInsideArea
						&& actorShouldAvoid
						&& actorAvoidCost > 0
						&& normalDanger == Danger.Deadly
						&& forcedDanger != Danger.Deadly
						&& draftedInterruptedToFlee == false
						&& startedJob == JobDefOf.Wait_Combat.defName
						&& tickHit > 0
						&& fleeJob?.playerForced == true
						&& fleeDestinationAvoids
						&& ObjectSuccess(meleeInterruption),
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					assignedArea = actor.playerSettings.AreaRestrictionInPawnCurrentMap?.Label,
					colonistDangerArea = actorDangerArea?.Label,
					zombieDangerArea = zombieDangerArea?.Label,
					warningEntries,
					avoidance = new
					{
						actorAvoidCost,
						actorShouldAvoid,
						normalDanger = normalDanger.ToString(),
						forcedDanger = forcedDanger.ToString()
					},
					drafted = new
					{
						jobAfterTicks = draftedJob,
						interruptedToFlee = draftedInterruptedToFlee
					},
					undrafted = new
					{
						startedJob,
						tickHit,
						maxTicks,
						fleeJob = fleeJob?.def?.defName,
						fleeJobPlayerForced = fleeJob?.playerForced,
						fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
						fleeDestinationAvoids,
						samples
					},
					meleeInterruption
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.highlightDangerousAreas = oldHighlightDangerousAreas;
			}
		}

		static object VerifyAreaWorkflowMeleeInterruption(Map map, IntVec3 root, Zombie zombie, AvoidGrid avoidGrid)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 0), 8f, out var meleeCell, out var cellError) == false)
				return cellError;

			var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, true);
			pawn.Name = new NameSingle("ZL_Area_MeleeInterruption");
			GenSpawn.Spawn(pawn, meleeCell, map, Rot4.South);
			DisablePawnWork(pawn);
			EquipAreaWorkflowMeleeWeapon(pawn);
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config != null)
				config.autoAvoidZombies = true;

			var avoidCost = AvoidCost(avoidGrid, map, pawn.Position);
			var shouldAvoid = avoidGrid.ShouldAvoid(map, pawn.Position);
			var meleeJob = JobMaker.MakeJob(JobDefOf.AttackMelee, zombie);
			meleeJob.playerForced = false;
			pawn.jobs.StartJob(meleeJob, JobCondition.InterruptForced, null, false, true);
			var startedJob = pawn.CurJobDef?.defName;
			var tickHit = -1;
			var samples = new List<object>();
			for (var tick = 1; tick <= 30; tick++)
			{
				AdvanceGameTicks(1);
				var currentJob = pawn.CurJob;
				if (tick == 1 || tick == 30 || currentJob?.def == JobDefOf.Flee)
				{
					samples.Add(new
					{
						tick,
						job = pawn.CurJobDef?.defName,
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
			var fleeJob = pawn.CurJob;
			var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
			var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;
			var result = new
			{
				success = shouldAvoid
					&& avoidCost > 0
					&& startedJob == JobDefOf.AttackMelee.defName
					&& tickHit > 0
					&& fleeJob?.playerForced == true
					&& fleeDestinationAvoids,
				pawn = DescribePawn(pawn),
				target = DescribeZombie(zombie),
				avoidCost,
				shouldAvoid,
				startedJob,
				tickHit,
				fleeJob = fleeJob?.def?.defName,
				fleeJobPlayerForced = fleeJob?.playerForced,
				fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
				fleeDestinationAvoids,
				samples
			};
			if (pawn.Destroyed == false)
				pawn.Destroy(DestroyMode.Vanish);
			return result;
		}

		static void DestroyAreaWorkflowPawns(Map map)
		{
			foreach (var pawn in map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn.Name?.ToStringShort?.StartsWith("ZL_Area_", StringComparison.Ordinal) == true)
				.ToArray())
			{
				pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static void SetAreaCells(Map map, Area area, params IntVec3[] activeCells)
		{
			foreach (var cell in area.ActiveCells.ToArray())
				area[cell] = false;
			foreach (var cell in activeCells)
				if (cell.InBounds(map))
					area[cell] = true;
		}

		static void RunZombieAreaStateUpdater()
		{
			ZombieAreaManager.pawnsInDanger.Clear();
			ZombieAreaManager.lastMap = null;
			var stateUpdater = typeof(ZombieAreaManager)
				.GetMethod("StateUpdater", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, null) as System.Collections.IEnumerator;
			ZombieAreaManager.stateUpdater = stateUpdater;
			for (var i = 0; i < 96; i++)
			{
				if (ZombieAreaManager.stateUpdater.MoveNext())
					continue;
				ZombieAreaManager.stateUpdater = stateUpdater;
				break;
			}
		}

		static object RunAreaWorkflowTargeting(Map map)
		{
			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			var oldAnimalsAttackZombies = ZombieSettings.Values.animalsAttackZombies;
			var oldDoubleTapRequired = ZombieSettings.Values.doubleTapRequired;
			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var spawnedThings = new List<Thing>();

			try
			{
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.enemiesAttackZombies = true;
				ZombieSettings.Values.animalsAttackZombies = true;
				ZombieSettings.Values.doubleTapRequired = true;
				ZombieSettings.Values.betterZombieAvoidance = false;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-10, 0, 0), 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;

				var targetCells = GenRadial.RadialCellsAround(shooterCell, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.Take(10)
					.ToArray();
				if (targetCells.Length < 8)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						targetCellCount = targetCells.Length,
						error = "Not enough line-of-sight target cells were found for the mixed-targeting fixture."
					};
				}

				var player = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_PlayerShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_EnemyShooter", shooterCell + new IntVec3(0, 0, -2), Faction.OfAncientsHostile, spawnedThings);
				var animal = SpawnAreaWorkflowAnimal(map, "ZL_Area_TestAnimal", shooterCell + new IntVec3(0, 0, 2), spawnedThings);
				if (player == null || enemy == null || animal == null)
				{
					return new
					{
						success = false,
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						error = "Could not create all mixed-targeting attacker pawns."
					};
				}

				var playerVerb = player.equipment?.PrimaryEq?.PrimaryVerb;
				var enemyVerb = enemy.equipment?.PrimaryEq?.PrimaryVerb;
				var animalVerb = animal.CurrentEffectiveVerb;
				if (playerVerb == null || enemyVerb == null || animalVerb == null)
				{
					return new
					{
						success = false,
						playerVerb = DescribeVerb(playerVerb),
						enemyVerb = DescribeVerb(enemyVerb),
						animalVerb = DescribeVerb(animalVerb),
						error = "At least one targeting attacker had no effective verb."
					};
				}

				var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_NormalTarget", spawnedThings);
				var roped = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_RopedTarget", spawnedThings);
				var confused = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_ConfusedTarget", spawnedThings);
				var electric = SpawnTargetZombie(map, targetCells[3], ZombieType.Electrifier, "ZL_Area_ElectricTarget", spawnedThings);
				var albino = SpawnTargetZombie(map, targetCells[4], ZombieType.Albino, "ZL_Area_AlbinoTarget", spawnedThings);
				var suicide = SpawnTargetZombie(map, targetCells[5], ZombieType.SuicideBomber, "ZL_Area_SuicideTarget", spawnedThings);
				var spitter = SpawnTargetSpitter(map, targetCells[6], "ZL_Area_SpitterTarget", spawnedThings);
				var blob = SpawnTargetBlob(map, targetCells[7], "ZL_Area_BlobTarget", spawnedThings);
				if (new Pawn[] { normal, roped, confused, electric, albino, suicide, spitter, blob }.Any(pawn => pawn == null))
				{
					return new
					{
						success = false,
						targets = new
						{
							normal = DescribeZombie(normal),
							roped = DescribeZombie(roped),
							confused = DescribeZombie(confused),
							electric = DescribeZombie(electric),
							albino = DescribeZombie(albino),
							suicide = DescribeZombie(suicide),
							spitter = DescribeZombie(spitter as ZombieSpitter),
							blob = DescribeZombie(blob as ZombieBlob)
						},
						error = "Could not create all mixed-targeting zombie pawns."
					};
				}
				roped.ropedBy = player;
				confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
				RefreshZombieTargetCache(map);

				var allTargets = new List<IAttackTarget> { normal, roped, confused, electric, albino, suicide, spitter, blob };
				var playerAvailable = InvokeAvailableTargetsPatch(allTargets, player, playerVerb);
				var playerTargetIds = TargetIds(playerAvailable);
				var playerBest = AttackTargetFinder.BestAttackTarget(player, TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				var playerRopedBest = BestSpecificTarget(player, roped);
				var playerConfusedBest = BestSpecificTarget(player, confused);

				ZombieSettings.Values.enemiesAttackZombies = false;
				var enemyDisabled = InvokeAvailableTargetsPatch(allTargets, enemy, enemyVerb);
				var enemyDisabledIds = TargetIds(enemyDisabled);
				var enemyNormalDisabledBest = BestSpecificTarget(enemy, normal);
				ZombieSettings.Values.enemiesAttackZombies = true;
				var enemyEnabled = InvokeAvailableTargetsPatch(allTargets, enemy, enemyVerb);
				var enemyEnabledIds = TargetIds(enemyEnabled);
				var enemyBest = AttackTargetFinder.BestAttackTarget(enemy, TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				var enemyNormalEnabledBest = BestSpecificTarget(enemy, normal);
				var enemySpitterBest = BestSpecificTarget(enemy, spitter);
				var enemyBlobBest = BestSpecificTarget(enemy, blob);
				var enemyElectricBest = BestSpecificTarget(enemy, electric);

				ZombieSettings.Values.animalsAttackZombies = false;
				var animalDisabledBest = AttackTargetFinder.BestAttackTarget(animal, TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				ZombieSettings.Values.animalsAttackZombies = true;
				var animalEnabledBest = AttackTargetFinder.BestAttackTarget(animal, TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);

				var normalScore = InvokeShootingScorePatch(normal, player, playerVerb);
				var suicideScore = InvokeShootingScorePatch(suicide, player, playerVerb);

				var oldFriendlyFireRadius = playerVerb.verbProps.ai_AvoidFriendlyFireRadius;
				float zombieBlastOffset;
				float colonistBlastOffset;
				float zombieConeOffset;
				float colonistConeOffset;
				try
				{
					playerVerb.verbProps.ai_AvoidFriendlyFireRadius = Math.Max(3f, oldFriendlyFireRadius);
					zombieBlastOffset = InvokeFriendlyFireOffset("FriendlyFireBlastRadiusTargetScoreOffset", normal, player, playerVerb);
					zombieConeOffset = InvokeFriendlyFireOffset("FriendlyFireConeTargetScoreOffset", normal, player, playerVerb);
					var friendly = SpawnAreaWorkflowPawn(map, "ZL_Area_FriendlyNearby", normal.Position + IntVec3.East, Faction.OfPlayer, spawnedThings);
					colonistBlastOffset = InvokeFriendlyFireOffset("FriendlyFireBlastRadiusTargetScoreOffset", normal, player, playerVerb);
					colonistConeOffset = InvokeFriendlyFireOffset("FriendlyFireConeTargetScoreOffset", normal, player, playerVerb);
				}
				finally
				{
					playerVerb.verbProps.ai_AvoidFriendlyFireRadius = oldFriendlyFireRadius;
				}

				var friendlyFirePatchOwners = new[]
				{
					PatchOwners("FriendlyFireBlastRadiusTargetScoreOffset"),
					PatchOwners("FriendlyFireConeTargetScoreOffset")
				};
				var friendlyFireHelper = VerifyFriendlyFireHelper(normal, player);
				var availablePatchOwners = PatchOwners("GetAvailableShootingTargetsByScore");
				var scorePatchOwners = PatchOwners("GetShootingTargetScore");
				var directHostility = VerifyAreaWorkflowDirectHostility(map, player, enemy, normal, spitter, blob, shooterCell, spawnedThings);
				var targetCache = VerifyAreaWorkflowTargetCache(map, normal, spitter, blob, shooterCell, spawnedThings);
				var availableBranches = VerifyAreaWorkflowAvailableTargetBranches(map, shooterCell, allTargets, normal, roped, confused, electric, albino, spitter, blob, spawnedThings);
				var waitAutoAttack = VerifyAreaWorkflowWaitAutoAttack(map, shooterCell, spawnedThings);
				var turretTargeting = VerifyAreaWorkflowTurretTargeting(map, shooterCell, player, spawnedThings);
				var tarSmokeMeleeTargeting = VerifyAreaWorkflowTarSmokeMeleeTargeting(map, shooterCell, spawnedThings);
				var tarSmokeAimChance = VerifyAreaWorkflowTarSmokeAimChance(map, shooterCell, spawnedThings);
				var downedCombat = VerifyAreaWorkflowDownedCombat(map, shooterCell, spawnedThings);
				var rangedProjectilePatches = VerifyAreaWorkflowRangedProjectilePatches(map, shooterCell + new IntVec3(24, 0, 13), spawnedThings);

				var success = playerTargetIds.Contains(StableId(normal))
					&& playerTargetIds.Contains(StableId(roped)) == false
					&& playerTargetIds.Contains(StableId(confused)) == false
					&& (playerVerb.CanHarmElectricZombies() || playerTargetIds.Contains(StableId(electric)) == false)
					&& playerRopedBest == null
					&& playerConfusedBest == null
					&& enemyDisabledIds.Contains(StableId(normal)) == false
					&& enemyEnabledIds.Contains(StableId(normal))
					&& enemyEnabledIds.Contains(StableId(spitter)) == false
					&& enemyEnabledIds.Contains(StableId(blob)) == false
					&& enemyEnabledIds.Contains(StableId(albino)) == false
					&& (enemyVerb.CanHarmElectricZombies() || enemyEnabledIds.Contains(StableId(electric)) == false)
					&& enemyNormalDisabledBest == null
					&& ReferenceEquals(enemyNormalEnabledBest?.Thing, normal)
					&& enemySpitterBest == null
					&& enemyBlobBest == null
					&& (enemyVerb.CanHarmElectricZombies() || enemyElectricBest == null)
					&& animalDisabledBest == null
					&& ReferenceEquals(animalEnabledBest?.Thing, normal)
					&& suicideScore > normalScore
					&& colonistBlastOffset < zombieBlastOffset
					&& friendlyFirePatchOwners.All(ownerSet => ownerSet.Contains("net.pardeike.zombieland"))
					&& availablePatchOwners.Contains("net.pardeike.zombieland")
					&& scorePatchOwners.Contains("net.pardeike.zombieland")
					&& friendlyFireHelper.zombiesRemoved
					&& friendlyFireHelper.nonZombiesKept
					&& ObjectSuccess(directHostility)
					&& ObjectSuccess(targetCache)
					&& ObjectSuccess(availableBranches)
					&& ObjectSuccess(waitAutoAttack)
					&& ObjectSuccess(turretTargeting)
					&& ObjectSuccess(tarSmokeMeleeTargeting)
					&& ObjectSuccess(tarSmokeAimChance)
					&& ObjectSuccess(downedCombat)
					&& ObjectSuccess(rangedProjectilePatches);

				return new
				{
					success,
					attackers = new
					{
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						playerVerb = DescribeVerb(playerVerb),
						enemyVerb = DescribeVerb(enemyVerb),
						animalVerb = DescribeVerb(animalVerb)
					},
					targets = new
					{
						normal = DescribeZombie(normal),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						electric = DescribeZombie(electric),
						albino = DescribeZombie(albino),
						suicide = DescribeZombie(suicide),
						spitter = DescribeZombie(spitter as ZombieSpitter),
						blob = DescribeZombie(blob as ZombieBlob)
					},
					available = new
					{
						player = DescribeTargetPairs(playerAvailable),
						enemyDisabled = DescribeTargetPairs(enemyDisabled),
						enemyEnabled = DescribeTargetPairs(enemyEnabled)
					},
					bestTargets = new
					{
						player = DescribeTarget(playerBest),
						playerRoped = DescribeTarget(playerRopedBest),
						playerConfused = DescribeTarget(playerConfusedBest),
						enemy = DescribeTarget(enemyBest),
						enemyNormalDisabled = DescribeTarget(enemyNormalDisabledBest),
						enemyNormalEnabled = DescribeTarget(enemyNormalEnabledBest),
						enemySpitter = DescribeTarget(enemySpitterBest),
						enemyBlob = DescribeTarget(enemyBlobBest),
						enemyElectric = DescribeTarget(enemyElectricBest),
						animalDisabled = DescribeTarget(animalDisabledBest),
						animalEnabled = DescribeTarget(animalEnabledBest)
					},
					scores = new
					{
						normalScore,
						suicideScore
					},
					friendlyFire = new
					{
						zombieBlastOffset,
						colonistBlastOffset,
						zombieConeOffset,
						colonistConeOffset,
						patchOwners = friendlyFirePatchOwners,
						helper = new
						{
							friendlyFireHelper.zombiesRemoved,
							friendlyFireHelper.nonZombiesKept,
							kept = friendlyFireHelper.kept
						}
					},
					patchOwners = new
					{
						availableTargets = availablePatchOwners,
						shootingScore = scorePatchOwners
					},
					directHostility,
					targetCache,
					availableBranches,
					waitAutoAttack,
					turretTargeting,
					tarSmokeMeleeTargeting,
					tarSmokeAimChance,
					downedCombat,
					rangedProjectilePatches
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				ZombieSettings.Values.animalsAttackZombies = oldAnimalsAttackZombies;
				ZombieSettings.Values.doubleTapRequired = oldDoubleTapRequired;
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowRangedProjectiles(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowRangedProjectilePatches(map, new IntVec3(114, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowDangerFlee(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowDangerAndFlee(map, new IntVec3(91, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSmartMelee(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowSmartMelee(map, new IntVec3(104, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowJobGate(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowJobGate(map, new IntVec3(88, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSpecialMeleeVerbs(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowSpecialMeleeVerbs(map, new IntVec3(104, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowElectrifierMeleeDamage(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowElectrifierMeleeDamage(map, new IntVec3(118, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowAnimalResponse(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowAnimalResponse(map, new IntVec3(80, 0, 142), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static Pawn SpawnAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			var pawn = GenerateAreaWorkflowPawn(faction, false);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn GenerateAreaWorkflowPawn(Faction faction, bool mustBeCapableOfViolence)
		{
			var request = new PawnGenerationRequest(
				PawnKindDefOf.Colonist,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				canGeneratePawnRelations: false,
				mustBeCapableOfViolence: mustBeCapableOfViolence,
				colonistRelationChanceFactor: 0f,
				forceNoIdeo: true,
				dontGiveWeapon: true,
				forceNoGear: true);
			return PawnGenerator.GeneratePawn(request);
		}

		static Pawn SpawnArmedAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			Pawn pawn = null;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				var candidate = GenerateAreaWorkflowPawn(faction, true);
				if (candidate.WorkTagIsDisabled(WorkTags.Violent))
					continue;
				pawn = candidate;
				break;
			}
			pawn ??= GenerateAreaWorkflowPawn(faction, true);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawnedThings.Add(pawn);
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon != null)
				pawn.equipment.AddEquipment(weapon);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowMech(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			var kindDef = PawnKindDefOf.Mech_Scyther
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
			if (kindDef == null)
				return null;
			var request = new PawnGenerationRequest(
				kindDef,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				canGeneratePawnRelations: false,
				colonistRelationChanceFactor: 0f,
				forceNoIdeo: true);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowAnimal(Map map, string name, IntVec3 cell, List<Thing> spawnedThings)
		{
			var kindDef = DefDatabase<PawnKindDef>.GetNamed("Warg", false)
				?? DefDatabase<PawnKindDef>.GetNamed("Husky", false)
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh);
			if (kindDef == null)
				return null;
			var pawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfPlayer);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowAnimal(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings, Predicate<PawnKindDef> predicate)
		{
			var kindDef = DefDatabase<PawnKindDef>.AllDefs
				.Where(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh)
				.Where(def => predicate?.Invoke(def) != false)
				.OrderByDescending(def => def.combatPower)
				.FirstOrDefault();
			if (kindDef == null)
				return null;
			var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Zombie SpawnTargetZombie(Map map, IntVec3 cell, ZombieType type, string name, List<Thing> spawnedThings)
		{
			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, type, true);
			if (zombie == null)
				return null;
			zombie.Name = new NameSingle(name);
			zombie.state = ZombieState.Tracking;
			spawnedThings.Add(zombie);
			return zombie;
		}

		static ZombieSpitter SpawnTargetSpitter(Map map, IntVec3 cell, string name, List<Thing> spawnedThings)
		{
			var existing = CurrentZombies(map).OfType<ZombieSpitter>().Select(StableId).ToHashSet();
			ZombieSpitter.Spawn(map, cell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter == null)
				return null;
			spitter.Name = new NameSingle(name);
			spitter.state = SpitterState.Idle;
			spawnedThings.Add(spitter);
			return spitter;
		}

		static ZombieBlob SpawnTargetBlob(Map map, IntVec3 cell, string name, List<Thing> spawnedThings)
		{
			var existing = CurrentZombies(map).OfType<ZombieBlob>().Select(StableId).ToHashSet();
			ZombieBlob.Spawn(map, cell);
			var blob = CurrentZombies(map).OfType<ZombieBlob>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieBlob>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (blob == null)
				return null;
			blob.Name = new NameSingle(name);
			spawnedThings.Add(blob);
			return blob;
		}

		static void RefreshZombieTargetCache(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager?.allZombiesCached == null)
				return;
			tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
			foreach (var zombie in CurrentZombies(map).OfType<Zombie>())
				_ = tickManager.allZombiesCached.Add(zombie);
		}

		static List<Pair<IAttackTarget, float>> InvokeAvailableTargetsPatch(List<IAttackTarget> targets, IAttackTargetSearcher searcher, Verb verb)
		{
			var rawTargets = targets.ToList();
			var prefix = typeof(AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			prefix?.Invoke(null, new object[] { rawTargets, searcher, verb });
			var result = rawTargets.Select(target => new Pair<IAttackTarget, float>(target, 0f)).ToList();
			var postfix = typeof(AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			postfix?.Invoke(null, new object[] { result, searcher, verb });
			return result;
		}

		static float InvokeShootingScorePatch(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
		{
			var prefix = typeof(AttackTargetFinder_GetShootingTargetScore_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var args = new object[] { searcher, target, verb, 0f };
			var runOriginal = (bool)(prefix?.Invoke(null, args) ?? true);
			if (runOriginal == false)
				return (float)args[3];
			var method = typeof(AttackTargetFinder).GetMethod("GetShootingTargetScore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return method == null ? 0f : (float)method.Invoke(null, new object[] { target, searcher, verb });
		}

		static IAttackTarget BestSpecificTarget(IAttackTargetSearcher searcher, Thing target)
		{
			return AttackTargetFinder.BestAttackTarget(
				searcher,
				TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat,
				thing => ReferenceEquals(thing, target),
				0f,
				20f);
		}

		static (bool zombiesRemoved, bool nonZombiesKept, object[] kept) VerifyFriendlyFireHelper(Zombie zombie, Pawn nonZombie)
		{
			var method = typeof(AttackTargetFinder_FriendlyFire_Patch).GetMethod("RemoveZombies", BindingFlags.Static | BindingFlags.NonPublic);
			var input = new List<Thing> { zombie, nonZombie };
			var kept = method?.Invoke(null, new object[] { input }) as List<Thing> ?? new List<Thing>();
			return (
				kept.Any(thing => thing is Zombie) == false,
				kept.Contains(nonZombie),
				kept.Select(thing => new
				{
					id = StableId(thing),
					defName = thing?.def?.defName,
					label = thing?.LabelCap
				}).Cast<object>().ToArray());
		}

		static object VerifyAreaWorkflowDirectHostility(Map map, Pawn player, Pawn enemy, Zombie normal, ZombieSpitter spitter, ZombieBlob blob, IntVec3 root, List<Thing> spawnedThings)
		{
			var settings = ZombieSettings.Values;
			var oldEnemiesAttackZombies = settings.enemiesAttackZombies;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			var oldAttackMode = settings.attackMode;
			var oldEnemyInfectionState = enemy.InfectionState();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);

			try
			{
				if (TryFindClearSpawnCell(map, root + new IntVec3(4, 0, 4), 10f, out var wildAnimalCell, out var cellError) == false)
					return cellError;

				if (zombieFaction == null)
				{
					return new
					{
						success = false,
						error = "No zombie faction was loaded."
					};
				}

				var wildAnimal = SpawnAreaWorkflowAnimal(map, "ZL_Area_WildHostilityAnimal", wildAnimalCell, spawnedThings);
				if (wildAnimal == null)
				{
					return new
					{
						success = false,
						error = "Could not create a non-colony animal for direct hostility checks."
					};
				}
				wildAnimal.SetFaction(null);

				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				settings.attackMode = AttackMode.Everything;
				var playerThing = TryHostileTo(player, normal);
				var enemyThingDisabled = TryHostileTo(enemy, normal);
				var animalThingDisabled = TryHostileTo(wildAnimal, normal);
				var normalThreatToPlayer = GenHostility.IsActiveThreatTo(normal, Faction.OfPlayer, false, false);
				var normalThreatToNull = GenHostility.IsActiveThreatTo(normal, null, false, false);
				var normalThreatToEnemyDisabled = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				var enemyThingEnabled = TryHostileTo(enemy, normal);
				var animalThingEnabled = TryHostileTo(wildAnimal, normal);
				var enemyFaction = TryHostileTo(enemy, zombieFaction);
				var spitterToEnemyFaction = TryHostileTo(spitter, enemy.Faction);
				var blobToEnemyFaction = TryHostileTo(blob, enemy.Faction);
				var normalThreatToEnemyEverything = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.attackMode = AttackMode.OnlyColonists;
				var normalThreatToEnemyOnlyColonists = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.attackMode = AttackMode.Everything;
				enemy.SetInfectionState(InfectionState.Infecting);
				var enemyThingInfecting = TryHostileTo(enemy, normal);

				var noErrors = new[]
				{
					playerThing,
					enemyThingDisabled,
					enemyThingEnabled,
					animalThingDisabled,
					animalThingEnabled,
					enemyFaction,
					spitterToEnemyFaction,
					blobToEnemyFaction,
					enemyThingInfecting
				}.All(sample => sample.error == null);

				var success = noErrors
					&& playerThing.value
					&& enemyThingDisabled.value == false
					&& enemyThingEnabled.value
					&& animalThingDisabled.value == false
					&& animalThingEnabled.value
					&& enemyFaction.value
					&& spitterToEnemyFaction.value == false
					&& blobToEnemyFaction.value == false
					&& enemyThingInfecting.value == false
					&& normalThreatToPlayer == false
					&& normalThreatToNull == false
					&& normalThreatToEnemyDisabled == false
					&& normalThreatToEnemyEverything
					&& normalThreatToEnemyOnlyColonists == false;

				return new
				{
					success,
					noErrors,
					samples = new
					{
						playerThing = DescribeHostility(playerThing),
						enemyThingDisabled = DescribeHostility(enemyThingDisabled),
						enemyThingEnabled = DescribeHostility(enemyThingEnabled),
						animalThingDisabled = DescribeHostility(animalThingDisabled),
						animalThingEnabled = DescribeHostility(animalThingEnabled),
						enemyFaction = DescribeHostility(enemyFaction),
						spitterToEnemyFaction = DescribeHostility(spitterToEnemyFaction),
						blobToEnemyFaction = DescribeHostility(blobToEnemyFaction),
						enemyThingInfecting = DescribeHostility(enemyThingInfecting)
					},
					activeThreat = new
					{
						normalThreatToPlayer,
						normalThreatToNull,
						normalThreatToEnemyDisabled,
						normalThreatToEnemyEverything,
						normalThreatToEnemyOnlyColonists
					}
				};
			}
			finally
			{
				settings.enemiesAttackZombies = oldEnemiesAttackZombies;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
				settings.attackMode = oldAttackMode;
				enemy.SetInfectionState(oldEnemyInfectionState);
			}
		}

		static object VerifyAreaWorkflowTargetCache(Map map, Zombie normal, ZombieSpitter spitter, ZombieBlob blob, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(4, 0, -4), 10f, out var hostileCell, out var cellError) == false)
				return cellError;

			var hostile = SpawnAreaWorkflowPawn(map, "ZL_Area_CacheHostile", hostileCell, Faction.OfAncientsHostile, spawnedThings);
			var cache = map.attackTargetsCache;
			cache.UpdateTarget(hostile);
			cache.UpdateTarget(normal);
			cache.UpdateTarget(spitter);
			cache.UpdateTarget(blob);

			var before = cache.TargetsHostileToColony;
			var containsHostileBefore = before.Contains(hostile);
			var containsNormalBefore = before.Contains(normal);
			var containsSpitterBefore = before.Contains(spitter);
			var containsBlobBefore = before.Contains(blob);
			var zombielandTargetsBefore = before
				.Select(target => target.Thing)
				.Where(thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieBlob)
				.Select(thing => thing.def?.defName)
				.Distinct()
				.OrderBy(defName => defName)
				.ToArray();

			var hostileDescription = DescribePawn(hostile);
			hostile.Destroy(DestroyMode.Vanish);
			cache.UpdateTarget(hostile);
			var after = cache.TargetsHostileToColony;
			var containsHostileAfter = after.Contains(hostile);

			return new
			{
				success = containsHostileBefore
					&& containsNormalBefore == false
					&& containsSpitterBefore == false
					&& containsBlobBefore == false
					&& zombielandTargetsBefore.Length == 0
					&& containsHostileAfter == false,
				hostile = hostileDescription,
				before = new
				{
					count = before.Count,
					containsHostile = containsHostileBefore,
					containsNormal = containsNormalBefore,
					containsSpitter = containsSpitterBefore,
					containsBlob = containsBlobBefore,
					zombielandTargets = zombielandTargetsBefore
				},
				after = new
				{
					count = after.Count,
					containsHostile = containsHostileAfter
				}
			};
		}

		static object VerifyAreaWorkflowAvailableTargetBranches(
			Map map,
			IntVec3 root,
			List<IAttackTarget> allTargets,
			Zombie normal,
			Zombie roped,
			Zombie confused,
			Zombie electric,
			Zombie albino,
			ZombieSpitter spitter,
			ZombieBlob blob,
			List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-4, 0, 4), 12f, out var friendlyHumanCell, out var friendlyHumanCellError) == false)
				return friendlyHumanCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(-2, 0, 6), 12f, out var playerMechCell, out var playerMechCellError) == false)
				return playerMechCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 6), 12f, out var friendlyMechCell, out var friendlyMechCellError) == false)
				return friendlyMechCellError;

			var friendlyHuman = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_FriendlyHumanSearcher", friendlyHumanCell, null, spawnedThings);
			var playerMech = SpawnAreaWorkflowMech(map, "ZL_Area_PlayerMechSearcher", playerMechCell, Faction.OfPlayer, spawnedThings);
			var friendlyMech = SpawnAreaWorkflowMech(map, "ZL_Area_FriendlyMechSearcher", friendlyMechCell, null, spawnedThings);
			if (friendlyHuman == null || playerMech == null || friendlyMech == null)
			{
				return new
				{
					success = false,
					friendlyHuman = DescribePawn(friendlyHuman),
					playerMech = DescribePawn(playerMech),
					friendlyMech = DescribePawn(friendlyMech),
					error = "Could not create all available-target branch pawn searchers."
				};
			}

			var friendlyHumanVerb = friendlyHuman.equipment?.PrimaryEq?.PrimaryVerb;
			var playerMechVerb = playerMech.CurrentEffectiveVerb;
			var friendlyMechVerb = friendlyMech.CurrentEffectiveVerb;
			if (friendlyHumanVerb == null || playerMechVerb == null || friendlyMechVerb == null)
			{
				return new
				{
					success = false,
					friendlyHumanVerb = DescribeVerb(friendlyHumanVerb),
					playerMechVerb = DescribeVerb(playerMechVerb),
					friendlyMechVerb = DescribeVerb(friendlyMechVerb),
					error = "At least one available-target branch searcher had no effective verb."
				};
			}

			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(4, 0, 6), Faction.OfPlayer, spawnedThings, out var playerTurret, out var playerTurretError) == false)
				return playerTurretError;
			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(8, 0, 6), Faction.OfAncientsHostile, spawnedThings, out var enemyTurret, out var enemyTurretError) == false)
				return enemyTurretError;

			var playerTurretVerb = playerTurret.CurrentEffectiveVerb;
			var enemyTurretVerb = enemyTurret.CurrentEffectiveVerb;
			if (playerTurretVerb == null || enemyTurretVerb == null)
			{
				return new
				{
					success = false,
					playerTurretVerb = DescribeVerb(playerTurretVerb),
					enemyTurretVerb = DescribeVerb(enemyTurretVerb),
					error = "At least one available-target branch turret had no effective verb."
				};
			}

			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			var oldAnimalsAttackZombies = ZombieSettings.Values.animalsAttackZombies;
			try
			{
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.enemiesAttackZombies = true;
				ZombieSettings.Values.animalsAttackZombies = true;

				var friendlyHumanIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, friendlyHuman, friendlyHumanVerb));
				var friendlyMechIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, friendlyMech, friendlyMechVerb));
				var playerThingIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerTurret, playerTurretVerb));

				ZombieSettings.Values.attackMode = AttackMode.OnlyHumans;
				var playerMechOnlyHumansIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerMech, playerMechVerb));

				ZombieSettings.Values.attackMode = AttackMode.Everything;
				var playerMechEverythingIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerMech, playerMechVerb));

				ZombieSettings.Values.enemiesAttackZombies = false;
				var enemyThingDisabledIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, enemyTurret, enemyTurretVerb));

				ZombieSettings.Values.enemiesAttackZombies = true;
				var enemyThingEnabledIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, enemyTurret, enemyTurretVerb));

				var cases = new[]
				{
					DescribeAvailableBranchCase(
						"friendlyHumanEverything",
						friendlyHumanIds,
						ContainsTarget(friendlyHumanIds, normal)
							&& ContainsTarget(friendlyHumanIds, roped) == false
							&& ContainsTarget(friendlyHumanIds, confused) == false
							&& ContainsTarget(friendlyHumanIds, albino) == false
							&& ContainsTarget(friendlyHumanIds, spitter) == false
							&& ContainsTarget(friendlyHumanIds, blob) == false
							&& (friendlyHumanVerb.CanHarmElectricZombies() || ContainsTarget(friendlyHumanIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"friendlyMechEverything",
						friendlyMechIds,
						ContainsTarget(friendlyMechIds, normal)
							&& ContainsTarget(friendlyMechIds, roped) == false
							&& ContainsTarget(friendlyMechIds, confused) == false
							&& ContainsTarget(friendlyMechIds, albino) == false
							&& ContainsTarget(friendlyMechIds, spitter) == false
							&& ContainsTarget(friendlyMechIds, blob) == false
							&& (friendlyMechVerb.CanHarmElectricZombies() || ContainsTarget(friendlyMechIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"playerMechOnlyHumans",
						playerMechOnlyHumansIds,
						playerMechOnlyHumansIds.Length == 0,
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"playerMechEverything",
						playerMechEverythingIds,
						ContainsTarget(playerMechEverythingIds, normal)
							&& ContainsTarget(playerMechEverythingIds, roped) == false
							&& ContainsTarget(playerMechEverythingIds, confused) == false
							&& ContainsTarget(playerMechEverythingIds, albino)
							&& ContainsTarget(playerMechEverythingIds, spitter)
							&& ContainsTarget(playerMechEverythingIds, blob)
							&& (playerMechVerb.CanHarmElectricZombies() || ContainsTarget(playerMechEverythingIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"playerThingEverything",
						playerThingIds,
						ContainsTarget(playerThingIds, normal)
							&& ContainsTarget(playerThingIds, roped) == false
							&& ContainsTarget(playerThingIds, confused) == false
							&& ContainsTarget(playerThingIds, albino) == false
							&& ContainsTarget(playerThingIds, spitter)
							&& ContainsTarget(playerThingIds, blob)
							&& (playerTurretVerb.CanHarmElectricZombies() || ContainsTarget(playerThingIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"enemyThingDisabled",
						enemyThingDisabledIds,
						enemyThingDisabledIds.Length == 0,
						normal, roped, confused, electric, albino, spitter, blob),
					DescribeAvailableBranchCase(
						"enemyThingEnabled",
						enemyThingEnabledIds,
						ContainsTarget(enemyThingEnabledIds, normal)
							&& ContainsTarget(enemyThingEnabledIds, roped)
							&& ContainsTarget(enemyThingEnabledIds, confused)
							&& ContainsTarget(enemyThingEnabledIds, albino) == false
							&& ContainsTarget(enemyThingEnabledIds, spitter) == false
							&& ContainsTarget(enemyThingEnabledIds, blob) == false
							&& (enemyTurretVerb.CanHarmElectricZombies() || ContainsTarget(enemyThingEnabledIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, blob)
				};

				return new
				{
					success = cases.All(ObjectSuccess),
					attackers = new
					{
						friendlyHuman = DescribePawn(friendlyHuman),
						friendlyHumanVerb = DescribeVerb(friendlyHumanVerb),
						playerMech = DescribePawn(playerMech),
						playerMechVerb = DescribeVerb(playerMechVerb),
						friendlyMech = DescribePawn(friendlyMech),
						friendlyMechVerb = DescribeVerb(friendlyMechVerb),
						playerTurret = DescribeTarget(playerTurret),
						playerTurretVerb = DescribeVerb(playerTurretVerb),
						enemyTurret = DescribeTarget(enemyTurret),
						enemyTurretVerb = DescribeVerb(enemyTurretVerb)
					},
					cases
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				ZombieSettings.Values.animalsAttackZombies = oldAnimalsAttackZombies;
			}
		}

		static object DescribeAvailableBranchCase(string label, string[] ids, bool success, params IAttackTarget[] targets)
		{
			return new
			{
				success,
				label,
				ids,
				contains = targets.ToDictionary(
					target => StableId(target),
					target => ids.Contains(StableId(target)))
			};
		}

		static bool ContainsTarget(string[] ids, IAttackTarget target)
		{
			return ids.Contains(StableId(target));
		}

		static bool SpawnAreaWorkflowTurretGun(
			Map map,
			IntVec3 root,
			Faction faction,
			List<Thing> spawnedThings,
			out Building_TurretGun turret,
			out object error)
		{
			turret = null;
			error = null;

			var turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.thingClass != null && typeof(Building_TurretGun).IsAssignableFrom(def.thingClass));
			if (turretDef == null)
			{
				error = new
				{
					success = false,
					error = "No Building_TurretGun ThingDef was available for non-pawn targeting checks."
				};
				return false;
			}

			if (TryFindClearBuildingFootprint(map, turretDef, root, 18f, out var turretCell, out error) == false)
				return false;

			var turretStuff = turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) ?? ThingDefOf.Steel : null;
			turret = ThingMaker.MakeThing(turretDef, turretStuff) as Building_TurretGun;
			if (turret == null)
			{
				error = new
				{
					success = false,
					turretDef = turretDef.defName,
					error = "The selected turret def did not create a Building_TurretGun."
				};
				return false;
			}
			turret.SetFactionDirect(faction);
			GenSpawn.Spawn(turret, turretCell, map, Rot4.North, WipeMode.Vanish, false);
			spawnedThings.Add(turret);

			var power = turret.GetComp<CompPowerTrader>();
			if (power != null)
				power.PowerOn = true;
			return true;
		}

		static object VerifyAreaWorkflowTarSmokeMeleeTargeting(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root + new IntVec3(10, 0, 8), out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			ClearGasAt(map, targetCell);
			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_TarSmokeMeleeActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var target = SpawnAreaWorkflowPawn(map, "ZL_Area_TarSmokeMeleeTarget", targetCell, Faction.OfAncientsHostile, spawnedThings);
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = DescribePawn(target),
					error = "The tar-smoke melee actor had no effective melee verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;
			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			if (smoke != null)
				spawnedThings.Add(smoke);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);

			return new
			{
				success = verb.IsMeleeAttack
					&& canHitBeforeSmoke
					&& gasAtTargetBefore == null
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				gasAtTargetBefore,
				gasAtTargetAfter,
				canHitBeforeSmoke,
				canHitAfterSmoke
			};
		}

		static object VerifyAreaWorkflowTarSmokeAimChance(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-12, 0, 10), 16f, out var actorCell, out var actorError) == false)
				return actorError;
			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 6f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No line-of-sight target cell was found for the tar-smoke aim-chance fixture."
				};
			}

			ClearGasAt(map, targetCell);
			var actor = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_TarSmokeAimActor", actorCell, Faction.OfPlayer, spawnedThings);
			var target = SpawnAreaWorkflowPawn(map, "ZL_Area_TarSmokeAimTarget", targetCell, Faction.OfAncientsHostile, spawnedThings);
			var verb = actor?.equipment?.PrimaryEq?.PrimaryVerb;
			if (actor == null || target == null || verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = DescribePawn(target),
					verb = DescribeVerb(verb),
					error = "Could not create the tar-smoke aim-chance fixture."
				};
			}

			var reportBeforeSmoke = ShotReport.HitReportFor(actor, verb, target);
			var aimChanceBeforeSmoke = reportBeforeSmoke.AimOnTargetChance_StandardTarget;
			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;

			var syntheticCoverSmoke = ThingMaker.MakeThing(CustomDefs.TarSmoke);
			var reportWithSyntheticCover = reportBeforeSmoke;
			var boxedReport = (object)reportWithSyntheticCover;
			AccessTools.Field(typeof(ShotReport), "covers")?.SetValue(boxedReport, new List<CoverInfo> { new CoverInfo(syntheticCoverSmoke, 1f) });
			reportWithSyntheticCover = (ShotReport)boxedReport;
			var aimChanceWithSyntheticCoverSmoke = reportWithSyntheticCover.AimOnTargetChance_StandardTarget;

			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			if (smoke != null)
				spawnedThings.Add(smoke);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceAfterTargetSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& syntheticCoverSmoke?.def == CustomDefs.TarSmoke
					&& aimChanceWithSyntheticCoverSmoke == 0f
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke == false
					&& aimChanceAfterTargetSmoke == 0f,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceWithSyntheticCoverSmoke,
				aimChanceAfterTargetSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter,
				coverBranchMode = "synthetic ShotReport.covers TarSmoke entry"
			};
		}

		static object VerifyAreaWorkflowTurretTargeting(Map map, IntVec3 root, Pawn roper, List<Thing> spawnedThings)
		{
			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(0, 0, 12), Faction.OfPlayer, spawnedThings, out var turret, out var turretError) == false)
				return turretError;

			var verb = turret.CurrentEffectiveVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					error = "The spawned turret had no CurrentEffectiveVerb."
				};
			}

			var maxDistance = Math.Min(verb.verbProps.range - 1f, 18f);
			var targetCells = GenRadial.RadialCellsAround(turret.Position, maxDistance, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstThing<Mineable>(map) == null)
				.Where(cell => cell.DistanceTo(turret.Position) >= 6f)
				.Where(cell => GenSight.LineOfSight(turret.Position, cell, map, true))
				.Where(cell => verb.CanHitTargetFrom(turret.Position, cell))
				.OrderBy(cell => cell.DistanceToSquared(turret.Position))
				.Take(6)
				.ToArray();
			if (targetCells.Length < 6)
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					verb = DescribeVerb(verb),
					targetCellCount = targetCells.Length,
					error = "Not enough turret-visible target cells were found."
				};
			}

			var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_TurretNormal", spawnedThings);
			var roped = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_TurretRoped", spawnedThings);
			var confused = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_TurretConfused", spawnedThings);
			var electric = SpawnTargetZombie(map, targetCells[3], ZombieType.Electrifier, "ZL_Area_TurretElectric", spawnedThings);
			var spitter = SpawnTargetSpitter(map, targetCells[4], "ZL_Area_TurretSpitter", spawnedThings);
			var blob = SpawnTargetBlob(map, targetCells[5], "ZL_Area_TurretBlob", spawnedThings);
			if (new Pawn[] { normal, roped, confused, electric, spitter, blob }.Any(pawn => pawn == null))
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					targets = new
					{
						normal = DescribeZombie(normal),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						electric = DescribeZombie(electric),
						spitter = DescribeZombie(spitter),
						blob = DescribeZombie(blob)
					},
					error = "Could not create all turret-targeting zombie fixtures."
				};
			}

			roped.ropedBy = roper;
			confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
			electric.electricDisabledUntil = GenTicks.TicksGame - 1;
			RefreshZombieTargetCache(map);

			var flags = TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat;
			var normalBest = BestSpecificTarget(turret, normal);
			var ropedBest = BestSpecificTarget(turret, roped);
			var confusedBest = BestSpecificTarget(turret, confused);
			var electricBest = BestSpecificTarget(turret, electric);
			var spitterBest = BestSpecificTarget(turret, spitter);
			var blobBest = BestSpecificTarget(turret, blob);
			var generalBest = AttackTargetFinder.BestAttackTarget(
				turret,
				flags,
				thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieBlob,
				0f,
				Math.Max(20f, verb.verbProps.range));
			var rejectedIds = new[] { StableId(roped), StableId(confused), StableId(electric) };
			var generalBestThing = generalBest?.Thing;
			var generalBestAllowed = generalBestThing switch
			{
				Zombie zombie => zombie.IsRopedOrConfused == false && (verb.CanHarmElectricZombies() || zombie.IsActiveElectric == false),
				ZombieSpitter => true,
				ZombieBlob => true,
				_ => false
			};
			var potentialIds = map.attackTargetsCache.GetPotentialTargetsFor(turret).Select(StableId).Where(id => id != null).ToArray();
			var patchOwners = PatchOwners("BestAttackTarget");

			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland")
					&& verb.CanHarmElectricZombies() == false
					&& electric.IsActiveElectric
					&& ReferenceEquals(normalBest?.Thing, normal)
					&& ropedBest == null
					&& confusedBest == null
					&& electricBest == null
					&& ReferenceEquals(spitterBest?.Thing, spitter)
					&& ReferenceEquals(blobBest?.Thing, blob)
					&& generalBestAllowed
					&& rejectedIds.Contains(StableId(generalBest)) == false,
				patchOwners,
				turret = DescribeTarget(turret),
				verb = DescribeVerb(verb),
				canHarmElectric = verb.CanHarmElectricZombies(),
				targets = new
				{
					normal = DescribeZombie(normal),
					roped = DescribeZombie(roped),
					confused = DescribeZombie(confused),
					electric = DescribeZombie(electric),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob)
				},
				bestTargets = new
				{
					normal = DescribeTarget(normalBest),
					roped = DescribeTarget(ropedBest),
					confused = DescribeTarget(confusedBest),
					electric = DescribeTarget(electricBest),
					spitter = DescribeTarget(spitterBest),
					blob = DescribeTarget(blobBest),
					general = DescribeTarget(generalBest),
					generalAllowed = generalBestAllowed
				},
				potentialTargetIds = potentialIds
			};
		}

		static object VerifyAreaWorkflowWaitAutoAttack(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchOwners = PatchOwners(typeof(JobDriver_Wait), "CheckForAutoAttack");
			var cases = new List<object>();
			var caseIndex = 0;

			object RunCase(string label, bool expectDamage, Func<IntVec3, Pawn, Pawn> spawnTarget, Action<Pawn, Pawn> configure = null, bool requireDamage = true, Action<Pawn, Pawn, int> maintain = null)
			{
				caseIndex++;
				if (TryFindAdjacentPawnPairCells(map, root + new IntVec3(caseIndex * 5, 0, 5), out var actorCell, out var targetCell, out var cellError) == false)
				{
					return new
					{
						success = false,
						label,
						error = cellError
					};
				}

				var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_WaitActor_" + label, actorCell, Faction.OfPlayer, spawnedThings);
				EquipAreaWorkflowMeleeWeapon(actor);
				var target = spawnTarget(targetCell, actor);
				if (target != null)
					spawnedThings.Add(target);
				if (actor == null || target == null)
				{
					return new
					{
						success = false,
						label,
						actor = DescribePawn(actor),
						target = DescribeTarget(target),
						error = "Could not create the Wait_Combat auto-attack fixture."
					};
				}

				actor.drafter.Drafted = true;
				configure?.Invoke(actor, target);
				RefreshZombieTargetCache(map);
				var injuryBefore = TotalInjurySeverity(target);
				var deadBefore = target.Dead;
				var samples = new List<object>();
				var sampledDamage = false;
				var attacked = false;

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = false;
				waitJob.canUseRangedWeapon = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);

				const int maxTicks = 900;
				for (var tick = 1; tick <= maxTicks && actor.Destroyed == false && target.Destroyed == false; tick++)
				{
					maintain?.Invoke(actor, target, tick);
					AdvanceGameTicks(1);
					maintain?.Invoke(actor, target, tick);
					var actorStance = actor.stances?.curStance?.GetType().Name;
					var startedAttack = actor.stances?.curStance is Stance_Cooldown;
					attacked |= startedAttack;
					if (tick == 1 || tick == 60 || tick == 180 || tick == maxTicks || TotalInjurySeverity(target) > injuryBefore || target.Dead)
					{
						var zombieTarget = target as Zombie;
						if ((TotalInjurySeverity(target) <= injuryBefore && target.Dead == false) || sampledDamage == false)
						{
							samples.Add(new
							{
								tick,
								actorJob = actor.CurJobDef?.defName,
								actorStance,
								startedAttack,
								targetInjury = TotalInjurySeverity(target),
								targetDead = target.Dead,
								targetRopedBy = zombieTarget?.ropedBy?.ThingID,
								targetIsRopedOrConfused = zombieTarget?.IsRopedOrConfused,
								targetParalyzedUntilDelta = zombieTarget == null ? (int?)null : zombieTarget.paralyzedUntil - GenTicks.TicksAbs
							});
							if (TotalInjurySeverity(target) > injuryBefore || target.Dead)
								sampledDamage = true;
						}
					}
					if (expectDamage && (TotalInjurySeverity(target) > injuryBefore || target.Dead || target.Downed))
						break;
					if (expectDamage && requireDamage == false && attacked)
						break;
				}

				var injuryAfter = TotalInjurySeverity(target);
				var damaged = injuryAfter > injuryBefore || (deadBefore == false && target.Dead) || target.Downed;
				var success = expectDamage
					? requireDamage ? damaged : damaged || attacked
					: damaged == false && attacked == false;
				var result = new
				{
					success,
					label,
					expectDamage,
					requireDamage,
					damaged,
					attacked,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					injuryBefore,
					injuryAfter,
					injuryDelta = injuryAfter - injuryBefore,
					deadBefore,
					deadAfter = target.Dead,
					downedAfter = target.Downed,
					actor = DescribePawn(actor),
					target = DescribeTarget(target),
					samples = samples.ToArray()
				};

				if (actor.Destroyed == false)
					actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				if (actor.Destroyed == false)
					actor.Destroy(DestroyMode.Vanish);
				if (target.Destroyed == false)
					target.Destroy(DestroyMode.Vanish);
				return result;
			}

			cases.Add(RunCase("normalZombie", true, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitNormal", spawnedThings)));
			cases.Add(RunCase("ropedZombie", false, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitRoped", spawnedThings), (actor, target) =>
			{
				if (target is Zombie zombie)
					zombie.ropedBy = actor;
			}, maintain: (actor, target, tick) =>
			{
				if (target is Zombie zombie)
					zombie.ropedBy = actor;
			}));
			cases.Add(RunCase("confusedZombie", false, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitConfused", spawnedThings), (actor, target) =>
			{
				if (target is Zombie zombie)
					zombie.paralyzedUntil = GenTicks.TicksAbs + 2500;
			}));
			cases.Add(RunCase("spitter", true, (cell, actor) => SpawnTargetSpitter(map, cell, "ZL_Area_WaitSpitter", spawnedThings)));
			cases.Add(RunCase("blob", true, (cell, actor) => SpawnTargetBlob(map, cell, "ZL_Area_WaitBlob", spawnedThings), requireDamage: false));
			cases.Add(RunCase("hostilePawn", true, (cell, actor) => SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_WaitHostile", cell, Faction.OfAncientsHostile, spawnedThings), requireDamage: false));

			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland") && cases.All(ObjectSuccess),
				patchOwners,
				cases = cases.ToArray()
			};
		}

		static object VerifyAreaWorkflowDownedCombat(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				ApplyZombieSettingsOverride(settings => settings.doubleTapRequired = false);
				var killIncappedTargets = PatchedMethodsForPatchClass("Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch");
				var downedReplacementTargets = PatchedMethodsForPatchClass("JobDriver_AttackStatic_TickAction_Patch");
				var meleeCase = VerifyDownedMeleeAttack(map, root + new IntVec3(0, 0, 13), spawnedThings);
				var attackStaticCase = VerifyDownedAttackStatic(map, root + new IntVec3(12, 0, 13), spawnedThings);

				return new
				{
					success = killIncappedTargets.Length > 0
						&& downedReplacementTargets.Length > 0
						&& ObjectSuccess(meleeCase)
						&& ObjectSuccess(attackStaticCase),
					doubleTapRequired = ZombieSettings.Values.doubleTapRequired,
					patchTargets = new
					{
						killIncapped = killIncappedTargets,
						downedReplacement = downedReplacementTargets
					},
					meleeCase,
					attackStaticCase
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyAreaWorkflowDangerAndFlee(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var dangerMethod = AccessTools.Method(typeof(DangerWatcher), "AffectsStoryDanger");
			var dangerPatchTargets = PatchedMethodsForPatchClass("DangerWatcher_AffectsStoryDanger_Patch");
			var fleePatchTargets = PatchedMethodsForPatchClass("FleeUtility_ShouldFleeFrom_Patch");
			var originalHome = new Dictionary<IntVec3, bool>();

			void SetHome(IntVec3 cell, bool value)
			{
				if (cell.IsValid == false || cell.InBounds(map) == false)
					return;
				if (originalHome.ContainsKey(cell) == false)
					originalHome[cell] = map.areaManager.Home[cell];
				map.areaManager.Home[cell] = value;
			}

			bool InvokeDanger(IAttackTarget target)
			{
				return dangerMethod != null && (bool)dangerMethod.Invoke(null, new object[] { target });
			}

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.betterZombieAvoidance = true;
					settings.attackMode = AttackMode.Everything;
					settings.enemiesAttackZombies = true;
					settings.animalsAttackZombies = true;
					settings.doubleTapRequired = false;
				});

				if (dangerMethod == null)
				{
					return new
					{
						success = false,
						error = "Could not reflect DangerWatcher.AffectsStoryDanger.",
						patchTargets = new
						{
							danger = dangerPatchTargets,
							flee = fleePatchTargets
						}
					};
				}
				if (TryFindClearSpawnCell(map, root, 18f, out var actorCell, out var actorError) == false)
					return actorError;

				var targetCells = GenRadial.RadialCellsAround(actorCell, 10f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell != actorCell)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.Take(9)
					.ToArray();
				if (targetCells.Length < 9)
				{
					return new
					{
						success = false,
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						foundTargetCells = targetCells.Length,
						error = "Could not find enough nearby cells for danger/flee patch fixtures."
					};
				}

				var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DangerFleeActor", actorCell, Faction.OfPlayer, spawnedThings);
				var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_DangerFleeNormal", spawnedThings);
				var outside = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_DangerFleeOutside", spawnedThings);
				var roped = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_DangerFleeRoped", spawnedThings);
				var confused = SpawnTargetZombie(map, targetCells[3], ZombieType.Normal, "ZL_Area_DangerFleeConfused", spawnedThings);
				var downed = SpawnTargetZombie(map, targetCells[4], ZombieType.Normal, "ZL_Area_DangerFleeDowned", spawnedThings);
				var unspawned = SpawnTargetZombie(map, targetCells[5], ZombieType.Normal, "ZL_Area_DangerFleeUnspawned", spawnedThings);
				var electric = SpawnTargetZombie(map, targetCells[6], ZombieType.Electrifier, "ZL_Area_DangerFleeElectric", spawnedThings);
				var albino = SpawnTargetZombie(map, targetCells[7], ZombieType.Albino, "ZL_Area_DangerFleeAlbino", spawnedThings);
				var hostile = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DangerFleeHostile", targetCells[8], Faction.OfAncientsHostile, spawnedThings);

				if (actor == null || normal == null || outside == null || roped == null || confused == null || downed == null || unspawned == null || electric == null || albino == null || hostile == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						normal = DescribeZombie(normal),
						outside = DescribeZombie(outside),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						downed = DescribeZombie(downed),
						unspawned = DescribeZombie(unspawned),
						electric = DescribeZombie(electric),
						albino = DescribeZombie(albino),
						hostile = DescribePawn(hostile),
						error = "Could not create all danger/flee patch fixtures."
					};
				}

				roped.ropedBy = actor;
				confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
				electric.electricDisabledUntil = GenTicks.TicksGame - 1;
				if (TryMakeDownedForCombat(downed, out var downedError) == false)
				{
					return new
					{
						success = false,
						downed = DescribeZombie(downed),
						error = downedError
					};
				}

				var unspawnedCell = unspawned.Position;
				unspawned.DeSpawn(DestroyMode.Vanish);

				SetHome(normal.Position, true);
				SetHome(outside.Position, false);
				SetHome(roped.Position, true);
				SetHome(downed.Position, true);
				SetHome(unspawnedCell, true);

				var dangerHome = InvokeDanger(normal);
				var dangerOutside = InvokeDanger(outside);
				var dangerRoped = InvokeDanger(roped);
				var dangerDowned = InvokeDanger(downed);
				var dangerUnspawned = InvokeDanger(unspawned);

				var normalThreat = FleeUtility.ShouldFleeFrom(normal, actor, true, false);
				var ropedThreat = FleeUtility.ShouldFleeFrom(roped, actor, true, false);
				var confusedThreat = FleeUtility.ShouldFleeFrom(confused, actor, true, false);
				var electricThreat = FleeUtility.ShouldFleeFrom(electric, actor, true, false);
				var albinoThreat = FleeUtility.ShouldFleeFrom(albino, actor, true, false);
				var hostileThreat = FleeUtility.ShouldFleeFrom(hostile, actor, true, false);

				return new
				{
					success = dangerPatchTargets.Length > 0
						&& fleePatchTargets.Length > 0
						&& dangerHome
						&& dangerOutside == false
						&& dangerRoped == false
						&& dangerDowned == false
						&& dangerUnspawned == false
						&& normalThreat
						&& ropedThreat == false
						&& confusedThreat == false
						&& electricThreat == false
						&& albinoThreat == false
						&& hostileThreat,
					patchTargets = new
					{
						danger = dangerPatchTargets,
						flee = fleePatchTargets
					},
					actor = DescribePawn(actor),
					danger = new
					{
						home = DescribeDangerCase("home", normal, dangerHome),
						outside = DescribeDangerCase("outsideHome", outside, dangerOutside),
						roped = DescribeDangerCase("roped", roped, dangerRoped),
						downed = DescribeDangerCase("downed", downed, dangerDowned),
						unspawned = new
						{
							label = "unspawned",
							result = dangerUnspawned,
							spawned = unspawned.Spawned,
							destroyed = unspawned.Destroyed,
							position = ZombieRuntimeActions.DescribeCell(unspawnedCell)
						}
					},
					flee = new
					{
						normal = DescribeFleeCase("normal", normal, actor, normalThreat),
						roped = DescribeFleeCase("roped", roped, actor, ropedThreat),
						confused = DescribeFleeCase("confused", confused, actor, confusedThreat),
						electric = DescribeFleeCase("electric", electric, actor, electricThreat),
						albino = DescribeFleeCase("albino", albino, actor, albinoThreat),
						hostile = new
						{
							label = "hostilePawn",
							result = hostileThreat,
							hostileToActor = hostile.HostileTo(actor),
							target = DescribePawn(hostile)
						}
					},
					settings = new
					{
						ZombieSettings.Values.betterZombieAvoidance,
						ZombieSettings.Values.attackMode,
						ZombieSettings.Values.enemiesAttackZombies,
						ZombieSettings.Values.animalsAttackZombies,
						ZombieSettings.Values.doubleTapRequired
					}
				};
			}
			finally
			{
				foreach (var pair in originalHome)
					map.areaManager.Home[pair.Key] = pair.Value;
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object DescribeDangerCase(string label, Zombie zombie, bool result)
		{
			return new
			{
				label,
				result,
				home = zombie?.Spawned == true && zombie.Map?.areaManager?.Home[zombie.Position] == true,
				zombie = DescribeZombie(zombie)
			};
		}

		static object DescribeFleeCase(string label, Zombie zombie, Pawn actor, bool result)
		{
			return new
			{
				label,
				result,
				seesAsThreat = actor.SeesZombieAsThreat(zombie),
				hostileToActor = zombie.HostileTo(actor),
				zombie = DescribeZombie(zombie)
			};
		}

		static object VerifyAreaWorkflowSmartMelee(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var meleePatchTargets = PatchedMethodsForPatchClass("Verb_MeleeAttack_TryCastShot_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.safeMeleeLimit = 1;
					settings.doubleTapRequired = false;
				});

				var smartBlock = VerifySmartMeleeBiteBlock(map, root, spawnedThings);
				var chainsawSuppression = VerifySmartMeleeChainsawSuppression(map, root + new IntVec3(8, 0, 0), spawnedThings);
				var ropedKill = VerifySmartMeleeRopedKill(map, root + new IntVec3(16, 0, 0), spawnedThings);
				var ordinaryFallback = VerifySmartMeleeOrdinaryFallback(map, root + new IntVec3(24, 0, 0), spawnedThings);

				return new
				{
					success = meleePatchTargets.Length > 0
						&& ObjectSuccess(smartBlock)
						&& ObjectSuccess(chainsawSuppression)
						&& ObjectSuccess(ropedKill)
						&& ObjectSuccess(ordinaryFallback),
					patchTargets = new
					{
						melee = meleePatchTargets
					},
					settings = new
					{
						ZombieSettings.Values.safeMeleeLimit,
						ZombieSettings.Values.doubleTapRequired
					},
					smartBlock,
					chainsawSuppression,
					ropedKill,
					ordinaryFallback
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifySmartMeleeBiteBlock(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Normal, "ZL_Area_SmartMeleeZombie", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeDefender", targetCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(target);
			target.skills?.GetSkill(SkillDefOf.Melee).Notify_SkillDisablesChanged();
			if (target.skills != null)
				target.skills.GetSkill(SkillDefOf.Melee).Level = 20;
			_ = target.meleeVerbs?.TryGetMeleeVerb(zombie);

			var biteVerb = zombie?.meleeVerbs?.GetUpdatedAvailableVerbsList(false)
				.Select(entry => entry.verb)
				.FirstOrDefault(verb => verb.GetDamageDef() == CustomDefs.ZombieBite);
			if (zombie == null || target == null || biteVerb == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					biteVerbFound = biteVerb != null,
					error = "Could not create a smart melee bite-block fixture."
				};
			}

			var injuryBefore = TotalInjurySeverity(target);
			var blockMotesBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.Mote_Block);
			var cast = InvokePatchedMeleeTryCastShot(biteVerb, target);
			var injuryAfter = TotalInjurySeverity(target);
			var blockMotesAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.Mote_Block);

			return new
			{
				success = ObjectSuccess(cast)
					&& injuryAfter == injuryBefore
					&& target.Dead == false
					&& target.Downed == false,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				biteVerb = DescribeVerb(biteVerb),
				targetMeleeLevel = target.skills?.GetSkill(SkillDefOf.Melee)?.Level,
				targetCurrentMeleeVerb = DescribeVerb(target.meleeVerbs?.curMeleeVerb),
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				blockMotesBefore,
				blockMotesAfter,
				cast
			};
		}

		static object VerifySmartMeleeChainsawSuppression(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeChainsawActor", actorCell, Faction.OfPlayer, spawnedThings);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeChainsawTarget", spawnedThings);
			if (TryEquipAreaWorkflowChainsaw(map, actor, actorCell, spawnedThings, out var chainsaw, out var chainsawError) == false)
				return chainsawError;

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			var injuryBefore = TotalInjurySeverity(zombie);
			var cast = InvokePatchedMeleeTryCastShot(verb, zombie);
			var injuryAfter = TotalInjurySeverity(zombie);

			return new
			{
				success = actor.equipment?.Primary is Chainsaw
					&& verb is Verb_MeleeAttack
					&& ObjectSuccess(cast)
					&& injuryAfter == injuryBefore
					&& zombie.Dead == false,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				chainsaw = DescribeTarget(chainsaw as IAttackTarget),
				verb = DescribeVerb(verb),
				cast,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore
			};
		}

		static object VerifySmartMeleeRopedKill(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeRopedActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeRopedTarget", spawnedThings);
			zombie.ropedBy = actor;
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			var deadBefore = zombie.Dead;
			var ropedOrConfusedBefore = zombie.IsRopedOrConfused;
			var cast = InvokePatchedMeleeTryCastShot(verb, zombie);
			var deadAfter = zombie.Dead;

			return new
			{
				success = verb is Verb_MeleeAttack
					&& ropedOrConfusedBefore
					&& deadBefore == false
					&& deadAfter
					&& ObjectSuccess(cast),
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				ropedByBefore = ZombieRuntimeActions.StableThingId(actor),
				ropedOrConfusedBefore,
				deadBefore,
				deadAfter,
				cast
			};
		}

		static object VerifySmartMeleeOrdinaryFallback(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeOrdinaryActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeOrdinaryTarget", spawnedThings);
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			var prefixProbe = ProbeSmartMeleePrefix(verb);
			var tryMeleeResult = actor.meleeVerbs?.TryMeleeAttack(zombie, verb, false);

			return new
			{
				success = verb is Verb_MeleeAttack
					&& ObjectSuccess(prefixProbe)
					&& tryMeleeResult == true
					&& zombie.Dead == false,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				prefixProbe,
				tryMeleeResult
			};
		}

		static object InvokePatchedMeleeTryCastShot(Verb verb, LocalTargetInfo target)
		{
			var tryCastShot = AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
			if (tryCastShot == null || verb is not Verb_MeleeAttack)
			{
				return new
				{
					success = false,
					tryCastShotFound = tryCastShot != null,
					verb = DescribeVerb(verb),
					error = "Could not invoke Verb_MeleeAttack.TryCastShot for the melee patch probe."
				};
			}
			try
			{
				verb.currentTarget = target;
				var result = (bool)tryCastShot.Invoke(verb, Array.Empty<object>());
				return new
				{
					success = result == false,
					result,
					verb = DescribeVerb(verb),
					target = DescribeTarget(target.Thing as IAttackTarget)
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					error = ex.GetBaseException().Message,
					verb = DescribeVerb(verb)
				};
			}
		}

		static object ProbeSmartMeleePrefix(Verb verb)
		{
			var prefix = FindNestedPatchMethod("Verb_MeleeAttack_TryCastShot_Patch", "Prefix");
			if (prefix == null || verb == null)
			{
				return new
				{
					success = false,
					prefixFound = prefix != null,
					verb = DescribeVerb(verb)
				};
			}
			var args = new object[] { verb, false };
			var continueOriginal = (bool)prefix.Invoke(null, args);
			var result = (bool)args[1];
			return new
			{
				success = continueOriginal && result == false,
				continueOriginal,
				result,
				verb = DescribeVerb(verb)
			};
		}

		static bool TryEquipAreaWorkflowChainsaw(Map map, Pawn actor, IntVec3 cell, List<Thing> spawnedThings, out Chainsaw chainsaw, out object error)
		{
			chainsaw = null;
			error = null;
			if (actor?.equipment == null)
			{
				error = new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "Chainsaw fixture actor had no equipment tracker."
				};
				return false;
			}

			chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				error = new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
				return false;
			}
			spawnedThings.Add(chainsaw);
			GenSpawn.Spawn(chainsaw, cell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				error = new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
				return false;
			}
			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, cell, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.DestroyAllEquipment(DestroyMode.Vanish);
			actor.equipment.AddEquipment(chainsaw);
			_ = spawnedThings.Remove(chainsaw);
			return true;
		}

		static object VerifyAreaWorkflowJobGate(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_JobTracker_StartJob_Patch");
			if (FindJobGateCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var normal = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_JobGateNormal", spawnedThings);
			var spitter = SpawnTargetSpitter(map, cells[1], "ZL_Area_JobGateSpitter", spawnedThings);
			var blob = SpawnTargetBlob(map, cells[2], "ZL_Area_JobGateBlob", spawnedThings);
			if (normal == null || spitter == null || blob == null)
			{
				return new
				{
					success = false,
					normal = DescribeZombie(normal),
					spitter = DescribeZombie(spitter),
					blob = DescribeZombie(blob),
					error = "Could not spawn all Zombieland pawn job-gate fixtures."
				};
			}

			var allowedCases = new[]
			{
				VerifyJobGateAllowed(map, normal, "normal"),
				VerifyJobGateAllowed(map, spitter, "spitter"),
				VerifyJobGateAllowed(map, blob, "blob")
			};
			var blockedCases = new[]
			{
				VerifyJobGateBlocked(map, normal, cells[3], "normal"),
				VerifyJobGateBlocked(map, spitter, cells[4], "spitter"),
				VerifyJobGateBlocked(map, blob, cells[5], "blob")
			};

			return new
			{
				success = patchTargets.Length > 0
					&& allowedCases.All(ObjectSuccess)
					&& blockedCases.All(ObjectSuccess),
				patchTargets = new
				{
					startJob = patchTargets
				},
				allowedCases,
				blockedCases
			};
		}

		static bool FindJobGateCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 18f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(6)
				.ToArray();
			if (cells.Length >= 6)
			{
				error = null;
				return true;
			}
			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the job-gate fixture."
			};
			return false;
		}

		static object VerifyJobGateAllowed(Map map, Pawn pawn, string label)
		{
			EndCurrentPawnJob(pawn);
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			var fields = DescribeJobTrackerFields(pawn.jobs);

			return new
			{
				success = pawn.CurJobDef == JobDefOf.Wait
					&& fields.startingNewJob == false,
				label,
				pawn = DescribeZombie(pawn),
				job = DescribeJob(pawn.CurJob),
				fields
			};
		}

		static object VerifyJobGateBlocked(Map map, Pawn pawn, IntVec3 itemCell, string label)
		{
			var item = ThingMaker.MakeThing(ThingDefOf.Steel);
			item.stackCount = 1;
			GenSpawn.Spawn(item, itemCell, map, WipeMode.Vanish);
			var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, item, pawn.Position);

			var reservedBefore = map.reservationManager.Reserve(pawn, haulJob, item, errorOnFailed: false);
			SetJobTrackerFields(pawn.jobs, 7, "dirty", true);
			var fieldsBefore = DescribeJobTrackerFields(pawn.jobs);
			var curJobBefore = pawn.CurJobDef?.defName;
			pawn.jobs.StartJob(haulJob, JobCondition.InterruptForced, null, false, true);
			var fieldsAfter = DescribeJobTrackerFields(pawn.jobs);
			var reservationAfter = map.reservationManager.ReservedBy(item, pawn, haulJob);
			var itemReservedAfter = map.reservationManager.IsReserved(item);

			return new
			{
				success = reservedBefore
					&& reservationAfter == false
					&& itemReservedAfter == false
					&& fieldsBefore.jobsGivenThisTick == 7
					&& fieldsBefore.jobsGivenThisTickTextual == "dirty"
					&& fieldsBefore.startingNewJob == true
					&& fieldsAfter.jobsGivenThisTick == 0
					&& fieldsAfter.jobsGivenThisTickTextual == ""
					&& fieldsAfter.startingNewJob == false
					&& pawn.CurJobDef?.defName == curJobBefore
					&& pawn.CurJobDef != JobDefOf.HaulToCell,
				label,
				pawn = DescribeZombie(pawn),
				disallowedJob = DescribeJob(haulJob),
				curJobBefore,
				curJobAfter = pawn.CurJobDef?.defName,
				reservedBefore,
				reservationAfter,
				itemReservedAfter,
				item = ZombieRuntimeActions.StableThingId(item),
				itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
				fieldsBefore,
				fieldsAfter
			};
		}

		static void SetJobTrackerFields(Pawn_JobTracker jobs, int jobsGivenThisTick, string textual, bool startingNewJob)
		{
			AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTick")?.SetValue(jobs, jobsGivenThisTick);
			AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTickTextual")?.SetValue(jobs, textual);
			AccessTools.Field(typeof(Pawn_JobTracker), "startingNewJob")?.SetValue(jobs, startingNewJob);
		}

		sealed class JobTrackerFieldSnapshot
		{
			public int? jobsGivenThisTick { get; set; }
			public string jobsGivenThisTickTextual { get; set; }
			public bool? startingNewJob { get; set; }
		}

		static JobTrackerFieldSnapshot DescribeJobTrackerFields(Pawn_JobTracker jobs)
		{
			var jobsGivenRaw = AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTick")?.GetValue(jobs);
			var startingRaw = AccessTools.Field(typeof(Pawn_JobTracker), "startingNewJob")?.GetValue(jobs);
			return new JobTrackerFieldSnapshot
			{
				jobsGivenThisTick = jobsGivenRaw is int jobsGiven ? jobsGiven : null,
				jobsGivenThisTickTextual = AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTickTextual")?.GetValue(jobs) as string,
				startingNewJob = startingRaw is bool starting ? starting : null
			};
		}

		static object DescribeJob(Job job)
		{
			return new
			{
				def = job?.def?.defName,
				targetA = DescribeLocalTarget(job?.targetA ?? LocalTargetInfo.Invalid),
				targetB = DescribeLocalTarget(job?.targetB ?? LocalTargetInfo.Invalid),
				playerForced = job?.playerForced
			};
		}

		static void EndCurrentPawnJob(Pawn pawn)
		{
			if (pawn?.jobs?.curJob != null)
				pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
		}

		static object VerifyAreaWorkflowSpecialMeleeVerbs(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList_Patch");
			if (FindSpecialMeleeVerbCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var normal = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_MeleeVerbsNormal", spawnedThings);
			var albino = SpawnTargetZombie(map, cells[1], ZombieType.Albino, "ZL_Area_MeleeVerbsAlbino", spawnedThings);
			var activeElectric = SpawnTargetZombie(map, cells[2], ZombieType.Electrifier, "ZL_Area_MeleeVerbsActiveElectric", spawnedThings);
			var disabledElectric = SpawnTargetZombie(map, cells[3], ZombieType.Electrifier, "ZL_Area_MeleeVerbsDisabledElectric", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_MeleeVerbsTarget", cells[4], Faction.OfPlayer, spawnedThings);

			if (normal == null || albino == null || activeElectric == null || disabledElectric == null || target == null)
			{
				return new
				{
					success = false,
					normal = DescribeZombie(normal),
					albino = DescribeZombie(albino),
					activeElectric = DescribeZombie(activeElectric),
					disabledElectric = DescribeZombie(disabledElectric),
					target = DescribePawn(target),
					error = "Could not create all special melee verb fixtures."
				};
			}

			activeElectric.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabledElectric.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;

			var normalCase = DescribeSpecialMeleeVerbCase("normal", normal, target);
			var albinoCase = DescribeSpecialMeleeVerbCase("albino", albino, target);
			var activeElectricCase = DescribeSpecialMeleeVerbCase("activeElectric", activeElectric, target);
			var disabledElectricCase = DescribeSpecialMeleeVerbCase("disabledElectric", disabledElectric, target);

			return new
			{
				success = patchTargets.Length > 0
					&& SpecialVerbCaseHasBite(normalCase)
					&& SpecialVerbCaseHidesBite(albinoCase)
					&& SpecialVerbCaseHidesBite(activeElectricCase)
					&& SpecialVerbCaseHidesBite(disabledElectricCase),
				patchTargets = new
				{
					meleeVerbs = patchTargets
				},
				normal = normalCase,
				albino = albinoCase,
				activeElectric = activeElectricCase,
				disabledElectric = disabledElectricCase,
				target = DescribePawn(target)
			};
		}

		static bool FindSpecialMeleeVerbCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 18f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(5)
				.ToArray();
			if (cells.Length >= 5)
			{
				error = null;
				return true;
			}
			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the special melee verb fixture."
			};
			return false;
		}

		sealed class SpecialMeleeVerbCase
		{
			public string label { get; set; }
			public bool success { get; set; }
			public bool hasZombieBite { get; set; }
			public string[] damageDefs { get; set; }
			public object pawn { get; set; }
			public bool isAlbino { get; set; }
			public bool isElectrifier { get; set; }
			public bool isActiveElectric { get; set; }
			public object selectedVerb { get; set; }
		}

		static SpecialMeleeVerbCase DescribeSpecialMeleeVerbCase(string label, Zombie zombie, Pawn target)
		{
			var entries = zombie.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var damageDefs = entries
				.Select(entry => entry.verb.GetDamageDef()?.defName)
				.Where(defName => defName != null)
				.ToArray();
			var hasZombieBite = damageDefs.Contains(CustomDefs.ZombieBite.defName);
			var selectedVerb = zombie.meleeVerbs.TryGetMeleeVerb(target);
			var shouldHideBite = zombie.isAlbino || zombie.isElectrifier;
			return new SpecialMeleeVerbCase
			{
				label = label,
				success = shouldHideBite ? hasZombieBite == false : hasZombieBite,
				hasZombieBite = hasZombieBite,
				damageDefs = damageDefs,
				pawn = DescribeZombie(zombie),
				isAlbino = zombie.isAlbino,
				isElectrifier = zombie.isElectrifier,
				isActiveElectric = zombie.IsActiveElectric,
				selectedVerb = DescribeVerb(selectedVerb)
			};
		}

		static bool SpecialVerbCaseHasBite(object value)
		{
			return value is SpecialMeleeVerbCase meleeCase && meleeCase.success && meleeCase.hasZombieBite;
		}

		static bool SpecialVerbCaseHidesBite(object value)
		{
			return value is SpecialMeleeVerbCase meleeCase && meleeCase.success && meleeCase.hasZombieBite == false;
		}

		static object VerifyAreaWorkflowAnimalResponse(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var manhunterPatchTargets = PatchedMethodsForPatchClass("PawnUtility_GetManhunterOnDamageChance_Patch");
			var preyPatchTargets = PatchedMethodsForPatchClass("FoodUtility_GetPreyScoreFor_Patch");
			try
			{
				if (FindAnimalResponseCells(map, root, out var cells, out var cellError) == false)
					return cellError;

				var manhunterVictim = SpawnAreaWorkflowAnimal(
					map,
					"ZL_Area_AnimalResponseVictim",
					cells[0],
					null,
					spawnedThings,
					def => def.race?.race?.manhunterOnDamageChance > 0f);
				var humanInstigator = SpawnAreaWorkflowPawn(map, "ZL_Area_AnimalResponseHumanInstigator", cells[1], Faction.OfPlayer, spawnedThings);
				var zombieInstigator = SpawnTargetZombie(map, cells[2], ZombieType.Normal, "ZL_Area_AnimalResponseZombieInstigator", spawnedThings);
				var spitterInstigator = SpawnTargetSpitter(map, cells[3], "ZL_Area_AnimalResponseSpitterInstigator", spawnedThings);
				var blobInstigator = SpawnTargetBlob(map, cells[4], "ZL_Area_AnimalResponseBlobInstigator", spawnedThings);
				var predator = SpawnAreaWorkflowAnimal(
					map,
					"ZL_Area_AnimalResponsePredator",
					cells[5],
					null,
					spawnedThings,
					def => def.combatPower > 0f && def.RaceProps?.maxPreyBodySize > 0f);
				var humanPrey = SpawnAreaWorkflowPawn(map, "ZL_Area_AnimalResponseHumanPrey", cells[6], Faction.OfPlayer, spawnedThings);
				var zombiePrey = SpawnTargetZombie(map, cells[7], ZombieType.Normal, "ZL_Area_AnimalResponseZombiePrey", spawnedThings);

				if (manhunterVictim == null || humanInstigator == null || zombieInstigator == null || spitterInstigator == null || blobInstigator == null || predator == null || humanPrey == null || zombiePrey == null)
				{
					return new
					{
						success = false,
						manhunterVictim = DescribePawn(manhunterVictim),
						humanInstigator = DescribePawn(humanInstigator),
						zombieInstigator = DescribeZombie(zombieInstigator),
						spitterInstigator = DescribeZombie(spitterInstigator),
						blobInstigator = DescribeZombie(blobInstigator),
						predator = DescribePawn(predator),
						humanPrey = DescribePawn(humanPrey),
						zombiePrey = DescribeZombie(zombiePrey),
						error = "Could not create all animal-response fixtures."
					};
				}

				var manhunter = VerifyAnimalResponseManhunter(manhunterVictim, humanInstigator, zombieInstigator, spitterInstigator, blobInstigator);
				var preyScores = VerifyAnimalResponsePreyScores(predator, humanPrey, zombiePrey, spitterInstigator, blobInstigator);

				return new
				{
					success = manhunterPatchTargets.Length > 0
						&& preyPatchTargets.Length > 0
						&& ObjectSuccess(manhunter)
						&& ObjectSuccess(preyScores),
					patchTargets = new
					{
						manhunter = manhunterPatchTargets,
						prey = preyPatchTargets
					},
					fixtures = new
					{
						manhunterVictim = DescribePawn(manhunterVictim),
						humanInstigator = DescribePawn(humanInstigator),
						zombieInstigator = DescribeZombie(zombieInstigator),
						spitterInstigator = DescribeZombie(spitterInstigator),
						blobInstigator = DescribeZombie(blobInstigator),
						predator = DescribePawn(predator),
						humanPrey = DescribePawn(humanPrey),
						zombiePrey = DescribeZombie(zombiePrey)
					},
					manhunter,
					preyScores
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static bool FindAnimalResponseCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			var candidates = map.AllCells
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.ToArray();
			var used = new HashSet<IntVec3>();
			var anchors = new[]
			{
				root,
				root + new IntVec3(2, 0, 0),
				root + new IntVec3(4, 0, 0),
				root + new IntVec3(6, 0, 0),
				root + new IntVec3(8, 0, 0),
				root + new IntVec3(0, 0, 12),
				root + new IntVec3(100, 0, 12),
				root + new IntVec3(1, 0, 12)
			};
			cells = anchors
				.Select(anchor =>
				{
					var cell = candidates
						.Where(candidate => used.Contains(candidate) == false)
						.OrderBy(candidate => candidate.DistanceToSquared(anchor))
						.FirstOrDefault();
					if (cell.IsValid)
						_ = used.Add(cell);
					return cell;
				})
				.ToArray();
			if (cells.All(cell => cell.IsValid))
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Count(cell => cell.IsValid),
				error = "Could not find enough cells for the animal-response fixture."
			};
			return false;
		}

		static object VerifyAnimalResponseManhunter(Pawn victim, Pawn humanInstigator, Zombie zombieInstigator, ZombieSpitter spitterInstigator, ZombieBlob blobInstigator)
		{
			const float distance = 3f;
			var settings = ZombieSettings.Values;
			var humanExpected = EstimateVanillaManhunterChance(victim, humanInstigator, distance);
			var zombieExpected = EstimateVanillaManhunterChance(victim, zombieInstigator, distance);
			var originalSetting = settings.zombiesCauseManhuntingResponse;
			try
			{
				settings.zombiesCauseManhuntingResponse = true;
				var humanEnabled = PawnUtility.GetManhunterOnDamageChance(victim, humanInstigator, distance);
				var zombieEnabled = PawnUtility.GetManhunterOnDamageChance(victim, zombieInstigator, distance);
				var spitterEnabled = PawnUtility.GetManhunterOnDamageChance(victim, spitterInstigator, distance);
				var blobEnabled = PawnUtility.GetManhunterOnDamageChance(victim, blobInstigator, distance);

				settings.zombiesCauseManhuntingResponse = false;
				var humanDisabled = PawnUtility.GetManhunterOnDamageChance(victim, humanInstigator, distance);
				var zombieDisabled = PawnUtility.GetManhunterOnDamageChance(victim, zombieInstigator, distance);
				var spitterDisabled = PawnUtility.GetManhunterOnDamageChance(victim, spitterInstigator, distance);
				var blobDisabled = PawnUtility.GetManhunterOnDamageChance(victim, blobInstigator, distance);

				var expectedZombieEnabled = zombieExpected / 20f;
				return new
				{
					success = humanExpected > 0f
						&& Approximately(humanEnabled, humanExpected)
						&& Approximately(humanDisabled, humanExpected)
						&& zombieExpected > 0f
						&& Approximately(zombieEnabled, expectedZombieEnabled)
						&& Approximately(zombieDisabled, 0f)
						&& Approximately(spitterEnabled, 0f)
						&& Approximately(spitterDisabled, 0f)
						&& Approximately(blobEnabled, 0f)
						&& Approximately(blobDisabled, 0f),
					distance,
					human = new
					{
						expectedVanilla = humanExpected,
						enabled = humanEnabled,
						disabled = humanDisabled
					},
					normalZombie = new
					{
						expectedVanilla = zombieExpected,
						expectedEnabled = expectedZombieEnabled,
						enabled = zombieEnabled,
						disabled = zombieDisabled
					},
					spitter = new
					{
						enabled = spitterEnabled,
						disabled = spitterDisabled
					},
					blob = new
					{
						enabled = blobEnabled,
						disabled = blobDisabled
					},
					victimManhunterOnDamageChance = victim.def?.race?.manhunterOnDamageChance,
					settingRestoredTo = originalSetting
				};
			}
			finally
			{
				settings.zombiesCauseManhuntingResponse = originalSetting;
			}
		}

		static float EstimateVanillaManhunterChance(Pawn victim, Thing instigator, float distance)
		{
			var chance = PawnUtility.GetManhunterOnDamageChance(victim.def);
			if (victim.health.hediffSet.HasHediff(HediffDefOf.Scaria))
				chance += 0.5f;
			if (instigator != null)
			{
				chance *= GenMath.LerpDoubleClamped(1f, 30f, 3f, 1f, distance);
				chance *= 1f - instigator.GetStatValue(StatDefOf.HuntingStealth);
				if (instigator is Pawn pawn)
					chance *= PawnUtility.GetManhunterChanceFactorForInstigator(pawn);
			}
			return Mathf.Clamp01(chance);
		}

		static object VerifyAnimalResponsePreyScores(Pawn predator, Pawn humanPrey, Zombie zombiePrey, ZombieSpitter spitterPrey, ZombieBlob blobPrey)
		{
			var settings = ZombieSettings.Values;
			var originalSetting = settings.animalsAttackZombies;
			try
			{
				settings.animalsAttackZombies = false;
				var humanDisabled = FoodUtility.GetPreyScoreFor(predator, humanPrey);
				var zombieDisabled = FoodUtility.GetPreyScoreFor(predator, zombiePrey);
				var spitterDisabled = FoodUtility.GetPreyScoreFor(predator, spitterPrey);
				var blobDisabled = FoodUtility.GetPreyScoreFor(predator, blobPrey);

				settings.animalsAttackZombies = true;
				var humanEnabled = FoodUtility.GetPreyScoreFor(predator, humanPrey);
				var zombieEnabled = FoodUtility.GetPreyScoreFor(predator, zombiePrey);
				var spitterEnabled = FoodUtility.GetPreyScoreFor(predator, spitterPrey);
				var blobEnabled = FoodUtility.GetPreyScoreFor(predator, blobPrey);

				return new
				{
					success = Approximately(humanEnabled, humanDisabled)
						&& humanDisabled > zombieDisabled
						&& zombieEnabled > zombieDisabled + 9000f
						&& zombieEnabled > humanEnabled
						&& Approximately(spitterDisabled, 0f)
						&& Approximately(spitterEnabled, 0f)
						&& Approximately(blobDisabled, 0f)
						&& Approximately(blobEnabled, 0f),
					human = new
					{
						disabled = humanDisabled,
						enabled = humanEnabled
					},
					normalZombie = new
					{
						disabled = zombieDisabled,
						enabled = zombieEnabled,
						enabledDelta = zombieEnabled - zombieDisabled
					},
					spitter = new
					{
						disabled = spitterDisabled,
						enabled = spitterEnabled
					},
					blob = new
					{
						disabled = blobDisabled,
						enabled = blobEnabled
					},
					distances = new
					{
						human = (predator.Position - humanPrey.Position).LengthHorizontal,
						normalZombie = (predator.Position - zombiePrey.Position).LengthHorizontal,
						spitter = (predator.Position - spitterPrey.Position).LengthHorizontal,
						blob = (predator.Position - blobPrey.Position).LengthHorizontal
					},
					settingRestoredTo = originalSetting
				};
			}
			finally
			{
				settings.animalsAttackZombies = originalSetting;
			}
		}

		static object VerifyAreaWorkflowElectrifierMeleeDamage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var patchTargets = PatchedMethodsForPatchClass("Pawn_MeleeVerbs_ChooseMeleeVerb_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings => settings.threatScale = 1f);

				var chainsawShock = VerifyElectrifierChainsawShock(map, root, spawnedThings);
				var smokepopConversion = VerifyElectrifierSmokepopConversion(map, root + new IntVec3(8, 0, 0), spawnedThings);
				var componentCarrier = VerifyElectrifierComponentCarrierDamage(map, root + new IntVec3(16, 0, 0), spawnedThings);
				var disabledFallback = VerifyElectrifierDisabledFallback(map, root + new IntVec3(24, 0, 0), spawnedThings);

				return new
				{
					success = patchTargets.Length > 0
						&& ObjectSuccess(chainsawShock)
						&& ObjectSuccess(smokepopConversion)
						&& ObjectSuccess(componentCarrier)
						&& ObjectSuccess(disabledFallback),
					patchTargets = new
					{
						damageInfosToApply = patchTargets
					},
					settings = new
					{
						ZombieSettings.Values.threatScale
					},
					chainsawShock,
					smokepopConversion,
					componentCarrier,
					disabledFallback
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyElectrifierChainsawShock(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageChainsaw", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageChainsawTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier chainsaw shock fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			if (TryEquipAreaWorkflowChainsaw(map, target, targetCell, spawnedThings, out var chainsaw, out var chainsawError) == false)
				return chainsawError;

			var stalledBefore = chainsaw.stalledCounter;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var stalledAfter = chainsaw.stalledCounter;

			return new
			{
				success = ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& target.equipment?.Primary == chainsaw
					&& stalledBefore == 0
					&& stalledAfter == 120
					&& damageInfos.Any(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				chainsaw = new
				{
					def = chainsaw.def?.defName,
					stalledBefore,
					stalledAfter
				},
				damageProbe
			};
		}

		static object VerifyElectrifierSmokepopConversion(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var apparelDef = DefDatabase<ThingDef>.GetNamed("Apparel_SmokepopBelt", false);
			if (apparelDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Apparel_SmokepopBelt was not found."
				};
			}

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageSmokepop", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageSmokepopTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier smokepop conversion fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			var belt = ThingMaker.MakeThing(apparelDef) as Apparel;
			if (belt == null)
			{
				return new
				{
					success = false,
					error = "Apparel_SmokepopBelt did not create Apparel."
				};
			}
			target.apparel.Wear(belt, false);

			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var converted = damageInfos.Any(info => info.Def == CustomDefs.ElectricalShock && info.Weapon == CustomDefs.ElectricalField);

			return new
			{
				success = ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& converted,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				belt = belt.def?.defName,
				converted,
				damageProbe
			};
		}

		static object VerifyElectrifierComponentCarrierDamage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageComponent", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageComponentTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target?.inventory?.innerContainer == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier component-carrier fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			var componentItem = ThingMaker.MakeThing(CustomDefs.Chainsaw) as ThingWithComps;
			if (componentItem == null)
			{
				return new
				{
					success = false,
					error = "Could not create inventory component-cost item."
				};
			}
			componentItem.HitPoints = componentItem.MaxHitPoints;
			var added = target.inventory.innerContainer.TryAdd(componentItem, true);
			var hasComponentCost = componentItem.def?.costList?.Any(cost => cost.thingDef == ThingDefOf.ComponentIndustrial || cost.thingDef == ThingDefOf.ComponentSpacer) == true;
			var hitPointsBefore = componentItem.HitPoints;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var hitPointsAfter = componentItem.Destroyed ? 0 : componentItem.HitPoints;

			return new
			{
				success = added
					&& hasComponentCost
					&& ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& hitPointsAfter < hitPointsBefore
					&& damageInfos.All(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				componentItem = new
				{
					def = componentItem.def?.defName,
					added,
					hasComponentCost,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsAfter - hitPointsBefore
				},
				damageProbe
			};
		}

		static object VerifyElectrifierDisabledFallback(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageDisabled", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageDisabledTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target?.inventory?.innerContainer == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the disabled electrifier fallback fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;
			var componentItem = ThingMaker.MakeThing(CustomDefs.Chainsaw) as ThingWithComps;
			if (componentItem == null)
			{
				return new
				{
					success = false,
					error = "Could not create disabled fallback component-cost item."
				};
			}
			componentItem.HitPoints = componentItem.MaxHitPoints;
			var added = target.inventory.innerContainer.TryAdd(componentItem, true);
			var hitPointsBefore = componentItem.HitPoints;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var hitPointsAfter = componentItem.Destroyed ? 0 : componentItem.HitPoints;

			return new
			{
				success = added
					&& ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric == false
					&& hitPointsAfter == hitPointsBefore
					&& damageInfos.Length > 0
					&& damageInfos.All(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				componentItem = new
				{
					def = componentItem.def?.defName,
					added,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsAfter - hitPointsBefore
				},
				damageProbe
			};
		}

		sealed class ElectrifierDamageProbe
		{
			public bool success { get; set; }
			public object zombie { get; set; }
			public object target { get; set; }
			public object verb { get; set; }
			public object[] damageInfos { get; set; }
			public string[] damageInfoDefs { get; set; }
			public int electricalShockCount { get; set; }
			public int electricalFieldCount { get; set; }
			public string error { get; set; }
		}

		static object ProbeElectrifierDamageInfos(Zombie zombie, Pawn target, out DamageInfo[] damageInfos)
		{
			damageInfos = Array.Empty<DamageInfo>();
			var verb = zombie?.meleeVerbs?.TryGetMeleeVerb(target);
			if (TryMeleeDamageInfosToApply(verb, target, out damageInfos, out var error) == false)
			{
				return new ElectrifierDamageProbe
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					verb = DescribeVerb(verb),
					error = error
				};
			}

			return new ElectrifierDamageProbe
			{
				success = damageInfos.Length > 0,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				damageInfos = DescribeDamageInfos(damageInfos),
				damageInfoDefs = damageInfos.Select(info => info.Def?.defName).ToArray(),
				electricalShockCount = damageInfos.Count(info => info.Def == CustomDefs.ElectricalShock),
				electricalFieldCount = damageInfos.Count(info => info.Weapon == CustomDefs.ElectricalField)
			};
		}

		static object VerifyAreaWorkflowRangedProjectilePatches(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var oldColonistsHitChance = Constants.COLONISTS_HIT_ZOMBIES_CHANCE;
			var forcedMissRadiusField = AccessTools.Field(typeof(VerbProperties), "forcedMissRadius");
			var warmupTimeField = AccessTools.Field(typeof(VerbProperties), "warmupTime");
			var impactSomething = AccessTools.Method(typeof(Projectile), "ImpactSomething");
			var destinationField = AccessTools.Field(typeof(Projectile), "destination");
			var projectilePatchTargets = PatchedMethodsForPatchClass("Projectile_ImpactSomething_Patch");
			var launchPatchTargets = PatchedMethodsForPatchClass("Verb_LaunchProjectile_TryCastShot_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings => settings.threatScale = 1f);
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = 1f;

				if (forcedMissRadiusField == null || warmupTimeField == null || impactSomething == null || destinationField == null)
				{
					return new
					{
						success = false,
						error = "Could not reflect all projectile patch probe members.",
						reflection = new
						{
							forcedMissRadiusField = forcedMissRadiusField != null,
							warmupTimeField = warmupTimeField != null,
							impactSomething = impactSomething != null,
							destinationField = destinationField != null
						},
						patchTargets = new
						{
							projectileImpact = projectilePatchTargets,
							launchProjectile = launchPatchTargets
						}
					};
				}

				if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;
				var targetCell = GenRadial.RadialCellsAround(shooterCell, 12f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
					.Where(cell => cell.DistanceTo(shooterCell) <= 12f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.FirstOrDefault();
				if (targetCell.IsValid == false)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						error = "No line-of-sight target cell was found for the ranged projectile patch fixture."
					};
				}

				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_RangedPatchShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_RangedPatchZombie", spawnedThings);
				if (shooter == null || zombie == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = "Could not create the ranged projectile patch fixture."
					};
				}
				if (TryMakeDownedForCombat(zombie, out var downedError) == false)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = downedError
					};
				}

				var verb = shooter.equipment?.PrimaryEq?.PrimaryVerb as Verb_LaunchProjectile;
				if (verb == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = "The shooter has no launch-projectile verb."
					};
				}

				var helperProbe = VerifyRangedProjectilePatchHelpers(verb, shooter, zombie);
				var oldForcedMissRadius = (float)forcedMissRadiusField.GetValue(verb.verbProps);
				var oldWarmupTime = (float)warmupTimeField.GetValue(verb.verbProps);
				var projectilesBefore = map.listerThings.AllThings.OfType<Projectile>().Select(projectile => projectile.ThingID).ToHashSet();
				Projectile launchedProjectile = null;
				var castResult = false;
				object castError = null;
				try
				{
					forcedMissRadiusField.SetValue(verb.verbProps, 9f);
					warmupTimeField.SetValue(verb.verbProps, 0f);
					castResult = verb.TryStartCastOn(zombie, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: false, nonInterruptingSelfCast: false);
					launchedProjectile = map.listerThings.AllThings
						.OfType<Projectile>()
						.FirstOrDefault(projectile => projectilesBefore.Contains(projectile.ThingID) == false);
					if (launchedProjectile != null)
						spawnedThings.Add(launchedProjectile);
				}
				catch (Exception ex)
				{
					castError = ex.GetBaseException().Message;
				}
				finally
				{
					forcedMissRadiusField.SetValue(verb.verbProps, oldForcedMissRadius);
					warmupTimeField.SetValue(verb.verbProps, oldWarmupTime);
				}

				var destination = launchedProjectile == null ? Vector3.zero : (Vector3)destinationField.GetValue(launchedProjectile);
				var destinationCell = destination.ToIntVec3();
				var destinationHitsZombie = launchedProjectile != null && destinationCell == zombie.Position;
				var intendedTargetIsZombie = launchedProjectile != null && launchedProjectile.intendedTarget.Thing == zombie;
				var usedTargetIsZombie = launchedProjectile != null && launchedProjectile.usedTarget.Thing == zombie;

				var injuryBeforeImpact = TotalInjurySeverity(zombie);
				var vanillaWouldMissSeed = FindRandChanceSeed(false, 0.5f);
				object impactError = null;
				var randPushed = false;
				try
				{
					if (launchedProjectile != null)
					{
						Rand.PushState(vanillaWouldMissSeed);
						randPushed = true;
						impactSomething.Invoke(launchedProjectile, Array.Empty<object>());
						Rand.PopState();
						randPushed = false;
					}
				}
				catch (Exception ex)
				{
					if (randPushed)
						Rand.PopState();
					impactError = ex.GetBaseException().Message;
				}
				var injuryAfterImpact = TotalInjurySeverity(zombie);
				var impactDamagedDownedZombie = injuryAfterImpact > injuryBeforeImpact || zombie.Dead;

				return new
				{
					success = projectilePatchTargets.Length > 0
						&& launchPatchTargets.Length > 0
						&& ObjectSuccess(helperProbe)
						&& castResult
						&& castError == null
						&& launchedProjectile != null
						&& intendedTargetIsZombie
						&& usedTargetIsZombie
						&& destinationHitsZombie
						&& impactError == null
						&& impactDamagedDownedZombie,
					patchTargets = new
					{
						projectileImpact = projectilePatchTargets,
						launchProjectile = launchPatchTargets
					},
					shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					distanceSquared = (targetCell - shooterCell).LengthHorizontalSquared,
					settings = new
					{
						ZombieSettings.Values.threatScale,
						Constants.COLONISTS_HIT_ZOMBIES_CHANCE
					},
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					verb = DescribeVerb(verb),
					helperProbe,
					forcedMissRadius = new
					{
						before = oldForcedMissRadius,
						probe = 9f,
						after = (float)forcedMissRadiusField.GetValue(verb.verbProps)
					},
					warmupTime = new
					{
						before = oldWarmupTime,
						probe = 0f,
						after = (float)warmupTimeField.GetValue(verb.verbProps)
					},
					cast = new
					{
						castResult,
						castError,
						projectileDef = launchedProjectile?.def?.defName,
						projectileThingId = launchedProjectile?.ThingID,
						usedTargetIsZombie,
						intendedTargetIsZombie,
						destination = DescribeVector(destination),
						destinationCell = launchedProjectile == null ? null : ZombieRuntimeActions.DescribeCell(destinationCell),
						destinationHitsZombie
					},
					impact = new
					{
						vanillaWouldMissSeed,
						impactError,
						injuryBeforeImpact,
						injuryAfterImpact,
						injuryDelta = injuryAfterImpact - injuryBeforeImpact,
						impactDamagedDownedZombie,
						zombieDead = zombie.Dead
					}
				};
			}
			finally
			{
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = oldColonistsHitChance;
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyRangedProjectilePatchHelpers(Verb verb, Pawn shooter, Zombie zombie)
		{
			var postureFix = FindNestedPatchMethod("Projectile_ImpactSomething_Patch", "GetPostureFix");
			var randChance = FindNestedPatchMethod("Projectile_ImpactSomething_Patch", "RandChance");
			var skipMissing = FindNestedPatchMethod("Verb_LaunchProjectile_TryCastShot_Patch", "SkipMissingShotsAtZombies");
			if (postureFix == null || randChance == null || skipMissing == null)
			{
				return new
				{
					success = false,
					postureFixFound = postureFix != null,
					randChanceFound = randChance != null,
					skipMissingFound = skipMissing != null
				};
			}

			var zombiePosture = (PawnPosture)postureFix.Invoke(null, new object[] { zombie });
			var shooterPosture = (PawnPosture)postureFix.Invoke(null, new object[] { shooter });
			var zombieHalfChance = (bool)randChance.Invoke(null, new object[] { 0.5f, zombie });
			var nonZombieZeroChance = (bool)randChance.Invoke(null, new object[] { 0f, shooter });
			var skipMissingAtZombie = (bool)skipMissing.Invoke(null, new object[] { verb, new LocalTargetInfo(zombie) });
			var skipMissingAtShooter = (bool)skipMissing.Invoke(null, new object[] { verb, new LocalTargetInfo(shooter) });

			return new
			{
				success = zombiePosture == PawnPosture.Standing
					&& shooterPosture == shooter.GetPosture()
					&& zombieHalfChance
					&& nonZombieZeroChance == false
					&& skipMissingAtZombie
					&& skipMissingAtShooter == false,
				zombiePosture = zombiePosture.ToString(),
				shooterPosture = shooterPosture.ToString(),
				shooterActualPosture = shooter.GetPosture().ToString(),
				zombieHalfChance,
				nonZombieZeroChance,
				skipMissingAtZombie,
				skipMissingAtShooter
			};
		}

		static MethodInfo FindNestedPatchMethod(string nestedTypeName, string methodName)
		{
			return typeof(Patches)
				.GetNestedTypes(BindingFlags.NonPublic)
				.FirstOrDefault(type => type.Name == nestedTypeName)
				?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		}

		static int FindRandChanceSeed(bool desiredResult, float chance)
		{
			for (var seed = 1; seed < 10000; seed++)
			{
				Rand.PushState(seed);
				var result = Rand.Chance(chance);
				Rand.PopState();
				if (result == desiredResult)
					return seed;
			}
			return 1;
		}

		static object VerifyDownedMeleeAttack(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DownedMeleeActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_DownedMeleeZombie", spawnedThings);
			if (actor == null || zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "Could not create the downed melee fixture."
				};
			}

			if (TryMakeDownedForCombat(zombie, out var downedError) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = downedError
				};
			}

			var healthDownedBefore = zombie.health.Downed;
			var publicDownedBefore = zombie.Downed;
			var injuryBefore = TotalInjurySeverity(zombie);
			var job = JobMaker.MakeJob(JobDefOf.AttackMelee, zombie);
			job.killIncappedTarget = false;
			job.playerForced = false;
			actor.drafter.Drafted = true;
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var startedJob = actor.CurJobDef?.defName;
			var samples = new List<object>();
			var attacked = false;
			for (var tick = 1; tick <= 900 && actor.Destroyed == false && zombie.Destroyed == false; tick++)
			{
				AdvanceGameTicks(1);
				var actorStance = actor.stances?.curStance?.GetType().Name;
				attacked |= actor.stances?.curStance is Stance_Cooldown;
				var injuryNow = TotalInjurySeverity(zombie);
				if (tick == 1 || tick == 60 || tick == 180 || tick == 900 || attacked || injuryNow > injuryBefore || zombie.Dead)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						actorStance,
						attacked,
						zombieHealthDowned = zombie.health.Downed,
						zombiePublicDowned = zombie.Downed,
						zombieInjury = injuryNow,
						zombieDead = zombie.Dead
					});
					if (attacked || injuryNow > injuryBefore || zombie.Dead)
						break;
				}
			}

			var injuryAfter = TotalInjurySeverity(zombie);
			var damaged = injuryAfter > injuryBefore || zombie.Dead;
			return new
			{
				success = healthDownedBefore
					&& publicDownedBefore
					&& startedJob == JobDefOf.AttackMelee.defName
					&& (attacked || damaged),
				startedJob,
				jobKillIncappedTarget = job.killIncappedTarget,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				healthDownedBefore,
				publicDownedBefore,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				damaged,
				attacked,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				samples = samples.ToArray()
			};
		}

		static object VerifyDownedAttackStatic(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
				return shooterError;
			var targetCell = GenRadial.RadialCellsAround(shooterCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
				.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(shooterCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
					error = "No line-of-sight target cell was found for the downed AttackStatic fixture."
				};
			}

			var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_DownedStaticShooter", shooterCell, Faction.OfPlayer, spawnedThings);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_DownedStaticZombie", spawnedThings);
			if (shooter == null || zombie == null)
			{
				return new
				{
					success = false,
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					error = "Could not create the downed AttackStatic fixture."
				};
			}
			if (TryMakeDownedForCombat(zombie, out var downedError) == false)
			{
				return new
				{
					success = false,
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					error = downedError
				};
			}

			RefreshZombieTargetCache(map);
			var healthDownedBefore = zombie.health.Downed;
			var publicDownedBefore = zombie.Downed;
			var injuryBefore = TotalInjurySeverity(zombie);
			var job = JobMaker.MakeJob(JobDefOf.AttackStatic, zombie);
			job.killIncappedTarget = false;
			job.playerForced = false;
			shooter.drafter.Drafted = true;
			shooter.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var startedJob = shooter.CurJobDef?.defName;
			var verb = shooter.equipment?.PrimaryEq?.PrimaryVerb;
			var samples = new List<object>();
			var attacked = false;
			for (var tick = 1; tick <= 1200 && shooter.Destroyed == false && zombie.Destroyed == false; tick++)
			{
				AdvanceGameTicks(1);
				var shooterStance = shooter.stances?.curStance?.GetType().Name;
				attacked |= shooter.stances?.curStance is Stance_Cooldown;
				var injuryNow = TotalInjurySeverity(zombie);
				if (tick == 1 || tick == 60 || tick == 180 || tick == 600 || tick == 1200 || attacked || injuryNow > injuryBefore || zombie.Dead)
				{
					samples.Add(new
					{
						tick,
						shooterJob = shooter.CurJobDef?.defName,
						shooterStance,
						attacked,
						zombieHealthDowned = zombie.health.Downed,
						zombiePublicDowned = zombie.Downed,
						zombieInjury = injuryNow,
						zombieDead = zombie.Dead
					});
					if (attacked || injuryNow > injuryBefore || zombie.Dead)
						break;
				}
			}

			var injuryAfter = TotalInjurySeverity(zombie);
			var damaged = injuryAfter > injuryBefore || zombie.Dead;
			return new
			{
				success = healthDownedBefore
					&& publicDownedBefore
					&& startedJob == JobDefOf.AttackStatic.defName
					&& (attacked || damaged),
				startedJob,
				jobKillIncappedTarget = job.killIncappedTarget,
				shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				healthDownedBefore,
				publicDownedBefore,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				damaged,
				attacked,
				shooter = DescribePawn(shooter),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				samples = samples.ToArray()
			};
		}

		static float InvokeFriendlyFireOffset(string methodName, IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
		{
			var method = typeof(AttackTargetFinder).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
			return (float)method.Invoke(null, new object[] { target, searcher, verb });
		}

		static string[] PatchOwners(string methodName)
		{
			var method = typeof(AttackTargetFinder).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return PatchOwners(method);
		}

		static string[] PatchOwners(Type type, string methodName)
		{
			var method = type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return PatchOwners(method);
		}

		static string[] PatchOwners(MethodBase method)
		{
			if (method == null)
				return Array.Empty<string>();
			var patchInfo = Harmony.GetPatchInfo(method);
			return (patchInfo?.Prefixes ?? Enumerable.Empty<Patch>())
				.Concat(patchInfo?.Postfixes ?? Enumerable.Empty<Patch>())
				.Concat(patchInfo?.Transpilers ?? Enumerable.Empty<Patch>())
				.Select(patch => patch.owner)
				.Distinct()
				.OrderBy(owner => owner)
				.ToArray() ?? Array.Empty<string>();
		}

		static object[] PatchedMethodsForPatchClass(string nestedClassName)
		{
			return Harmony.GetAllPatchedMethods()
				.Select(method => new
				{
					method,
					patchInfo = Harmony.GetPatchInfo(method)
				})
				.Select(entry => new
				{
					entry.method,
					patches = (entry.patchInfo?.Prefixes ?? Enumerable.Empty<Patch>())
						.Concat(entry.patchInfo?.Postfixes ?? Enumerable.Empty<Patch>())
						.Concat(entry.patchInfo?.Transpilers ?? Enumerable.Empty<Patch>())
						.Where(patch => patch.PatchMethod?.DeclaringType?.Name == nestedClassName)
						.ToArray()
				})
				.Where(entry => entry.patches.Length > 0)
				.Select(entry => new
				{
					method = entry.method.FullDescription(),
					owners = entry.patches.Select(patch => patch.owner).Distinct().OrderBy(owner => owner).ToArray(),
					patchMethods = entry.patches.Select(patch => patch.PatchMethod?.FullDescription()).Distinct().OrderBy(text => text).ToArray()
				})
				.Cast<object>()
				.ToArray();
		}

		static bool TryMakeDownedForCombat(Pawn pawn, out string error)
		{
			error = null;
			if (pawn == null)
			{
				error = "Pawn was null.";
				return false;
			}
			if (pawn.RaceProps.IsFlesh)
			{
				var bloodLoss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
				bloodLoss.Severity = 0.45f;
				pawn.health.hediffSet.AddDirect(bloodLoss);
			}
			var anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
			anesthetic.Severity = 1f;
			pawn.health.hediffSet.AddDirect(anesthetic);

			var makeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
			if (makeDowned == null)
			{
				error = "Could not reflect Pawn_HealthTracker.MakeDowned.";
				return false;
			}
			makeDowned.Invoke(pawn.health, new object[makeDowned.GetParameters().Length]);
			if (pawn.health.Downed == false)
			{
				error = "Pawn_HealthTracker.MakeDowned did not leave the pawn health-downed.";
				return false;
			}
			return true;
		}

		static Pawn SpawnMeleeAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			Pawn pawn = null;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				var candidate = GenerateAreaWorkflowPawn(faction, true);
				if (candidate.WorkTagIsDisabled(WorkTags.Violent))
					continue;
				pawn = candidate;
				break;
			}
			pawn ??= GenerateAreaWorkflowPawn(faction, true);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			spawnedThings.Add(pawn);
			return pawn;
		}

		static void EquipAreaWorkflowMeleeWeapon(Pawn pawn)
		{
			if (pawn?.equipment == null)
				return;
			pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club", false)
				?? DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.IsMeleeWeapon);
			var weaponStuff = weaponDef?.MadeFromStuff == true ? GenStuff.DefaultStuffFor(weaponDef) ?? ThingDefOf.Steel : null;
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef, weaponStuff) as ThingWithComps;
			if (weapon != null)
				pawn.equipment.AddEquipment(weapon);
		}

		static bool TryFindAdjacentPawnPairCells(Map map, IntVec3 root, out IntVec3 actorCell, out IntVec3 targetCell, out object error)
		{
			actorCell = IntVec3.Invalid;
			targetCell = IntVec3.Invalid;
			error = null;

			var candidates = GenRadial.RadialCellsAround(root, 12f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.ToArray();
			foreach (var candidate in candidates)
			{
				var adjacent = GenAdj.AdjacentCells
					.Select(offset => candidate + offset)
					.FirstOrDefault(cell => cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.Fogged(map) == false
						&& cell.GetFirstPawn(map) == null);
				if (adjacent.IsValid == false)
					continue;
				actorCell = candidate;
				targetCell = adjacent;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				error = "No adjacent clear pawn cells were found for the Wait_Combat auto-attack fixture."
			};
			return false;
		}

		static string StableId(object obj)
		{
			return ZombieRuntimeActions.StableThingId((obj as IAttackTarget)?.Thing ?? obj as Thing);
		}

		static string[] TargetIds(IEnumerable<Pair<IAttackTarget, float>> pairs)
		{
			return pairs.Select(pair => StableId(pair.first)).Where(id => id != null).ToArray();
		}

		static object[] DescribeTargetPairs(IEnumerable<Pair<IAttackTarget, float>> pairs)
		{
			return pairs.Select(pair => new
			{
				id = StableId(pair.first),
				thing = DescribeTarget(pair.first),
				score = pair.second
			}).Cast<object>().ToArray();
		}

		static object DescribeTarget(IAttackTarget target)
		{
			var thing = target?.Thing;
			return new
			{
				id = StableId(target),
				defName = thing?.def?.defName,
				kindDef = (thing as Pawn)?.kindDef?.defName,
				label = thing?.LabelCap,
				position = thing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(thing.Position) : null
			};
		}

		static Area_Allowed EnsureAreaWorkflowArea(Map map, string suffix, AreaRiskMode mode, Color color, IEnumerable<IntVec3> activeCells)
		{
			var label = AreaWorkflowPrefix + suffix;
			var area = map.areaManager.AllAreas.OfType<Area_Allowed>().FirstOrDefault(candidate => candidate.Label == label);
			if (area == null)
			{
				if (map.areaManager.TryMakeNewAllowed(out area) == false)
					throw new InvalidOperationException("Could not create area workflow fixture area " + label + ".");
				area.labelInt = label;
			}
			else if (area.Label != label)
				area.labelInt = label;

			foreach (var cell in area.ActiveCells.ToArray())
				area[cell] = false;
			foreach (var cell in activeCells)
				if (cell.InBounds(map))
					area[cell] = true;

			area.colorInt = color;
			area.colorTextureInt = null;
			area.Drawer.material = null;
			area.Drawer.SetDirty();

			if (mode == AreaRiskMode.Ignore)
				_ = ZombieSettings.Values.dangerousAreas.Remove(area);
			else
				ZombieSettings.Values.dangerousAreas[area] = mode;

			return area;
		}

		static (Area_Allowed area, AreaRiskMode mode)[] FindAreaWorkflowAreas(Map map)
		{
			return map.areaManager.AllAreas
				.OfType<Area_Allowed>()
				.Where(area => area.Label.StartsWith(AreaWorkflowPrefix, StringComparison.Ordinal))
				.Select(area => (area, Dialog_ManageAreas_Patches.GetMode(area)))
				.OrderBy(pair => pair.area.Label)
				.ToArray();
		}

		static Color ExpectedAreaLabelColor(AreaRiskMode mode)
		{
			return mode switch
			{
				AreaRiskMode.ColonistInside => Dialog_ManageAreas_Patches.areaNameColonistInside,
				AreaRiskMode.ColonistOutside => Dialog_ManageAreas_Patches.areaNameColonistOutside,
				AreaRiskMode.ZombieInside => Dialog_ManageAreas_Patches.areaNameZombieInside,
				AreaRiskMode.ZombieOutside => Dialog_ManageAreas_Patches.areaNameZombieOutside,
				_ => Color.white,
			};
		}

	}
}
