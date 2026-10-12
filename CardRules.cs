using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using DiskCardGame;
using InscryptionAPI.Card;

namespace KCMDeckBuilder
{
    // Which cards the builder offers and how they are grouped (pelts, boss cards, other cards, modded cards)
    internal static class CardRules
    {
        // Cards Act 1 bosses play (from the boss encounter blueprints), plus the Trapper's traps and the Prospector's gold
        private static readonly HashSet<string> BossCandidates = new HashSet<string>
        {
            "Shark", "BaitBucket", "Snapper", "Kingfisher",                     // Angler
            "SkeletonPirate", "SkeletonParrot",                                 // Royal
            "Mule", "Bloodhound", "Grizzly", "Coyote", "GoldNugget",            // Prospector
            "TrapFrog", "Trap",                                                 // Trapper
            "Urayuli", "Amalgam",                                               // Leshy
        };

        // Odd cards that are not real picks (talking cards, menu/dev variants)
        private static readonly HashSet<string> Excluded = new HashSet<string>
        {
            "CardMergeStones", "Ijiraq_UnlockScreen", "Stoat_Talking", "Wolf_Talking", "Stinkbug_Talking",
        };

        internal static bool IsRare(CardInfo c) => c.metaCategories.Contains(CardMetaCategory.Rare);

        internal static bool IsPelt(CardInfo c) => CardLoader.PeltNames.Contains(c.name);

        private static bool Draftable(CardInfo c) =>
            c.metaCategories.Contains(CardMetaCategory.ChoiceNode) || IsRare(c);

        // Regular (non-rare) cards you can be offered in card choices; used by the random deck
        internal static bool IsRegularChoice(CardInfo c) =>
            c.metaCategories.Contains(CardMetaCategory.ChoiceNode) && !IsRare(c);

        internal static bool IsBossCard(CardInfo c) => BossCandidates.Contains(c.name) && !Draftable(c);

        // Everything else in the Nature set you can't normally draft (terrain, Dam, Smoke, ...)
        internal static bool IsOtherCard(CardInfo c, HashSet<string> starterNames) =>
            !Draftable(c) && !IsPelt(c) && !IsBossCard(c) && !starterNames.Contains(c.name);

        // Cards added by other mods (card packs); the API keeps the base game's cards in their own list
        private static HashSet<string> baseGameNames;
        internal static bool IsModded(CardInfo c)
        {
            if (baseGameNames == null) baseGameNames = new HashSet<string>(CardManager.BaseGameCards.Select(b => b.name));
            return !baseGameNames.Contains(c.name);
        }

        // The GUID of the plugin that added the card, and the prefix its pack gives card IDs; null if unknown
        internal static string ModTag(CardInfo c) => Safe(c.GetModTag);
        internal static string ModPrefix(CardInfo c) => Safe(c.GetModPrefix);

        private static string Safe(Func<string> get)
        {
            try { string s = get(); return string.IsNullOrEmpty(s) ? null : s; }
            catch { return null; }
        }

        internal static string PluginName(string guid) =>
            Chainloader.PluginInfos.TryGetValue(guid, out var p) && !string.IsNullOrEmpty(p.Metadata.Name) ? p.Metadata.Name : null;

        // English name, falling back to the ID for cards (usually from other mods) that have none
        internal static string DisplayName(CardInfo c)
        {
            string n = null;
            try { n = c.DisplayedNameEnglish; } catch { }
            return string.IsNullOrEmpty(n) ? c.name : n;
        }

        // Reads everything the builder reads from a card while drawing. Some of these are getters that other mods
        // patch, so a card with broken data throws here (and is left out) instead of in the middle of drawing.
        private static bool Readable(CardInfo c)
        {
            if (c == null || string.IsNullOrEmpty(c.name) || c.metaCategories == null || c.appearanceBehaviour == null) return false;
            _ = c.BloodCost + c.BonesCost + c.Attack + c.Health;
            return c.Abilities != null;
        }

        private static bool Usable(CardInfo c) =>
            !c.name.StartsWith("!") && !Excluded.Contains(c.name) && c.temple == CardTemple.Nature
            && !c.appearanceBehaviour.Contains(CardAppearanceBehaviour.Appearance.HologramPortrait) // Act 3 holograms
            && HasArt(c);

        // Cut/unused cards ship without any Act 1 art
        private static bool HasArt(CardInfo c)
        {
            if (c.portraitTex != null && c.portraitTex.texture != null) return true;
            if (c.appearanceBehaviour.Contains(CardAppearanceBehaviour.Appearance.AddSnelkDecals)) return true;
            try { if (c.Decals.Any(d => d != null)) return true; } catch { }
            return CardFace.FallbackPortrait(c) != null;
        }

        internal static HashSet<string> StarterCardNames() => DeckCardNames(d => true);

        // Card IDs in the starter decks (except ours) that pass the check
        private static HashSet<string> DeckCardNames(Func<StarterDeckInfo, bool> include)
        {
            var names = new HashSet<string>();
            foreach (var d in ScriptableObjectLoader<StarterDeckInfo>.AllData)
            {
                try
                {
                    if (d == null || d.cards == null || d.name == Plugin.CustomDeckId || !include(d)) continue;
                    foreach (var c in d.cards) if (c != null) names.Add(c.name);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Skipping starter deck '{d?.name}': {e.Message}"); }
            }
            return names;
        }

        // Every card the current rules allow, in no particular order
        internal static List<CardInfo> CardPool()
        {
            // Cards from unlocked starter decks (e.g. Stoat, which is not a ChoiceNode card) are allowed too
            var unlockedStarter = DeckCardNames(d =>
                !Plugin.OnlyUnlocked.Value || d.name == Plugin.VanillaDeckId
                || AscensionUnlockSchedule.StarterDeckIsUnlockedForLevel(d.name, AscensionSaveData.Data.challengeLevel));
            var allStarter = StarterCardNames();

            // Each card is checked on its own: one card with broken data (usually from another mod) is left out
            // instead of breaking the builder. Readable() also makes sure the drawing code can trust the pool.
            return ScriptableObjectLoader<CardInfo>.AllData.FindAll(c =>
            {
                try
                {
                    return Readable(c) && Usable(c)
                        && (unlockedStarter.Contains(c.name)
                            || IsPelt(c)
                            || (Draftable(c) && (!Plugin.OnlyUnlocked.Value || ConceptProgressionTree.Tree.CardUnlocked(c)))
                            || (Plugin.AllowSpecial.Value && (IsBossCard(c) || IsOtherCard(c, allStarter))));
                }
                catch (Exception e)
                {
                    CardFace.Report(c, "card data, leaving it out", e);
                    return false;
                }
            });
        }
    }
}
