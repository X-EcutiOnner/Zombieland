using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI.Group;

namespace ZombieLand
{
	[HarmonyPatch]
	static class PsychicRitualToil_SkipAbductionPlayer_ApplyOutcome_Patch
	{
		static HediffDef darkPsychicShockDef;

		static bool Prepare() => ModsConfig.AnomalyActive && TargetMethod() != null;

		static MethodBase TargetMethod()
		{
			return AccessTools.Method(
				"Verse.AI.Group.PsychicRitualToil_SkipAbductionPlayer:ApplyOutcome",
				new[] { typeof(PsychicRitual), typeof(Pawn) }
			);
		}

		static void Prefix(PsychicRitual psychicRitual, ref List<ZombieShockSnapshot> __state)
		{
			__state = null;
			var map = psychicRitual?.Map;
			var hediffDef = DarkPsychicShockDef();
			if (map == null || hediffDef == null)
				return;

			__state = map.mapPawns.AllPawnsSpawned
				.OfType<Zombie>()
				.Where(zombie => zombie.Destroyed == false)
				.Select(zombie => new ZombieShockSnapshot(zombie, DarkPsychicShockCount(zombie, hediffDef)))
				.ToList();
		}

		static void Postfix(List<ZombieShockSnapshot> __state)
		{
			var hediffDef = DarkPsychicShockDef();
			if (__state == null || hediffDef == null)
				return;

			var abductedZombie = __state
				.Where(snapshot => snapshot.Zombie != null && snapshot.Zombie.Destroyed == false)
				.FirstOrDefault(snapshot => DarkPsychicShockCount(snapshot.Zombie, hediffDef) > snapshot.DarkPsychicShockCount)
				?.Zombie;
			if (abductedZombie == null)
				return;

			var ticks = ZombieParalysis.SkipAbductionParalysisTicks();
			if (abductedZombie.TryParalyze(ticks, out var error, true, true) == false)
				Log.Warning($"Zombieland could not paralyze skip-abducted zombie {abductedZombie.ThingID}: {error}");
		}

		static HediffDef DarkPsychicShockDef()
		{
			darkPsychicShockDef ??= DefDatabase<HediffDef>.GetNamedSilentFail("DarkPsychicShock");
			return darkPsychicShockDef;
		}

		static int DarkPsychicShockCount(Pawn pawn, HediffDef hediffDef)
		{
			return pawn?.health?.hediffSet?.hediffs?.Count(hediff => hediff.def == hediffDef) ?? 0;
		}

		sealed class ZombieShockSnapshot
		{
			public readonly Zombie Zombie;
			public readonly int DarkPsychicShockCount;

			public ZombieShockSnapshot(Zombie zombie, int darkPsychicShockCount)
			{
				Zombie = zombie;
				DarkPsychicShockCount = darkPsychicShockCount;
			}
		}
	}
}
