using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiskCardGame;
using HarmonyLib;
using InscryptionAPI;
using InscryptionAPI.Ascension;
using InscryptionAPI.Saves;
using UnityEngine;

namespace KCMDeckBuilder
{
    // Entry point: config, the custom starter deck registered with the API, and the "custom deck selected" flag
    [BepInPlugin(Guid, "KCM Deck Builder", "2.1.0")]
    [BepInDependency(InscryptionAPIPlugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "emre.inscryption.kcmdeckbuilder";
        public const string CustomDeckId = Guid + "_Custom"; // the name the API gives the deck
        internal const string VanillaDeckId = "Vanilla";
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
            DeckStorage.Init(Paths.ConfigPath);

            // A normal starter deck for the API: it gets its own icon on the deck screen next to the game's decks
            apiDeck = StarterDeckManager.New(Guid, "Custom", "Custom Deck", LoadTexture("deck_icon.png"), DeckCode.ParseList(Deck.Value).ToArray());

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
    }
}
