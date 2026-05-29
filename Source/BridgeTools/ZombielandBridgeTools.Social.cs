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

	}
}
