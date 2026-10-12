using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;

namespace KCMDeckBuilder
{
    // Deck building rules, separate from the drawing code: filters, search, sorting, limits, random decks
    internal static class DeckLogic
    {
        internal static readonly string[] BaseFilters = { "ALL", "PELTS", "BOSS", "OTHER", "BLOOD", "BONES", "FREE", "RARE" };
        internal static readonly string[] Sorts = { "DEFAULT", "COST", "ATTACK", "HEALTH", "NAME" };

        // The selectable cards in display order (pelts, blood, bones, free, modded, boss/other)
        internal static List<CardInfo> BuildPool(HashSet<string> starterNames) =>
            CardRules.CardPool()
                .OrderBy(c => CardRules.IsPelt(c) ? 0 : CardRules.IsModded(c) ? 4 : CardRules.IsBossCard(c) || CardRules.IsOtherCard(c, starterNames) ? 5
                    : c.BloodCost > 0 ? 1 : c.BonesCost > 0 ? 2 : 3)
                .ThenBy(c => c.BloodCost + c.BonesCost).ThenBy(CardRules.DisplayName).ToList();

        // Filter tabs, with MODDED only when card packs are installed
        internal static string[] Filters(List<CardInfo> pool) =>
            pool.Exists(CardRules.IsModded) ? BaseFilters.Concat(new[] { "MODDED" }).ToArray() : BaseFilters;

        internal const string AllPacks = "ALL", BaseGamePack = "INSCRYPTION";

        // The mod each modded card comes from (card ID -> name). Usually that is the plugin that added it; a loader
        // such as JSONLoader adds cards for many packs, so its cards are named by their pack's prefix instead.
        internal static Dictionary<string, string> PackNames(List<CardInfo> pool)
        {
            var cards = pool.Where(CardRules.IsModded)
                .Select(c => new { c.name, tag = CardRules.ModTag(c), prefix = CardRules.ModPrefix(c) })
                .ToList();
            var loaders = new HashSet<string>(cards.GroupBy(c => c.tag)
                .Where(g => g.Key != null && g.Select(c => c.prefix).Distinct().Count() > 1)
                .Select(g => g.Key));
            var names = new Dictionary<string, string>();
            foreach (var c in cards)
            {
                string pack = loaders.Contains(c.tag) || c.tag == null ? c.prefix : CardRules.PluginName(c.tag);
                names[c.name] = (pack ?? c.prefix ?? "OTHER MODS").ToUpperInvariant();
            }
            return names;
        }

        // ALL, the base game, then each mod; just ALL when no card packs are installed
        internal static string[] Packs(Dictionary<string, string> packNames) =>
            packNames.Count == 0 ? new[] { AllPacks }
            : new[] { AllPacks, BaseGamePack }.Concat(packNames.Values.Distinct().OrderBy(p => p)).ToArray();

        internal static bool InPack(CardInfo c, string pack, Dictionary<string, string> packNames)
        {
            if (pack == AllPacks) return true;
            return packNames.TryGetValue(c.name, out var p) ? p == pack : pack == BaseGamePack;
        }

