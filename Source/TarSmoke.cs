using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class TarSmoke : Gas
	{
		const float positionVariance = 0.45f;
		const float sizeVarianceMin = 0.8f;
		const float sizeVarianceMax = 1.2f;

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			var graphicData = def.graphicData;
			var drawSize = graphicData?.drawSize ?? new Vector2(2.5f, 2.5f);
			var alpha = Mathf.Clamp(graphicData?.color.a ?? 1f, 0.85f, 1f);
			var material = MaterialPool.MatFrom("TarSmoke", ShaderDatabase.Mote, new Color(0f, 0f, 0f, alpha));

			Rand.PushState(thingIDNumber.GetHashCode());
			var angle = Rand.Range(0f, 360f) + graphicRotation;
			var pos = this.TrueCenter() + new Vector3(Rand.Range(-positionVariance, positionVariance), 0f, Rand.Range(-positionVariance, positionVariance));
			var scale = new Vector3(Rand.Range(sizeVarianceMin, sizeVarianceMax) * drawSize.x, 0f, Rand.Range(sizeVarianceMin, sizeVarianceMax) * drawSize.y);
			Rand.PopState();

			var matrix = default(Matrix4x4);
			matrix.SetTRS(pos, Quaternion.AngleAxis(angle, Vector3.up), scale);
			Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
		}
	}
}
