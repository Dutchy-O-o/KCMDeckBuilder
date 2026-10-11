using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiskCardGame;
using HarmonyLib;

namespace KCMDeckBuilder
{
    [BepInPlugin(Guid, "KCM Deck Builder", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "emre.inscryption.kcmdeckbuilder";
        public const string CustomDeckId = "KCM_Custom";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<string> Deck;
        internal static ConfigEntry<int> MaxCards;
        internal static ConfigEntry<int> MaxCopies;
        internal static ConfigEntry<bool> OnlyUnlocked;
        internal static ConfigEntry<bool> AllowSpecial;
        internal static ConfigEntry<bool> CustomSelected;
        internal static string CardListPath;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Deck = Config.Bind("Deck", "Cards", "Stoat, Bullfrog, Wolf",
                "Comma-separated card IDs. Usually edited from the in-game builder; see BepInEx/config/KCMDeckBuilder_cards.txt for valid IDs.");
            MaxCards = Config.Bind("Rules", "MaxCards", 5, "Maximum number of chosen cards. 0 = no limit.");
            MaxCopies = Config.Bind("Rules", "MaxCopiesPerCard", 0, "Maximum copies of the same card. 0 = no limit.");
            OnlyUnlocked = Config.Bind("Rules", "OnlyUnlockedCards", true, "Only allow cards you have unlocked.");
            AllowSpecial = Config.Bind("Rules", "AllowBossAndSpecialCards", true, "Allow boss-only cards and other cards you can't normally draft (terrain, Dam, ...).");
            CustomSelected = Config.Bind("Internal", "CustomDeckSelected", false, "Set by the mod: whether the custom deck is the selected starter deck. The save file itself never stores the custom deck, so removing the mod is safe.");
            CardListPath = Path.Combine(Paths.ConfigPath, "KCMDeckBuilder_cards.txt");

            gameObject.AddComponent<DeckBuilderUI>();
            new Harmony(Guid).PatchAll();
            Log.LogInfo("KCM Deck Builder loaded.");
        }

        internal static bool IsRare(CardInfo c) => c.metaCategories.Contains(CardMetaCategory.Rare);

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

        private static bool Draftable(CardInfo c) =>
            c.metaCategories.Contains(CardMetaCategory.ChoiceNode) || IsRare(c);

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

        internal static bool IsPelt(CardInfo c) => CardLoader.PeltNames.Contains(c.name);

        // Boss cards you can't normally get in a run
        internal static bool IsBossCard(CardInfo c) => BossCandidates.Contains(c.name) && !Draftable(c);

        // Everything else in the Nature set you can't normally draft (terrain, Dam, Smoke, ...)
        internal static bool IsOtherCard(CardInfo c, HashSet<string> starterNames) =>
            !Draftable(c) && !IsPelt(c) && !IsBossCard(c) && !starterNames.Contains(c.name);

        internal static HashSet<string> StarterCardNames() =>
            new HashSet<string>(ScriptableObjectLoader<StarterDeckInfo>.AllData.SelectMany(d => d.cards).Select(c => c.name));

        internal static List<CardInfo> CardPool()
        {
            // Cards from unlocked starter decks (e.g. Stoat, which is not a ChoiceNode card) are allowed too
            var unlockedStarter = new HashSet<string>(ScriptableObjectLoader<StarterDeckInfo>.AllData
                .Where(d => !OnlyUnlocked.Value || d.name == "Vanilla" || AscensionUnlockSchedule.StarterDeckIsUnlockedForLevel(d.name, AscensionSaveData.Data.challengeLevel))
                .SelectMany(d => d.cards).Select(c => c.name));
            var allStarter = StarterCardNames();

            return ScriptableObjectLoader<CardInfo>.AllData.FindAll(c =>
                Usable(c)
                && (unlockedStarter.Contains(c.name)
                    || IsPelt(c)
                    || (Draftable(c) && (!OnlyUnlocked.Value || ConceptProgressionTree.Tree.CardUnlocked(c)))
                    || (AllowSpecial.Value && (IsBossCard(c) || IsOtherCard(c, allStarter)))));
        }

        // The custom deck is served through StarterDecksUtil.GetInfo instead of being added to the deck list,
        // so mods that rebuild that list (e.g. the Inscryption API) can't drop it
        private static StarterDeckInfo customDeckInfo;
        internal static StarterDeckInfo CustomDeckInfo
        {
            get
            {
                if (customDeckInfo == null)
                {
                    customDeckInfo = UnityEngine.ScriptableObject.CreateInstance<StarterDeckInfo>();
                    customDeckInfo.name = CustomDeckId;
                    customDeckInfo.title = "Custom Deck";
                    customDeckInfo.cards = new List<CardInfo>();
                    UnityEngine.Object.DontDestroyOnLoad(customDeckInfo);
                }
                return customDeckInfo;
            }
        }

        internal static void WriteCardList()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# KCM Deck Builder - selectable cards (ID | name | blood/bones | sigils)");
                sb.AppendLine($"# Max cards: {LimitText(MaxCards.Value)}, max copies per card: {LimitText(MaxCopies.Value)}");
                foreach (var c in CardPool().OrderBy(c => c.name))
                {
                    string sigils = string.Join(", ", c.Abilities.Select(a => a.ToString()).ToArray());
                    string rare = IsRare(c) ? " [RARE]" : "";
                    sb.AppendLine($"{c.name} | {c.DisplayedNameEnglish}{rare} | {c.BloodCost}blood/{c.BonesCost}bones | {sigils}");
                }
                File.WriteAllText(CardListPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.LogError("Could not write card list: " + e);
            }
        }