        internal static bool Matches(CardInfo c, string filter, string search, HashSet<string> starterNames, Dictionary<CardInfo, string> searchCache)
        {
            search = search?.Trim() ?? "";
            if (search.Length > 0 && SearchText(c, searchCache).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            switch (filter)
            {
                case "PELTS": return CardRules.IsPelt(c);
                case "BOSS": return CardRules.IsBossCard(c);
                case "OTHER": return CardRules.IsOtherCard(c, starterNames) && !CardRules.IsModded(c);
                case "BLOOD": return c.BloodCost > 0;
                case "BONES": return c.BonesCost > 0;
                case "FREE": return c.BloodCost == 0 && c.BonesCost == 0;
                case "RARE": return CardRules.IsRare(c);
                case "MODDED": return CardRules.IsModded(c);
                default: return true;
            }
        }

        // What the search box looks at: ID, name and sigil names
        private static string SearchText(CardInfo c, Dictionary<CardInfo, string> cache)
        {
            if (!cache.TryGetValue(c, out var s))
            {
                s = c.name + "\n" + CardRules.DisplayName(c);
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
                case "NAME": return cards.OrderBy(CardRules.DisplayName);
                default: return cards;
            }
        }

        // 3 random regular cards (at least one costs exactly 1 blood) + 2 Rabbit Pelts; null if there aren't enough cards
        internal static List<CardInfo> RandomDeck(List<CardInfo> pool, Random rng)
        {
            var regular = pool.Where(CardRules.IsRegularChoice).ToList();
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

        // Why the deck can't be played, or null if it can
        internal static string Validate(List<CardInfo> deck)
        {
            if (deck.Count == 0) return "Deck is empty.";
            if (Plugin.MaxCards.Value > 0 && deck.Count > Plugin.MaxCards.Value) return $"Too many cards: {deck.Count} > {Plugin.MaxCards.Value}";
            var tooMany = deck.GroupBy(c => c.name).FirstOrDefault(g => Plugin.MaxCopies.Value > 0 && g.Count() > Plugin.MaxCopies.Value);
            if (tooMany != null) return $"Too many copies of '{tooMany.Key}'.";
            return null;
        }

        // The pool's card for each ID (case-insensitive), or null where there is none (locked, or from a missing mod)
        internal static List<CardInfo> Resolve(IEnumerable<string> names, List<CardInfo> pool) =>
            names.Select(n => pool.Find(c => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase))).ToList();

        // The deck in the config, checked against the current rules; null (with the reason logged) if it isn't valid
        internal static List<CardInfo> DeckFromConfig()
        {
            var names = DeckCode.ParseList(Plugin.Deck.Value);
            var deck = Resolve(names, CardRules.CardPool());
            int missing = deck.IndexOf(null);
            if (missing >= 0) { Plugin.Log.LogWarning($"Invalid or locked card: '{names[missing]}'"); return null; }

            string err = Validate(deck);
            if (err != null) { Plugin.Log.LogWarning(err); return null; }

            Plugin.Log.LogInfo($"Using custom deck: {DeckCode.JoinList(deck.Select(c => c.name))}");
            return deck;
        }

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

        // Name, cost and stats
        internal static string Header(CardInfo c)
        {
            string name = CardRules.DisplayName(c).ToUpperInvariant();
            try { return $"{name} - {Cost(c)}, {c.Attack}/{c.Health}".ToUpperInvariant(); }
            catch { return name; }
        }

        // Each sigil once, with its rulebook name and text
        internal static List<KeyValuePair<Ability, string>> Sigils(CardInfo c)
        {
            var list = new List<KeyValuePair<Ability, string>>();
            string name = CardRules.DisplayName(c);
            try
            {
                foreach (var a in c.Abilities.Distinct())
                {
                    string d;
                    try { d = AbilitiesUtil.GetInfo(a)?.LocalizedRulebookDescription ?? ""; } catch { d = ""; }
                    list.Add(new KeyValuePair<Ability, string>(a, $"{AbilityName(a).ToUpperInvariant()}: {d.Replace("[creature]", name)}"));
                }
            }
            catch (Exception e) { CardFace.Report(c, "sigils", e); }
            return list;
        }

        internal static string Totals(List<CardInfo> deck)
        {
            int blood = 0, bones = 0, atk = 0, hp = 0;
            foreach (var c in deck) { blood += c.BloodCost; bones += c.BonesCost; atk += c.Attack; hp += c.Health; }
            return $"{blood} BLOOD  {bones} BONES  {atk} ATK  {hp} HP";
        }

        // Saves the deck to the config and shows it on the deck screen
        internal static void SaveDeck(List<CardInfo> deck)
        {
            Plugin.Deck.Value = DeckCode.JoinList(deck.Select(c => c.name));
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
