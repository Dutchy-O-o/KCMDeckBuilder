using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using UnityEngine;

namespace KCMDeckBuilder
{
    // The ITEMS and TOTEM pages: the items and the totem the custom deck run starts with
    internal partial class DeckBuilderUI
    {
        private List<string> startItems = new List<string>(); // in slot order; the same item can be picked more than once
        private Tribe totemTribe = Tribe.None;
        private AbilityInfo totemSigil;
        private List<ConsumableItemData> itemChoices = new List<ConsumableItemData>();
        private List<AbilityInfo> sigilChoices = new List<AbilityInfo>();
        private List<AbilityInfo> visibleSigils;
        private string sigilSearch = "", visibleSigilSearch;
        private Vector2 sigilScroll;
        private string extrasText; // the summary under the deck, e.g. "3 ITEMS   TOTEM: BIRD + AIRBORNE" (logs, saved decks)
        private string itemsLine, totemLine; // the same, one line each for the deck panel
        private CardTip hoverTip; // a tip for something that isn't a card (item, tribe, sigil)
        private readonly Dictionary<object, CardTip> extraTipCache = new Dictionary<object, CardTip>();
        private readonly Dictionary<string, string> itemBadges = new Dictionary<string, string>(); // item ID -> "x2"

        private string TotemValue => totemTribe != Tribe.None && totemSigil != null ? RunStart.TotemText(totemTribe, totemSigil) : null;

        private DeckSpec CurrentSpec() =>
            new DeckSpec { Cards = deck.Select(c => c.name).ToList(), Items = startItems.ToList(), Totem = TotemValue };

        // Item slots the run will have with the challenges picked last time (Less Consumables takes slots away)
        private static int ItemSlots()
        {
            try { return Mathf.Max(0, RunStart.MaxItems - AscensionSaveData.Data.GetNumChallengesOfTypeActive(AscensionChallenge.LessConsumables)); }
            catch { return RunStart.MaxItems; }
        }

        // The choices under the current rules; picks that are no longer allowed are dropped
        private void RebuildExtras()
        {
            LoadItems();
            sigilChoices = RunStart.TotemSigils(pool);
            visibleSigils = null;
            extraTipCache.Clear();
            if (totemSigil != null && !sigilChoices.Contains(totemSigil)) totemSigil = null;
            ExtrasChanged();
        }

        // Lists the items under the current rules and drops picks that are no longer allowed
        private void LoadItems()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            itemChoices = RunStart.Items();
            int before = startItems.Count;
            startItems.RemoveAll(n => !itemChoices.Exists(i => i.name == n));
            if (startItems.Count < before) Flash($"Removed {before - startItems.Count} item(s) that aren't available.");
            ExtrasChanged();
            Plugin.Log.LogDebug($"Items listed in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
        }

        // Sets the items and totem from saved text; returns how many of them aren't available here
        private int SetExtras(List<string> items, string totem)
        {
            startItems = items.Where(n => itemChoices.Exists(i => i.name == n)).Take(RunStart.MaxItems).ToList();
            int missing = items.Count - startItems.Count;
            totemTribe = Tribe.None;
            totemSigil = null;
            if (!string.IsNullOrEmpty(totem))
            {
                if (RunStart.ParseTotem(totem, out var t, out var s) && sigilChoices.Contains(s)) { totemTribe = t; totemSigil = s; }
                else missing++;
            }
            ExtrasChanged();
            return missing;
        }

        private void ExtrasChanged()
        {
            itemBadges.Clear();
            foreach (var g in startItems.GroupBy(n => n)) itemBadges[g.Key] = "x" + g.Count();
            var parts = new List<string>();
            if (startItems.Count > 0) parts.Add(startItems.Count == 1 ? "1 ITEM" : startItems.Count + " ITEMS");
            if (TotemValue != null) parts.Add($"TOTEM: {RunStart.TribeName(totemTribe).ToUpperInvariant()} + {DeckLogic.AbilityName(totemSigil.ability).ToUpperInvariant()}");
            extrasText = parts.Count > 0 ? string.Join("   ", parts.ToArray()) : null;
            itemsLine = startItems.Count > 0 ? parts[0] : null;
            totemLine = TotemValue != null ? parts[parts.Count - 1] : null;
        }

