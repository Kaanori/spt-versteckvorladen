// ---------------------------------------------------------------
//  Versteck vorladen
//
//  Nach dem Raid stehst du erst in der Auswertung (Ergebnis,
//  Erfahrung, Beute). Erst wenn du dich da durchgeklickt hast,
//  faengt das Spiel an, das Versteck zu laden - und du wartest.
//
//  Dieser Mod nutzt die Klickzeit: Sobald die Auswertung steht,
//  laedt er im Hintergrund schon die Daten, die das Versteck
//  danach braucht.
//
//  WAS GENAU VORGELADEN WIRD, UND WARUM NICHT MEHR:
//
//  Das Spiel baut das Versteck in drei Schritten zusammen
//  (TarkovApplication.HideoutController.method_2):
//
//    1. method_1  - die Versteck-Szenen (bunker_2 und Nachbarn)
//    2. method_5  - Bundles fuer Aussehen, Ausruestung und die
//                   feste Liste InGameBundles.BUNDLES_TO_PRELOAD
//    3. method_4  - die Prefabs aller herstellbaren Gegenstaende
//
//  Schritt 1 laesst sich NICHT vorziehen. ComebackToMainMenu
//  laedt die Menueszene mit LoadSceneMode.Single - das raeumt
//  jede andere Szene ab. Alles, was wir vorher an Szenen laden,
//  waere Sekunden spaeter wieder weg.
//
//  Die Schritte 2 und 3 sind dagegen reine Bundle-Ladevorgaenge.
//  Bundles haengen nicht an Szenen, sondern an einem Zaehlwerk
//  (DependencyGraph). Wer ein Bundle "festhaelt" (Retain), haelt
//  es geladen - ueber Szenenwechsel, Resources.UnloadUnusedAssets
//  und die erzwungene Speicherbereinigung hinweg. Genau das macht
//  dieser Mod: festhalten, bis das Versteck wirklich dran ist.
//
//  Wenn das Spiel danach selbst Retain aufruft, findet es die
//  Bundles fertig geladen vor und ist sofort durch.
//
//  FREIGEGEBEN wird beim naechsten Raidstart - dort ruft das
//  Spiel UnloadHideout auf, und ab da soll der Speicher der
//  Karte gehoeren, nicht mehr dem Versteck.
//
//  Einstellbar unter F12 -> Versteck vorladen.
// ---------------------------------------------------------------
using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;

