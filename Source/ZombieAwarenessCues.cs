using Verse;

namespace ZombieLand
{
	static class ZombieAwarenessCues
	{
		static SettingsGroup Settings => ZombieSettings.Values;

		public static bool ShouldShowZombieEventLetter()
		{
			return Settings?.showZombieEventLetters ?? true;
		}

		public static bool ShouldPlayZombieEventSiren()
		{
			return Constants.USE_SOUND && Prefs.VolumeAmbient > 0f && (Settings?.playZombieEventSiren ?? true);
		}

		public static bool ShouldPlaySpecialZombieAmbientSound()
		{
			return Constants.USE_SOUND && Prefs.VolumeAmbient > 0f && (Settings?.playSpecialZombieAmbientSounds ?? true);
		}

		public static bool ShouldPlayZombieActionSound()
		{
			return Constants.USE_SOUND && (Settings?.playZombieActionSounds ?? true);
		}

		public static bool ShouldPlayWallAndSabotageSound()
		{
			return Constants.USE_SOUND && (Settings?.playWallAndSabotageSounds ?? true);
		}

		public static bool ShouldShowZombieThoughtBubble()
		{
			return Settings?.showZombieThoughtBubbles ?? true;
		}
	}
}
