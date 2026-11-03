// ---------------------------------------------------------------
//  Vorlader - sammelt die Bundles und haelt sie fest.
//
//  Die Schluesselliste ist bewusst dieselbe, die das Spiel
//  spaeter selbst anfordert (siehe Plugin.cs). Wir bauen nichts
//  nach - wir sind nur frueher dran.
// ---------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using UnityEngine;

namespace VersteckVorladen
{
	internal static class Vorlader
	{
		// Solange diese Marken leben, bleiben die Bundles geladen.
		private static readonly List<DependencyGraph<IEasyBundle>.Token> Marken =
			new List<DependencyGraph<IEasyBundle>.Token>();

		private static Coroutine _lauf;

		internal static void Starten(TarkovApplication anwendung)
		{
			if (Plugin.Instanz == null || anwendung == null)
			{
				return;
			}

			// Ein vorheriger Vorlauf haette sonst zwei Saetze Marken
			// gleichzeitig offen - der alte Satz ist ohnehin veraltet.
			Freigeben("neuer Vorlauf");

			Plugin.Log.LogInfo("Versteck vorladen: Auswertung erkannt, starte in "
				+ Mathf.Max(0f, Plugin.Verzoegerung.Value).ToString("0.0") + " s.");

			_lauf = Plugin.Instanz.StartCoroutine(Ablauf(anwendung));
		}

