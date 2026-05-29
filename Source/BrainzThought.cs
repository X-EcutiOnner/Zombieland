using RimWorld;

namespace ZombieLand
{
	public class BrainzThought : Thought
	{
		public override int CurStageIndex => 0;

		public override void ExposeData()
		{
			base.ExposeData();
		}

		public override float MoodOffset()
		{
			return 1f;
		}
	}
}
