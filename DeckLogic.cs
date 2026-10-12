using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;

namespace KCMDeckBuilder
{
    // Deck building rules shared by the native screen and the classic panel: filters, search, sorting, random decks
    internal static class DeckLogic
    {
        internal static readonly string[] BaseFilters = { "ALL", "PELTS", "BOSS", "OTHER", "BLOOD", "BONES", "FREE", "RARE" };
        internal static readonly string[] Sorts = { "DEFAULT", "COST", "ATTACK", "HEALTH", "NAME" };

        // The selectable cards in display order (pelts, blood, bones, free, modded, boss/other)
        internal static List<CardInfo> BuildPool(HashSet<string> starterNames) =>
            Plugin.CardPool()
                .OrderBy(c => Plugin.IsPelt(c) ? 0 : Plugin.IsModded(c) ? 4 : Plugin.IsBossCard(c) || Plugin.IsOtherCard(c, starterNames) ? 5
                    : c.BloodCost > 0 ? 1 : c.BonesCost > 0 ? 2 : 3)
                .ThenBy(c => c.BloodCost + c.BonesCost).ThenBy(Plugin.DisplayName).ToList();

        // Filter tabs, with MODDED only when card packs are installed
        internal static string[] Filters(List<CardInfo> pool) =>
            pool.Exists(Plugin.IsModded) ? BaseFilters.Concat(new[] { "MODDED" }).ToArray() : BaseFilters;

        internal static bool Matches(CardInfo c, string filter, string search, HashSet<string> starterNames, Dictionary<CardInfo, string> searchCache)
        {
            search = search?.Trim() ?? "";
            if (search.Length > 0 && SearchText(c, searchCache).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            switch (filter)
            {
                case "PELTS": return Plugin.IsPelt(c);
                case "BOSS": return Plugin.IsBossCard(c);
                case "OTHER": return Plugin.IsOtherCard(c, starterNames) && !Plugin.IsModded(c);
                case "BLOOD": return c.BloodCost > 0;
                case "BONES": return c.BonesCost > 0;
                case "FREE": return c.BloodCost == 0 && c.BonesCost == 0;
                case "RARE": return Plugin.IsRare(c);
                case "MODDED": return Plugin.IsModded(c);
                default: return true;
            }
        }

        // What the search box looks at: ID, name and sigil names
        private static string SearchText(CardInfo c, Dictionary<CardInfo, string> cache)
        {
            if (!cache.TryGetValue(c, out var s))
            {
                s = c.name + "\n" + Plugin.DisplayName(c);
                try { foreach (var a in c.Abilities.Distinct()) s += "\n" + AbilityName(a); } catch { }
                cache[c] = s;
            }
            return s;
        }

        internal static IEnumerable<CardInfo> Sort(IEnumerable<CardInfo> cards, string sort)
        {
            switch (sort)
            {
                case "COST": return cards.OrderBy(c => c.BloodCost).ThenBy(c => c.BonesCost);
                case "ATTACK": return cards.OrderByDescending(c => c.Attack).ThenByDescending(c => c.Health);
                case "HEALTH": return cards.OrderByDescending(c => c.Health).ThenByDescending(c => c.Attack);
                case "NAME": return cards.OrderBy(Plugin.DisplayName);
                default: return cards;
            }
        }

        // 3 random regular cards (at least one costs exactly 1 blood) + 2 Rabbit Pelts; null if there aren't enough cards
        internal static List<CardInfo> RandomDeck(List<CardInfo> pool, Random rng)
        {
            var regular = pool.Where(Plugin.IsRegularChoice).ToList();
            var oneBlood = regular.Where(c => c.BloodCost == 1).ToList();
            if (oneBlood.Count == 0 || regular.Select(c => c.name).Distinct().Count() < 3) return null;
            var picks = new List<CardInfo> { oneBlood[rng.Next(oneBlood.Count)] };
            while (picks.Count < 3)
            {
                var c = regular[rng.Next(regular.Count)];
                if (!picks.Exists(p => p.name == c.name)) picks.Add(c);
            }
            picks = picks.OrderBy(_ => rng.Next()).ToList();
            var hare = pool.Find(c => c.name == "PeltHare");
            if (hare != null) { picks.Add(hare); picks.Add(hare); }
            return picks;
        }

        // Why the card can't be added, or null if it can
        internal static string CannotAdd(List<CardInfo> deck, CardInfo c)
        {
            if (Plugin.MaxCards.Value > 0 && deck.Count + 1 > Plugin.MaxCards.Value) return $"At most {Plugin.MaxCards.Value} cards.";
            if (Plugin.MaxCopies.Value > 0 && deck.Count(d => d.name == c.name) + 1 > Plugin.MaxCopies.Value) return $"At most {Plugin.MaxCopies.Value} copies of a card.";
            return null;
        }

        internal static List<CardInfo> Resolve(IEnumerable<string> names, List<CardInfo> pool) =>
            names.Select(n => pool.Find(c => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase))).ToList();

        internal static string Cost(CardInfo c)
        {
            if (c.BloodCost > 0) return $"{c.BloodCost} blood";
            if (c.BonesCost > 0) return $"{c.BonesCost} bones";
            return "free";
        }

        internal static string AbilityName(Ability a)
        {
            try { return AbilitiesUtil.GetInfo(a)?.rulebookName ?? a.ToString(); }
            catch { return a.ToString(); }
        }

        // Name, cost and stats, then one line per sigil with its rulebook text
        internal static string Describe(CardInfo c)
        {
            string name = Plugin.DisplayName(c);
            string s;
            try { s = $"{name.ToUpperInvariant()} - {Cost(c)}, {c.Attack}/{c.Health}"; }
            catch { s = name.ToUpperInvariant(); }
            try
            {
                foreach (var a in c.Abilities)
                {
                    string d;
                    try { d = AbilitiesUtil.GetInfo(a)?.LocalizedRulebookDescription ?? ""; } catch { d = ""; }
                    s += $"\n{AbilityName(a).ToUpperInvariant()}: {d.Replace("[creature]", name)}";
                }
            }
            catch (Exception e) { CardFace.Report(c, "sigils", e); }
            return s;
        }

        internal static void Totals(List<CardInfo> deck, out int blood, out int bones, out int atk, out int hp)
        {
            blood = bones = atk = hp = 0;
            foreach (var c in deck) { blood += c.BloodCost; bones += c.BonesCost; atk += c.Attack; hp += c.Health; }
        }

        // Saves the deck to the config and shows it on the deck screen
        internal static void SaveDeck(List<CardInfo> deck)
        {
            Plugin.Deck.Value = string.Join(", ", deck.Select(c => c.name).ToArray());
            Plugin.Instance.Config.Save();
            Plugin.UpdateDeckInfo(deck);
        }

        // Same as clicking one of the game's deck icons: select the deck and go to the challenge screen
        internal static void StartWithDeck(List<CardInfo> deck)
        {
            SaveDeck(deck);
            AscensionSaveData.Data.currentStarterDeck = Plugin.CustomDeckId;
            Plugin.Log.LogInfo($"Custom deck selected: {Plugin.Deck.Value}");
            AscensionMenuScreens.Instance.SwitchToScreen(AscensionMenuScreens.Screen.SelectChallenges);
        }
    }
}
