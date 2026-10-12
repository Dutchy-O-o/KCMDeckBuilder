using System;
using System.Collections.Generic;
using System.Linq;
using DiskCardGame;
using UnityEngine;

namespace KCMDeckBuilder
{
    // Draws an Act 1 style card face in IMGUI from the game's own textures
    // (card background, portrait, cost, sigil icons) and the HEAVYWEIGHT card font.
    // Layout coordinates are in the card background's native 125x190 pixel space.
    internal static class CardFace
    {
        internal const float W = 125f, H = 190f;

        private static readonly Color Ink = new Color(0.1f, 0.09f, 0.12f);
        private static readonly Color Paper = new Color(1f, 0.86f, 0.72f); // warm tint, like the candle-lit table

        private static Texture2D bg, bgRare, bgTerrain;
        private static readonly Color Gold = new Color(1f, 0.78f, 0.22f);
        private static Font font;
        private static bool loaded;
        private static readonly Dictionary<string, Texture> costCache = new Dictionary<string, Texture>();
        private static readonly Dictionary<Ability, Texture> sigilCache = new Dictionary<Ability, Texture>();
        private static GUIStyle text;

        // Everything about a card that doesn't change while the builder is open. Reading these from CardInfo every
        // frame is slow (the getters build lists and load textures, and other mods may patch them).
        private class Info
        {
            internal Texture2D bg;
            internal Color tint;
            internal Texture2D portrait;
            internal Rect portraitUv;
            internal bool portraitScaled;
            internal List<Texture> decals;
            internal Texture cost;
            internal List<Texture> sigils;
            internal Texture statIcon;
            internal string name, attack, health;
            internal int nameSize = -1, nameFit;
        }
        private static readonly Dictionary<CardInfo, Info> infoCache = new Dictionary<CardInfo, Info>();

        internal static void ClearCache() => infoCache.Clear();

        // Logs a problem with a card once (cards from other mods can have unusual data) and carries on
        private static readonly HashSet<string> reported = new HashSet<string>();
        internal static void Report(CardInfo c, string what, Exception e)
        {
            string key = (c != null ? c.name : "?") + ":" + what;
            if (reported.Add(key)) Plugin.Log.LogWarning($"Card '{(c != null ? c.name : "?")}': could not read {what} ({e.GetType().Name}: {e.Message})");
        }

        private static void Try(CardInfo c, string what, Action a)
        {
            try { a(); } catch (Exception e) { Report(c, what, e); }
        }

        internal static string Name(CardInfo c) => Get(c).name;

        private static Info Get(CardInfo c)
        {
            if (infoCache.TryGetValue(c, out var i)) return i;
            i = new Info { decals = new List<Texture>(), sigils = new List<Texture>(), tint = Paper, bg = bg };
            Try(c, "background", () => i.bg = Background(c, out i.tint));
            Try(c, "portrait", () =>
            {
                var sp = c.portraitTex;
                if (sp != null && sp.texture != null)
                {
                    i.portrait = sp.texture;
                    i.portraitScaled = i.portrait.width > 128 || i.portrait.height > 128;
                    var t = sp.textureRect;
                    i.portraitUv = new Rect(t.x / i.portrait.width, t.y / i.portrait.height, t.width / i.portrait.width, t.height / i.portrait.height);
                }
                else i.portrait = FallbackPortrait(c);
            });
            Try(c, "decals", () => i.decals = Decals(c).ToList());
            Try(c, "cost", () => i.cost = Cost(c));
            Try(c, "sigils", () => i.sigils = (c.Abilities ?? new List<Ability>()).Distinct().Select(Sigil).Where(t => t != null).ToList());
            Try(c, "stat icon", () =>
            {
                var stat = c.SpecialStatIcon != SpecialStatIcon.None ? StatIconInfo.GetIconInfo(c.SpecialStatIcon) : null;
                if (stat != null && stat.iconGraphic != null && StatIconInfo.IconAppliesToAttack(c.SpecialStatIcon)) i.statIcon = stat.iconGraphic;
            });
            i.name = CardRules.DisplayName(c).ToUpperInvariant();
            i.attack = "?"; i.health = "?";
            Try(c, "stats", () => { i.attack = c.Attack.ToString(); i.health = c.Health.ToString(); });
            infoCache[c] = i;
            return i;
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            bg = Resources.Load<Texture2D>("art/cards/card_empty");
            bgRare = Resources.Load<Texture2D>("art/cards/card_empty_rare");
            bgTerrain = Resources.Load<Texture2D>("art/cards/card_terrain_empty");
            font = FindFont();
            Plugin.Log.LogDebug($"Card face: background={(bg != null)}, font={(font != null ? font.name : "fallback")}");
        }

        private static Font FindFont()
        {
            // Load it from Resources so it works even if no 3D card has been shown yet this session
            Font f = Resources.Load<Font>("fonts/3d scene fonts/heavyweight");
            if (f != null) return f;
            f = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name == "HEAVYWEIGHT");
            if (f != null) return f;
            try
            {
                var tmp = Resources.Load<TMPro.TMP_FontAsset>("fonts/3d scene fonts/heavyweight");
                if (tmp != null && tmp.sourceFontFile != null) return tmp.sourceFontFile;
            }
            catch { }
            return null;
        }

        private static bool Has(CardInfo c, CardAppearanceBehaviour.Appearance a) => c.appearanceBehaviour.Contains(a);

