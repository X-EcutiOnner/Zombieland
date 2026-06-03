using RimWorld;
using System;
using Verse;

namespace ZombieLand
{
	public enum AnomalyTargetingCategory
	{
		None,
		Ghouls,
		ShamblerMutants,
		OtherEntities,
		Nociosphere
	}

	public static class AnomalyTargeting
	{
		static bool IsAnomalyDef(Def def)
			=> string.Equals(def?.modContentPack?.PackageId, ModContentPack.AnomalyModPackageId, StringComparison.OrdinalIgnoreCase);

		static bool IsEntityKind(Pawn pawn)
			=> pawn?.kindDef?.defaultFactionDef?.defName == "Entities" || pawn?.Faction?.def?.defName == "Entities";

		static bool IsEntityFaction(Faction faction)
			=> ModsConfig.AnomalyActive && faction?.def?.defName == "Entities";

		public static bool TryGetCategory(Pawn pawn, out AnomalyTargetingCategory category)
		{
			category = AnomalyTargetingCategory.None;
			if (ModsConfig.AnomalyActive == false || pawn == null)
				return false;

			var kindName = pawn.kindDef?.defName;
			var thingName = pawn.def?.defName;
			if (kindName == "Nociosphere" || thingName == "Nociosphere")
			{
				category = AnomalyTargetingCategory.Nociosphere;
				return true;
			}

			if (kindName == "Ghoul")
			{
				category = AnomalyTargetingCategory.Ghouls;
				return true;
			}

			if (pawn.IsShambler || pawn.IsSubhuman && pawn.RaceProps.Humanlike)
			{
				category = AnomalyTargetingCategory.ShamblerMutants;
				return true;
			}

			if ((IsAnomalyDef(pawn.kindDef) || IsAnomalyDef(pawn.def)) && IsEntityKind(pawn))
			{
				category = AnomalyTargetingCategory.OtherEntities;
				return true;
			}

			return false;
		}

		public static AnomalyTargetingOverride OverrideFor(SettingsGroup settings, AnomalyTargetingCategory category)
		{
			settings ??= ZombieSettings.Values;
			return category switch
			{
				AnomalyTargetingCategory.Ghouls => settings?.anomalyGhoulTargeting ?? AnomalyTargetingOverride.Automatic,
				AnomalyTargetingCategory.ShamblerMutants => settings?.anomalyShamblerTargeting ?? AnomalyTargetingOverride.Automatic,
				AnomalyTargetingCategory.OtherEntities => settings?.anomalyEntityTargeting ?? AnomalyTargetingOverride.Automatic,
				AnomalyTargetingCategory.Nociosphere => settings?.anomalyNociosphereTargeting ?? AnomalyTargetingOverride.Automatic,
				_ => AnomalyTargetingOverride.Automatic,
			};
		}

		public static bool TryGetAttractionOverride(Pawn pawn, out bool attracts)
		{
			attracts = false;
			if (TryGetCategory(pawn, out var category) == false)
				return false;

			var mode = OverrideFor(ZombieSettings.Values, category);
			if (mode == AnomalyTargetingOverride.Automatic)
				return false;

			attracts = mode == AnomalyTargetingOverride.Allow;
			return true;
		}

		public static bool IsForcedTarget(Pawn pawn)
			=> TryGetAttractionOverride(pawn, out var attracts) && attracts;

		static bool IsActiveValidAnomalyPawn(Pawn pawn)
		{
			if (pawn == null)
				return false;
			if (pawn is Zombie)
				return false;
			if (pawn.Spawned == false)
				return false;
			if (pawn.Dead || pawn.Downed)
				return false;
			if (pawn.activity?.IsDormant == true || pawn.activity?.Deactivated == true)
				return false;
			if (pawn.canBeDormant?.Awake == false)
				return false;
			if (pawn.RaceProps.Humanlike && pawn.InfectionState() >= InfectionState.Infecting)
				return false;
			return true;
		}

		public static bool TryGetZombieHostilityOverride(Pawn pawn, out bool attacksZombies)
		{
			attacksZombies = false;
			if (TryGetCategory(pawn, out _) == false)
				return false;

			var mode = ZombieSettings.Values?.anomalyAttacksZombies ?? AnomalyTargetingOverride.Automatic;
			if (mode == AnomalyTargetingOverride.Automatic)
				return false;

			attacksZombies = mode == AnomalyTargetingOverride.Allow && IsActiveValidAnomalyPawn(pawn);
			return true;
		}

		public static bool TryGetZombieHostilityOverride(Faction faction, out bool attacksZombies)
		{
			attacksZombies = false;
			if (IsEntityFaction(faction) == false)
				return false;

			var mode = ZombieSettings.Values?.anomalyAttacksZombies ?? AnomalyTargetingOverride.Automatic;
			if (mode == AnomalyTargetingOverride.Automatic)
				return false;

			attacksZombies = mode == AnomalyTargetingOverride.Allow;
			return true;
		}

		public static bool BaseRuleTargets(AttackMode attackMode, AnomalyTargetingCategory category)
			=> attackMode == AttackMode.Everything
				|| category == AnomalyTargetingCategory.Ghouls && attackMode == AttackMode.OnlyHumans
				|| category == AnomalyTargetingCategory.ShamblerMutants && attackMode == AttackMode.OnlyHumans;

	}
}