        private void SetHoverTip(CardTip tip, Rect r)
        {
            hoverTip = tip;
            var p = GUIUtility.GUIToScreenPoint(r.position) / guiScale;
            hoveredRect = new Rect(p.x, p.y, r.width, r.height);
        }

        private CardTip ItemTip(ConsumableItemData i)
        {
            if (!extraTipCache.TryGetValue(i, out var tip))
            {
                string mod = CardRules.ModOfAsset(i.name);
                extraTipCache[i] = tip = new CardTip { header = RunStart.ItemName(i), pack = mod != null ? "FROM " + mod.ToUpperInvariant() : "", sigils = new List<KeyValuePair<Texture, string>> { new KeyValuePair<Texture, string>(null, RunStart.ItemText(i)) } };
            }
            return tip;
        }

        private CardTip SigilTip(AbilityInfo a)
        {
            if (!extraTipCache.TryGetValue(a, out var tip))
            {
                string d;
                try { d = a.LocalizedRulebookDescription ?? ""; } catch { d = ""; }
                extraTipCache[a] = tip = new CardTip
                {
                    header = DeckLogic.AbilityName(a.ability).ToUpperInvariant(),
                    pack = CardRules.ModOfSigil(a.ability) is string mod ? "FROM " + mod.ToUpperInvariant() : "",
                    sigils = new List<KeyValuePair<Texture, string>> { new KeyValuePair<Texture, string>(CardFace.Sigil(a.ability), d.Replace("[creature]", "The card")) },
                };
            }
            return tip;
        }


        // Card tiles at the card grid's size, in a scroll view; tile draws one entry and returns its click (see PlainCard)
        private void TileGrid<T>(Rect view, List<T> entries, ref Vector2 scroll, Func<Rect, T, int> tile, Action<T, int> onClick, bool scrollbar = true, float size = 1f)
        {
            float cardScale = Mathf.Max(1f, Mathf.Floor(u)) * size;
            float tw = CardFace.W * cardScale, th = CardFace.H * cardScale, gap = 14f * u;
            int cols = Mathf.Max(1, (int)((view.width - 24 * u) / (tw + gap)));
            int rows = (entries.Count + cols - 1) / cols;
            float left = 6 * u + Mathf.Max(0f, (view.width - 20 * u - 12 * u) - (cols * (tw + gap) - gap)) / 2f;
            var content = new Rect(0, 0, view.width - 20 * u, rows * (th + gap) + 10 * u);
            clipView = view;
            scroll = GUI.BeginScrollView(view, scroll, content, false, scrollbar);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt((scroll.y - 8 * u) / (th + gap)) - 1);
            int lastRow = Mathf.CeilToInt((scroll.y + view.height) / (th + gap)) + 1;
            T clicked = default;
            int click = 0;
            for (int i = firstRow * cols; i < entries.Count && i / cols <= lastRow; i++)
            {
                var r = new Rect(left + (i % cols) * (tw + gap), 8 * u + (i / cols) * (th + gap), tw, th);
                int c = tile(r, entries[i]);
                if (c != 0) { clicked = entries[i]; click = c; }
            }
            GUI.EndScrollView();
            clipView = null;
            if (click != 0) onClick(clicked, click);
        }

        private Vector2 itemScroll;

        private void DrawItems(Rect v)
        {
            float y = v.y;
            Label(new Rect(v.x, y, 100 * u, 30 * u), "ITEMS", 18, Red, TextAnchor.MiddleLeft);
            DrawItemSlots(v.x + 100 * u, y, out string note, out bool warn);
            if (startItems.Count > 0 && FrameButton(new Rect(v.xMax - 220 * u, y, 200 * u, 30 * u), "USUAL ITEMS", false, 15)) { startItems.Clear(); ExtrasChanged(); }
            if (FrameButton(new Rect(v.xMax - 360 * u, y, 130 * u, 30 * u), "RANDOM", false, 15)) RandomItems();
            y += 36 * u;
            Label(new Rect(v.x, y, v.width, 24 * u), note, 15, warn ? RedBright : RedDim, TextAnchor.MiddleLeft);
            y += 30 * u;
            if (itemChoices.Count == 0) { Label(new Rect(v.x, y, v.width, 30 * u), "NO ITEMS AVAILABLE.", 15, RedDim, TextAnchor.MiddleLeft); return; }

            TileGrid(new Rect(v.x, y, v.width, v.yMax - y), itemChoices, ref itemScroll, (r, item) =>
            {
                int c = PlainCard(r, Key(Kind.Card, "item:" + item.name, false, r), RunStart.ItemName(item), ItemSprite(item), false,
                    itemBadges.TryGetValue(item.name, out var b) ? b : null);
                if (Over(r)) SetHoverTip(ItemTip(item), r);
                return c;
            }, (item, click) =>
            {
                if (click == 1)
                {
                    if (startItems.Count >= RunStart.MaxItems) Flash($"At most {RunStart.MaxItems} items.");
                    else startItems.Add(item.name);
                }
                else
                {
                    int i = startItems.LastIndexOf(item.name);
                    if (i >= 0) startItems.RemoveAt(i);
                }
                ExtrasChanged();
            });
        }