        internal static string LimitText(int n) => n > 0 ? n.ToString() : "no limit";

        internal static List<string> ParseDeck(string s) =>
            s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        // Checks the deck against the rules; returns null if valid, otherwise an error message.
        internal static string Validate(List<CardInfo> deck)
        {
            if (deck.Count == 0) return "Deck is empty.";
            if (MaxCards.Value > 0 && deck.Count > MaxCards.Value) return $"Too many cards: {deck.Count} > {MaxCards.Value}";
            var tooMany = deck.GroupBy(c => c.name).FirstOrDefault(g => MaxCopies.Value > 0 && g.Count() > MaxCopies.Value);
            if (tooMany != null) return $"Too many copies of '{tooMany.Key}'.";
            return null;
        }

        // Builds the deck from config; returns null and logs the reason if invalid.
        internal static List<CardInfo> BuildDeck()
        {
            var pool = CardPool();
            var deck = new List<CardInfo>();
            foreach (var n in ParseDeck(Deck.Value))
            {
                var card = pool.Find(c => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase));
                if (card == null) { Log.LogWarning($"Invalid or locked card: '{n}'"); return null; }
                deck.Add(card);
            }

            string err = Validate(deck);
            if (err != null) { Log.LogWarning(err); return null; }

            Log.LogInfo($"Using custom deck: {string.Join(", ", deck.Select(c => c.name).ToArray())}");
            return deck;
        }
    }

    [HarmonyPatch(typeof(AscensionMenuScreens), "Start")]
    internal static class MenuStartPatch
    {
        private static void Postfix()
        {
            Plugin.WriteCardList();
        }
    }

    [HarmonyPatch(typeof(AscensionSaveData), nameof(AscensionSaveData.NewRun))]
    internal static class NewRunPatch
    {
        private static List<CardInfo> applied;

        private static void Prefix(ref List<CardInfo> starterDeck)
        {
            applied = null;
            // Only act when the custom deck is selected; leave the other starter decks alone
            if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
            Plugin.Instance.Config.Reload();
            var deck = Plugin.BuildDeck();
            if (deck != null) starterDeck = applied = deck;
            else
            {
                Plugin.Log.LogWarning("Custom deck invalid, using the Vanilla deck.");
                starterDeck = StarterDecksUtil.GetInfo("Vanilla").cards;
            }
        }

        // NewRun adds catch-up cards (2 pelts, or Opossum/Ring Worm after early losses).
        // With a custom deck the player picks everything, so rebuild the deck from the chosen cards only.
        private static void Postfix()
        {
            if (applied == null) return;
            var run = AscensionSaveData.Data.currentRun;
            run.playerDeck = new DeckInfo();
            foreach (var c in applied) run.playerDeck.AddCard(CardLoader.GetCardByName(c.name));
            applied = null;
        }
    }
}

