using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed class ZombieCorpseAppearance : IExposable
	{
		public Gender gender = Gender.None;
		public long biologicalAgeTicks = -1L;
		public long chronologicalAgeTicks = -1L;
		public long birthAbsTicks = -1L;
		public BodyTypeDef bodyType;
		public HeadTypeDef headType;
		public HairDef hairDef;
		public float melanin = -1f;
		public Color hairColor = Color.clear;
		public bool hasHairColor;
		public bool wasMapPawnBefore;
		public bool isToxicSplasher;
		public bool isMiner;
		public bool isElectrifier;
		public bool isAlbino;
		public bool isDarkSlimer;
		public bool isHealer;
		public float bombTickingInterval = -1f;
		public float hasTankyShield = -1f;
		public float hasTankyHelmet = -1f;
		public float hasTankySuit = -1f;

		public static ZombieCorpseAppearance From(Zombie zombie)
		{
			if (zombie == null)
				return null;

			var snapshot = new ZombieCorpseAppearance();
			snapshot.Capture(zombie);
			return snapshot;
		}

		public void Capture(Zombie zombie)
		{
			if (zombie == null)
				return;

			gender = zombie.gender;
			wasMapPawnBefore = zombie.wasMapPawnBefore;
			isToxicSplasher = zombie.isToxicSplasher;
			isMiner = zombie.isMiner;
			isElectrifier = zombie.isElectrifier;
			isAlbino = zombie.isAlbino;
			isDarkSlimer = zombie.isDarkSlimer;
			isHealer = zombie.isHealer;
			bombTickingInterval = zombie.IsSuicideBomber ? 60f : -1f;
			hasTankyShield = zombie.hasTankyShield;
			hasTankyHelmet = zombie.hasTankyHelmet;
			hasTankySuit = zombie.hasTankySuit;

			if (zombie.ageTracker != null)
			{
				biologicalAgeTicks = zombie.ageTracker.AgeBiologicalTicks;
				chronologicalAgeTicks = zombie.ageTracker.AgeChronologicalTicks;
				birthAbsTicks = zombie.ageTracker.BirthAbsTicks;
			}

			if (zombie.story != null)
			{
				bodyType = zombie.story.bodyType;
				headType = zombie.story.headType;
				hairDef = zombie.story.hairDef;
				melanin = zombie.story.melanin;
				hairColor = zombie.story.hairColor;
				hasHairColor = true;
			}
		}

		public ZombieType SpawnType
		{
			get
			{
				if (bombTickingInterval > 0f)
					return ZombieType.SuicideBomber;
				if (isToxicSplasher)
					return ZombieType.ToxicSplasher;
				if (hasTankyHelmet > 0f || hasTankySuit > 0f)
					return ZombieType.TankyOperator;
				if (isMiner)
					return ZombieType.Miner;
				if (isElectrifier)
					return ZombieType.Electrifier;
				if (isAlbino)
					return ZombieType.Albino;
				if (isDarkSlimer)
					return ZombieType.DarkSlimer;
				if (isHealer)
					return ZombieType.Healer;
				return ZombieType.Normal;
			}
		}

		public void ApplyTo(Zombie zombie)
		{
			if (zombie == null)
				return;

			if (gender != Gender.None)
				zombie.gender = gender;
			zombie.wasMapPawnBefore = wasMapPawnBefore;
			zombie.isToxicSplasher = isToxicSplasher;
			zombie.isMiner = isMiner;
			zombie.isElectrifier = isElectrifier;
			zombie.isAlbino = isAlbino;
			zombie.isDarkSlimer = isDarkSlimer;
			zombie.isHealer = isHealer;
			zombie.bombTickingInterval = bombTickingInterval > 0f ? 60f : -1f;
			zombie.bombWillGoOff = false;
			zombie.hasTankyShield = hasTankyShield;
			zombie.hasTankyHelmet = hasTankyHelmet;
			zombie.hasTankySuit = hasTankySuit;
			zombie.electricDisabledUntil = 0;

			if (zombie.ageTracker != null && biologicalAgeTicks >= 0L)
			{
				zombie.ageTracker.AgeBiologicalTicks = biologicalAgeTicks;
				if (chronologicalAgeTicks >= 0L)
					zombie.ageTracker.AgeChronologicalTicks = chronologicalAgeTicks;
				if (birthAbsTicks >= 0L)
					zombie.ageTracker.BirthAbsTicks = birthAbsTicks;
				zombie.ageTracker.RecalculateLifeStageIndex();
			}

			if (zombie.story != null)
			{
				if (bodyType != null)
				{
					if (zombie.story.bodyType != bodyType)
						zombie.apparel?.DestroyAll();
					zombie.story.bodyType = bodyType;
				}
				if (headType != null)
					zombie.story.headType = headType;
				if (hairDef != null)
					zombie.story.hairDef = hairDef;
				if (melanin >= 0f)
					zombie.story.melanin = melanin;
				if (hasHairColor)
					zombie.story.hairColor = hairColor;
			}
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref gender, "gender", Gender.None);
			Scribe_Values.Look(ref biologicalAgeTicks, "biologicalAgeTicks", -1L);
			Scribe_Values.Look(ref chronologicalAgeTicks, "chronologicalAgeTicks", -1L);
			Scribe_Values.Look(ref birthAbsTicks, "birthAbsTicks", -1L);
			Scribe_Defs.Look(ref bodyType, "bodyType");
			Scribe_Defs.Look(ref headType, "headType");
			Scribe_Defs.Look(ref hairDef, "hairDef");
			Scribe_Values.Look(ref melanin, "melanin", -1f);
			Scribe_Values.Look(ref hairColor, "hairColor");
			Scribe_Values.Look(ref hasHairColor, "hasHairColor");
			Scribe_Values.Look(ref wasMapPawnBefore, "wasMapPawnBefore");
			Scribe_Values.Look(ref isToxicSplasher, "isToxicSplasher");
			Scribe_Values.Look(ref isMiner, "isMiner");
			Scribe_Values.Look(ref isElectrifier, "isElectrifier");
			Scribe_Values.Look(ref isAlbino, "isAlbino");
			Scribe_Values.Look(ref isDarkSlimer, "isDarkSlimer");
			Scribe_Values.Look(ref isHealer, "isHealer");
			Scribe_Values.Look(ref bombTickingInterval, "bombTickingInterval", -1f);
			Scribe_Values.Look(ref hasTankyShield, "hasTankyShield", -1f);
			Scribe_Values.Look(ref hasTankyHelmet, "hasTankyHelmet", -1f);
			Scribe_Values.Look(ref hasTankySuit, "hasTankySuit", -1f);
		}
	}
}
