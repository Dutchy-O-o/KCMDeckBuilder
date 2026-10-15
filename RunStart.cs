using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using InscryptionAPI.Card;
using InscryptionAPI.Items;
using UnityEngine;

namespace KCMDeckBuilder
{
    // The items and the totem a custom deck run starts with
    internal static class RunStart
    {
        internal const int MaxItems = 3; // the game's slots; the Less Consumables challenge takes some away at run start

        private static readonly Tribe[] BaseTribes = { Tribe.Squirrel, Tribe.Bird, Tribe.Canine, Tribe.Hooved, Tribe.Insect, Tribe.Reptile };

        // The totem heads: the game's six tribes, then tribes added by card mods (the API gives those a totem head)
        internal static List<Tribe> Tribes()
        {
            var list = BaseTribes.ToList();
            try { foreach (var t in TribeManager.NewTribes) if (t != null && !list.Contains(t.tribe)) list.Add(t.tribe); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not list modded tribes: " + e.Message); }
            return list;
        }

        private static TribeManager.TribeInfo ModTribe(Tribe t)
        {
            try { return TribeManager.IsCustomTribe(t) ? TribeManager.GetCustomTribeInfo(t) : null; } catch { return null; }
        }

        // Modded tribes are often named like "DivineTribe"; shown as "Divine"
        internal static string TribeName(Tribe t)
        {
            var info = ModTribe(t);
            if (info == null || string.IsNullOrEmpty(info.name)) return t.ToString();
            string n = info.name;
            if (n.Length > 5 && n.EndsWith("Tribe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
            return System.Text.RegularExpressions.Regex.Replace(n.Replace("_", " "), "(?<=[a-z])(?=[A-Z])", " ").Trim();
        }

        // The mod a tribe comes from, or null for the game's own
        internal static string TribeMod(Tribe t)
        {
            var info = ModTribe(t);
            return info != null ? CardRules.PluginName(info.guid) ?? info.guid : null;
        }

        // How a tribe is written in saves and codes; modded tribes by mod and name, since their numbers can differ
        // between installs
        private static string TribeKey(Tribe t)
        {
            var info = ModTribe(t);
            return info != null ? info.guid + "_" + info.name : t.ToString();
        }

        private static bool FindTribe(string key, out Tribe tribe)
        {
            tribe = Tribes().Find(t => TribeKey(t) == key);
            return tribe != Tribe.None;
        }

        // The tribe's head picture: the card tribe icon, or the one a mod gave its tribe
        internal static Texture TribeArt(Tribe t, out Rect uv, out float aspect)
        {
            uv = new Rect(0, 0, 1, 1);
            aspect = 1f;
            if (ModTribe(t) != null)
            {
                try { return TribeManager.GetTribeIcon(t, false); } catch { return null; }
            }
            var s = TribeIcon(t);
            if (s == null || s.texture == null) return null;
            var r = s.textureRect;
            uv = new Rect(r.x / s.texture.width, r.y / s.texture.height, r.width / s.texture.width, r.height / s.texture.height);
            aspect = r.width / r.height;
            return s.texture;
        }

        // Act 1 items; with "only unlocked", the ones the game has unlocked. Read from the game's item folder and the
        // API's list of modded items rather than ItemsUtil.AllConsumables: that loads everything under Data/ (cards,
        // encounters, videos...) the first time and freezes the game for about two seconds. The Special Dagger is left out:
        // it belongs to the story, not to Kaycee's Mod.
        internal static List<ConsumableItemData> Items()
        {
            try
            {
                var all = new List<ConsumableItemData>(Resources.LoadAll<ConsumableItemData>("Data/Consumables"));
                try { all.AddRange(ConsumableItemManager.NewConsumableItemDatas); }
                catch (Exception e) { Plugin.Log.LogWarning("Could not list modded items: " + e.Message); }
                return all
                    .Where(i => i != null && i.name != "SpecialDagger" && i.rulebookCategory == AbilityMetaCategory.Part1Rulebook
                        && (!Plugin.OnlyUnlocked.Value || ConceptProgressionTree.Tree.ConsumableUnlocked(i)))
                    .GroupBy(i => i.name).Select(g => g.First())
                    .OrderBy(i => i.powerLevel).ThenBy(ItemName).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not list items: " + e.Message);
                return new List<ConsumableItemData>();
            }
        }

        internal static string ItemName(ConsumableItemData i)
        {
            string n = null;
            try { n = Localization.Translate(i.rulebookName); } catch { }
            return (string.IsNullOrEmpty(n) ? i.name : n).ToUpperInvariant();
        }

        internal static string ItemText(ConsumableItemData i)
        {
            string d = null;
            try { d = Localization.Translate(i.rulebookDescription); } catch { }
            return d ?? "";
        }

        // Sigils the game allows on a totem; with "only unlocked", the ones learned or on a card you can pick
        internal static List<AbilityInfo> TotemSigils(List<CardInfo> pool)
        {
            try
            {
                var onCards = new HashSet<Ability>();
                foreach (var c in pool) try { onCards.UnionWith(c.Abilities); } catch { }
                return ScriptableObjectLoader<AbilityInfo>.AllData
                    .Where(a => a != null && a.metaCategories != null && a.metaCategories.Contains(AbilityMetaCategory.Part1Modular)
                        && (!Plugin.OnlyUnlocked.Value || onCards.Contains(a.ability) || Learned(a.ability)))
                    .GroupBy(a => a.ability).Select(g => g.First())
                    .OrderBy(a => DeckLogic.AbilityName(a.ability)).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not list totem sigils: " + e.Message);
                return new List<AbilityInfo>();
            }
        }

        private static bool Learned(Ability a)
        {
            try { return ProgressionData.LearnedAbility(a); } catch { return false; }
        }

        // How a sigil is written in saves and codes: the game's name for its own sigils, the asset name for modded ones
        // (their numbers can differ between installs)
        internal static string SigilKey(AbilityInfo a) => Enum.IsDefined(typeof(Ability), a.ability) ? a.ability.ToString() : a.name;

        internal static string TotemText(Tribe tribe, AbilityInfo sigil) => TribeKey(tribe) + ":" + SigilKey(sigil);

        // "Bird:Flying" -> tribe and sigil; false if either is unknown here (e.g. from a mod that isn't installed)
        internal static bool ParseTotem(string text, out Tribe tribe, out AbilityInfo sigil)
        {
            tribe = Tribe.None;
            sigil = null;
            if (string.IsNullOrEmpty(text)) return false;
            int colon = text.IndexOf(':');
            if (colon <= 0) return false;
            string t = text.Substring(0, colon), s = text.Substring(colon + 1);
            if (!FindTribe(t, out tribe)) return false;
            sigil = ScriptableObjectLoader<AbilityInfo>.AllData.Find(a => a != null && (SigilKey(a) == s || a.name == s));
            return sigil != null;
        }

        // Called right after the game creates a custom deck run
        internal static void Apply(RunState run)
        {
            var items = DeckCode.ParseList(Plugin.Items.Value).Where(n => ItemsUtil.GetConsumableByName(n) != null).ToList();
            if (items.Count > 0)
            {
                run.consumables = items.Take(run.MaxConsumables).ToList();
                Plugin.Log.LogInfo("Starting items: " + DeckCode.JoinList(run.consumables));
            }
            if (ParseTotem(Plugin.Totem.Value, out var tribe, out var sigil))
            {
                run.totems = new List<TotemDefinition> { new TotemDefinition { tribe = tribe, ability = sigil.ability } };
                // Also as loose pieces, like ones found during a run, so the Woodcarver offers them for a new totem
                if (!run.totemTops.Contains(tribe)) run.totemTops.Add(tribe);
                if (!run.totemBottoms.Contains(sigil.ability)) run.totemBottoms.Add(sigil.ability);
                Plugin.Log.LogInfo($"Starting totem: {TribeName(tribe)} + {DeckLogic.AbilityName(sigil.ability)}");
            }
            else if (!string.IsNullOrEmpty(Plugin.Totem.Value)) Plugin.Log.LogWarning($"Unknown totem '{Plugin.Totem.Value}', starting without one.");
        }

        // Icons, cached: item artwork from the rulebook, tribe heads from the card tribe icons
        private static readonly Dictionary<Tribe, Sprite> tribeIcons = new Dictionary<Tribe, Sprite>();
        internal static Sprite TribeIcon(Tribe t)
        {
            if (!tribeIcons.TryGetValue(t, out var s))
            {
                try { s = Resources.Load<Sprite>("Art/Cards/TribeIcons/tribeicon_" + t.ToString().ToLowerInvariant()); } catch { s = null; }
                tribeIcons[t] = s;
            }
            return s;
        }
    }
}
