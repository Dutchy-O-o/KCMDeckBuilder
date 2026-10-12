using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using GBC;
using HarmonyLib;
using UnityEngine;

namespace KCMDeckBuilder
{
    // Deck building panel opened from the starter deck screen, styled after Kaycee's Mod's red CRT menus
    internal class DeckBuilderUI : MonoBehaviour
    {
        internal static AscensionMenuScreens Menu;

        private static readonly string[] Filters = { "ALL", "PELTS", "BOSS", "OTHER", "BLOOD", "BONES", "FREE", "RARE" };

        // Kaycee's Mod menu palette
        private static readonly Color Red = new Color(0.80f, 0.16f, 0.19f);
        private static readonly Color RedDim = new Color(0.42f, 0.08f, 0.10f);
        private static readonly Color RedBright = new Color(1f, 0.32f, 0.34f);
        private static readonly Color Bg = new Color(0.075f, 0.06f, 0.07f, 1f);

        private bool open;

        // Hover state, like the game's menu items: click sound on enter, blinking highlight, pointer cursor
        private Vector2 outerMouse;
        private float guiScale = 1f;
        private Rect? clipView;
        private string hoverKey, lastHoverKey;
        private float hoverSince, blinkFrequency = 0.2f;

        // The game's own cursor, drawn on top of the panel with its trail effect
        private List<Sprite> cursorSprites, cursorDownSprites;
        private float cursorScale, trailFrequency = 0.03f, trailTimer, trailBrightness = 0.9f;
        private bool cursorReady, cursorTried;
        private struct Ghost { public Vector2 Pos; public Sprite Sprite; public float Born; }
        private readonly List<Ghost> ghosts = new List<Ghost>();
        private const float GhostLife = 0.115f;
        private bool showDecks;
        private string deckName = "";
        private List<KeyValuePair<string, string>> savedDecks = new List<KeyValuePair<string, string>>();
        private Vector2 savedScroll;
        private readonly System.Random rng = new System.Random();
        private List<CardInfo> pool = new List<CardInfo>();
        private List<CardInfo> deck = new List<CardInfo>();
        private HashSet<string> starterNames = new HashSet<string>();
        private int filter;
        private string search = "";
        private Vector2 poolScroll, deckScroll;
        private string message = "";
        private float messageUntil;
        private CardInfo hovered;

        // Layout: GUI is scaled by an integer factor so pixel art stays crisp; u scales the 720p layout to the virtual screen
        private float vw, vh, u;

        private GUISkin skin;
        private Font pixelFont;
        private GUIStyle text, frame, frameHover, frameFilled, frameFilledHover;
        private Texture2D texBg, texRed, texRedBright, texDash, texScan, texFrame, texFrameHover, texFrameFilled, texFrameFilledHover;
        private Texture texBack, texContinue;

        private bool OnDeckScreen =>
            Menu != null && Menu.isActiveAndEnabled &&
            Menu.CurrentScreen == AscensionMenuScreens.Screen.StarterDeckSelect;

        private void Update()
        {
            if (open && cursorReady)
            {
                Cursor.visible = false;
                trailTimer += Time.unscaledDeltaTime;
                if (trailTimer > trailFrequency)
                {
                    trailTimer = 0f;
                    ghosts.Add(new Ghost { Pos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y), Sprite = CurrentCursorSprite(), Born = Time.unscaledTime });
                }
            }
            if (!open && !OnDeckScreen && forcedPointer) { SetGameCursorPointer(false); lastHoverKey = null; }
            if (open && !OnDeckScreen) Close(save: false);
            if (open && Input.GetKeyDown(KeyCode.Escape)) Close(save: false);
        }

        private void OnGUI()
        {
            if (!OnDeckScreen) return;
            InitStyles();
            GUI.skin = skin;

            float scale = Mathf.Max(1, Mathf.Floor(Screen.height / 700f));
            guiScale = scale;
            vw = Screen.width / scale;
            vh = Screen.height / scale;
            u = vh / 720f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            outerMouse = Event.current.mousePosition;
            clipView = null;
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) hoverKey = null;

            if (!open)
            {
                bool selected = AscensionSaveData.Data.currentStarterDeck == Plugin.CustomDeckId;
                var r = R(vw / u / 2 - 150, 116, 300, 34);
                if (FrameButton(r, selected ? "CUSTOM DECK [SELECTED]" : "BUILD CUSTOM DECK", primary: false, size: 16))
                    Open();
            }
            else DrawPanel();

