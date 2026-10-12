using System;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace KCMDeckBuilder
{
    // The game's cursor ends up behind an IMGUI panel, so a copy of it is drawn on top, with the same trail effect.
    // If its sprites can't be found, the system cursor is shown instead.
    internal class GameCursor
    {
        private const float GhostLife = 0.115f;
        private struct Ghost { public Vector2 Pos; public Sprite Sprite; public float Born; }

        private readonly List<Ghost> ghosts = new List<Ghost>();
        private List<Sprite> sprites, downSprites;
        private float scale, trailFrequency = 0.03f, trailTimer, trailBrightness = 0.9f;
        private bool ready, tried;

        internal bool PointerForced { get; private set; }

        private static Vector2 MousePosition => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        // Takes the cursor from the game for the panel, or gives it back
        internal void Take(bool take)
        {
            var cursor = Singleton<InteractionCursor>.Instance;
            if (cursor == null) return;
            cursor.InteractionDisabled = take;
            SetPointer(false);
            if (take) Init();
            bool systemCursor = take && !ready;
            cursor.OSCursorShown = systemCursor;
            Cursor.visible = systemCursor;
            ghosts.Clear();
        }

        // Shows the game's pointer hand while it is the game's cursor that is visible
        internal void SetPointer(bool on)
        {
            var cursor = Singleton<InteractionCursor>.Instance;
            if (cursor == null || on == PointerForced) return;
            if (on) cursor.ForceCursorType(CursorType.Point);
            else cursor.ClearForcedCursorType();
            PointerForced = on;
        }

        internal void Update(bool pointer)
        {
            if (!ready) return;
            Cursor.visible = false;
            trailTimer += Time.unscaledDeltaTime;
            if (trailTimer <= trailFrequency) return;
            trailTimer = 0f;
            ghosts.Add(new Ghost { Pos = MousePosition, Sprite = Current(pointer), Born = Time.unscaledTime });
        }

        internal void Draw(bool pointer)
        {
            if (!ready) return;
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float now = Time.unscaledTime;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (now - ghosts[i].Born > GhostLife) ghosts.RemoveAt(i);
            foreach (var g in ghosts)
            {
                float a = 0.5f * (1f - (now - g.Born) / GhostLife);
                DrawSprite(g.Sprite, g.Pos, new Color(trailBrightness, trailBrightness, trailBrightness, a));
            }
            DrawSprite(Current(pointer), MousePosition, Color.white);
            GUI.matrix = prevMatrix;
        }

        private Sprite Current(bool pointer)
        {
            int index = Mathf.Min(sprites.Count - 1, pointer ? (int)CursorType.Point : (int)CursorType.Default);
            var list = Input.GetMouseButton(0) && downSprites != null && downSprites.Count > index ? downSprites : sprites;
            return list[index];
        }

        private void DrawSprite(Sprite sp, Vector2 pos, Color color)
        {
            var tex = sp.texture;
            var tr = sp.textureRect;
            var rect = new Rect(pos.x - sp.pivot.x * scale, pos.y - (tr.height - sp.pivot.y) * scale, tr.width * scale, tr.height * scale);
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
            GUI.color = prev;
        }

        private void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                var tr = Traverse.Create(Singleton<InteractionCursor>.Instance);
                sprites = tr.Field("cursorTextures").GetValue<List<Sprite>>();
                downSprites = tr.Field("cursorDownTextures").GetValue<List<Sprite>>();
                var sr = tr.Field("mainCursor").GetValue<SpriteRenderer>();
                var cam = tr.Field("rayCamera").GetValue<Camera>();
                var trail = tr.Field("trailEffect").GetValue<CursorTrailEffect>();
                if (trail != null)
                {
                    if (trail.originalFrequency > 0) trailFrequency = trail.originalFrequency;
                    trailBrightness = Traverse.Create(trail).Field("cloneBrightness").GetValue<float>();
                }

                // Same on-screen size as the game's cursor, which is a sprite seen by an orthographic camera
                var sp = sprites != null && sprites.Count > 0 ? sprites[0] : null;
                if (sp != null && sr != null && cam != null && cam.orthographic)
                    scale = sr.transform.lossyScale.x * Screen.height / (2f * cam.orthographicSize * sp.pixelsPerUnit);
                if (scale <= 0.5f || scale > 20f) scale = Mathf.Max(1f, Mathf.Round(Screen.height / 360f));
                ready = sp != null;
                Plugin.Log.LogDebug($"Game cursor: {(ready ? "ok" : "not found")}, scale {scale:0.##}, trail {trailFrequency:0.###}s");
            }
            catch (Exception e)
            {
                ready = false;
                Plugin.Log.LogWarning("Game cursor unavailable, using the system cursor: " + e.Message);
            }
        }
    }
}
