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
        internal const float Aspect = W / H;

        private static readonly Color Ink = new Color(0.1f, 0.09f, 0.12f);
        private static readonly Color Paper = new Color(1f, 0.86f, 0.72f); // warm tint, like the candle-lit table

        private static Texture2D bg, bgRare, bgTerrain;
        private static readonly Color Gold = new Color(1f, 0.78f, 0.22f);
        private static Font font;
        private static bool loaded;
        private static readonly Dictionary<string, Texture> costCache = new Dictionary<string, Texture>();
        private static readonly Dictionary<Ability, Texture> sigilCache = new Dictionary<Ability, Texture>();
        private static GUIStyle text;

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            bg = Resources.Load<Texture2D>("art/cards/card_empty");
            bgRare = Resources.Load<Texture2D>("art/cards/card_empty_rare");
            bgTerrain = Resources.Load<Texture2D>("art/cards/card_terrain_empty");
            font = FindFont();
            Plugin.Log.LogInfo($"Card face: background={(bg != null)}, font={(font != null ? font.name : "fallback")}");
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
            return Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name == "HEAVYWEIGHT");
        }

        private static bool Has(CardInfo c, CardAppearanceBehaviour.Appearance a) => c.appearanceBehaviour.Contains(a);

        // Background texture and tint, following the card's appearance behaviours
        private static Texture2D Background(CardInfo c, out Color tint)
        {
            tint = Has(c, CardAppearanceBehaviour.Appearance.GoldEmission) ? Gold : Paper;
            if (Has(c, CardAppearanceBehaviour.Appearance.TerrainBackground) && bgTerrain != null) return bgTerrain;
            if ((Has(c, CardAppearanceBehaviour.Appearance.RareCardBackground) || Plugin.IsRare(c)) && bgRare != null) return bgRare;
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
            var uv = new Rect(5 / W, (H - 126) / H, 114 / W, 94 / H);
            if (bg != null)
            {
                var prev = GUI.color;
                var back = Background(c, out var tint);
                GUI.color = tint;
                GUI.DrawTextureWithTexCoords(r, back, uv);
                GUI.color = prev;
            }
            var sp = c.portraitTex;
            if (sp != null && sp.texture != null)
            {
                var tex = sp.texture;
                if (tex.width <= 128 && tex.height <= 128) GUI.DrawTexture(r, tex);
                else
                {
                    var t = sp.textureRect;
                    GUI.DrawTextureWithTexCoords(r, tex, new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
                }
            }
            else
            {
                var fallback = FallbackPortrait(c);
                if (fallback != null) GUI.DrawTexture(r, fallback);
            }
            foreach (var d in Decals(c)) GUI.DrawTextureWithTexCoords(r, d, uv);
        }

        internal static void Draw(Rect r, CardInfo c)
        {
            Load();
            float k = r.width / W;
            Rect P(float x, float y, float w, float h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);

            var prevColor = GUI.color;
            var bgTex = Background(c, out var bgTint);
            GUI.color = bgTint;
            GUI.DrawTexture(r, bgTex);
            GUI.color = prevColor;

            // Portrait (portrait textures are 114x94 and sit right under the name bar)
            var sp = c.portraitTex;
            if (sp != null && sp.texture != null)
            {
                var tex = sp.texture;
                if (tex.width <= 128 && tex.height <= 128)
                    GUI.DrawTexture(P(5, 32, tex.width, tex.height), tex);
                else
                {
                    var t = sp.textureRect;
                    GUI.DrawTextureWithTexCoords(P(5, 32, 114, 94), tex,
                        new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
                }
            }
            else
            {
                // Cards with dynamic/animated portraits (e.g. Curious Egg) usually still ship a static portrait texture
                var fallback = FallbackPortrait(c);
                if (fallback != null) GUI.DrawTexture(P(5, 32, fallback.width, fallback.height), fallback);
            }

            // Decals cover the whole card (e.g. Long Elk's body is a decal)
            foreach (var d in Decals(c)) GUI.DrawTexture(r, d);

            var cost = Cost(c);
            if (cost != null) GUI.DrawTexture(P(59, 14, 64, 64), cost);

            // Sigils
            var sigils = c.Abilities.Distinct().Select(Sigil).Where(t => t != null).ToList();
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
            string name = c.DisplayedNameEnglish.ToUpperInvariant();
            int size = Mathf.Max(1, Mathf.RoundToInt(22 * k));
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            float avail = 115 * k;
            float wName = text.CalcSize(new GUIContent(name)).x;
            if (wName > avail) text.fontSize = Mathf.Max(1, Mathf.FloorToInt(size * avail / wName));
            GUI.Label(P(5, 4, 115, 28), name, text);

            // Attack / health (special stat icons such as Ants replace the attack number)
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(40 * k));
            text.alignment = TextAnchor.MiddleLeft;
            var statIcon = c.SpecialStatIcon != SpecialStatIcon.None ? StatIconInfo.GetIconInfo(c.SpecialStatIcon) : null;
            if (statIcon != null && statIcon.iconGraphic != null && StatIconInfo.IconAppliesToAttack(c.SpecialStatIcon))
                GUI.DrawTexture(P(4, 138, 40, 40), statIcon.iconGraphic);
            else
                GUI.Label(P(10, 136, 40, 50), c.Attack.ToString(), text);
            text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(P(92, 146, 30, 46), c.Health.ToString(), text);
        }
    }
}