        // Background texture and tint, following the card's appearance behaviours
        private static Texture2D Background(CardInfo c, out Color tint)
        {
            tint = Has(c, CardAppearanceBehaviour.Appearance.GoldEmission) ? Gold : Paper;
            if (Has(c, CardAppearanceBehaviour.Appearance.TerrainBackground) && bgTerrain != null) return bgTerrain;
            if ((Has(c, CardAppearanceBehaviour.Appearance.RareCardBackground) || CardRules.IsRare(c)) && bgRare != null) return bgRare;
            return bg;
        }

        internal static bool Available
        {
            get { Load(); return bg != null; }
        }

        private static Texture Cost(CardInfo c)
        {
            string key = c.BloodCost > 0 ? $"cost_{c.BloodCost}blood" : c.BonesCost > 0 ? $"cost_{c.BonesCost}bone" : null;
            if (key == null) return null;
            if (!costCache.TryGetValue(key, out var t))
                costCache[key] = t = Resources.Load<Texture>("art/cards/costs/" + key);
            return t;
        }

        private static readonly Dictionary<string, Texture2D> portraitCache = new Dictionary<string, Texture2D>();

        internal static Texture2D FallbackPortrait(CardInfo c)
        {
            if (!portraitCache.TryGetValue(c.name, out var t))
                portraitCache[c.name] = t = Resources.Load<Texture2D>("art/cards/portraits/portrait_" + c.name.ToLowerInvariant());
            return t;
        }

        private static IEnumerable<Texture> Decals(CardInfo c)
        {
            List<Texture> list;
            try { list = c.Decals.Where(t => t != null).ToList(); } catch { list = new List<Texture>(); }
            // Added at runtime by an appearance behaviour, so not part of the card's static decals
            if (c.appearanceBehaviour.Contains(CardAppearanceBehaviour.Appearance.AddSnelkDecals))
            {
                var snelk = Resources.Load<Texture>("art/cards/decals/decal_snelk_main");
                if (snelk != null) list.Add(snelk);
            }
            return list;
        }

        private static Texture Sigil(Ability a)
        {
            if (!sigilCache.TryGetValue(a, out var t))
            {
                try { t = AbilitiesUtil.LoadAbilityIcon(a.ToString()); } catch { t = null; }
                sigilCache[a] = t;
            }
            return t;
        }

        // Just the portrait window of the card (paper background + portrait + decals), for small thumbnails
        internal static void DrawPortraitBox(Rect r, CardInfo c)
        {
            Load();
            var info = Get(c);
            var uv = new Rect(5 / W, (H - 126) / H, 114 / W, 94 / H);
            if (info.bg != null)
            {
                var prev = GUI.color;
                GUI.color = info.tint;
                GUI.DrawTextureWithTexCoords(r, info.bg, uv);
                GUI.color = prev;
            }
            if (info.portrait != null)
            {
                if (info.portraitScaled) GUI.DrawTextureWithTexCoords(r, info.portrait, info.portraitUv);
                else GUI.DrawTexture(r, info.portrait);
            }
            foreach (var d in info.decals) GUI.DrawTextureWithTexCoords(r, d, uv);
        }

        internal static void Draw(Rect r, CardInfo c)
        {
            Load();
            float k = r.width / W;
            Rect P(float x, float y, float w, float h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);

            var info = Get(c);
            var prevColor = GUI.color;
            GUI.color = info.tint;
            GUI.DrawTexture(r, info.bg);
            GUI.color = prevColor;

            // Portrait (portrait textures are 114x94 and sit right under the name bar)
            if (info.portrait != null)
            {
                if (info.portraitScaled) GUI.DrawTextureWithTexCoords(P(5, 32, 114, 94), info.portrait, info.portraitUv);
                else GUI.DrawTexture(P(5, 32, info.portrait.width, info.portrait.height), info.portrait);
            }

            // Decals cover the whole card (e.g. Long Elk's body is a decal)
            foreach (var d in info.decals) GUI.DrawTexture(r, d);

            if (info.cost != null) GUI.DrawTexture(P(59, 14, 64, 64), info.cost);

            // Sigils
            var sigils = info.sigils;
            if (sigils.Count == 1) GUI.DrawTexture(P(38, 133, 49, 49), sigils[0]);
            else if (sigils.Count >= 2)
            {
                GUI.DrawTexture(P(28, 140, 34, 34), sigils[0]);
                GUI.DrawTexture(P(63, 140, 34, 34), sigils[1]);
            }

            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), wordWrap = false };
                if (font != null) text.font = font; else text.fontStyle = FontStyle.Bold;
            }
            text.normal.textColor = Ink;

            // Name, shrunk to fit the name bar
            int size = Mathf.Max(1, Mathf.RoundToInt(22 * k));
            text.alignment = TextAnchor.MiddleCenter;
            if (info.nameSize != size)
            {
                text.fontSize = size;
                float avail = 115 * k;
                float wName = text.CalcSize(new GUIContent(info.name)).x;
                info.nameSize = size;
                info.nameFit = wName > avail ? Mathf.Max(1, Mathf.FloorToInt(size * avail / wName)) : size;
            }
            text.fontSize = info.nameFit;
            GUI.Label(P(5, 4, 115, 28), info.name, text);

            // Attack / health (special stat icons such as Ants replace the attack number)
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(40 * k));
            text.alignment = TextAnchor.MiddleLeft;
            if (info.statIcon != null) GUI.DrawTexture(P(4, 138, 40, 40), info.statIcon);
            else GUI.Label(P(10, 136, 40, 50), info.attack, text);
            text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(P(92, 146, 30, 46), info.health, text);
        }
    }
}
