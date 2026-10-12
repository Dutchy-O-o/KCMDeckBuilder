using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DiskCardGame;
using UnityEngine;

namespace KCMDeckBuilder
{
    // The deck builder panel on the starter deck screen, styled after Kaycee's Mod's red CRT menus
    internal partial class DeckBuilderUI : MonoBehaviour
    {
        internal static DeckBuilderUI Instance;
        internal static AscensionMenuScreens Menu;

        private static readonly string[] SortLabels = DeckLogic.Sorts.Select(s => "SORT: " + s).ToArray();

        private enum Page { Cards, Decks, Settings }

        private readonly GameCursor cursor = new GameCursor();
        private readonly System.Random rng = new System.Random();
        private bool open;
        private Page page;
        private int errorsLogged;

        private List<CardInfo> pool = new List<CardInfo>();
        private HashSet<string> starterNames = new HashSet<string>();
        private string[] filters = DeckLogic.BaseFilters;
        private string filter = "ALL";
        private int sort;
        private string search = "";
        private Dictionary<string, string> packNames = new Dictionary<string, string>();
        private string[] packs = { DeckLogic.AllPacks }, packLabels = { "PACK: " + DeckLogic.AllPacks };
        private int pack;
        private Vector2 poolScroll, deckScroll, savedScroll;
        private CardInfo hovered;
        private string message = "";
        private float messageUntil;

        private List<CardInfo> deck = new List<CardInfo>();
        // Derived from the deck and the rules, so OnGUI (which runs several times per frame) only reads them
        private readonly Dictionary<string, string> copyBadges = new Dictionary<string, string>(); // card ID -> "x2"
        private string deckError, deckTotals, deckCountText;
        private bool deckFull;

        private string deckName = "";
        private List<KeyValuePair<string, string>> savedDecks = new List<KeyValuePair<string, string>>();

        // Rebuilt only when the input changes
        private List<CardInfo> visibleCache;
        private string visibleFilter, visibleSearch;
        private int visiblePack = -1, visibleSort = -1;
        private readonly Dictionary<CardInfo, string> searchCache = new Dictionary<CardInfo, string>();
        private readonly Dictionary<CardInfo, string> describeCache = new Dictionary<CardInfo, string>();
        private readonly Dictionary<string, List<CardInfo>> savedCardsCache = new Dictionary<string, List<CardInfo>>();
        private readonly Dictionary<string, string> upperCache = new Dictionary<string, string>();

        private bool OnDeckScreen =>
            Menu != null && Menu.isActiveAndEnabled && Menu.CurrentScreen == AscensionMenuScreens.Screen.StarterDeckSelect;

        private void Awake()
        {
            Instance = this;
            useGUILayout = false; // no GUILayout is used, so skip its extra layout pass before every OnGUI event
        }

        private void Update()
        {
            try
            {
                // Cheapest checks first: outside the builder this is a field read per frame, with no calls into Unity
                if (open)
                {
                    cursor.Update(lastHoverKey != 0);
                    if (!OnDeckScreen || Input.GetKeyDown(KeyCode.Escape)) Close(save: false);
                }
                else if (cursor.PointerForced && !OnDeckScreen) { cursor.SetPointer(false); lastHoverKey = 0; }
            }
            catch (Exception e) { Fail(e); }
        }

        private void OnGUI()
        {
            // Closed, with the game-style menu item in place, there is nothing to draw
            if ((!open && OpenButtonPatch.Added) || !OnDeckScreen) return;
            try { DrawUI(); }
            catch (Exception e) when (!(e is ExitGUIException)) { Fail(e); }
        }

        // Never leaves the player stuck behind a broken panel: closes it and logs the error (a few times at most)
        private void Fail(Exception e)
        {
            if (errorsLogged++ < 3) Plugin.Log.LogError("Deck builder error, closing it: " + e);
            GUI.matrix = Matrix4x4.identity;
            try { if (open) Close(save: false); } catch { open = false; }
        }

        private void DrawUI()
        {
            InitStyles();
            GUI.skin = skin;

            guiScale = Mathf.Max(1, Mathf.Floor(Screen.height / 700f));
            vw = Screen.width / guiScale;
            vh = Screen.height / guiScale;
            u = vh / 720f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(guiScale, guiScale, 1f));
            outerMouse = Event.current.mousePosition;
            clipView = null;
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) hoverKey = 0;