		internal static void Freigeben(string grund)
		{
			if (_lauf != null && Plugin.Instanz != null)
			{
				Plugin.Instanz.StopCoroutine(_lauf);
			}
			_lauf = null;

			if (Marken.Count == 0)
			{
				return;
			}

			int anzahl = Marken.Count;
			for (int i = 0; i < Marken.Count; i++)
			{
				try
				{
					if (Marken[i] != null)
					{
						Marken[i].Release();
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("Versteck vorladen: Marke nicht freigegeben - " + ex.Message);
				}
			}
			Marken.Clear();

			if (Plugin.Protokoll.Value)
			{
				Plugin.Log.LogInfo("Versteck vorladen: " + anzahl + " Bundles freigegeben (" + grund + ").");
			}
		}

		private static IEnumerator Ablauf(TarkovApplication anwendung)
		{
			// Dem Spiel erst seinen Auftakt lassen: Raidkarte abraeumen,
			// Speicher bereinigen, Profil vom Server holen.
			float warten = Mathf.Max(0f, Plugin.Verzoegerung.Value);
			if (warten > 0f)
			{
				yield return new WaitForSecondsRealtime(warten);
			}

			List<string> schluessel = null;
			try
			{
				schluessel = Sammeln(anwendung);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Versteck vorladen: Sammeln - " + ex);
			}

			if (schluessel == null || schluessel.Count == 0 || !Singleton<IEasyAssets>.Instantiated)
			{
				Plugin.Log.LogWarning("Versteck vorladen: nichts zu laden ("
					+ ((schluessel == null) ? "Sammeln fehlgeschlagen" : (schluessel.Count + " Schluessel"))
					+ ", Bundle-Verwaltung "
					+ (Singleton<IEasyAssets>.Instantiated ? "da" : "fehlt") + ").");
				_lauf = null;
				yield break;
			}

			IEasyAssets anlage = Singleton<IEasyAssets>.Instance;

			// Entscheidende Zahl: Wie viel davon liegt ohnehin schon im
			// Speicher? Ist fast alles schon geladen, kann Vorladen
			// nichts mehr einsparen - dann steckt die Wartezeit
			// woanders.
			int schonDa = 0;
			for (int p = 0; p < schluessel.Count; p++)
			{
				try
				{
					if (anlage.IsAssetLoaded(schluessel[p]))
					{
						schonDa++;
					}
				}
				catch
				{
					// Unbekannter Pfad - zaehlt als nicht geladen.
				}
			}
			Plugin.Log.LogInfo("Versteck vorladen: " + schluessel.Count
				+ " Bundles noetig, davon " + schonDa + " schon im Speicher.");

			float start = Time.realtimeSinceStartup;
			int stapel = Mathf.Max(1, Plugin.Stapelgroesse.Value);
			int geladen = 0;
			int uebersprungen = 0;

			for (int i = 0; i < schluessel.Count; i += stapel)
			{
				var dieserStapel = new List<DependencyGraph<IEasyBundle>.Token>();
				int ende = Mathf.Min(i + stapel, schluessel.Count);

				for (int j = i; j < ende; j++)
				{
					try
					{
						// Retain startet den Ladevorgang von selbst; LoadingJob
						// ist der dazugehoerige Auftrag.
						DependencyGraph<IEasyBundle>.Token marke =
							anlage.Retain(new string[1] { schluessel[j] });
						if (marke != null)
						{
							dieserStapel.Add(marke);
							Marken.Add(marke);
						}
					}
					catch (Exception ex)
					{
						// Pfad, den das Spiel gar nicht kennt (kommt bei
						// Gegenstaenden aus Server-Mods vor) - einfach weiter.
						uebersprungen++;
						if (Plugin.Protokoll.Value)
						{
							Plugin.Log.LogWarning("Versteck vorladen: uebersprungen "
								+ schluessel[j] + " - " + ex.Message);
						}
					}
				}

				geladen += dieserStapel.Count;

				// Auf diesen Stapel warten, bevor der naechste startet. Das
				// ist zugleich die Bremse: nie mehr als "Stapelgroesse"
				// Ladevorgaenge gleichzeitig.
				while (!StapelFertig(dieserStapel))
				{
					yield return null;
				}
			}

			_lauf = null;

			Plugin.Log.LogInfo("Versteck vorladen: " + geladen + " Bundles in "
				+ (Time.realtimeSinceStartup - start).ToString("0.0") + " s vorgeladen"
				+ (uebersprungen > 0 ? (", " + uebersprungen + " uebersprungen") : "") + ".");
		}

		private static bool StapelFertig(List<DependencyGraph<IEasyBundle>.Token> stapel)
		{
			for (int i = 0; i < stapel.Count; i++)
			{
				try
				{
					System.Threading.Tasks.Task auftrag = stapel[i].LoadingJob;
					if (auftrag != null && !auftrag.IsCompleted)
					{
						return false;
					}
				}
				catch
				{
					// Kaputte Marke blockiert den Rest nicht.
				}
			}
			return true;
		}

		// Dieselben drei Quellen, aus denen sich das Spiel spaeter
		// bedient - siehe Kommentar in Plugin.cs.
		private static List<string> Sammeln(TarkovApplication anwendung)
		{
			var menge = new HashSet<string>(StringComparer.Ordinal);

			// 1. Aussehen, Uhr, Ausruestung, Stimme  (wie method_5)
			Profile profil = (anwendung.Session != null) ? anwendung.Session.Profile : null;
			if (profil != null)
			{
				foreach (ResourceKey eintrag in profil.GetAllPrefabPaths())
				{
					if (eintrag != null && !string.IsNullOrEmpty(eintrag.path))
					{
						menge.Add(eintrag.path);
					}
				}
			}

			// 2. Die feste Vorladeliste des Spiels  (wie method_5)
			string[] feste = InGameBundles.BUNDLES_TO_PRELOAD;
			for (int i = 0; i < feste.Length; i++)
			{
				if (!string.IsNullOrEmpty(feste[i]))
				{
					menge.Add(feste[i]);
				}
			}

			// 3. Prefabs aller herstellbaren Gegenstaende  (wie method_4)
			if (Plugin.Rezepte.Value
				&& Singleton<HideoutRepresentation>.Instantiated
				&& Singleton<ItemFactory>.Instantiated)
			{
				ItemFactory fabrik = Singleton<ItemFactory>.Instance;
				ProductionSchemesCollection sammlung =
					Singleton<HideoutRepresentation>.Instance.ProductionSchemesCollection;
				ProductionScheme[] schemata = (sammlung != null) ? sammlung.ProductionSchemes : null;

				if (schemata != null)
				{
					for (int i = 0; i < schemata.Length; i++)
					{
						try
						{
							string kennung = schemata[i].endProduct;
							if (string.IsNullOrEmpty(kennung))
							{
								continue;
							}
							Item vorlage = fabrik.GetPresetItem(kennung);
							if (vorlage != null && vorlage.Prefab != null
								&& !string.IsNullOrEmpty(vorlage.Prefab.path))
							{
								menge.Add(vorlage.Prefab.path);
							}
						}
						catch
						{
							// Rezept ohne brauchbares Endprodukt - ueberspringen.
						}
					}
				}
			}

			return new List<string>(menge);
		}
	}
}
