using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	class ListableOption_Zombieland : ListableOption
	{
		public ListableOption_Zombieland(Action action) : base("Zombieland", action)
		{
			minHeight = 45f;
		}

		public override float DrawOption(Vector2 pos, float width)
		{
			var height = Mathf.Max(minHeight, Text.CalcHeight(label, width));
			var rect = new Rect(pos.x, pos.y, width, height);
			if (ButtonTextured(rect, label))
				action?.Invoke();
			return height;
		}

		static bool ButtonTextured(Rect rect, string label)
		{
			var anchor = Text.Anchor;
			var color = GUI.color;
			var wordWrap = Text.WordWrap;

			var atlas = Widgets.ButtonBGAtlas;
			if (Mouse.IsOver(rect))
			{
				atlas = Widgets.ButtonBGAtlasMouseover;
				if (Input.GetMouseButton(0))
					atlas = Widgets.ButtonBGAtlasClick;
				MouseoverSounds.DoRegion(rect);
			}

			Widgets.DrawAtlas(rect, atlas);
			var texture = Tools.GetZombieButtonBackground();
			if (texture != null)
				GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f);

			Text.Anchor = TextAnchor.MiddleCenter;
			Text.WordWrap = false;
			GUI.color = Color.white;
			Widgets.Label(rect, label);

			Text.Anchor = anchor;
			Text.WordWrap = wordWrap;
			GUI.color = color;

			return Widgets.ButtonInvisible(rect, false);
		}
	}
}
