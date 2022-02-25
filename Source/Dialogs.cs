﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	class SettingsDialog : Page
	{
		public override string PageTitle => "ZombielandGameSettings".Translate();

		public override void PreOpen()
		{
			base.PreOpen();
			Dialogs.scrollPosition = Vector2.zero;
			Dialogs.focusOnSearch = true;
			Dialogs.searchWidget.Reset();
		}

		public override void DoWindowContents(Rect inRect)
		{
			DrawPageTitle(inRect);
			var mainRect = GetMainRect(inRect, 0f, false);
			Dialogs.DoWindowContentsInternal(ref ZombieSettings.Values, mainRect);
			DoBottomButtons(inRect, null, null, null, true, true);
		}
	}

	public class Dialog_SaveThenUninstall : Dialog_SaveFileList
	{
		public override bool ShouldDoTypeInField => true;

		public Dialog_SaveThenUninstall()
		{
			interactButLabel = "OverwriteButton".Translate();
			bottomAreaHeight = 85f;
			if (Faction.OfPlayer.HasName)
				typingName = Faction.OfPlayer.Name;
			else
				typingName = SaveGameFilesUtility.UnusedDefaultFileName(Faction.OfPlayer.def.LabelCap);
		}

		public override void DoFileInteraction(string fileName)
		{
			Close(true);
			ZombieRemover.RemoveZombieland(fileName);
		}

		public override void PostClose()
		{
		}

		public static void Run()
		{
			// for quick debugging
			// ZombieRemover.RemoveZombieland(null);
			// return;

			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmUninstallZombieland".Translate(), () =>
			{
				Find.WindowStack.currentlyDrawnWindow.Close();
				Find.WindowStack.Add(new Dialog_SaveThenUninstall());

			}, true, null));
		}
	}

	public class Dialog_ErrorMessage : Window
	{
		public string text;
		Vector2 scrollPosition;

		public override Vector2 InitialSize => new Vector2(640f, 460f);

		public Dialog_ErrorMessage(string text)
		{
			this.text = text;
			doCloseX = true;
			forcePause = true;
			absorbInputAroundWindow = true;
			onlyOneOfTypeAllowed = true;
			closeOnAccept = true;
			closeOnCancel = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			var y = inRect.y;

			Text.Font = GameFont.Small;
			Widgets.Label(new Rect(0f, y, inRect.width, 42f), "Zombieland Error");
			y += 42f;

			Text.Font = GameFont.Tiny;
			var outRect = new Rect(inRect.x, y, inRect.width, inRect.height - 35f - 5f - y);
			float width = outRect.width - 16f;
			var viewRect = new Rect(0f, 0f, width, Text.CalcHeight(text, width));
			Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);
			Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), text);
			Widgets.EndScrollView();
		}
	}

	static class Dialogs
	{
		static Color contentColor = new Color(1f, 1f, 1f, 0.7f);
		const float inset = 6f;
		public static string currentHelpItem = null;

		public static void Help(this Listing_Standard list, string helpItem, float height = 0f)
		{
			var curX = list.curX;
			var curY = list.curY;
			var rect = new Rect(curX, curY, list.ColumnWidth, height > 0f ? height : Text.LineHeight);
			if (Mouse.IsOver(rect))
				currentHelpItem = helpItem;
		}

		public static void Dialog_Label(this Listing_Standard list, string labelId, Color color, bool provideHelp = true)
		{
			var labelText = provideHelp ? labelId.SafeTranslate() : labelId;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var textHeight = Text.CalcHeight(labelText, list.ColumnWidth - 3f - inset) + 2 * 3f;

			if (provideHelp) list.Help(labelId);

			var rect = list.GetRect(textHeight).Rounded();
			var color2 = color;
			color2.r *= 0.25f;
			color2.g *= 0.25f;
			color2.b *= 0.25f;
			color2.a *= 0.2f;
			GUI.color = color2;
			var r = rect.ContractedBy(1f);
			r.yMax -= 2f;
			GUI.DrawTexture(r, BaseContent.WhiteTex);
			GUI.color = color;
			rect.xMin += inset;
			Widgets.Label(rect, labelText);
			GUI.color = Color.white;
			Text.Anchor = anchor;
		}

		public static void Dialog_Text(this Listing_Standard list, GameFont font, string textId, params object[] args)
		{
			var text = textId.SafeTranslate(args);
			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var savedFont = Text.Font;
			Text.Font = font;
			var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - inset) + 2 * 3f;
			list.Help(textId);
			var rect = list.GetRect(textHeight).Rounded();
			GUI.color = Color.white;
			rect.xMin += inset;
			Widgets.Label(rect, text);
			Text.Anchor = anchor;
			Text.Font = savedFont;
		}

		public static void Dialog_Button(this Listing_Standard list, string desc, string labelId, bool dangerous, Action action)
		{
			list.Gap(6f);

			var description = desc.SafeTranslate();
			var buttonText = labelId.SafeTranslate();
			var descriptionWidth = (list.ColumnWidth - 3 * inset) * 2 / 3;
			var buttonWidth = list.ColumnWidth - 3 * inset - descriptionWidth;
			var height = Math.Max(30f, Text.CalcHeight(description, descriptionWidth));

			list.Help(labelId, height);

			var rect = list.GetRect(height);
			var rect2 = rect;
			rect.xMin += inset;
			rect.width = descriptionWidth;
			Widgets.Label(rect, description);

			rect2.xMax -= inset;
			rect2.xMin = rect2.xMax - buttonWidth;
			rect2.yMin += (height - 30f) / 2;
			rect2.yMax -= (height - 30f) / 2;

			var color = GUI.color;
			GUI.color = dangerous ? new Color(1f, 0.3f, 0.35f) : Color.white;
			if (Widgets.ButtonText(rect2, buttonText, true, true, true)) action();
			GUI.color = color;
		}

		public static void Dialog_Checkbox(this Listing_Standard list, string labelId, ref bool forBool)
		{
			list.Gap(2f);

			var label = labelId.SafeTranslate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			list.Help(labelId, height);

			var rect = list.GetRect(height);
			rect.xMin += inset;

			var oldValue = forBool;
			var butRect = rect;
			butRect.xMin += 24f;
			if (Widgets.ButtonInvisible(butRect, false))
				forBool = !forBool;
			if (forBool != oldValue)
			{
				if (forBool)
					SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera(null);
				else
					SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera(null);
			}

			Widgets.Checkbox(new Vector2(rect.x, rect.y - 1f), ref forBool);

			var curX = list.curX;
			list.curX = curX + indent;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			rect.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(rect, label);
			GUI.color = color;
			Text.Anchor = anchor;

			list.curX = curX;
		}

		public static bool Dialog_RadioButton(this Listing_Standard list, bool active, string labelId)
		{
			var label = labelId.SafeTranslate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			list.Help(labelId, height);

			var rect = list.GetRect(height);
			rect.xMin += inset;
			var line = new Rect(rect);
			var result = Widgets.RadioButton(line.xMin, line.yMin, active);

			var curX = list.curX;
			list.curX = curX + indent;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			line.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(line, label);
			GUI.color = color;
			Text.Anchor = anchor;

			list.curX = curX;

			result |= Widgets.ButtonInvisible(rect, false);
			if (result && !active)
				SoundDefOf.Click.PlayOneShotOnCamera(null);

			return result;
		}

		public static void Dialog_Enum<T>(this Listing_Standard list, string desc, ref T forEnum)
		{
			list.Dialog_Label(desc, Color.yellow);

			var type = forEnum.GetType();
			var choices = Enum.GetValues(type);
			foreach (var choice in choices)
			{
				list.Gap(2f);
				var label = type.Name + "_" + choice.ToString();
				if (list.Dialog_RadioButton(forEnum.Equals(choice), label))
					forEnum = (T)choice;
			}
		}

		public static void Dialog_List<T>(this Listing_Standard list, string labelId, T value, Action<T> updateValue, List<T> choices, Func<T, string> translator, T defaultValue)
		{
			var labelText = labelId.SafeTranslate();
			var valueText = choices.Contains(value) ? value.ToString() : defaultValue.ToString();

			var extraSpace = "_".GetWidthCached();
			var descLength = labelText.GetWidthCached() + extraSpace;
			var valueLength = valueText.GetWidthCached();

			if (translator == null) translator = val => val.ToString();

			list.Help(labelId, Text.LineHeight);

			var rectLine = list.GetRect(Text.LineHeight);
			rectLine.xMin += inset;
			rectLine.xMax -= inset;

			var rectLeft = rectLine.LeftPartPixels(descLength).Rounded();
			var rectRight = rectLine.RightPartPixels(valueLength).Rounded();

			var color = GUI.color;
			var anchor = Text.Anchor;
			GUI.color = contentColor;
			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rectLeft, labelText);
			GUI.color = Color.white;
			Widgets.Label(rectRight, valueText);
			Text.Anchor = anchor;
			GUI.color = color;

			if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rectLine))
			{
				var found = false;
				var options = choices.Select(choice =>
				{
					var matches = choice.Equals(value);
					found |= matches;
					return new FloatMenuOption($"{translator(choice)}{(choice.Equals(value) ? " ✓" : "")}", () => updateValue(choice));
				}).ToList();
				if (choices.Contains(defaultValue) == false)
					options.Insert(0, new FloatMenuOption($"{translator(defaultValue)}{(found ? "" : " ✓")}", () => updateValue(default)));
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		public static void Dialog_Integer(this Listing_Standard list, string labelId, string unit, int min, int max, ref int value)
		{
			list.Gap(6f);

			var unitString = unit.SafeTranslate();
			var extraSpace = "_".GetWidthCached();
			var descLength = labelId.Translate().GetWidthCached() + extraSpace;
			var unitLength = (unit == null) ? 0 : unitString.GetWidthCached() + extraSpace;

			list.Help(labelId, Text.LineHeight);

			var rectLine = list.GetRect(Text.LineHeight);
			rectLine.xMin += inset;
			rectLine.xMax -= inset;

			var rectLeft = rectLine.LeftPartPixels(descLength).Rounded();
			var rectRight = rectLine.RightPartPixels(unitLength).Rounded();
			var rectMiddle = new Rect(rectLeft.xMax, rectLeft.yMin, rectRight.xMin - rectLeft.xMax, rectLeft.height);

			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(rectLeft, labelId.Translate());

			var alignment = Text.CurTextFieldStyle.alignment;
			Text.CurTextFieldStyle.alignment = TextAnchor.MiddleRight;
			var buffer = value.ToString();
			Widgets.TextFieldNumeric(rectMiddle, ref value, ref buffer, min, max);
			Text.CurTextFieldStyle.alignment = alignment;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleRight;
			Widgets.Label(rectRight, unitString);
			Text.Anchor = anchor;

			GUI.color = color;
		}

		public static void Dialog_FloatSlider(this Listing_Standard list, string labelId, Func<float, string> labelFormatFunc, bool logarithmic, ref float value, float min, float max, Func<float, float> formatFunc = null)
		{
			list.Help(labelId, 32f);

			list.Gap(12f);

			var format = labelFormatFunc(value);
			var valstr = string.Format(format, formatFunc != null ? formatFunc(value) : value);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var inValue = logarithmic ? (float)(1 - Math.Pow(1 - (double)value, 10)) : value;
			if (inValue < min) inValue = min;
			if (inValue > max) inValue = max;
			var outValue = Widgets.HorizontalSlider(srect, inValue, min, max, false, null, labelId.SafeTranslate(), valstr, -1f);
			value = logarithmic ? (float)(1 - Math.Pow(1 - outValue, 1 / (double)10)) : outValue;
			if (value < min) value = min;
			if (value > max) value = max;
		}

		public static void Dialog_EnumSlider<T>(this Listing_Standard list, string labelId, ref T forEnum)
		{
			list.Help(labelId, 32f);

			var type = forEnum.GetType();
			var choices = Enum.GetValues(type);
			var max = choices.Length - 1;

			list.Gap(12f);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var value = $"{typeof(T).Name}_{forEnum}".SafeTranslate();
			var n = (int)Widgets.HorizontalSlider(srect, Convert.ToInt32(forEnum), 0, max, false, null, labelId.SafeTranslate(), value, 1);
			forEnum = (T)Enum.ToObject(typeof(T), n);
		}

		public static void Dialog_IntSlider(this Listing_Standard list, string labelId, Func<int, string> format, ref int value, int min, int max)
		{
			list.Help(labelId, 32f);

			list.Gap(12f);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			value = (int)(0.5f + Widgets.HorizontalSlider(srect, value, min, max, false, null, labelId.SafeTranslate(), format(value), -1f));
		}

		public static void Dialog_TimeSlider(this Listing_Standard list, string labelId, ref int value, int min, int max, Func<int, string> valueStringConverter = null, bool fullDaysOnly = false)
		{
			list.Gap(-4f);
			list.Help(labelId, 32f);

			list.Gap(12f);

			if (valueStringConverter == null) valueStringConverter = (n) => null;
			var valstr = valueStringConverter(value) ?? Tools.TranslateHoursToText(value);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var newValue = (double)Widgets.HorizontalSlider(srect, value, min, max, false, null, labelId.SafeTranslate(), valstr, -1f);
			if (fullDaysOnly)
				value = (int)(Math.Round(newValue / 24f, MidpointRounding.ToEven) * 24f);
			else
				value = (int)newValue;
		}

		public static void ChooseExtractArea(Listing_Standard list, SettingsGroup settings)
		{
			if (Current.Game == null) return;
			var multiMap = Find.Maps.Count > 1;
			var areas = Find.Maps
				.SelectMany(map => map.areaManager.AllAreas
				.Where(area => area.Mutable)
				.OrderBy(area => area.ListPriority)
				.Select(area => (map, area)))
				.Select(pair => multiMap ? $"{pair.area.Label}:{pair.map.Index + 1}" : pair.area.Label)
				.ToList();
			list.Gap(-2f);
			list.Dialog_List("ExtractZombieArea", settings.extractZombieArea, area => settings.extractZombieArea = area ?? "", areas, null, "Everywhere".Translate());
		}

		public static void ChooseWanderingStyle(Listing_Standard list, SettingsGroup settings)
		{
			var defaultChoice = Enum.GetName(typeof(WanderingStyle), WanderingStyle.Smart);
			var choices = Enum.GetValues(typeof(WanderingStyle)).Cast<WanderingStyle>().ToList();
			list.Dialog_List("SmartWandering", settings.wanderingStyle, value => settings.wanderingStyle = value, choices, value => $"SmartWandering_{value}".Translate(), WanderingStyle.Smart);
		}

		public static string ExtractAmount(float f)
		{
			if (f == 0) return "Off".TranslateSimple();
			return "{0:0%} " + "CorpsesExtractChance".Translate(f);
		}

		public static void MiniButton(this Listing_Standard list, Texture2D texture, Action action)
		{
			const float size = 11f;
			var butRect = new Rect(list.curX + 1, list.curY + 2, size, size);
			if (Widgets.ButtonImage(butRect, texture, true))
				action();
		}

		public static int exampleMeleeSkill = 10;
		public static int exampleZombieCount = 1;
		public static void ExplainSafeMelee(Listing_Standard list, int safeMeleeLimit)
		{
			Text.Font = GameFont.Tiny;
			var chance = Mathf.FloorToInt(100f * exampleMeleeSkill * Mathf.Max(0, safeMeleeLimit - exampleZombieCount + 1) / 20f);
			var text = "SafeMeleeExample".Translate(exampleMeleeSkill, exampleZombieCount, chance).Resolve();
			var buttonText = "[_]";
			var buttonWidth = buttonText.GetWidthCached();
			list.curX = 7f;
			for (var i = 0; i <= 4; i++)
			{
				var idx = text.IndexOf(buttonText);
				var part = idx == -1 ? text : text.Substring(0, idx);

				var num = Text.CalcHeight("x", list.ColumnWidth);
				var rect = new Rect(list.curX, list.curY, list.ColumnWidth, num);
				Widgets.Label(rect, part);
				list.curX += part.GetWidthCached();
				if (i == 4)
					break;

				list.MiniButton(i % 2 == 0 ? Constants.MinusButton : Constants.PlusButton, () =>
				{
					switch (i)
					{
						case 0:
							if (exampleMeleeSkill > 0) exampleMeleeSkill--;
							break;
						case 1:
							exampleMeleeSkill++;
							break;
						case 2:
							if (exampleZombieCount > 1) exampleZombieCount--;
							break;
						case 3:
							exampleZombieCount++;
							break;
					}
				});
				list.curX += buttonWidth;

				text = text.Substring(idx + buttonText.Length);
			}

			list.curX = 0;
			list.Gap(12);
		}

		public static bool Section<T>(params string[] term)
		{
			var search = searchWidget.filter.Text.Trim().ToLower();
			if (search == "") return true;
			if (term.Any(t =>
			{
				if (t.StartsWith(":"))
				{
					t = t.Substring(1);
					if (t.SafeTranslate().ToLower().Contains(search)) return true;
					if ($"{t}_Help".SafeTranslate().ToLower().Contains(search)) return true;
					return false;
				}
				return t.ToLower().Contains(search);
			})) return true;
			var type = typeof(T);
			if (type != typeof(string))
			{
				if (type.Name.SafeTranslate().ToLower().Contains(search)) return true;
				var choices = Enum.GetValues(type);
				foreach (var choice in choices)
				{
					var label = type.Name + "_" + choice.ToString();
					if (label.SafeTranslate().ToLower().Contains(search)) return true;
					if ($"{label}_Help".SafeTranslate().ToLower().Contains(search)) return true;
				}
			}
			return false;
		}

		public static QuickSearchWidget searchWidget = new QuickSearchWidget();
		public static bool focusOnSearch = true;
		public static Vector2 scrollPosition = Vector2.zero;
		public static void DoWindowContentsInternal(ref SettingsGroup settings, Rect inRect)
		{
			if (settings == null) settings = new SettingsGroup();

			inRect.yMin += 15f;
			inRect.yMax -= 15f;

			var firstColumnWidth = (inRect.width - Listing.ColumnSpacing) * 3.5f / 5f;
			var secondColumnWidth = inRect.width - Listing.ColumnSpacing - firstColumnWidth;

			var outerRect = new Rect(inRect.x, inRect.y, firstColumnWidth, inRect.height);
			var innerRect = new Rect(0f, 0f, firstColumnWidth - 24f, 3300);
			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			currentHelpItem = null;
			var headerColor = Color.yellow;
			var inGame = Current.Game != null && Current.ProgramState == ProgramState.Playing;

			var list = new Listing_Standard();
			list.Begin(innerRect);

			{
				// About
				var intro = "Zombieland_Settings".SafeTranslate();
				var textHeight = Text.CalcHeight(intro, list.ColumnWidth - 3f - inset) + 2 * 3f;
				Widgets.Label(list.GetRect(textHeight).Rounded(), intro);
				list.Gap(10f);

				// When?
				if (Section<SpawnWhenType>(":WhenDoZombiesSpawn"))
				{
					list.Dialog_Enum("WhenDoZombiesSpawn", ref settings.spawnWhenType);
					list.Gap(26f);
				}

				// How?
				if (Section<SpawnHowType>(":HowDoZombiesSpawn", ":SmartWandering"))
				{
					list.Dialog_Enum("HowDoZombiesSpawn", ref settings.spawnHowType);
					list.Gap(4);
					ChooseWanderingStyle(list, settings);
					list.Gap(30f);
				}

				// Attack?
				if (Section<AttackMode>(":WhatDoZombiesAttack", ":EnemiesAttackZombies", ":AnimalsAttackZombies"))
				{
					list.Dialog_Enum("WhatDoZombiesAttack", ref settings.attackMode);
					list.Dialog_Checkbox("EnemiesAttackZombies", ref settings.enemiesAttackZombies);
					list.Dialog_Checkbox("AnimalsAttackZombies", ref settings.animalsAttackZombies);
					list.Gap(30f);
				}

				// Smash?
				if (Section<SmashMode>(":WhatDoZombiesSmash", ":SmashOnlyWhenAgitated"))
				{
					list.Dialog_Enum("WhatDoZombiesSmash", ref settings.smashMode);
					if (settings.smashMode != SmashMode.Nothing)
					{
						list.Dialog_Checkbox("SmashOnlyWhenAgitated", ref settings.smashOnlyWhenAgitated);
					}
					list.Gap(30f);
				}

				// Senses
				if (Section<ZombieInstinct>(":ZombieInstinctTitle", ":RagingZombies", ":RageLevel"))
				{
					list.Dialog_Enum("ZombieInstinctTitle", ref settings.zombieInstinct);
					list.Dialog_Checkbox("RagingZombies", ref settings.ragingZombies);
					var rageLevelNames = new string[] { "RageLevelVeryLow", "RageLevelLow", "RageLevelNormal", "RageLevelHigh", "RageLevelVeryHigh" };
					list.Gap(8f);
					if (settings.ragingZombies)
						list.Dialog_IntSlider("RageLevel", level => rageLevelNames[level - 1].Translate(), ref settings.zombieRageLevel, 1, 5);
					list.Gap(22f);
				}

				// Health
				if (Section<string>(":ZombieHealthTitle", ":DoubleTapRequired", ":ZombiesDieVeryEasily"))
				{
					list.Dialog_Label("ZombieHealthTitle", headerColor);
					list.Dialog_Checkbox("DoubleTapRequired", ref settings.doubleTapRequired);
					list.Dialog_Checkbox("ZombiesDieVeryEasily", ref settings.zombiesDieVeryEasily);
					list.Gap(30f);
				}

				// Eating
				if (Section<string>(":ZombieEatingTitle", ":ZombiesEatDowned", ":ZombiesEatCorpses"))
				{
					list.Dialog_Label("ZombieEatingTitle", headerColor);
					list.Dialog_Checkbox("ZombiesEatDowned", ref settings.zombiesEatDowned);
					list.Dialog_Checkbox("ZombiesEatCorpses", ref settings.zombiesEatCorpses);
					list.Gap(30f);
				}

				// Types
				if (Section<string>(":SpecialZombiesTitle", ":SuicideBomberChance", ":ToxicSplasherChance", ":TankyOperatorChance", ":MinerChance", ":ElectrifierChance", ":AlbinoChance", ":DarkSlimerChance", ":HealerChance", ":NormalZombieChance"))
				{
					list.Dialog_Label("SpecialZombiesTitle", headerColor);
					list.Gap(8f);
					var allMax = Mathf.Max(0.04f, settings.suicideBomberChance, settings.toxicSplasherChance, settings.tankyOperatorChance, settings.minerChance, settings.electrifierChance, settings.albinoChance, settings.darkSlimerChance, settings.healerChance);
					var max = Mathf.Min(1f, 2f * allMax);
					list.Dialog_FloatSlider("SuicideBomberChance", _ => "{0:0.00%}", false, ref settings.suicideBomberChance, 0f, Mathf.Min(max, 1f - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("ToxicSplasherChance", _ => "{0:0.00%}", false, ref settings.toxicSplasherChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("TankyOperatorChance", _ => "{0:0.00%}", false, ref settings.tankyOperatorChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("MinerChance", _ => "{0:0.00%}", false, ref settings.minerChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("ElectrifierChance", _ => "{0:0.00%}", false, ref settings.electrifierChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("AlbinoChance", _ => "{0:0.00%}", false, ref settings.albinoChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.darkSlimerChance - settings.healerChance));
					list.Dialog_FloatSlider("DarkSlimerChance", _ => "{0:0.00%}", false, ref settings.darkSlimerChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.healerChance));
					list.Dialog_FloatSlider("HealerChance", _ => "{0:0.00%}", false, ref settings.healerChance, 0f, Mathf.Min(max, 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance));
					var normalChance = 1f - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance - settings.minerChance - settings.electrifierChance - settings.albinoChance - settings.darkSlimerChance - settings.healerChance;
					list.Gap(-6f);
					list.Dialog_Text(GameFont.Tiny, "NormalZombieChance", string.Format("{0:0.00%}", normalChance));
					list.Gap(30f);
				}

				// Days
				if (Section<string>(":NewGameTitle", ":DaysBeforeZombiesCome"))
				{
					list.Dialog_Label("NewGameTitle", headerColor);
					list.Dialog_Integer("DaysBeforeZombiesCome", null, 0, 100, ref settings.daysBeforeZombiesCome);
					list.Gap(34f);
				}

				// Total
				if (Section<string>(":ZombiesOnTheMap", ":MaximumNumberOfZombies", ":ColonyMultiplier"))
				{
					list.Dialog_Label("ZombiesOnTheMap", headerColor);
					list.Dialog_Integer("MaximumNumberOfZombies", "Zombies", 0, 5000, ref settings.maximumNumberOfZombies);
					list.Gap(8f);
					list.Dialog_FloatSlider("ColonyMultiplier", _ => "{0:0.0}x", false, ref settings.colonyMultiplier, 1f, 10f);
					list.Gap(28f);
				}

				if (Section<string>(":DynamicThreatLevelTitle", ":UseDynamicThreatLevel", ":DynamicThreatSmoothness", ":DynamicThreatStretch", ":ZombiesDieOnZeroThreat"))
				{
					list.Dialog_Label("DynamicThreatLevelTitle", headerColor);
					list.Gap(8f);
					list.Dialog_Checkbox("UseDynamicThreatLevel", ref settings.useDynamicThreatLevel);
					if (settings.useDynamicThreatLevel)
					{
						list.Gap(8f);
						list.Dialog_FloatSlider("DynamicThreatSmoothness", _ => "{0:0%}", false, ref settings.dynamicThreatSmoothness, 1f, 5f, f => (f - 1f) / 4f);
						list.Gap(-4f);
						list.Dialog_FloatSlider("DynamicThreatStretch", _ => "{0:0%}", false, ref settings.dynamicThreatStretch, 10f, 30f, f => (f - 10f) / 20f);
						list.Gap(-6f);
						list.Dialog_Checkbox("ZombiesDieOnZeroThreat", ref settings.zombiesDieOnZeroThreat);
					}
					list.Gap(28f);
				}

				// Events
				if (Section<string>(":ZombieEventTitle", ":ZombiesPerColonistInEvent", ":ExtraDaysBetweenEvents", ":InfectedRaidsChance"))
				{
					list.Dialog_Label("ZombieEventTitle", headerColor);
					list.Dialog_Integer("ZombiesPerColonistInEvent", null, 0, 200, ref settings.baseNumberOfZombiesinEvent);
					list.Dialog_Integer("ExtraDaysBetweenEvents", null, 0, 10000, ref settings.extraDaysBetweenEvents);
					list.Gap(12f);
					list.Dialog_FloatSlider("InfectedRaidsChance", f => f == 0 ? "Off".TranslateSimple() : "{0:0.0%}", true, ref settings.infectedRaidsChance, 0f, 1f);
					list.Gap(28f);
				}

				// Speed
				if (Section<string>(":ZombieSpeedTitle", ":MoveSpeedIdle", ":MoveSpeedTracking"))
				{
					list.Dialog_Label("ZombieSpeedTitle", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("MoveSpeedIdle", _ => "{0:0.00}x", false, ref settings.moveSpeedIdle, 0.01f, 2f);
					list.Dialog_FloatSlider("MoveSpeedTracking", _ => "{0:0.00}x", false, ref settings.moveSpeedTracking, 0.05f, 3f);
					list.Gap(24f);
				}

				// Damage
				if (Section<string>(":ZombieDamageTitle", ":ZombieDamageFactor", ":SafeMeleeLimit", ":ZombiesCauseManhunting"))
				{
					list.Dialog_Label("ZombieDamageTitle", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ZombieDamageFactor", _ => "{0:0.0}x", false, ref settings.damageFactor, 0.1f, 4f);
					list.Dialog_IntSlider("SafeMeleeLimit", n => n == 0 ? "Off".TranslateSimple() : n.ToString(), ref settings.safeMeleeLimit, 0, 4);
					if (settings.safeMeleeLimit > 0)
					{
						list.Gap(-2f);
						ExplainSafeMelee(list, settings.safeMeleeLimit);
						list.Gap(12f);
					}
					list.Gap(6f);
					list.Dialog_Checkbox("ZombiesCauseManhunting", ref settings.zombiesCauseManhuntingResponse);
					list.Gap(36f);
				}

				// Tweaks
				if (Section<string>(":ZombieGameTweaks", ":ReduceTurretConsumption"))
				{
					list.Dialog_Label("ZombieGameTweaks", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ReduceTurretConsumption", _ => "{0:0%}", false, ref settings.reducedTurretConsumption, 0f, 1f);
					list.Gap(28f);
				}

				// Infections
				if (Section<string>(":ZombieInfection", ":ZombieBiteInfectionChance", ":ZombieBiteInfectionUnknown", ":ZombieBiteInfectionTreatable", ":ZombieBiteInfectionTreatable", ":ZombieBiteInfectionPersists", ":AnyTreatmentStopsInfection", ":HoursAfterDeathToBecomeZombie", ":DeadBecomesZombieMessage"))
				{
					list.Dialog_Label("ZombieInfection", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ZombieBiteInfectionChance", _ => "{0:0%}", false, ref settings.zombieBiteInfectionChance, 0f, 1f);
					list.Dialog_TimeSlider("ZombieBiteInfectionUnknown", ref settings.hoursInfectionIsUnknown, 0, 48);
					list.Dialog_TimeSlider("ZombieBiteInfectionTreatable", ref settings.hoursInfectionIsTreatable, 0, 6 * 24);
					list.Dialog_TimeSlider("ZombieBiteInfectionPersists", ref settings.hoursInfectionPersists, 0, 30 * 24, null, true);
					list.Gap(-4f);
					list.Dialog_Checkbox("AnyTreatmentStopsInfection", ref settings.anyTreatmentStopsInfection);
					list.Gap(22f);
					static string hoursTranslator(int n) => n == -1 ? "Off".Translate() : (n == 0 ? "Immediately".Translate() : null);
					list.Dialog_TimeSlider("HoursAfterDeathToBecomeZombie", ref settings.hoursAfterDeathToBecomeZombie, -1, 6 * 24, hoursTranslator, false);
					if (settings.hoursAfterDeathToBecomeZombie > -1)
					{
						list.Gap(-4f);
						list.Dialog_Checkbox("DeadBecomesZombieMessage", ref settings.deadBecomesZombieMessage);
					}
					list.Gap(30f);
				}

				// Zombie loot
				if (Section<string>(":ZombieHarvestingTitle", ":CorpsesExtractAmount", ":LootExtractAmount", ":CorpsesDaysToDessicated"))
				{
					list.Dialog_Label("ZombieHarvestingTitle", headerColor);
					list.Gap(8f);
					var f1 = Mathf.Round(settings.corpsesExtractAmount * 100f) / 100f;
					list.Dialog_FloatSlider("CorpsesExtractAmount", f => ExtractAmount(f), false, ref f1, 0, 4);
					settings.corpsesExtractAmount = Mathf.Round(f1 * 100f) / 100f;
					var f2 = Mathf.Round(settings.lootExtractAmount * 100f) / 100f;
					list.Dialog_FloatSlider("LootExtractAmount", f => ExtractAmount(f), false, ref f2, 0, 4);
					settings.lootExtractAmount = Mathf.Round(f2 * 100f) / 100f;
					list.Dialog_TimeSlider("CorpsesDaysToDessicated", ref settings.corpsesHoursToDessicated, 1, 120);
					ChooseExtractArea(list, settings);
					list.Gap(28f);
				}

				// Miscellaneous
				if (Section<string>(":ZombieMiscTitle", ":UseCustomTextures", ":ReplaceTwinkie", ":PlayCreepyAmbientSound", ":BetterZombieAvoidance", ":ZombiesDropBlood", ":ZombiesBurnLonger", ":ShowHealthBar"))
				{
					list.Dialog_Label("ZombieMiscTitle", headerColor);
					list.Dialog_Checkbox("UseCustomTextures", ref settings.useCustomTextures);
					list.Dialog_Checkbox("ReplaceTwinkie", ref settings.replaceTwinkie);
					list.Dialog_Checkbox("PlayCreepyAmbientSound", ref settings.playCreepyAmbientSound);
					list.Dialog_Checkbox("BetterZombieAvoidance", ref settings.betterZombieAvoidance);
					list.Dialog_Checkbox("ZombiesDropBlood", ref settings.zombiesDropBlood);
					list.Dialog_Checkbox("ZombiesBurnLonger", ref settings.zombiesBurnLonger);
					list.Dialog_Checkbox("ShowHealthBar", ref settings.showHealthBar);
					list.Gap(30f);
				}

				// Actions
				if (Section<string>(":ZombieActionsTitle", ":ZombieSettingsReset", ":UninstallZombieland"))
				{
					list.Dialog_Label("ZombieActionsTitle", headerColor);
					list.Gap(8f);
					list.Dialog_Button("ZombieSettingsReset", "ResetButton", false, settings.Reset);
					if (inGame) list.Dialog_Button("UninstallZombieland", "UninstallButton", true, Dialog_SaveThenUninstall.Run);
				}
			}

			list.End();
			Widgets.EndScrollView();

			var boxHeight = 136f;
			var clipboardActionsRect = new Rect(inRect.x + firstColumnWidth + Listing.ColumnSpacing, inRect.y + inRect.height - boxHeight, inRect.width - firstColumnWidth - Listing.ColumnSpacing, boxHeight);

			list = new Listing_Standard();
			list.Begin(clipboardActionsRect);

			if (inGame == false) list.Gap(46f);

			list.Dialog_Label("ClipboardActionTitle", headerColor);
			list.Gap(8f);
			list.Dialog_Button("CopySettings", "CopyButton", false, settings.ToClipboard);
			if (inGame) list.Dialog_Button("PasteSettings", "PasteButton", true, settings.FromClipboard);

			list.End();

			if (currentHelpItem != null)
			{
				outerRect.x += firstColumnWidth + Listing.ColumnSpacing;
				outerRect.width = secondColumnWidth;

				list = new Listing_Standard();
				list.Begin(outerRect);

				var title = currentHelpItem.SafeTranslate().Replace(": {0}", "");
				list.Dialog_Label(title, Color.white, false);

				list.Gap(8f);

				var text = (currentHelpItem + "_Help").SafeTranslate();
				var anchor = Text.Anchor;
				Text.Anchor = TextAnchor.MiddleLeft;
				var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - inset) + 2 * 3f;
				var rect = list.GetRect(textHeight).Rounded();
				GUI.color = Color.white;
				Widgets.Label(rect, text);
				Text.Anchor = anchor;

				list.End();
			}

			GUI.EndGroup();
			var searchRect = inRect;
			searchRect.yMin = searchRect.yMax + 20 + 20;
			searchRect.height = 35;
			searchRect.xMin += 20;
			searchRect.width = 350;
			searchWidget.OnGUI(searchRect, () => scrollPosition = Vector2.zero);
			if (focusOnSearch)
			{
				focusOnSearch = false;
				GUI.FocusControl(searchWidget.controlName);
			}
			GUI.BeginGroup(Rect.zero);
		}
	}
}
