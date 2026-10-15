using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using GBC;
using HarmonyLib;
using InscryptionAPI;
using UnityEngine;

namespace KCMDeckBuilder
{
    // All Harmony patches. Each one only acts when the custom deck is selected (or, for the menu ones, on the
    // starter deck screen), and catches its own errors so the game carries on normally.

    [HarmonyPatch(typeof(AscensionSaveData), nameof(AscensionSaveData.NewRun))]
    internal static class NewRunPatch
    {
        // NewRun adds catch-up cards based on numRunsSinceReachedFirstBoss: 0 -> 2 pelts, 1 -> Opossum + pelt,
        // more -> Opossum + Ring Worm. A negative value matches none of those, so while the run is created we set it
        // to -1 and restore it right after. The deck itself is never touched, so cards other mods add are kept.
        private static void Prefix(ref List<CardInfo> starterDeck, out int? __state)
        {
            __state = null;
            if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
            List<CardInfo> deck = null;
            try
            {
                Plugin.Instance.Config.Reload();
                deck = DeckLogic.DeckFromConfig();
            }
            catch (Exception e) { Plugin.Log.LogError("Building the custom deck failed: " + e); }
            if (deck == null)
            {
                Plugin.Log.LogWarning("Custom deck invalid, using the Vanilla deck.");
                starterDeck = StarterDecksUtil.GetInfo(Plugin.VanillaDeckId).cards;
                return;
            }
            starterDeck = deck;
            var data = AscensionSaveData.Data;
            __state = data.numRunsSinceReachedFirstBoss;
            data.numRunsSinceReachedFirstBoss = -1;
        }

        // The items and totem picked in the builder replace the run's usual ones
        private static void Postfix(AscensionSaveData __instance, int? __state)
        {
            if (!__state.HasValue) return;
            try { RunStart.Apply(__instance.currentRun); }
            catch (Exception e) { Plugin.Log.LogError("Could not set the starting items and totem: " + e); }
        }

        // Runs even if NewRun throws, so the counter is always restored
        private static Exception Finalizer(Exception __exception, int? __state)
        {
            if (__state.HasValue) AscensionSaveData.Data.numRunsSinceReachedFirstBoss = __state.Value;
            return __exception;
        }
    }

    // EndRun raises numRunsSinceReachedFirstBoss when a run is given up in the first region (and resets it otherwise),
    // and that counter sets the catch-up cards of the next runs. A custom deck run leaves it as it was, so it never
    // changes what the normal starter decks get. EndRun saves the game itself, so the save hook puts the value back
    // before writing.
    [HarmonyPatch(typeof(AscensionSaveData), nameof(AscensionSaveData.EndRun))]
    internal static class EndRunPatch
    {
        internal static int? KeepCounter;

        private static void Prefix(AscensionSaveData __instance)
        {
            KeepCounter = null;
            try
            {
                if (__instance.currentRun != null && __instance.currentStarterDeck == Plugin.CustomDeckId)
                    KeepCounter = __instance.numRunsSinceReachedFirstBoss;
            }
            catch (Exception e) { Plugin.Log.LogError("End run hook failed: " + e.Message); }
        }

        internal static void Restore(AscensionSaveData data)
        {
            if (KeepCounter.HasValue && data != null) data.numRunsSinceReachedFirstBoss = KeepCounter.Value;
        }

        private static Exception Finalizer(Exception __exception, AscensionSaveData __instance)
        {
            Restore(__instance);
            KeepCounter = null;
            return __exception;
        }
    }

    // Kaycee's Mod puts the trader on the first map node while numRunsSinceReachedFirstBoss < 2, to sell the
    // catch-up pelts it gives then. The custom deck gets no catch-up cards, so there the trader comes exactly when
    // the deck has pelts of its own.
    [HarmonyPatch(typeof(MapGenerator), "ForceFirstNodeTraderForAscension")]
    internal static class FirstTraderPatch
    {
        private static void Postfix(int rowIndex, ref bool __result)
        {
            try
            {
                if (!SaveFile.IsAscension || rowIndex != 1 || RunState.Run.regionTier != 0) return;
                if (AscensionSaveData.Data.currentStarterDeck != Plugin.CustomDeckId) return;
                __result = RunState.Run.playerDeck.Cards.Exists(CardRules.IsPelt);
            }
            catch (Exception e) { Plugin.Log.LogError("Trader hook failed: " + e.Message); }
        }
    }

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
                EndRunPatch.Restore(data);
                bool custom = data.currentStarterDeck == Plugin.CustomDeckId;
                if (custom) { data.currentStarterDeck = Plugin.VanillaDeckId; __state = data; }
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
                if (data != null && Plugin.CustomSelected && data.currentStarterDeck == Plugin.VanillaDeckId)
                    data.currentStarterDeck = Plugin.CustomDeckId;
            }
            catch (Exception e) { Plugin.Log.LogError("Load hook failed: " + e.Message); }
        }
    }

    // Leshy's "reset punishment" lines ("Were you expecting more pelts?") explain the catch-up cards,
    // which the custom deck doesn't get, so skip them.
    [HarmonyPatch(typeof(TextDisplayer), nameof(TextDisplayer.PlayDialogueEvent))]
    internal static class PunishmentDialoguePatch
    {
        private static bool Prefix(string eventId, ref IEnumerator __result)
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

    // A "- BUILD CUSTOM DECK -" menu item on the starter deck screen, cloned from one of the game's own menu
    // items so it has the same font, blinking highlight, pointer cursor and sound
    [HarmonyPatch(typeof(AscensionChooseStarterDeckScreen), "Start")]
    internal static class OpenButtonPatch
    {
        internal static bool Added;

        private static void Postfix(AscensionChooseStarterDeckScreen __instance)
        {
            try
            {
                var unlocks = Traverse.Create(AscensionMenuScreens.Instance).Field("unlockSummaryScreen").GetValue<GameObject>();
                var proto = unlocks.transform.Find("Stats/ScreenAnchor").GetChild(0).gameObject;
                var go = UnityEngine.Object.Instantiate(proto, __instance.transform);
                go.name = "KCM_BuildDeckButton";
                // Halfway between the header line and the top row of deck icons
                go.transform.localPosition = new Vector3(0f, 0.82f, 0f);
                var text = go.GetComponentInChildren<PixelText>(true);
                text.SetText("- BUILD CUSTOM DECK -");
                // Fit the click area and the highlight bar to the text instead of the whole screen width
                var uiText = text.GetComponentInChildren<UnityEngine.UI.Text>(true);
                float w = (uiText != null ? uiText.preferredWidth * text.transform.localScale.x : 1.3f) + 0.04f;
                var col = go.GetComponent<BoxCollider2D>();
                if (col != null) { col.size = new Vector2(w, 0.1f); col.offset = Vector2.zero; }
                var back = text.transform.Find("Text/Back");
                if (back != null) back.localScale = new Vector3(w / text.transform.localScale.x, back.localScale.y, 1f);
                go.GetComponent<AscensionMenuInteractable>().CursorSelectStarted = _ => { if (DeckBuilderUI.Instance != null) DeckBuilderUI.Instance.Open(); }; // Unity's null check, not ?.
                go.SetActive(true);
                Added = true;
            }
            catch (Exception e) { Plugin.Log.LogError("Could not add the BUILD CUSTOM DECK button, using the fallback one: " + e); }
        }
    }

    [HarmonyPatch(typeof(AscensionMenuScreens), "Start")]
    internal static class MenuTrackPatch
    {
        private static void Postfix(AscensionMenuScreens __instance) => DeckBuilderUI.Menu = __instance;
    }
}