            if (repaint)
            {
                // Same as the game's menu items: a click sound when the cursor enters something clickable
                if (hoverKey != lastHoverKey)
                {
                    if (hoverKey != null) CommandLineTextDisplayer.PlayCommandLineClickSound();
                    lastHoverKey = hoverKey;
                    hoverSince = Time.unscaledTime;
                    // On the deck screen the game's own cursor is visible: switch it to the pointer over our button
                    if (!open) SetGameCursorPointer(hoverKey != null);
                }
                if (open) DrawGameCursor();
            }
        }

        private bool forcedPointer;
        private void SetGameCursorPointer(bool on)
        {
            var ic = Singleton<InteractionCursor>.Instance;
            if (ic == null || on == forcedPointer) return;
            if (on) ic.ForceCursorType(CursorType.Point);
            else ic.ClearForcedCursorType();
            forcedPointer = on;
        }

        // Mouse over r, and not hidden outside the scroll view it is drawn in
        private bool Over(Rect r) =>
            r.Contains(Event.current.mousePosition) && (!clipView.HasValue || clipView.Value.Contains(outerMouse));

        private bool Hover(Rect r, string id)
        {
            if (!Over(r)) return false;
            if (Event.current.type == EventType.Repaint) hoverKey = id;
            return true;
        }

        private bool BlinkOn(string id) =>
            id == lastHoverKey && ((int)((Time.unscaledTime - hoverSince) / blinkFrequency) % 2) == 0;

        private void InitCursor()
        {
            if (cursorTried) return;
            cursorTried = true;
            try
            {
                var ic = Singleton<InteractionCursor>.Instance;
                var tr = Traverse.Create(ic);
                cursorSprites = tr.Field("cursorTextures").GetValue<List<Sprite>>();
                cursorDownSprites = tr.Field("cursorDownTextures").GetValue<List<Sprite>>();
                var sr = tr.Field("mainCursor").GetValue<SpriteRenderer>();
                var cam = tr.Field("rayCamera").GetValue<Camera>();
                var trail = tr.Field("trailEffect").GetValue<CursorTrailEffect>();
                if (trail != null)
                {
                    if (trail.originalFrequency > 0) trailFrequency = trail.originalFrequency;
                    trailBrightness = Traverse.Create(trail).Field("cloneBrightness").GetValue<float>();
                }
                var blink = Resources.FindObjectsOfTypeAll<AscensionMenuBlinkEffect>().FirstOrDefault(b => b.originalFrequency > 0);
                if (blink != null) blinkFrequency = blink.originalFrequency;

                var sp = cursorSprites != null && cursorSprites.Count > 0 ? cursorSprites[0] : null;
                if (sp != null && sr != null && cam != null && cam.orthographic)
                    cursorScale = sr.transform.lossyScale.x * Screen.height / (2f * cam.orthographicSize * sp.pixelsPerUnit);
                if (cursorScale <= 0.5f || cursorScale > 20f) cursorScale = Mathf.Max(1f, Mathf.Round(Screen.height / 360f));
                cursorReady = sp != null;
                Plugin.Log.LogInfo($"Game cursor: {(cursorReady ? "ok" : "not found")}, scale {cursorScale:0.##}, trail {trailFrequency:0.###}s, blink {blinkFrequency:0.###}s");
            }
            catch (Exception e)
            {
                cursorReady = false;
                Plugin.Log.LogWarning("Game cursor unavailable, using the system cursor: " + e.Message);
            }
        }

        private Sprite CurrentCursorSprite()
        {
            // Point (hand) over clickable things, like the game's menus; "down" variant while pressed
            int index = Mathf.Min(cursorSprites.Count - 1, lastHoverKey != null ? (int)CursorType.Point : (int)CursorType.Default);
            var list = Input.GetMouseButton(0) && cursorDownSprites != null && cursorDownSprites.Count > index ? cursorDownSprites : cursorSprites;
            return list[index];
        }

        private void DrawSprite(Sprite sp, Vector2 mouse, Color color)
        {
            var tex = sp.texture;
            var tr = sp.textureRect;
            var rect = new Rect(mouse.x - sp.pivot.x * cursorScale, mouse.y - (tr.height - sp.pivot.y) * cursorScale,
                tr.width * cursorScale, tr.height * cursorScale);
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
            GUI.color = prev;
        }

        private void DrawGameCursor()
        {
            if (!cursorReady) return;
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float now = Time.unscaledTime;
            ghosts.RemoveAll(g => now - g.Born > GhostLife);
            // Trail: faded copies that fade out quickly, like CursorTrailEffect
            foreach (var g in ghosts)
            {
                float a = 0.5f * (1f - (now - g.Born) / GhostLife);
                DrawSprite(g.Sprite, g.Pos, new Color(trailBrightness, trailBrightness, trailBrightness, a));
            }
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            DrawSprite(CurrentCursorSprite(), mouse, Color.white);
            GUI.matrix = prevMatrix;
        }

        // Rect in 720p layout units
        private Rect R(float x, float y, float w, float h) => new Rect(x * u, y * u, w * u, h * u);
        private int F(int size) => Mathf.Max(8, Mathf.RoundToInt(size * u));

        private void Open()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Plugin.Instance.Config.Reload();
            starterNames = Plugin.StarterCardNames();
            pool = Plugin.CardPool()
                .OrderBy(c => Plugin.IsPelt(c) ? 0 : Plugin.IsBossCard(c) || Plugin.IsOtherCard(c, starterNames) ? 5
                    : c.BloodCost > 0 ? 1 : c.BonesCost > 0 ? 2 : 3)
                .ThenBy(c => c.BloodCost + c.BonesCost).ThenBy(c => c.DisplayedNameEnglish).ToList();
            deck = Plugin.ParseDeck(Plugin.Deck.Value)
                .Select(n => pool.Find(c => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase)))
                .Where(c => c != null).ToList();
            savedDecks = Plugin.LoadSavedDecks();
            // Written here rather than at the menu: by now the deck screen has loaded the starter decks, so this is cheap
            Plugin.WriteCardList();
            showDecks = false;
            open = true;
            Plugin.Log.LogInfo($"Deck builder opened in {sw.Elapsed.TotalMilliseconds:0.0} ms ({pool.Count} cards).");
            CommandLineTextDisplayer.PlayCommandLineClickSound();
            SetInteraction(disabled: true);
        }

        private void Close(bool save)
        {
            if (save)
            {
                Plugin.Deck.Value = string.Join(", ", deck.Select(c => c.name).ToArray());
                Plugin.Instance.Config.Save();
                Plugin.Log.LogInfo($"Deck saved: {Plugin.Deck.Value}");
            }
            open = false;
            hovered = null;
            SetInteraction(disabled: false);
        }

        // Same flow as clicking one of the game's deck icons: select the deck, go to the challenge screen
        private void StartWithDeck()
        {
            Close(save: true);
            AscensionSaveData.Data.currentStarterDeck = Plugin.CustomDeckId;
            Plugin.Log.LogInfo("Custom deck selected.");
            Menu.SwitchToScreen(AscensionMenuScreens.Screen.SelectChallenges);
        }

        private void SetInteraction(bool disabled)
        {
            var cursor = Singleton<InteractionCursor>.Instance;
            if (cursor == null) return;
            cursor.InteractionDisabled = disabled;
            // The game's cursor ends up behind the panel, so we draw a copy of it on top;
            // the system cursor is only a fallback if that isn't possible
            SetGameCursorPointer(false);
            if (disabled) InitCursor();
            bool osCursor = disabled && !cursorReady;
            cursor.OSCursorShown = osCursor;
            Cursor.visible = osCursor;
            ghosts.Clear();
            lastHoverKey = hoverKey = null;
        }

        private void Flash(string msg)
        {
            message = msg;
            messageUntil = Time.unscaledTime + 2.5f;
        }

        private void DrawPanel()
        {
            float lw = vw / u; // layout width in 720p units
            const float pad = 24f;
            GUI.DrawTexture(new Rect(0, 0, vw, vh), texBg);

            // Header, like the game's "SELECT STARTER DECK" screen
            Dash(R(0, 12, lw, 2));
            if (IconButton(R(pad + 6, 24, 36, 36), texBack)) Close(save: false);
            Label(R(0, 22, lw, 40), "BUILD YOUR DECK", 30, Red, TextAnchor.MiddleCenter);
            Dash(R(0, 70, lw, 2));

            // --- Left: card pool ---
            float rightW = 330f;
            float leftW = lw - rightW - pad * 3;
            float y = 84;

            float fx = pad;
            for (int i = 0; i < Filters.Length; i++)
            {
                float w = Mathf.Max(60f, Measure(Filters[i], 16) / u + 24);
                if (TabButton(R(fx, y, w, 28), Filters[i], !showDecks && i == filter)) { filter = i; showDecks = false; }
                fx += w + 8;
            }
            float sw = Measure("SEARCH", 15) / u + 10;
            float sx = pad + leftW - 170;
            Label(R(sx - sw - 6, y, sw, 28), "SEARCH", 15, RedDim, TextAnchor.MiddleLeft);
            var sbox = R(sx, y, 170, 28);
            GUI.Box(sbox, GUIContent.none, frame);
            var prevM = BeginText(new Rect(sbox.x + 6 * u, sbox.y, sbox.width - 12 * u, sbox.height), 15, RedBright, TextAnchor.MiddleLeft, out var sr);
            search = GUI.TextField(sr, search, text);
            GUI.matrix = prevM;

            var view = R(pad, y + 40, leftW, 0);
            view.height = vh - view.y - 92 * u;
            hovered = null;
            if (showDecks) DrawSavedDecks(view);
            else DrawGrid(view);

            // Footer: hovered card description
            Dash(new Rect(0, vh - 84 * u, vw, 2 * u));
            var info = new Rect(pad * u, vh - 76 * u, (leftW + pad) * u, 70 * u);
            text.wordWrap = true;
            if (hovered != null) Label(info, Describe(hovered), 14, Red, TextAnchor.UpperLeft);
            else if (showDecks) Label(info, "TYPE A NAME AND SAVE YOUR DECK, OR LOAD ONE YOU SAVED BEFORE.", 14, RedDim, TextAnchor.UpperLeft);
            else Label(info, "CLICK A CARD TO ADD IT.  ESC OR X TO GO BACK.", 14, RedDim, TextAnchor.UpperLeft);
            text.wordWrap = false;

            DrawDeckPanel(lw, rightW, pad, y);

            // CRT scanlines over everything
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, vw, vh), texScan, new Rect(0, 0, 1, vh / 3f));
        }

        private void DrawGrid(Rect view)
        {
            var visible = pool.Where(Matches).ToList();
            float cardScale = Mathf.Max(1f, Mathf.Floor(u));
            float tw = CardFace.W * cardScale, th = CardFace.H * cardScale, gap = 14f * u;
            int cols = Mathf.Max(1, (int)((view.width - 24 * u) / (tw + gap)));
            int rows = (visible.Count + cols - 1) / cols;
            var content = new Rect(0, 0, view.width - 20 * u, rows * (th + gap) + 10 * u);

            clipView = view;
            poolScroll = GUI.BeginScrollView(view, poolScroll, content, false, true);
            for (int i = 0; i < visible.Count; i++)
            {
                var r = new Rect(6 * u + (i % cols) * (tw + gap), 8 * u + (i / cols) * (th + gap), tw, th);
                DrawTile(r, visible[i]);
            }
            GUI.EndScrollView();
            clipView = null;
        }

        private void DrawDeckPanel(float lw, float rightW, float pad, float y)
        {
            float rx = lw - rightW - pad;
            var deckBox = R(rx, y, rightW, 0);
            deckBox.height = vh - deckBox.y - 96 * u;
            GUI.Box(deckBox, GUIContent.none, frame);
            Label(R(rx + 14, y + 8, rightW - 28, 28), "YOUR DECK", 22, Red, TextAnchor.MiddleLeft);
            int max = Plugin.MaxCards.Value;
            Label(R(rx + 14, y + 8, rightW - 28, 28), max > 0 ? $"{deck.Count}/{max}" : deck.Count.ToString(), 22,
                max > 0 && deck.Count >= max ? RedBright : RedDim, TextAnchor.MiddleRight);
            Dash(R(rx + 14, y + 42, rightW - 28, 2));

            float rowH = 56f;
            var dview = R(rx + 8, y + 52, rightW - 16, 0);
            dview.height = deckBox.yMax - dview.y - 190 * u;
            var dcontent = new Rect(0, 0, dview.width - 20 * u, deck.Count * rowH * u);
            clipView = dview;
            deckScroll = GUI.BeginScrollView(dview, deckScroll, dcontent, false, false);
            for (int i = 0; i < deck.Count; i++)
            {
                var c = deck[i];
                var row = new Rect(0, i * rowH * u, dview.width - 20 * u, (rowH - 6) * u);
                bool over = Over(row);
                if (over) GUI.Box(row, GUIContent.none, frameHover);
                var thumb = new Rect(row.x + 4 * u, row.y + 2 * u, 55 * u, 45 * u);
                CardFace.DrawPortraitBox(thumb, c);
                GUI.Box(thumb, GUIContent.none, frame);
                Label(new Rect(row.x + 68 * u, row.y, 190 * u, row.height), c.DisplayedNameEnglish.ToUpperInvariant(), 15, over ? RedBright : Red, TextAnchor.MiddleLeft);
                if (IconButton(new Rect(row.xMax - 30 * u, row.y + 6 * u, 26 * u, 26 * u), texBack)) { deck.RemoveAt(i); break; }
            }
            GUI.EndScrollView();
            clipView = null;
            if (deck.Count == 0) Label(R(rx + 14, y + 60, rightW - 28, 60), "EMPTY.\nPICK CARDS ON THE LEFT.", 14, RedDim, TextAnchor.UpperLeft);

            float bottom = deckBox.yMax / u - 180;
            string err = Plugin.Validate(deck);
            var msgRect = R(rx + 14, bottom, rightW - 28, 30);
            if (Time.unscaledTime < messageUntil) Label(msgRect, message.ToUpperInvariant(), 14, RedBright, TextAnchor.MiddleLeft);
            else if (err != null) Label(msgRect, err.ToUpperInvariant(), 14, RedDim, TextAnchor.MiddleLeft);
            else Label(msgRect, "DECK READY.", 14, Red, TextAnchor.MiddleLeft);

            float bw = (rightW - 28 - 16) / 3f;
            if (FrameButton(R(rx + 14, bottom + 36, bw, 32), "RANDOM", false, 15)) Randomize();
            if (FrameButton(R(rx + 22 + bw, bottom + 36, bw, 32), showDecks ? "CARDS" : "DECKS", false, 15)) showDecks = !showDecks;
            if (FrameButton(R(rx + 30 + bw * 2, bottom + 36, bw, 32), "CLEAR", false, 15)) deck.Clear();
            float bw2 = (rightW - 28 - 8) / 2f;
            if (FrameButton(R(rx + 14, bottom + 74, bw2, 32), "CANCEL", false, 15)) Close(save: false);
            if (FrameButton(R(rx + 22 + bw2, bottom + 74, bw2, 32), "SAVE", false, 15)) Close(save: true);
            var start = R(rx + 14, bottom + 116, rightW - 28, 44);
            if (FrameButton(start, "START WITH THIS DECK", true, 17))
            {
                if (err != null) Flash("Invalid deck: " + err);
                else StartWithDeck();
            }
        }

        // 3 random regular cards (at least one costs exactly 1 blood) + 2 Rabbit Pelts
        private void Randomize()
        {
            var regular = pool.Where(Plugin.IsRegularChoice).ToList();
            var oneBlood = regular.Where(c => c.BloodCost == 1).ToList();
            if (oneBlood.Count == 0 || regular.Select(c => c.name).Distinct().Count() < 3)
            {
                Flash("Not enough unlocked cards to randomize.");
                return;
            }
            var picks = new List<CardInfo> { oneBlood[rng.Next(oneBlood.Count)] };
            while (picks.Count < 3)
            {
                var c = regular[rng.Next(regular.Count)];
                if (!picks.Exists(p => p.name == c.name)) picks.Add(c);
            }
            picks = picks.OrderBy(_ => rng.Next()).ToList();
            var hare = pool.Find(c => c.name == "PeltHare");
            if (hare != null) { picks.Add(hare); picks.Add(hare); }
            deck = picks;
            showDecks = false;
        }

        private void DrawSavedDecks(Rect view)
        {
            // Name field + save button
            var nameLabel = new Rect(view.x, view.y, 70 * u, 30 * u);
            Label(nameLabel, "NAME", 15, RedDim, TextAnchor.MiddleLeft);
            var box = new Rect(view.x + 72 * u, view.y, 260 * u, 30 * u);
            GUI.Box(box, GUIContent.none, frame);
            var prevM = BeginText(new Rect(box.x + 6 * u, box.y, box.width - 12 * u, box.height), 15, RedBright, TextAnchor.MiddleLeft, out var nr);
            deckName = GUI.TextField(nr, deckName, 24, text);
            GUI.matrix = prevM;
            if (FrameButton(new Rect(box.xMax + 10 * u, view.y, 200 * u, 30 * u), "SAVE CURRENT DECK", false, 15)) SaveCurrentAs();

            var list = new Rect(view.x, view.y + 44 * u, view.width, view.height - 44 * u);
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
            for (int i = 0; i < savedDecks.Count; i++)
            {
                var d = savedDecks[i];
                var row = new Rect(0, i * rowH, content.width, rowH - 8 * u);
                bool over = Over(row);
                if (over) GUI.Box(row, GUIContent.none, frameHover);
                Label(new Rect(row.x + 8 * u, row.y, 220 * u, row.height), d.Key.ToUpperInvariant(), 16, over ? RedBright : Red, TextAnchor.MiddleLeft);

                var cards = Plugin.ParseDeck(d.Value).Select(n => pool.Find(c => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase))).ToList();
                float tx = row.x + 236 * u;
                foreach (var c in cards)
                {
                    var thumb = new Rect(tx, row.y + 4 * u, 59 * u, 48 * u);
                    if (thumb.xMax > row.xMax - 170 * u) break;
                    if (c != null) { CardFace.DrawPortraitBox(thumb, c); if (Over(thumb)) hovered = c; }
                    GUI.Box(thumb, GUIContent.none, frame);
                    tx += 65 * u;
                }

                if (FrameButton(new Rect(row.xMax - 160 * u, row.y + 12 * u, 100 * u, 32 * u), "LOAD", false, 15)) { LoadSaved(d, cards); break; }
                if (IconButton(new Rect(row.xMax - 44 * u, row.y + 14 * u, 28 * u, 28 * u), texBack))
                {
                    savedDecks.RemoveAt(i);
                    Plugin.WriteSavedDecks(savedDecks);
                    Flash($"Deleted '{d.Key}'.");
                    break;
                }
            }
            GUI.EndScrollView();
            clipView = null;
        }

        private void SaveCurrentAs()
        {
            string name = deckName.Replace("|", "").Replace("\n", " ").Trim();
            if (name.Length == 0) { Flash("Type a name first."); return; }
            if (deck.Count == 0) { Flash("Deck is empty."); return; }
            var entry = new KeyValuePair<string, string>(name, string.Join(", ", deck.Select(c => c.name).ToArray()));
            int idx = savedDecks.FindIndex(d => string.Equals(d.Key, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) savedDecks[idx] = entry; else savedDecks.Add(entry);
            Plugin.WriteSavedDecks(savedDecks);
            Flash(idx >= 0 ? $"Updated '{name}'." : $"Saved '{name}'.");
        }

        private void LoadSaved(KeyValuePair<string, string> d, List<CardInfo> cards)
        {
            deck = cards.Where(c => c != null).ToList();
            deckName = d.Key;
            int missing = cards.Count(c => c == null);
            Flash(missing > 0 ? $"Loaded '{d.Key}' ({missing} locked or missing card(s) skipped)." : $"Loaded '{d.Key}'.");
            showDecks = false;
        }

        private void DrawTile(Rect r, CardInfo c)
        {
            bool over = Hover(r, "card:" + c.name + GUIUtility.GUIToScreenPoint(r.position));
            if (over) hovered = c;
            int inDeck = deck.Count(d => d.name == c.name);

            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) TryAdd(c);

            if (CardFace.Available)
            {
                var face = over ? new Rect(r.x - 3 * u, r.y - 5 * u, r.width + 6 * u, r.height + 9 * u) : r; // slight lift on hover
                CardFace.Draw(face, c);
            }
            else
            {
                GUI.Box(r, GUIContent.none, frame);
                DrawPortrait(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height * 0.5f), c);
                Label(new Rect(r.x, r.y + r.height * 0.55f, r.width, 30 * u), c.DisplayedNameEnglish.ToUpperInvariant(), 13, Red, TextAnchor.MiddleCenter);
            }
            if (inDeck > 0)
            {
                var b = new Rect(r.xMax - 26 * u, r.y - 8 * u, 32 * u, 24 * u);
                GUI.DrawTexture(b, texRed);
                Label(b, "x" + inDeck, 15, Bg, TextAnchor.MiddleCenter);
            }
        }

        private void TryAdd(CardInfo c)
        {
            var test = new List<CardInfo>(deck) { c };
            if (Plugin.MaxCards.Value > 0 && test.Count > Plugin.MaxCards.Value) { Flash($"At most {Plugin.MaxCards.Value} cards."); return; }
            if (Plugin.MaxCopies.Value > 0 && test.Count(d => d.name == c.name) > Plugin.MaxCopies.Value) { Flash($"At most {Plugin.MaxCopies.Value} copies of a card."); return; }
            deck.Add(c);
            CommandLineTextDisplayer.PlayCommandLineClickSound();
        }

        private bool Matches(CardInfo c)
        {
            if (search.Length > 0 &&
                c.DisplayedNameEnglish.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                c.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            switch (filter)
            {
                case 1: return Plugin.IsPelt(c);
                case 2: return Plugin.IsBossCard(c);
                case 3: return Plugin.IsOtherCard(c, starterNames);
                case 4: return c.BloodCost > 0;
                case 5: return c.BonesCost > 0;
                case 6: return c.BloodCost == 0 && c.BonesCost == 0;
                case 7: return Plugin.IsRare(c);
                default: return true;
            }
        }

        private static string Cost(CardInfo c)
        {
            if (c.BloodCost > 0) return $"{c.BloodCost} blood";
            if (c.BonesCost > 0) return $"{c.BonesCost} bones";
            return "free";
        }

        private static string AbilityName(Ability a)
        {
            try { return AbilitiesUtil.GetInfo(a)?.rulebookName ?? a.ToString(); }
            catch { return a.ToString(); }
        }

        private static string Describe(CardInfo c)
        {
            string s = $"{c.DisplayedNameEnglish.ToUpperInvariant()} - {Cost(c)}, {c.Attack}/{c.Health}";
            foreach (var a in c.Abilities)
            {
                string d;
                try { d = AbilitiesUtil.GetInfo(a)?.LocalizedRulebookDescription ?? ""; } catch { d = ""; }
                s += $"\n{AbilityName(a).ToUpperInvariant()}: {d.Replace("[creature]", c.DisplayedNameEnglish)}";
            }
            return s;
        }

        // ---- drawing helpers ----

        // The menu font is a bitmap font, which ignores fontSize; scale it up by a whole factor instead
        private int TextScale(int size)
        {
            if (pixelFont == null || pixelFont.dynamic) return 1;
            int native = pixelFont.lineHeight > 0 ? pixelFont.lineHeight : 10;
            return Mathf.Max(1, Mathf.RoundToInt(F(size) / (float)native));
        }

        // Sets up the text style; for bitmap fonts scales GUI.matrix around r and returns the rect to draw in.
        // Restore GUI.matrix with the returned previous matrix afterwards.
        private Matrix4x4 BeginText(Rect r, int size, Color color, TextAnchor anchor, out Rect inner)
        {
            var prev = GUI.matrix;
            int m = TextScale(size);
            text.fontSize = m == 1 ? F(size) : 0;
            text.normal.textColor = color;
            text.alignment = anchor;
            inner = r;
            if (m > 1)
            {
                GUIUtility.ScaleAroundPivot(new Vector2(m, m), r.position);
                inner = new Rect(r.x, r.y, r.width / m, r.height / m);
            }
            return prev;
        }

        private void Label(Rect r, string s, int size, Color color, TextAnchor anchor)
        {
            // Scaled text isn't clipped by scroll views, so skip labels that stick out of the visible area
            if (clipView.HasValue)
            {
                float top = GUIUtility.GUIToScreenPoint(r.position).y / guiScale;
                float bottom = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.yMax)).y / guiScale;
                if (top < clipView.Value.y - 1f || bottom > clipView.Value.yMax + 1f) return;
            }
            var prev = BeginText(r, size, color, anchor, out var inner);
            GUI.Label(inner, s, text);
            GUI.matrix = prev;
        }

        private float Measure(string s, int size)
        {
            int m = TextScale(size);
            text.fontSize = m == 1 ? F(size) : 0;
            return text.CalcSize(new GUIContent(s)).x * m;
        }

        private void Dash(Rect r)
        {
            // Tiled dashed line like the game's menu separators
            GUI.DrawTextureWithTexCoords(r, texDash, new Rect(0, 0, r.width / (8f * u), 1));
        }

        private bool FrameButton(Rect r, string label, bool primary, int size)
        {
            string id = "btn:" + label + GUIUtility.GUIToScreenPoint(r.position);
            bool over = Hover(r, id);
            bool lit = over && BlinkOn(id); // blinking highlight, like the game's menu items
            var style = primary ? (lit ? frameFilledHover : frameFilled) : (lit ? frameFilledHover : over ? frameHover : frame);
            bool clicked = GUI.Button(r, GUIContent.none, style);
            Label(r, label, size, primary || lit ? Bg : (over ? RedBright : Red), TextAnchor.MiddleCenter);
            if (primary && texContinue != null)
            {
                var prev = GUI.color; GUI.color = Bg;
                GUI.DrawTexture(new Rect(r.xMax - r.height * 0.8f, r.y + r.height * 0.2f, r.height * 0.6f, r.height * 0.6f), texContinue);
                GUI.color = prev;
            }
            if (clicked) CommandLineTextDisplayer.PlayCommandLineClickSound();
            return clicked;
        }

        private bool TabButton(Rect r, string label, bool active)
        {
            string id = "tab:" + label;
            bool over = Hover(r, id);
            bool lit = over && !active && BlinkOn(id);
            if (active || lit) GUI.DrawTexture(r, lit ? texRedBright : texRed);
            bool clicked = GUI.Button(r, GUIContent.none, active || lit ? GUIStyle.none : (over ? frameHover : GUIStyle.none));
            Label(r, label, 16, active || lit ? Bg : (over ? RedBright : Red), TextAnchor.MiddleCenter);
            if (clicked) CommandLineTextDisplayer.PlayCommandLineClickSound();
            return clicked;
        }

        private bool IconButton(Rect r, Texture icon)
        {
            string id = "icon:" + GUIUtility.GUIToScreenPoint(r.position);
            bool over = Hover(r, id);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            var prev = GUI.color;
            GUI.color = over && BlinkOn(id) ? Color.white : over ? RedBright : Red;
            if (icon != null) GUI.DrawTexture(r, icon);
            else Label(r, "X", 20, GUI.color, TextAnchor.MiddleCenter);
            GUI.color = prev;
            if (clicked) CommandLineTextDisplayer.PlayCommandLineClickSound();
            return clicked;
        }

        private static void DrawPortrait(Rect r, CardInfo c)
        {
            var sp = c.portraitTex != null ? c.portraitTex : c.pixelPortrait;
            if (sp == null || sp.texture == null) return;
            var t = sp.textureRect;
            var tex = sp.texture;
            GUI.DrawTextureWithTexCoords(Fit(r, t.width / t.height), tex,
                new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
        }

        // Largest rect with the given aspect that fits centered inside r
        private static Rect Fit(Rect r, float aspect)
        {
            var dst = r;
            if (r.width / r.height > aspect) { dst.width = r.height * aspect; dst.x += (r.width - dst.width) / 2; }
            else { dst.height = r.width / aspect; dst.y += (r.height - dst.height) / 2; }
            return dst;
        }

        // ---- styles ----

        private static Texture2D Tex(int w, int h, Func<int, int, Color> px, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = wrap, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) t.SetPixel(x, y, px(x, y));
            t.Apply();
            return t;
        }

        // 3x3 nine-slice: 1px border, filled or transparent center
        private static Texture2D FrameTex(Color border, Color fill) =>
            Tex(3, 3, (x, y) => x == 1 && y == 1 ? fill : border);

        private static GUIStyle FrameStyle(Texture2D t) =>
            new GUIStyle { normal = { background = t }, border = new RectOffset(1, 1, 1, 1) };

        private static Font FindPixelFont()
        {
            // Use whatever font the menu's own PixelText labels use
            try
            {
                foreach (var pt in Menu.GetComponentsInChildren<PixelText>(true))
                {
                    var f = Traverse.Create(pt).Field("defaultFont").GetValue<Font>();
                    if (f != null) return f;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pixel font lookup failed: " + e.Message); }
            return Resources.Load<Font>("fonts/pixel art fonts/marksman crisp/marksman (crisp)");
        }

        private void InitStyles()
        {
            if (skin != null && texBg != null) return;
            var clear = new Color(0, 0, 0, 0);
            texBg = Tex(1, 1, (x, y) => Bg);
            texRed = Tex(1, 1, (x, y) => Red);
            texRedBright = Tex(1, 1, (x, y) => RedBright);
            texDash = Tex(8, 1, (x, y) => x < 5 ? Red : clear, TextureWrapMode.Repeat);
            texScan = Tex(1, 3, (x, y) => y == 0 ? new Color(0, 0, 0, 0.22f) : clear, TextureWrapMode.Repeat);
            texFrame = FrameTex(Red, clear);
            texFrameHover = FrameTex(RedBright, new Color(Red.r, Red.g, Red.b, 0.15f));
            texFrameFilled = FrameTex(Red, Red);
            texFrameFilledHover = FrameTex(RedBright, RedBright);
            frame = FrameStyle(texFrame);
            frameHover = FrameStyle(texFrameHover);
            frameFilled = FrameStyle(texFrameFilled);
            frameFilledHover = FrameStyle(texFrameFilledHover);

            texBack = Resources.Load<Texture>("art/ui/ascension/ascensionmenu_back");
            texContinue = Resources.Load<Texture>("art/ui/ascension/ascensionmenu_continue");

            pixelFont = FindPixelFont();
            Plugin.Log.LogInfo("Menu font: " + (pixelFont != null ? $"{pixelFont.name} (dynamic={pixelFont.dynamic}, size={pixelFont.fontSize}, lineHeight={pixelFont.lineHeight})" : "default"));

            skin = Instantiate(GUI.skin);
            if (pixelFont != null) skin.font = pixelFont;
            // Red scrollbar
            skin.verticalScrollbar = new GUIStyle { normal = { background = Tex(1, 1, (x, y) => new Color(Red.r, Red.g, Red.b, 0.15f)) }, fixedWidth = 8, margin = new RectOffset(6, 0, 0, 0) };
            skin.verticalScrollbarThumb = new GUIStyle { normal = { background = texRed }, fixedWidth = 8 };
            skin.verticalScrollbarUpButton = GUIStyle.none;
            skin.verticalScrollbarDownButton = GUIStyle.none;
            skin.settings.cursorColor = Red;
            skin.settings.selectionColor = new Color(Red.r, Red.g, Red.b, 0.4f);

            text = new GUIStyle { font = pixelFont, richText = false, wordWrap = false, clipping = TextClipping.Clip };
        }
    }

    [HarmonyPatch(typeof(AscensionMenuScreens), "Start")]
    internal static class MenuTrackPatch
    {
        private static void Postfix(AscensionMenuScreens __instance) => DeckBuilderUI.Menu = __instance;
    }
}