        private static readonly CardTip lastSlotTip = new CardTip
        {
            header = "LAST SLOT",
            pack = "",
            sigils = new List<KeyValuePair<Texture, string>> { new KeyValuePair<Texture, string>(null, "If you take the Less Consumables challenge, you start with one item less: the one in this slot stays behind. Challenges are picked after this screen.") },
        };

        // The item slots in order. The last one is always marked: the Less Consumables challenge, picked after this
        // screen, takes it away. The mark is brighter when that challenge was picked last time.
        private void DrawItemSlots(float x, float y, out string note, out bool warn)
        {
            bool lessOn = ItemSlots() < RunStart.MaxItems;
            float s = 30 * u;
            for (int i = 0; i < RunStart.MaxItems; i++)
            {
                var r = new Rect(x + i * (s + 6 * u), y, s, s);
                bool last = i == RunStart.MaxItems - 1;
                var item = i < startItems.Count ? itemChoices.Find(c => c.name == startItems[i]) : null;
                if (last && item != null && lessOn)
                    GUI.Box(new Rect(r.x - 2 * u, r.y - 2 * u, r.width + 4 * u, r.height + 4 * u), GUIContent.none, frameFilledHover);
                if (i < startItems.Count)
                {
                    var prevColor = GUI.color;
                    GUI.color = CardFace.Paper;
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = prevColor;
                    var sp = item != null ? ItemSprite(item) : null;
                    if (sp != null && sp.texture != null)
                    {
                        var t = sp.textureRect;
                        GUI.DrawTextureWithTexCoords(Fit(new Rect(r.x + 2 * u, r.y + 2 * u, r.width - 4 * u, r.height - 4 * u), t.width / t.height), sp.texture,
                            new Rect(t.x / sp.texture.width, t.y / sp.texture.height, t.width / sp.texture.width, t.height / sp.texture.height));
                    }
                }
                GUI.Box(r, GUIContent.none, frame);
                if (last)
                {
                    var mark = new Rect(r.xMax - 12 * u, r.y - 6 * u, 16 * u, 16 * u);
                    GUI.DrawTexture(mark, lessOn ? texRedBright : texRed);
                    Label(mark, "!", 13, Bg, TextAnchor.MiddleCenter);
                }
                if (Over(r))
                {
                    if (last) SetHoverTip(lastSlotTip, r);
                    else if (item != null) SetHoverTip(ItemTip(item), r);
                }
            }
            bool lastFilled = startItems.Count >= RunStart.MaxItems;
            warn = lastFilled && lessOn;
            note = startItems.Count == 0 ? "NONE PICKED = THE GAME'S USUAL ITEMS. THE LAST SLOT (!) IS LOST WITH THE LESS CONSUMABLES CHALLENGE."
                : lastFilled ? "WITH THE LESS CONSUMABLES CHALLENGE, THE ITEM IN THE LAST SLOT (!) STAYS BEHIND."
                : "THE LAST SLOT (!) IS LOST WITH THE LESS CONSUMABLES CHALLENGE.";
        }

        // Random picks for this page only; the deck panel's RANDOM leaves items and totem alone
        private void RandomItems()
        {
            if (itemChoices.Count == 0) { Flash("No items to pick from."); return; }
            startItems = Enumerable.Range(0, RunStart.MaxItems).Select(_ => itemChoices[rng.Next(itemChoices.Count)].name).ToList();
            ExtrasChanged();
        }

