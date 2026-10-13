using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using GBC;
using HarmonyLib;
using UnityEngine;

namespace KCMDeckBuilder
{
    // Drawing building blocks for the panel: Kaycee's Mod style text, buttons and frames
    internal partial class DeckBuilderUI
    {
        private static readonly Color Red = new Color(0.80f, 0.16f, 0.19f);
        private static readonly Color RedDim = new Color(0.42f, 0.08f, 0.10f);
        private static readonly Color RedBright = new Color(1f, 0.32f, 0.34f);
        private static readonly Color Bg = new Color(0.075f, 0.06f, 0.07f, 1f);

        // The GUI is scaled by a whole factor so pixel art stays crisp; u then scales the 720p layout to the virtual screen
        private float vw, vh, u, guiScale = 1f;

        // Hover works like the game's menu items: a click sound on enter, a blinking highlight and the pointer cursor
        private enum Kind { Button = 1, Tab, Icon, Card }
        private int hoverKey, lastHoverKey; // 0 = nothing hovered
        private float hoverSince, blinkFrequency = 0.2f;
        private Vector2 outerMouse;
        private Rect? clipView;

        private GUISkin skin;
        private Font pixelFont;
        private GUIStyle text, frame, frameHover, frameFilled, frameFilledHover;
        private Texture2D texBg, texRed, texRedBright, texDash, texScan;
        private Texture texBack, texContinue;
        private readonly Dictionary<int, Dictionary<string, float>> measureCache = new Dictionary<int, Dictionary<string, float>>(); // size -> text -> width
        private float measureU;

        private Rect R(float x, float y, float w, float h) => new Rect(x * u, y * u, w * u, h * u);
        private int F(int size) => Mathf.Max(8, Mathf.RoundToInt(size * u));

        // Mouse over r, and not hidden outside the scroll view it is drawn in
        private bool Over(Rect r) =>
            r.Contains(Event.current.mousePosition) && (!clipView.HasValue || clipView.Value.Contains(outerMouse));

        private bool Hover(Rect r, int id)
        {
            if (!Over(r)) return false;
            if (Event.current.type == EventType.Repaint) hoverKey = id;
            return true;
        }

        // Identifies a control without allocating: its kind, its label and, for labels that repeat in several rows,
        // where it is on screen
        private static int Key(Kind kind, string label, bool byPosition, Rect r)
        {
            unchecked
            {
                int h = (int)kind * 397 ^ (label != null ? label.GetHashCode() : 0);
                if (byPosition)
                {
                    var p = GUIUtility.GUIToScreenPoint(r.position);
                    h = (h * 397 ^ (int)p.x) * 397 ^ (int)p.y;
                }
                return h != 0 ? h : 1;
            }
        }

        private bool BlinkOn(int id) =>
            id == lastHoverKey && ((int)((Time.unscaledTime - hoverSince) / blinkFrequency) % 2) == 0;

        // The menu font is a bitmap font, which ignores fontSize; scale it up by a whole factor instead
        private int TextScale(int size)
        {
            if (pixelFont == null || pixelFont.dynamic) return 1;
            int native = pixelFont.lineHeight > 0 ? pixelFont.lineHeight : 10;
            return Mathf.Max(1, Mathf.RoundToInt(F(size) / (float)native));
        }

        // Sets up the text style; for bitmap fonts scales GUI.matrix around r and gives the rect to draw in.
        // Restore GUI.matrix with the returned matrix afterwards.
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
            // Scaled text isn't clipped by scroll views, so skip labels whose text sticks out of the visible area
            if (clipView.HasValue)
            {
                int lines = 1;
                foreach (char ch in s) if (ch == '\n') lines++;
                float textH = F(size) * 1.2f * lines;
                float y0 = r.y, y1 = r.yMax;
                if (anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.MiddleRight)
                {
                    y0 = r.center.y - textH / 2;
                    y1 = r.center.y + textH / 2;
                }
                else if (anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight)
                    y1 = Mathf.Min(r.yMax, r.y + textH);
                float top = GUIUtility.GUIToScreenPoint(new Vector2(r.x, y0)).y / guiScale;
                float bottom = GUIUtility.GUIToScreenPoint(new Vector2(r.x, y1)).y / guiScale;
                if (top < clipView.Value.y - 1f || bottom > clipView.Value.yMax + 1f) return;
            }
            var prev = BeginText(r, size, color, anchor, out var inner);
            GUI.Label(inner, s, text);
            GUI.matrix = prev;
        }

        private float Measure(string s, int size)
        {
            if (measureU != u) { measureCache.Clear(); measureU = u; }
            if (!measureCache.TryGetValue(size, out var widths)) measureCache[size] = widths = new Dictionary<string, float>();
            if (widths.TryGetValue(s, out var w)) return w;
            int m = TextScale(size);
            text.fontSize = m == 1 ? F(size) : 0;
            return widths[s] = text.CalcSize(new GUIContent(s)).x * m;
        }