            if (open) DrawPanel();
            else DrawFallbackButton();

            if (!repaint) return;
            if (hoverKey != lastHoverKey)
            {
                if (hoverKey != 0) CommandLineTextDisplayer.PlayCommandLineClickSound();
                lastHoverKey = hoverKey;
                hoverSince = Time.unscaledTime;
                if (!open) cursor.SetPointer(hoverKey != 0);
            }
            if (open) cursor.Draw(lastHoverKey != 0);
        }

        // Only if the game-style menu item (OpenButtonPatch) couldn't be added
        private void DrawFallbackButton()
        {
            bool selected = AscensionSaveData.Data.currentStarterDeck == Plugin.CustomDeckId;
            if (FrameButton(R(vw / u / 2 - 150, 116, 300, 34), selected ? "CUSTOM DECK [SELECTED]" : "BUILD CUSTOM DECK", primary: false, size: 16))
                Open();
        }

        internal void Open()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Plugin.Instance.Config.Reload();
            RebuildPool();
            SetDeck(DeckLogic.Resolve(DeckCode.ParseList(Plugin.Deck.Value), pool).Where(c => c != null).ToList());
            savedDecks = DeckStorage.LoadSavedDecks();
            // Written here rather than at the menu: by now the deck screen has loaded the starter decks, so this is cheap
            DeckStorage.WriteCardList(pool, packNames);
            page = Page.Cards;
            open = true;
            Plugin.Log.LogDebug($"Deck builder opened in {sw.Elapsed.TotalMilliseconds:0.0} ms ({pool.Count} cards).");
            CommandLineTextDisplayer.PlayCommandLineClickSound();
            cursor.Take(true);
            lastHoverKey = hoverKey = 0;
        }

        private void Close(bool save)
        {
            if (save)
            {
                DeckLogic.SaveDeck(deck);
                Plugin.Log.LogInfo($"Deck saved: {Plugin.Deck.Value}");
            }
            open = false;
            hovered = null;
            cursor.Take(false);
            lastHoverKey = hoverKey = 0;
        }

        // The selectable cards under the current rules; also drops everything cached about the old ones
        private void RebuildPool()
        {
            starterNames = CardRules.StarterCardNames();
            CardFace.ClearCache(); // other mods can change card data between visits
            visibleCache = null;
            describeCache.Clear();
            savedCardsCache.Clear();
            searchCache.Clear();
            pool = DeckLogic.BuildPool(starterNames);
            filters = DeckLogic.Filters(pool);
            if (Array.IndexOf(filters, filter) < 0) filter = "ALL";
            string selectedPack = packs[pack];
            packNames = DeckLogic.PackNames(pool);
            packs = DeckLogic.Packs(packNames);
            packLabels = packs.Select(p => "PACK: " + p).ToArray();
            pack = Mathf.Max(0, Array.IndexOf(packs, selectedPack));
        }

        private void SetDeck(List<CardInfo> cards)
        {
            deck = cards;
            DeckChanged();
        }

        // Call after any change to the deck or to the card limits
        private void DeckChanged()
        {
            copyBadges.Clear();
            foreach (var g in deck.GroupBy(c => c.name)) copyBadges[g.Key] = "x" + g.Count();
            deckError = DeckLogic.Validate(deck)?.ToUpperInvariant();
            deckTotals = DeckLogic.Totals(deck);
            int max = Plugin.MaxCards.Value;
            deckCountText = max > 0 ? $"{deck.Count}/{max}" : deck.Count.ToString();
            deckFull = max > 0 && deck.Count >= max;
        }

        private void Flash(string msg)
        {
            message = msg.ToUpperInvariant();
            messageUntil = Time.unscaledTime + 2.5f;
        }

        private void DrawPanel()
        {
            float lw = vw / u; // layout width in 720p units
            const float pad = 24f;
            GUI.DrawTexture(new Rect(0, 0, vw, vh), texBg);

            Dash(R(0, 12, lw, 2));
            if (IconButton(R(pad + 6, 24, 36, 36), texBack)) Close(save: false);
            Label(R(0, 22, lw, 40), "BUILD YOUR DECK", 30, Red, TextAnchor.MiddleCenter);
            if (FrameButton(R(lw - pad - 150, 27, 150, 32), page == Page.Settings ? "CARDS" : "SETTINGS", false, 15))
                page = page == Page.Settings ? Page.Cards : Page.Settings;
            Dash(R(0, 70, lw, 2));

            float rightW = 330f;
            float leftW = lw - rightW - pad * 3;
            float y = 84;

            float fx = pad;
            foreach (var f in filters)
            {
                float w = Mathf.Max(60f, Measure(f, 16) / u + 24);
                if (TabButton(R(fx, y, w, 28), f, page == Page.Cards && f == filter)) { filter = f; page = Page.Cards; }
                fx += w + 8;
            }

            float top = y + 40;
            if (page == Page.Cards)
            {
                DrawSearchRow(top, leftW, pad);
                top += 38;
            }

            var view = R(pad, top, leftW, 0);
            view.height = vh - view.y - 92 * u;
            hovered = null;
            if (page == Page.Decks) DrawSavedDecks(view);
            else if (page == Page.Settings) DrawSettings(view);
            else DrawGrid(view);

            DrawFooter(leftW, pad);
            DrawDeckPanel(lw, rightW, pad, y);

            // CRT scanlines over everything
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, vw, vh), texScan, new Rect(0, 0, 1, vh / 3f));
        }

        private void DrawSearchRow(float top, float leftW, float pad)
        {
            float labelW = Measure("SEARCH", 15) / u + 10;
            Label(R(pad, top, labelW, 28), "SEARCH", 15, RedDim, TextAnchor.MiddleLeft);
            float sortX = pad + leftW - 200;
            if (FrameButton(R(sortX, top, 200, 28), SortLabels[sort], false, 15)) sort = (sort + 1) % SortLabels.Length;

            // With card packs installed, a PACK button between the search box and SORT; the search box gives up
            // room for it on narrow screens
            float boxX = pad + labelW + 6, boxW = 240;
            if (packs.Length > 1)
            {
                float packW = Mathf.Min(Measure(packLabels[pack], 15) / u + 24, 300);
                float packX = Mathf.Max(boxX + 100 + 12, sortX - 12 - packW);
                boxW = Mathf.Min(boxW, packX - 12 - boxX);
                if (FrameButton(R(packX, top, sortX - 12 - packX, 28), packLabels[pack], false, 15)) pack = (pack + 1) % packs.Length;
            }

            var box = R(boxX, top, boxW, 28);
            GUI.Box(box, GUIContent.none, frame);
            var prev = BeginText(new Rect(box.x + 6 * u, box.y, box.width - 12 * u, box.height), 15, RedBright, TextAnchor.MiddleLeft, out var field);
            search = GUI.TextField(field, search, text);
            GUI.matrix = prev;
            if (search.Length == 0) Label(new Rect(box.x + 8 * u, box.y, box.width - 16 * u, box.height), "NAME OR SIGIL", 15, RedDim, TextAnchor.MiddleLeft);
        }

        // The hovered card's description, or a hint for the page
        private void DrawFooter(float leftW, float pad)
        {
            Dash(new Rect(0, vh - 84 * u, vw, 2 * u));
            var info = new Rect(pad * u, vh - 76 * u, (leftW + pad) * u, 70 * u);
            text.wordWrap = true;
            if (hovered != null)
            {
                if (!describeCache.TryGetValue(hovered, out var desc))
                {
                    desc = DeckLogic.Describe(hovered);
                    if (packNames.TryGetValue(hovered.name, out var from))
                    {
                        int end = desc.IndexOf('\n');
                        desc = desc.Insert(end < 0 ? desc.Length : end, $"  ({from})");
                    }
                    describeCache[hovered] = desc;
                }
                Label(info, desc, 14, Red, TextAnchor.UpperLeft);
            }
            else if (page == Page.Decks) Label(info, "SAVE YOUR DECK UNDER A NAME, OR SHARE IT: COPY CODE PUTS IT IN YOUR CLIPBOARD, PASTE CODE LOADS ONE.", 14, RedDim, TextAnchor.UpperLeft);
            else if (page == Page.Settings) Label(info, "CHANGES ARE SAVED RIGHT AWAY.", 14, RedDim, TextAnchor.UpperLeft);
            else Label(info, "CLICK A CARD TO ADD IT.  ESC OR X TO GO BACK.", 14, RedDim, TextAnchor.UpperLeft);
            text.wordWrap = false;
        }

        private void DrawGrid(Rect view)
        {
            if (visibleCache == null || visibleFilter != filter || visibleSearch != search || visiblePack != pack || visibleSort != sort)
            {
                visibleCache = DeckLogic.Sort(pool.Where(c => DeckLogic.InPack(c, packs[pack], packNames) && DeckLogic.Matches(c, filter, search, starterNames, searchCache)), DeckLogic.Sorts[sort]).ToList();
                visibleFilter = filter;
                visibleSearch = search;
                visiblePack = pack;
                visibleSort = sort;
            }
            var visible = visibleCache;
            float cardScale = Mathf.Max(1f, Mathf.Floor(u));
            float tw = CardFace.W * cardScale, th = CardFace.H * cardScale, gap = 14f * u;
            int cols = Mathf.Max(1, (int)((view.width - 24 * u) / (tw + gap)));
            int rows = (visible.Count + cols - 1) / cols;
            // Centered in the space left of the scrollbar
            float left = 6 * u + Mathf.Max(0f, (view.width - 20 * u - 12 * u) - (cols * (tw + gap) - gap)) / 2f;
            var content = new Rect(0, 0, view.width - 20 * u, rows * (th + gap) + 10 * u);

            clipView = view;
            poolScroll = GUI.BeginScrollView(view, poolScroll, content, false, true);
            // Only the rows inside the scroll view, plus one on each side for the hover lift
            int firstRow = Mathf.Max(0, Mathf.FloorToInt((poolScroll.y - 8 * u) / (th + gap)) - 1);
            int lastRow = Mathf.CeilToInt((poolScroll.y + view.height) / (th + gap)) + 1;
            for (int i = firstRow * cols; i < visible.Count && i / cols <= lastRow; i++)
            {
                var r = new Rect(left + (i % cols) * (tw + gap), 8 * u + (i / cols) * (th + gap), tw, th);
                DrawTile(r, visible[i]);
            }
            GUI.EndScrollView();
            clipView = null;
        }

        private void DrawTile(Rect r, CardInfo c)
        {
            bool over = Hover(r, Key(Kind.Card, c.name, false, r));
            if (over) hovered = c;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) TryAdd(c);

            if (CardFace.Available)
            {
                var face = over ? new Rect(r.x - 3 * u, r.y - 5 * u, r.width + 6 * u, r.height + 9 * u) : r; // lifted on hover
                try { CardFace.Draw(face, c); }
                catch (Exception e) { CardFace.Report(c, "card face", e); GUI.Box(r, GUIContent.none, frame); }
            }
            else
            {
                GUI.Box(r, GUIContent.none, frame);
                DrawPortrait(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height * 0.5f), c);
                Label(new Rect(r.x, r.y + r.height * 0.55f, r.width, 30 * u), CardFace.Name(c), 13, Red, TextAnchor.MiddleCenter);
            }

            if (copyBadges.TryGetValue(c.name, out var badgeText))
            {
                // In the portrait's top-left corner, clear of the name
                float k = r.width / CardFace.W;
                var badge = new Rect(r.x + 5 * k, r.y + 32 * k, 32 * u, 24 * u);
                GUI.DrawTexture(badge, texRed);
                Label(badge, badgeText, 15, Bg, TextAnchor.MiddleCenter);
            }
        }

        private void TryAdd(CardInfo c)
        {
            string err = DeckLogic.CannotAdd(deck, c);
            if (err != null) { Flash(err); return; }
            deck.Add(c);
            DeckChanged();
            CommandLineTextDisplayer.PlayCommandLineClickSound();
        }

        private void DrawDeckPanel(float lw, float rightW, float pad, float y)
        {
            float rx = lw - rightW - pad;
            var deckBox = R(rx, y, rightW, 0);
            deckBox.height = vh - deckBox.y - 96 * u;
            GUI.Box(deckBox, GUIContent.none, frame);
            Label(R(rx + 14, y + 8, rightW - 28, 28), "YOUR DECK", 22, Red, TextAnchor.MiddleLeft);
            Label(R(rx + 14, y + 8, rightW - 28, 28), deckCountText, 22, deckFull ? RedBright : RedDim, TextAnchor.MiddleRight);
            Dash(R(rx + 14, y + 42, rightW - 28, 2));

            float rowH = 54f;
            var list = R(rx + 8, y + 52, rightW - 16, 0);
            list.height = deckBox.yMax - list.y - 216 * u;
            var content = new Rect(0, 0, list.width - 20 * u, deck.Count * rowH * u);
            clipView = list;
            deckScroll = GUI.BeginScrollView(list, deckScroll, content, false, false);
            int last = Mathf.Min(deck.Count - 1, Mathf.CeilToInt((deckScroll.y + list.height) / (rowH * u)));
            for (int i = Mathf.Max(0, Mathf.FloorToInt(deckScroll.y / (rowH * u))); i <= last; i++)
            {
                var c = deck[i];
                var row = new Rect(0, i * rowH * u, content.width, (rowH - 6) * u);
                bool over = Over(row);
                if (over) GUI.Box(row, GUIContent.none, frameHover);
                var thumb = new Rect(row.x + 4 * u, row.y + 2 * u, 55 * u, 45 * u);
                DrawPortraitBox(thumb, c);
                GUI.Box(thumb, GUIContent.none, frame);
                Label(new Rect(row.x + 68 * u, row.y, 190 * u, row.height), CardFace.Name(c), 15, over ? RedBright : Red, TextAnchor.MiddleLeft);
                if (IconButton(new Rect(row.xMax - 30 * u, row.y + 6 * u, 26 * u, 26 * u), texBack)) { deck.RemoveAt(i); DeckChanged(); break; }
            }
            GUI.EndScrollView();
            clipView = null;
            if (deck.Count == 0) Label(R(rx + 14, y + 60, rightW - 28, 60), "EMPTY.\nPICK CARDS ON THE LEFT.", 14, RedDim, TextAnchor.UpperLeft);

            float bottom = deckBox.yMax / u - 180;
            if (deck.Count > 0) Label(R(rx + 14, bottom - 30, rightW - 28, 26), deckTotals, 15, RedDim, TextAnchor.MiddleLeft);
            var msgRect = R(rx + 14, bottom, rightW - 28, 30);
            if (Time.unscaledTime < messageUntil) Label(msgRect, message, 14, RedBright, TextAnchor.MiddleLeft);
            else if (deckError != null) Label(msgRect, deckError, 14, RedDim, TextAnchor.MiddleLeft);
            else Label(msgRect, "DECK READY.", 14, Red, TextAnchor.MiddleLeft);

            float bw = (rightW - 28 - 16) / 3f;
            if (FrameButton(R(rx + 14, bottom + 36, bw, 32), "RANDOM", false, 15)) Randomize();
            if (FrameButton(R(rx + 22 + bw, bottom + 36, bw, 32), page == Page.Decks ? "CARDS" : "DECKS", false, 15)) page = page == Page.Decks ? Page.Cards : Page.Decks;
            if (FrameButton(R(rx + 30 + bw * 2, bottom + 36, bw, 32), "CLEAR", false, 15)) { deck.Clear(); DeckChanged(); }
            float bw2 = (rightW - 28 - 8) / 2f;
            if (FrameButton(R(rx + 14, bottom + 74, bw2, 32), "CANCEL", false, 15)) Close(save: false);
            if (FrameButton(R(rx + 22 + bw2, bottom + 74, bw2, 32), "SAVE", false, 15)) Close(save: true);
            if (FrameButton(R(rx + 14, bottom + 116, rightW - 28, 44), "START WITH THIS DECK", true, 17))
            {
                if (deckError != null) Flash("Invalid deck: " + deckError);
                else StartWithDeck();
            }
        }

        private void Randomize()
        {
            var picks = DeckLogic.RandomDeck(pool, rng);
            if (picks == null) { Flash("Not enough unlocked cards to randomize."); return; }
            SetDeck(picks);
            page = Page.Cards;
        }

        private void StartWithDeck()
        {
            Close(save: false);
            DeckLogic.StartWithDeck(deck);
        }

        private void DrawSavedDecks(Rect view)
        {
            Label(new Rect(view.x, view.y, 70 * u, 30 * u), "NAME", 15, RedDim, TextAnchor.MiddleLeft);
            var box = new Rect(view.x + 72 * u, view.y, 260 * u, 30 * u);
            GUI.Box(box, GUIContent.none, frame);
            var prev = BeginText(new Rect(box.x + 6 * u, box.y, box.width - 12 * u, box.height), 15, RedBright, TextAnchor.MiddleLeft, out var field);
            deckName = GUI.TextField(field, deckName, 24, text);
            GUI.matrix = prev;
            if (FrameButton(new Rect(box.xMax + 10 * u, view.y, 200 * u, 30 * u), "SAVE CURRENT DECK", false, 15)) SaveCurrentAs();

            Label(new Rect(view.x, view.y + 38 * u, 70 * u, 30 * u), "SHARE", 15, RedDim, TextAnchor.MiddleLeft);
            if (FrameButton(new Rect(view.x + 72 * u, view.y + 38 * u, 160 * u, 30 * u), "COPY CODE", false, 15)) CopyCode();
            if (FrameButton(new Rect(view.x + 242 * u, view.y + 38 * u, 160 * u, 30 * u), "PASTE CODE", false, 15)) PasteCode();

            var list = new Rect(view.x, view.y + 84 * u, view.width, view.height - 84 * u);
            Dash(new Rect(list.x, list.y - 6 * u, list.width - 20 * u, 2 * u));
            if (savedDecks.Count == 0)
            {
                Label(new Rect(list.x, list.y + 10 * u, list.width, 30 * u), "NO SAVED DECKS YET.", 15, RedDim, TextAnchor.MiddleLeft);
                return;
            }

            float rowH = 64f * u;
            var content = new Rect(0, 0, list.width - 20 * u, savedDecks.Count * rowH);
            clipView = list;
            savedScroll = GUI.BeginScrollView(list, savedScroll, content, false, true);
            int last = Mathf.Min(savedDecks.Count - 1, Mathf.CeilToInt((savedScroll.y + list.height) / rowH));
            for (int i = Mathf.Max(0, Mathf.FloorToInt(savedScroll.y / rowH)); i <= last; i++)
            {
                var d = savedDecks[i];
                var row = new Rect(0, i * rowH, content.width, rowH - 8 * u);
                bool over = Over(row);
                if (over) GUI.Box(row, GUIContent.none, frameHover);
                Label(new Rect(row.x + 8 * u, row.y, 220 * u, row.height), Upper(d.Key), 16, over ? RedBright : Red, TextAnchor.MiddleLeft);

                if (!savedCardsCache.TryGetValue(d.Value, out var cards))
                    savedCardsCache[d.Value] = cards = DeckLogic.Resolve(DeckCode.ParseList(d.Value), pool);
                float tx = row.x + 236 * u;
                foreach (var c in cards)
                {
                    var thumb = new Rect(tx, row.y + 4 * u, 59 * u, 48 * u);
                    if (thumb.xMax > row.xMax - 170 * u) break;
                    if (c != null) { DrawPortraitBox(thumb, c); if (Over(thumb)) hovered = c; }
                    GUI.Box(thumb, GUIContent.none, frame);
                    tx += 65 * u;
                }

                if (FrameButton(new Rect(row.xMax - 160 * u, row.y + 12 * u, 100 * u, 32 * u), "LOAD", false, 15)) { LoadSaved(d, cards); break; }
                if (IconButton(new Rect(row.xMax - 44 * u, row.y + 14 * u, 28 * u, 28 * u), texBack))
                {
                    savedDecks.RemoveAt(i);
                    DeckStorage.WriteSavedDecks(savedDecks);
                    Flash($"Deleted '{d.Key}'.");
                    break;
                }
            }
            GUI.EndScrollView();
            clipView = null;
        }

        private string Upper(string s)
        {
            if (!upperCache.TryGetValue(s, out var up)) upperCache[s] = up = s.ToUpperInvariant();
            return up;
        }

        private void SaveCurrentAs()
        {
            string name = deckName.Replace("|", "").Replace("\n", " ").Trim();
            if (name.Length == 0) { Flash("Type a name first."); return; }
            if (deck.Count == 0) { Flash("Deck is empty."); return; }
            var entry = new KeyValuePair<string, string>(name, DeckCode.JoinList(deck.Select(c => c.name)));
            int idx = savedDecks.FindIndex(d => string.Equals(d.Key, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) savedDecks[idx] = entry; else savedDecks.Add(entry);
            DeckStorage.WriteSavedDecks(savedDecks);
            Flash(idx >= 0 ? $"Updated '{name}'." : $"Saved '{name}'.");
        }

        private void LoadSaved(KeyValuePair<string, string> d, List<CardInfo> cards)
        {
            int missing = LoadCards(cards);
            deckName = d.Key;
            Flash(missing > 0 ? $"Loaded '{d.Key}' ({missing} locked or missing card(s) skipped)." : $"Loaded '{d.Key}'.");
        }

        private void CopyCode()
        {
            if (deck.Count == 0) { Flash("Deck is empty."); return; }
            GUIUtility.systemCopyBuffer = DeckCode.Encode(deck.Select(c => c.name));
            Flash("Deck code copied to the clipboard.");
        }

        private void PasteCode()
        {
            var names = DeckCode.Decode(GUIUtility.systemCopyBuffer);
            if (names == null) { Flash("No deck code in the clipboard."); return; }
            int missing = LoadCards(DeckLogic.Resolve(names, pool));
            Flash(missing > 0 ? $"Deck loaded ({missing} locked or missing card(s) skipped)." : "Deck loaded from the code.");
        }

        // Makes the resolved cards the deck and shows it; returns how many were missing (null)
        private int LoadCards(List<CardInfo> cards)
        {
            SetDeck(cards.Where(c => c != null).ToList());
            page = Page.Cards;
            return cards.Count - deck.Count;
        }

        private void DrawSettings(Rect v)
        {
            float y = v.y + 4 * u;
            if (Stepper(v.x, ref y, "MAX CARDS IN DECK", "HOW MANY CARDS YOU CAN PICK. 0 = NO LIMIT.", Plugin.MaxCards.Value, out int maxCards))
                SetRule(Plugin.MaxCards, maxCards, rebuild: false);
            if (Stepper(v.x, ref y, "MAX COPIES OF A CARD", "HOW MANY OF THE SAME CARD YOU CAN PICK. 0 = NO LIMIT.", Plugin.MaxCopies.Value, out int maxCopies))
                SetRule(Plugin.MaxCopies, maxCopies, rebuild: false);
            if (Toggle(v.x, ref y, "ONLY UNLOCKED CARDS", "ONLY CARDS YOU HAVE UNLOCKED IN KAYCEE'S MOD.", Plugin.OnlyUnlocked.Value))
                SetRule(Plugin.OnlyUnlocked, !Plugin.OnlyUnlocked.Value, rebuild: true);
            if (Toggle(v.x, ref y, "BOSS AND SPECIAL CARDS", "BOSS CARDS, TERRAIN AND OTHER CARDS YOU CAN'T NORMALLY DRAFT.", Plugin.AllowSpecial.Value))
                SetRule(Plugin.AllowSpecial, !Plugin.AllowSpecial.Value, rebuild: true);
        }

        // Saves a rule right away; rules that change which cards are allowed also rebuild the pool and drop cards from the deck
        private void SetRule<T>(ConfigEntry<T> entry, T value, bool rebuild)
        {
            entry.Value = value;
            Plugin.Instance.Config.Save();
            if (!rebuild) { DeckChanged(); return; }
            RebuildPool();
            int before = deck.Count;
            SetDeck(deck.Where(pool.Contains).ToList());
            if (deck.Count < before) Flash($"Removed {before - deck.Count} card(s) that are no longer allowed.");
        }

        private bool Stepper(float x, ref float y, string label, string hint, int value, out int result)
        {
            SettingLabel(x, y, label, hint);
            float bx = x + 360 * u;
            result = value;
            if (FrameButton(new Rect(bx, y, 34 * u, 34 * u), "-", false, 24)) result = Mathf.Max(0, value - 1);
            Label(new Rect(bx + 40 * u, y, 130 * u, 34 * u), value > 0 ? value.ToString() : "NO LIMIT", 16, RedBright, TextAnchor.MiddleCenter);
            if (FrameButton(new Rect(bx + 176 * u, y, 34 * u, 34 * u), "+", false, 24)) result = Mathf.Min(99, value + 1);
            y += 76 * u;
            return result != value;
        }

        private bool Toggle(float x, ref float y, string label, string hint, bool value)
        {
            SettingLabel(x, y, label, hint);
            bool clicked = FrameButton(new Rect(x + 360 * u, y, 210 * u, 34 * u), value ? "[X] ON" : "[ ] OFF", false, 16);
            y += 76 * u;
            return clicked;
        }

        private void SettingLabel(float x, float y, string label, string hint)
        {
            Label(new Rect(x, y, 350 * u, 34 * u), label, 17, Red, TextAnchor.MiddleLeft);
            Label(new Rect(x, y + 36 * u, 800 * u, 26 * u), hint, 15, RedDim, TextAnchor.MiddleLeft);
        }
    }
}
