using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DiskCardGame;

namespace KCMDeckBuilder
{
    // Files next to the config: the named saved decks and the list of valid card IDs
    internal static class DeckStorage
    {
        private static string cardListPath, savedDecksPath;
        private static string lastCardList;

        internal static void Init(string configDir)
        {
            cardListPath = Path.Combine(configDir, "KCMDeckBuilder_cards.txt");
            savedDecksPath = Path.Combine(configDir, "KCMDeckBuilder_decks.txt");
        }

        // Saved decks, one per line: "Name|Card, Card, ..."
        internal static List<KeyValuePair<string, string>> LoadSavedDecks()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                if (!File.Exists(savedDecksPath)) return list;
                foreach (var line in File.ReadAllLines(savedDecksPath, Encoding.UTF8))
                {
                    int bar = line.IndexOf('|');
                    if (line.StartsWith("#") || bar <= 0) continue;
                    list.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim(), line.Substring(bar + 1).Trim()));
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Could not read saved decks: " + e.Message); }
            return list;
        }

        internal static void WriteSavedDecks(List<KeyValuePair<string, string>> decks)
        {
            try
            {
                var lines = new List<string> { "# KCM Deck Builder - saved decks (Name|Card, Card, ...)" };
                lines.AddRange(decks.Select(d => d.Key + "|" + d.Value));
                File.WriteAllLines(savedDecksPath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception e) { Plugin.Log.LogError("Could not write saved decks: " + e.Message); }
        }

        // Writes the list of valid card IDs for editing the config by hand; skipped if nothing changed
        internal static void WriteCardList(List<CardInfo> pool, Dictionary<string, string> packNames)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# KCM Deck Builder - selectable cards (ID | name | blood/bones | sigils | mod)");
                sb.AppendLine($"# Max cards: {LimitText(Plugin.MaxCards.Value)}, max copies per card: {LimitText(Plugin.MaxCopies.Value)}");
                foreach (var c in pool.OrderBy(c => c.name))
                {
                    try
                    {
                        string sigils = string.Join(", ", c.Abilities.Select(a => a.ToString()).ToArray());
                        string rare = CardRules.IsRare(c) ? " [RARE]" : "";
                        string mod = packNames.TryGetValue(c.name, out var p) ? " | " + p : "";
                        sb.AppendLine($"{c.name} | {CardRules.DisplayName(c)}{rare} | {c.BloodCost}blood/{c.BonesCost}bones | {sigils}{mod}");
                    }
                    catch { sb.AppendLine(c.name); }
                }
                string list = sb.ToString();
                if (list == lastCardList && File.Exists(cardListPath)) return;
                File.WriteAllText(cardListPath, list, Encoding.UTF8);
                lastCardList = list;
                Plugin.Log.LogDebug($"Card list written in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
            }
            catch (Exception e) { Plugin.Log.LogError("Could not write card list: " + e); }
        }

        private static string LimitText(int n) => n > 0 ? n.ToString() : "no limit";
    }
}
