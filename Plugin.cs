using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiskCardGame;
using HarmonyLib;
using InscryptionAPI;
using InscryptionAPI.Ascension;
using InscryptionAPI.Card;
using InscryptionAPI.Saves;
using UnityEngine;

namespace KCMDeckBuilder
{
    [BepInPlugin(Guid, "KCM Deck Builder", "2.0.0")]
    [BepInDependency(InscryptionAPIPlugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "emre.inscryption.kcmdeckbuilder";
        public const string CustomDeckId = Guid + "_Custom"; // the name the API gives the deck
        private const string SelectedKey = "CustomDeckSelected";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<string> Deck;
        internal static ConfigEntry<int> MaxCards;
        internal static ConfigEntry<int> MaxCopies;
        internal static ConfigEntry<bool> OnlyUnlocked;
        internal static ConfigEntry<bool> AllowSpecial;
        private static ConfigEntry<bool> legacySelected;
        private static StarterDeckManager.FullStarterDeck apiDeck;
        internal static string CardListPath;
        internal static string SavedDecksPath;

        private void Awake()
        {
            var sw = Stopwatch.StartNew();
            Instance = this;
            Log = Logger;
            Deck = Config.Bind("Deck", "Cards", "Stoat, Bullfrog, Wolf",
                "Comma-separated card IDs. Usually edited from the in-game builder; see BepInEx/config/KCMDeckBuilder_cards.txt for valid IDs.");
            MaxCards = Config.Bind("Rules", "MaxCards", 5, "Maximum number of chosen cards. 0 = no limit.");
            MaxCopies = Config.Bind("Rules", "MaxCopiesPerCard", 0, "Maximum copies of the same card. 0 = no limit.");
            OnlyUnlocked = Config.Bind("Rules", "OnlyUnlockedCards", true, "Only allow cards you have unlocked.");
            AllowSpecial = Config.Bind("Rules", "AllowBossAndSpecialCards", true, "Allow boss-only cards and other cards you can't normally draft (terrain, Dam, ...).");
            legacySelected = Config.Bind("Internal", "CustomDeckSelected", false, "Only read when upgrading from 1.x. The choice is now kept in the API's ModdedSaveFile.gwsave.");
            CardListPath = Path.Combine(Paths.ConfigPath, "KCMDeckBuilder_cards.txt");
            SavedDecksPath = Path.Combine(Paths.ConfigPath, "KCMDeckBuilder_decks.txt");

            // A normal starter deck for the API: it gets its own icon on the deck screen next to the game's decks
            apiDeck = StarterDeckManager.New(Guid, "Custom", "Custom Deck", LoadTexture("deck_icon.png"), ParseDeck(Deck.Value).ToArray());

            gameObject.AddComponent<DeckBuilderUI>();
            new Harmony(Guid).PatchAll();
            Log.LogInfo($"KCM Deck Builder loaded in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
        }

        // Whether the custom deck is the selected starter deck. Kept in the API's modded save, not the game's save.
        internal static bool CustomSelected
        {
            get
            {
                string v = ModdedSaveManager.SaveData?.GetValue(Guid, SelectedKey);
                return v != null ? v == bool.TrueString : legacySelected.Value;
            }
            set => ModdedSaveManager.SaveData?.SetValue(Guid, SelectedKey, value);
        }

        // Shows the new cards on the deck screen; the run itself is always built from the config (see NewRunPatch)
        internal static void UpdateDeckInfo(List<CardInfo> deck)
        {
            try
            {
                // The API rebuilds its deck list from CardNames each time the deck screen opens
                apiDeck.CardNames = deck.Select(c => c.name).ToList();
                // Until then, update the copies already in use: the one the deck icons show and the one GetInfo returns
                var shown = StarterDeckManager.AllDecks.FirstOrDefault(d => d.Info.name == CustomDeckId)?.Info;
                var loaded = StarterDecksUtil.GetInfo(CustomDeckId);
                foreach (var info in new[] { shown, loaded }.Where(i => i != null).Distinct())
                    info.cards = deck.Select(c => CardLoader.GetCardByName(c.name)).ToList();
            }
            catch (Exception e) { Log.LogError("Could not update the deck shown on the deck screen: " + e.Message); }
        }

        private static Texture2D LoadTexture(string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            try
            {
                using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name))
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    tex.LoadImage(ms.ToArray());
                }
            }
            catch (Exception e) { Log.LogWarning($"Could not load {name}: {e.Message}"); }
            return tex;
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

        // Everything the builder reads from a card every frame
        private static bool Readable(CardInfo c)
        {
            if (c == null || string.IsNullOrEmpty(c.name) || c.metaCategories == null || c.appearanceBehaviour == null) return false;
            int stats = c.BloodCost + c.BonesCost + c.Attack + c.Health;
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

        // Cards added by other mods (card packs); the API keeps the base game's cards in their own list
        private static HashSet<string> baseGameNames;
        internal static bool IsModded(CardInfo c)
        {
            if (baseGameNames == null) baseGameNames = new HashSet<string>(CardManager.BaseGameCards.Select(b => b.name));
            return !baseGameNames.Contains(c.name);
        }

        // Share codes: "KCM:" + the card IDs in base64, so chat apps can't mangle names with underscores
        private const string CodePrefix = "KCM:";

        internal static string DeckCode(IEnumerable<CardInfo> deck) =>
            CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(",", deck.Select(c => c.name).ToArray()))).TrimEnd('=');