namespace VersteckVorladen
{
	[BepInPlugin("com.kaan.versteckvorladen", "Versteck vorladen", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;

		internal static Plugin Instanz;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<float> Verzoegerung;

		internal static ConfigEntry<int> Stapelgroesse;

		internal static ConfigEntry<bool> Rezepte;

		internal static ConfigEntry<bool> Protokoll;

		internal static ConfigEntry<bool> Messen;

		private void Awake()
		{
			Log = base.Logger;
			Instanz = this;

			An = base.Config.Bind("1. Allgemein", "Eingeschaltet", defaultValue: true,
				"Laedt waehrend der Raid-Auswertung schon die Versteck-Daten.");

			Verzoegerung = base.Config.Bind("1. Allgemein", "Startverzoegerung (Sekunden)", 3f,
				new ConfigDescription(
					"Wartezeit, nachdem die Auswertung aufgegangen ist. Das Spiel\n"
					+ "raeumt in diesen ersten Sekunden noch die Raidkarte ab und\n"
					+ "holt dein Profil vom Server - da soll ihm nichts dazwischen-\n"
					+ "funken. Kleiner heisst frueher fertig, aber ruckeliger.",
					new AcceptableValueRange<float>(0f, 30f)));

			Stapelgroesse = base.Config.Bind("2. Tempo", "Bundles gleichzeitig", 16,
				new ConfigDescription(
					"Wie viele Bundles auf einmal geladen werden. Hoeher ist\n"
					+ "schneller fertig, kann die Auswertung aber stocken lassen.",
					new AcceptableValueRange<int>(1, 128)));

			Rezepte = base.Config.Bind("2. Tempo", "Herstellungs-Rezepte mitladen", defaultValue: true,
				"An: laedt auch die Prefabs aller herstellbaren Gegenstaende vor\n"
				+ "(der dritte Schritt des Spiels). Das ist der groesste Brocken.\n"
				+ "Aus: nur Aussehen und Ausruestung - weniger Speicher, weniger\n"
				+ "Zeitgewinn.");

			Protokoll = base.Config.Bind("3. Diagnose", "Protokoll", defaultValue: false,
				"Schreibt Anzahl und Dauer des Vorladens in die BepInEx-Konsole.");

			Messen = base.Config.Bind("3. Diagnose", "Ladezeiten messen", defaultValue: true,
				"Schreibt nach jedem Betreten des Verstecks, wie lange die\n"
				+ "einzelnen Schritte gedauert haben. Nur damit laesst sich\n"
				+ "sagen, ob das Vorladen ueberhaupt am richtigen Hebel sitzt:\n"
				+ "Sind \"Szenen laden\" die meisten Sekunden, ist mit Vorladen\n"
				+ "nichts mehr zu holen.");

			// Bewusst einzeln statt PatchAll: Die Messung haengt an
			// Methodennamen (method_0, method_1, ...), die BSG in einer
			// neuen Fassung umbenennen kann. Faellt sie aus, soll das
			// Vorladen selbst weiterlaufen.
			Harmony harmonie = new Harmony("com.kaan.versteckvorladen");
			bool kern = Einhaengen(harmonie, typeof(AuswertungPatch), "Auswertung erkennen")
				& Einhaengen(harmonie, typeof(VersteckAbraeumenPatch), "Bundles freigeben");
			Einhaengen(harmonie, typeof(MessPatch), "Ladezeiten messen");

			if (kern)
			{
				Log.LogInfo("Versteck vorladen: bereit.");
			}
			else
			{
				Log.LogError("Versteck vorladen: Kernstellen fehlen - der Mod tut nichts.");
			}
		}

		private static bool Einhaengen(Harmony harmonie, Type klasse, string bezeichnung)
		{
			try
			{
				harmonie.CreateClassProcessor(klasse).Patch();
				return true;
			}
			catch (Exception ex)
			{
				Log.LogError("Versteck vorladen: \"" + bezeichnung + "\" nicht eingehaengt - " + ex.Message);
				return false;
			}
		}
	}

	// Startpunkt: Die Auswertung geht auf.
	//
	// ShowSessionResult ist async. Ein Postfix laeuft deshalb nicht
	// am Ende der Auswertung, sondern sobald die Methode das erste
	// Mal wartet - also praktisch sofort, wenn der Ergebnis-
	// bildschirm gebaut wird. Genau der Zeitpunkt, den wir wollen.
	[HarmonyPatch]
	internal static class AuswertungPatch
	{
		[HarmonyTargetMethod]
		internal static MethodBase Ziel()
		{
			return AccessTools.Method(typeof(TarkovApplication),
				nameof(TarkovApplication.ShowSessionResult));
		}

		[HarmonyPostfix]
		internal static void Nachher(TarkovApplication __instance)
		{
			try
			{
				if (!Plugin.An.Value)
				{
					return;
				}
				Vorlader.Starten(__instance);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Versteck vorladen: Start - " + ex);
			}
		}
	}

	// Endpunkt: Das Spiel raeumt das Versteck weg - beim Raidstart
	// (LoadMapAndData) und beim Abmelden. Ab hier soll der Speicher
	// der Karte gehoeren, also geben wir unsere Bundles frei.
	[HarmonyPatch]
	internal static class VersteckAbraeumenPatch
	{
		[HarmonyTargetMethod]
		internal static MethodBase Ziel()
		{
			return AccessTools.Method(typeof(TarkovApplication.HideoutController),
				nameof(TarkovApplication.HideoutController.UnloadHideout));
		}

		[HarmonyPrefix]
		internal static void Vorher()
		{
			try
			{
				Vorlader.Freigeben("Versteck wird abgeraeumt");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Versteck vorladen: Freigeben - " + ex);
			}
		}
	}
}