        private void RandomTotem()
        {
            if (sigilChoices.Count == 0) { Flash("No sigils to pick from."); return; }
            var tribes = RunStart.Tribes();
            totemTribe = tribes[rng.Next(tribes.Count)];
            totemSigil = sigilChoices[rng.Next(sigilChoices.Count)];
            ExtrasChanged();
        }

        private Vector2 tribeScroll;

        // The totem: 1. a head (the tribe), 2. a base (the sigil every card of that tribe gets)
        private void DrawTotem(Rect v)
        {
            float y = v.y;
            Label(new Rect(v.x, y, 100 * u, 30 * u), "TOTEM", 18, Red, TextAnchor.MiddleLeft);
            string tribeName = RunStart.TribeName(totemTribe).ToUpperInvariant();
            string state = TotemValue != null ? $"{tribeName} + {DeckLogic.AbilityName(totemSigil.ability).ToUpperInvariant()}. EVERY {tribeName} CARD GETS THE SIGIL."
                : totemTribe != Tribe.None ? "NOW PICK ITS BASE BELOW." : totemSigil != null ? "NOW PICK ITS HEAD." : "NONE. PICK A HEAD AND A BASE.";

            if (FrameButton(new Rect(v.xMax - 360 * u, y, 130 * u, 30 * u), "RANDOM", false, 15)) RandomTotem();
            if ((totemTribe != Tribe.None || totemSigil != null) && FrameButton(new Rect(v.xMax - 220 * u, y, 200 * u, 30 * u), "NO TOTEM", false, 15))
            {
                totemTribe = Tribe.None;
                totemSigil = null;
                ExtrasChanged();
            }
            y += 34 * u;
            Label(new Rect(v.x, y, v.width, 24 * u), state, 15, TotemValue != null ? RedBright : RedDim, TextAnchor.MiddleLeft);
            y += 30 * u;

            // Modded tribes join the game's six; with many of them the row scrolls
            const float tileSize = 0.75f;
            float cardScale = Mathf.Max(1f, Mathf.Floor(u)) * tileSize;
            float th = CardFace.H * cardScale, tw = CardFace.W * cardScale;
            var tribes = RunStart.Tribes();
            int perRow = Mathf.Max(1, (int)((v.width - 24 * u) / (tw + 14 * u)));
            int tribeRows = Mathf.Min(2, (tribes.Count + perRow - 1) / perRow);
            Label(new Rect(v.x, y, 400 * u, 26 * u), "1. HEAD: THE TRIBE", 15, Red, TextAnchor.MiddleLeft);
            y += 26 * u;
            float tribeH = tribeRows * (th + 14 * u) + 4 * u;
            TileGrid(new Rect(v.x, y, v.width, tribeH), tribes, ref tribeScroll, (r, tribe) =>
            {
                Texture icon = RunStart.TribeArt(tribe, out var uv, out float aspect);
                // The game has no tribe icon for squirrels, so the Squirrel card's portrait stands in
                if (icon == null && tribe == Tribe.Squirrel && SquirrelCard?.portraitTex != null)
                    return PlainCard(r, Key(Kind.Card, "tribe:" + tribe, false, r), "SQUIRREL", SquirrelCard.portraitTex, totemTribe == tribe, null);
                int c = PlainCard(r, Key(Kind.Card, "tribe:" + (int)tribe, false, r), RunStart.TribeName(tribe).ToUpperInvariant(), icon, uv, aspect, totemTribe == tribe, null);
                if (Over(r) && RunStart.TribeMod(tribe) is string mod)
                    SetHoverTip(new CardTip { header = RunStart.TribeName(tribe).ToUpperInvariant(), pack = "FROM " + mod.ToUpperInvariant(), sigils = new List<KeyValuePair<Texture, string>>() }, r);
                return c;
            }, (tribe, click) =>
            {
                if (click != 1) return;
                totemTribe = totemTribe == tribe ? Tribe.None : tribe;
                ExtrasChanged();
            }, scrollbar: tribes.Count > tribeRows * perRow, size: tileSize);
            y += tribeH + 8 * u;

            Label(new Rect(v.x, y, 300 * u, 30 * u), "2. BASE: THE SIGIL", 15, Red, TextAnchor.MiddleLeft);
            var box = new Rect(v.x + 210 * u, y, 240 * u, 30 * u);
            GUI.Box(box, GUIContent.none, frame);
            var prev = BeginText(new Rect(box.x + 6 * u, box.y, box.width - 12 * u, box.height), 15, RedBright, TextAnchor.MiddleLeft, out var field);
            sigilSearch = GUI.TextField(field, sigilSearch, text);
            GUI.matrix = prev;
            if (sigilSearch.Length == 0) Label(new Rect(box.x + 8 * u, box.y, box.width - 16 * u, box.height), "SEARCH", 15, RedDim, TextAnchor.MiddleLeft);
            y += 36 * u;

            if (visibleSigils == null || visibleSigilSearch != sigilSearch)
            {
                string q = sigilSearch.Trim();
                visibleSigils = q.Length == 0 ? sigilChoices
                    : sigilChoices.Where(a => DeckLogic.AbilityName(a.ability).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                visibleSigilSearch = sigilSearch;
            }
            if (visibleSigils.Count == 0) { Label(new Rect(v.x, y, v.width, 30 * u), "NO SIGILS MATCH.", 15, RedDim, TextAnchor.MiddleLeft); return; }
            TileGrid(new Rect(v.x, y, v.width, v.yMax - y), visibleSigils, ref sigilScroll, (r, a) =>
            {
                int c = PlainCard(r, Key(Kind.Card, "sigil:" + RunStart.SigilKey(a), false, r), DeckLogic.AbilityName(a.ability).ToUpperInvariant(),
                    CardFace.Sigil(a.ability), new Rect(0, 0, 1, 1), 1f, a == totemSigil, null);
                if (Over(r)) SetHoverTip(SigilTip(a), r);
                return c;
            }, (a, click) =>
            {
                if (click != 1) return;
                totemSigil = totemSigil == a ? null : a;
                ExtrasChanged();
            }, size: tileSize);
        }

        // A card-like tile with a name and a picture, lifted on hover like the card tiles. Picked tiles get a red
        // glow; badge is drawn like the copy count on cards. Returns 1 for a left click, 2 for a right click, else 0.
        private int PlainCard(Rect r, int id, string name, Sprite sprite, bool picked, string badge)
        {
            Texture tex = null;
            var uv = new Rect(0, 0, 1, 1);
            float aspect = 1f;
            if (sprite != null && sprite.texture != null)
            {
                tex = sprite.texture;
                var t = sprite.textureRect;
                uv = new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height);
                aspect = t.width / t.height;
            }
            return PlainCard(r, id, name, tex, uv, aspect, picked, badge);
        }

