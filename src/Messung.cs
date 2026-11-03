// ---------------------------------------------------------------
//  Messung - wo geht die Zeit wirklich hin?
//
//  Das Spiel misst seine Versteck-Ladeschritte selbst (ueber
//  DebugTimeMeasurer), schreibt das Ergebnis aber in Unitys
//  Player.log - und die wird unter Proton nicht mitgefuehrt. Im
//  BepInEx-Protokoll steht davon nichts.
//
//  Darum messen wir selbst. Die vier Schritte in
//  TarkovApplication.HideoutController:
//
//    method_0  - Versteck betreten, gesamt
//    method_1  - die Szenen laden      (NICHT vorladbar)
//    method_5  - Bundles + Pools       (vorladbar)
//    method_4  - Rezept-Prefabs        (vorladbar)
//
//  Alle vier sind async. Ein Postfix laeuft deshalb nicht am Ende,
//  sondern sobald die Methode das erste Mal wartet - er bekommt
//  aber die Aufgabe (Task) in die Hand. An die haengen wir eine
//  Fortsetzung, und die feuert wirklich am Ende.
//
//  Stoppuhr statt Time.realtimeSinceStartup: die Fortsetzung kann
//  auf einem fremden Faden laufen, und Unity-Aufrufe sind dort
//  nicht erlaubt.
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;

namespace VersteckVorladen
{
	[HarmonyPatch]
	internal static class MessPatch
	{
		[HarmonyTargetMethods]
		internal static IEnumerable<MethodBase> Ziele()
		{
			string[] namen = new string[4] { "method_0", "method_1", "method_5", "method_4" };
			for (int i = 0; i < namen.Length; i++)
			{
				MethodBase ziel = AccessTools.Method(
					typeof(TarkovApplication.HideoutController), namen[i]);
				if (ziel != null)
				{
					yield return ziel;
				}
				else
				{
					Plugin.Log.LogWarning("Versteck vorladen: " + namen[i]
						+ " nicht gefunden - Messung unvollstaendig.");
				}
			}
		}

		[HarmonyPostfix]
		internal static void Danach(MethodBase __originalMethod, Task __result)
		{
			if (Plugin.Messen == null || !Plugin.Messen.Value || __result == null)
			{
				return;
			}

			string was = Benennung(__originalMethod.Name);
			Stopwatch uhr = Stopwatch.StartNew();
			__result.ContinueWith(delegate
			{
				uhr.Stop();
				Plugin.Log.LogInfo("Versteck-Zeit: " + was + " "
					+ (uhr.ElapsedMilliseconds / 1000f).ToString("0.00") + " s");
			});
		}

		private static string Benennung(string name)
		{
			switch (name)
			{
			case "method_0":
				return "GESAMT (Versteck betreten)";
			case "method_1":
				return "Szenen laden (nicht vorladbar)";
			case "method_5":
				return "Bundles + Pools (vorgeladen)";
			case "method_4":
				return "Rezept-Prefabs (vorgeladen)";
			default:
				return name;
			}
		}
	}
}