        // Card IDs from a share code anywhere in the text, or null if there is none
        internal static List<string> ParseDeckCode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf(CodePrefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string b64 = new string(text.Substring(i + CodePrefix.Length).TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '+' || ch == '/' || ch == '=').ToArray()).TrimEnd('=');
            if (b64.Length == 0) return null;
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            try
            {
                var names = ParseDeck(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                return names.Count > 0 ? names : null;
            }
            catch { return null; }
        }

        // English name, falling back to the ID for cards (usually from other mods) that have none
        internal static string DisplayName(CardInfo c)
        {
            string n = null;
            try { n = c.DisplayedNameEnglish; } catch { }
            return string.IsNullOrEmpty(n) ? c.name : n;
        }

        internal static bool IsPelt(CardInfo c) => CardLoader.PeltNames.Contains(c.name);

        // Regular (non-rare) cards you can be offered in card choices; used by the random deck
        internal static bool IsRegularChoice(CardInfo c) =>
            c.metaCategories.Contains(CardMetaCategory.ChoiceNode) && !IsRare(c);

        // Saved decks, one per line: "Name|Card, Card, ..."
        internal static List<KeyValuePair<string, string>> LoadSavedDecks()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                if (!File.Exists(SavedDecksPath)) return list;
                foreach (var line in File.ReadAllLines(SavedDecksPath, Encoding.UTF8))
                {
                    int bar = line.IndexOf('|');
                    if (line.StartsWith("#") || bar <= 0) continue;
                    list.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim(), line.Substring(bar + 1).Trim()));
                }
            }
            catch (Exception e) { Log.LogError("Could not read saved decks: " + e.Message); }
            return list;
        }

        internal static void WriteSavedDecks(List<KeyValuePair<string, string>> decks)
        {
            try
            {
                var lines = new List<string> { "# KCM Deck Builder - saved decks (Name|Card, Card, ...)" };
                lines.AddRange(decks.Select(d => d.Key + "|" + d.Value));
                File.WriteAllLines(SavedDecksPath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception e) { Log.LogError("Could not write saved decks: " + e.Message); }
        }

        // Boss cards you can't normally get in a run
        internal static bool IsBossCard(CardInfo c) => BossCandidates.Contains(c.name) && !Draftable(c);

        // Everything else in the Nature set you can't normally draft (terrain, Dam, Smoke, ...)
        internal static bool IsOtherCard(CardInfo c, HashSet<string> starterNames) =>
            !Draftable(c) && !IsPelt(c) && !IsBossCard(c) && !starterNames.Contains(c.name);

        internal static HashSet<string> StarterCardNames() => DeckCardNames(d => true);

        // Card IDs in the starter decks (except ours) that pass the check
        private static HashSet<string> DeckCardNames(Func<StarterDeckInfo, bool> include)
        {
            var names = new HashSet<string>();
            foreach (var d in ScriptableObjectLoader<StarterDeckInfo>.AllData)
            {
                try
                {
                    if (d == null || d.cards == null || d.name == CustomDeckId || !include(d)) continue;
                    foreach (var c in d.cards) if (c != null) names.Add(c.name);
                }
                catch (Exception e) { Log.LogWarning($"Skipping starter deck '{d?.name}': {e.Message}"); }
            }
            return names;
        }

        internal static List<CardInfo> CardPool()
        {
            // Cards from unlocked starter decks (e.g. Stoat, which is not a ChoiceNode card) are allowed too
            var unlockedStarter = DeckCardNames(d =>
                !OnlyUnlocked.Value || d.name == "Vanilla" || AscensionUnlockSchedule.StarterDeckIsUnlockedForLevel(d.name, AscensionSaveData.Data.challengeLevel));
            var allStarter = StarterCardNames();

            // Each card is checked on its own: one card with broken data (usually from another mod) is left out
            // instead of breaking the builder. Readable() also makes sure the per-frame drawing code can trust the pool.
            return ScriptableObjectLoader<CardInfo>.AllData.FindAll(c =>
            {
                try
                {
                    return Readable(c) && Usable(c)
                        && (unlockedStarter.Contains(c.name)
                            || IsPelt(c)
                            || (Draftable(c) && (!OnlyUnlocked.Value || ConceptProgressionTree.Tree.CardUnlocked(c)))
                            || (AllowSpecial.Value && (IsBossCard(c) || IsOtherCard(c, allStarter))));
                }
                catch (Exception e)
                {
                    CardFace.Report(c, "card data, leaving it out", e);
                    return false;
                }
            });
        }

        private static string lastCardList;

        // Writes the list of valid card IDs for editing the config by hand; skipped if nothing changed
        internal static void WriteCardList(List<CardInfo> pool)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# KCM Deck Builder - selectable cards (ID | name | blood/bones | sigils)");
                sb.AppendLine($"# Max cards: {LimitText(MaxCards.Value)}, max copies per card: {LimitText(MaxCopies.Value)}");
                foreach (var c in pool.OrderBy(c => c.name))
                {
                    try
                    {
                        string sigils = string.Join(", ", c.Abilities.Select(a => a.ToString()).ToArray());
                        string rare = IsRare(c) ? " [RARE]" : "";
                        sb.AppendLine($"{c.name} | {DisplayName(c)}{rare} | {c.BloodCost}blood/{c.BonesCost}bones | {sigils}");
                    }
                    catch { sb.AppendLine(c.name); }
                }
                string list = sb.ToString();
                if (list == lastCardList && File.Exists(CardListPath)) return;
                File.WriteAllText(CardListPath, list, Encoding.UTF8);
                lastCardList = list;
                Log.LogInfo($"Card list written in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
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

    [HarmonyPatch(typeof(AscensionSaveData), nameof(AscensionSaveData.NewRun))]
    internal static class NewRunPatch
    {
        // NewRun adds catch-up cards based on numRunsSinceReachedFirstBoss: 0 -> 2 pelts, 1 -> Opossum + pelt,
        // more -> Opossum + Ring Worm. A negative value matches none of those, so while the run is created we set it
        // to -1 and restore it right after. The deck itself is never touched, so cards other mods add are kept.
        private static void Prefix(ref List<CardInfo> starterDeck, out int? __state)
        {
            __state = null;
            // Only act when the custom deck is selected; leave the other starter decks alone
            if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
            List<CardInfo> deck = null;
            try
            {
                Plugin.Instance.Config.Reload();
                deck = Plugin.BuildDeck();
            }
            catch (Exception e) { Plugin.Log.LogError("Building the custom deck failed: " + e); }
            if (deck == null)
            {
                Plugin.Log.LogWarning("Custom deck invalid, using the Vanilla deck.");
                starterDeck = StarterDecksUtil.GetInfo("Vanilla").cards;
                return;
            }
            starterDeck = deck;
            var data = AscensionSaveData.Data;
            __state = data.numRunsSinceReachedFirstBoss;
            data.numRunsSinceReachedFirstBoss = -1;
        }

        // Runs even if NewRun throws, so the counter is always restored
        private static Exception Finalizer(Exception __exception, int? __state)
        {
            if (__state.HasValue) AscensionSaveData.Data.numRunsSinceReachedFirstBoss = __state.Value;
            return __exception;
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
            try
            {
                if (__result || !SaveFile.IsAscension || rowIndex != 1 || RunState.Run.regionTier != 0) return;
                if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
                if (RunState.Run.playerDeck.Cards.Exists(c => Plugin.IsPelt(c))) __result = true;
            }
            catch (Exception e) { Plugin.Log.LogError("Trader hook failed: " + e.Message); }
        }
    }
}

namespace KCMDeckBuilder
{
    // Keep the custom deck id out of the save file: write "Vanilla" instead and remember the choice in the API's modded save.
    // That way the save stays valid for the unmodded game if the mod is removed.
    internal static class SaveAccess
    {
        private static readonly AccessTools.FieldRef<SaveFile> saveFile = AccessTools.StaticFieldRefAccess<SaveFile>(AccessTools.Field(typeof(SaveManager), "saveFile"));

        // The loaded ascension data, or null; never triggers a load
        internal static AscensionSaveData Data
        {
            get
            {
                try { return saveFile()?.ascensionData; }
                catch { return null; }
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveToFile))]
    internal static class SavePatch
    {
        private static void Prefix(out AscensionSaveData __state)
        {
            __state = null;
            try
            {
                var data = SaveAccess.Data;
                if (data == null) return;
                bool custom = data.currentStarterDeck == Plugin.CustomDeckId;
                if (custom) { data.currentStarterDeck = "Vanilla"; __state = data; }
                Plugin.CustomSelected = custom; // written to the modded save by the API right after the game saves
            }
            catch (Exception e) { Plugin.Log.LogError("Save hook failed (the game still saves normally): " + e.Message); }
        }

        // Runs after saving, even if saving throws: put the custom deck back for this session
        private static Exception Finalizer(Exception __exception, AscensionSaveData __state)
        {
            if (__state != null) __state.currentStarterDeck = Plugin.CustomDeckId;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadFromFile))]
    [HarmonyAfter(InscryptionAPIPlugin.ModGUID)] // the API reads its modded save in its own postfix
    internal static class LoadPatch
    {
        private static void Postfix()
        {
            try
            {
                var data = SaveAccess.Data;
                if (data != null && Plugin.CustomSelected && data.currentStarterDeck == "Vanilla")
                    data.currentStarterDeck = Plugin.CustomDeckId;
            }
            catch (Exception e) { Plugin.Log.LogError("Load hook failed: " + e.Message); }
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
            if (eventId != "ResetRunPunishment") return true;
            try
            {
                var data = SaveAccess.Data;
                if (data == null || !SaveFile.IsAscension || data.currentStarterDeck != Plugin.CustomDeckId) return true;
            }
            catch { return true; }
            __result = Enumerable.Empty<object>().GetEnumerator();
            return false;
        }
    }
}