        private int PlainCard(Rect r, int id, string name, Texture tex, Rect uv, float aspect, bool picked, string badge)
        {
            bool over = Hover(r, id);
            var ev = Event.current;
            int click = 0;
            if (over && ev.type == EventType.MouseDown && ev.button == 1) { click = 2; ev.Use(); }
            else if (GUI.Button(r, GUIContent.none, GUIStyle.none) && ev.button == 0) click = 1;
            if (click != 0) CommandLineTextDisplayer.PlayCommandLineClickSound();

            var face = over || picked ? new Rect(r.x - 3 * u, r.y - 5 * u, r.width + 6 * u, r.height + 9 * u) : r;
            if (picked) GUI.DrawTexture(new Rect(face.x - 4 * u, face.y - 4 * u, face.width + 8 * u, face.height + 8 * u), texRed);
            try { CardFace.DrawPlain(face, name, tex, uv, aspect); }
            catch { GUI.Box(r, GUIContent.none, frame); }

            if (badge != null)
            {
                float k = r.width / CardFace.W;
                var b = new Rect(r.x + 5 * k, r.y + 32 * k, 32 * u, 24 * u);
                GUI.DrawTexture(b, texRed);
                Label(b, badge, 15, Bg, TextAnchor.MiddleCenter);
            }
            return click;
        }

        private static CardInfo squirrelCard;
        private static CardInfo SquirrelCard
        {
            get
            {
                if (squirrelCard == null) try { squirrelCard = CardLoader.GetCardByName("Squirrel"); } catch { }
                return squirrelCard;
            }
        }

        private static Sprite ItemSprite(ConsumableItemData i)
        {
            try { return i.rulebookSprite; } catch { return null; }
        }
    }
}