        // Height of the text wrapped to a width, in 720p units
        private readonly Dictionary<string, float> heightCache = new Dictionary<string, float>();
        private float heightU;
        private float WrappedHeight(string s, int size, float width)
        {
            if (heightU != u) { heightCache.Clear(); heightU = u; }
            string key = size + "|" + width + "|" + s;
            if (heightCache.TryGetValue(key, out var h)) return h;
            int m = TextScale(size);
            text.fontSize = m == 1 ? F(size) : 0;
            bool wrap = text.wordWrap;
            text.wordWrap = true;
            h = text.CalcHeight(new GUIContent(s), width * u / m) * m / u;
            text.wordWrap = wrap;
            return heightCache[key] = h;
        }

        private void Dash(Rect r) =>
            GUI.DrawTextureWithTexCoords(r, texDash, new Rect(0, 0, r.width / (8f * u), 1));

        private bool FrameButton(Rect r, string label, bool primary, int size, bool arrow = false)
        {
            int id = Key(Kind.Button, label, true, r);
            bool over = Hover(r, id);
            bool lit = over && BlinkOn(id);
            var style = primary ? (lit ? frameFilledHover : frameFilled) : (lit ? frameFilledHover : over ? frameHover : frame);
            bool clicked = GUI.Button(r, GUIContent.none, style);
            Label(r, label, size, primary || lit ? Bg : (over ? RedBright : Red), TextAnchor.MiddleCenter);
            if (arrow && texContinue != null)
            {
                var prev = GUI.color;
                GUI.color = Bg;
                GUI.DrawTexture(new Rect(r.xMax - r.height * 0.8f, r.y + r.height * 0.2f, r.height * 0.6f, r.height * 0.6f), texContinue);
                GUI.color = prev;
            }
            if (clicked) CommandLineTextDisplayer.PlayCommandLineClickSound();
            return clicked;
        }

        private bool TabButton(Rect r, string label, bool active)
        {
            int id = Key(Kind.Tab, label, false, r);
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
            int id = Key(Kind.Icon, null, true, r);
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

        private static void DrawPortraitBox(Rect r, CardInfo c)
        {
            try { CardFace.DrawPortraitBox(r, c); }
            catch (Exception e) { CardFace.Report(c, "portrait", e); }
        }

        // Only used if the card background texture is missing and CardFace can't draw
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

        private static Texture2D Tex(int w, int h, Func<int, int, Color> px, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = wrap, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) t.SetPixel(x, y, px(x, y));
            t.Apply();
            return t;
        }

        // 3x3 nine-slice: 1px border, filled or transparent center
        private static GUIStyle FrameStyle(Color border, Color fill) =>
            new GUIStyle { normal = { background = Tex(3, 3, (x, y) => x == 1 && y == 1 ? fill : border) }, border = new RectOffset(1, 1, 1, 1) };

        // The font the menu's own PixelText labels use
        private static Font FindPixelFont()
        {
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
            var redTint = new Color(Red.r, Red.g, Red.b, 0.15f);
            texBg = Tex(1, 1, (x, y) => Bg);
            texRed = Tex(1, 1, (x, y) => Red);
            texRedBright = Tex(1, 1, (x, y) => RedBright);
            texDash = Tex(8, 1, (x, y) => x < 5 ? Red : clear, TextureWrapMode.Repeat);
            texScan = Tex(1, 3, (x, y) => y == 0 ? new Color(0, 0, 0, 0.22f) : clear, TextureWrapMode.Repeat);
            frame = FrameStyle(Red, clear);
            frameHover = FrameStyle(RedBright, redTint);
            frameFilled = FrameStyle(Red, Red);
            frameFilledHover = FrameStyle(RedBright, RedBright);

            texBack = Resources.Load<Texture>("art/ui/ascension/ascensionmenu_back");
            texContinue = Resources.Load<Texture>("art/ui/ascension/ascensionmenu_continue");
            var blink = Resources.FindObjectsOfTypeAll<AscensionMenuBlinkEffect>().FirstOrDefault(b => b.originalFrequency > 0);
            if (blink != null) blinkFrequency = blink.originalFrequency;

            pixelFont = FindPixelFont();
            Plugin.Log.LogDebug("Menu font: " + (pixelFont != null ? $"{pixelFont.name} (dynamic={pixelFont.dynamic}, lineHeight={pixelFont.lineHeight})" : "default"));

            skin = Instantiate(GUI.skin);
            if (pixelFont != null) skin.font = pixelFont;
            skin.verticalScrollbar = new GUIStyle { normal = { background = Tex(1, 1, (x, y) => redTint) }, fixedWidth = 8, margin = new RectOffset(6, 0, 0, 0) };
            skin.verticalScrollbarThumb = new GUIStyle { normal = { background = texRed }, fixedWidth = 8 };
            skin.verticalScrollbarUpButton = GUIStyle.none;
            skin.verticalScrollbarDownButton = GUIStyle.none;
            skin.settings.cursorColor = Red;
            skin.settings.selectionColor = new Color(Red.r, Red.g, Red.b, 0.4f);

            text = new GUIStyle { font = pixelFont, richText = false, wordWrap = false, clipping = TextClipping.Clip };
        }
    }
}