namespace KCMDeckBuilder
{
    // Kaycee's Mod only puts the trader on the first map node while numRunsSinceReachedFirstBoss < 2.
    // With a custom deck that has pelts, always give the player that trader.
    [HarmonyPatch(typeof(MapGenerator), "ForceFirstNodeTraderForAscension")]
    internal static class FirstTraderPatch
    {
        private static void Postfix(int rowIndex, ref bool __result)
        {
            if (__result || !SaveFile.IsAscension || rowIndex != 1 || RunState.Run.regionTier != 0) return;
            if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
            if (RunState.Run.playerDeck.Cards.Exists(c => Plugin.IsPelt(c))) __result = true;
        }
    }
}

namespace KCMDeckBuilder
{
    [HarmonyPatch(typeof(StarterDecksUtil), nameof(StarterDecksUtil.GetInfo))]
    internal static class GetInfoPatch
    {
        private static void Postfix(string deckId, ref StarterDeckInfo __result)
        {
            if (__result == null && deckId == Plugin.CustomDeckId) __result = Plugin.CustomDeckInfo;
        }
    }

    // Keep the custom deck id out of the save file: write "Vanilla" instead and remember the choice in the config.
    // That way the save stays valid for the unmodded game if the mod is removed.
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveToFile))]
    internal static class SavePatch
    {
        private static void Prefix(out bool __state)
        {
            __state = false;
            var data = SaveManager.SaveFile?.ascensionData;
            if (data == null) return;
            bool custom = data.currentStarterDeck == Plugin.CustomDeckId;
            if (Plugin.CustomSelected.Value != custom) { Plugin.CustomSelected.Value = custom; Plugin.Instance.Config.Save(); }
            if (custom) { data.currentStarterDeck = "Vanilla"; __state = true; }
        }

        private static void Postfix(bool __state)
        {
            if (__state) SaveManager.SaveFile.ascensionData.currentStarterDeck = Plugin.CustomDeckId;
        }

        // Also restore the id if saving throws, so the session keeps the custom deck
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__exception != null && __state) SaveManager.SaveFile.ascensionData.currentStarterDeck = Plugin.CustomDeckId;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadFromFile))]
    internal static class LoadPatch
    {
        private static void Postfix()
        {
            var data = SaveManager.SaveFile?.ascensionData;
            if (data != null && Plugin.CustomSelected.Value && data.currentStarterDeck == "Vanilla")
                data.currentStarterDeck = Plugin.CustomDeckId;
        }
    }
}

namespace KCMDeckBuilder
{
    // Leshy's "reset punishment" lines ("Were you expecting more pelts?") explain the catch-up cards,
    // which the custom deck doesn't get, so skip them.
    [HarmonyPatch(typeof(TextDisplayer), nameof(TextDisplayer.PlayDialogueEvent))]
    internal static class PunishmentDialoguePatch
    {
        private static bool Prefix(string eventId, ref System.Collections.IEnumerator __result)
        {
            if (eventId != "ResetRunPunishment" || !SaveFile.IsAscension
                || AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return true;
            __result = Enumerable.Empty<object>().GetEnumerator();
            return false;
        }
    }
}
