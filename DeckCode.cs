using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCMDeckBuilder
{
    // A deck as text: the cards, plus the items and the totem to start the run with
    internal class DeckSpec
    {
        internal List<string> Cards = new List<string>();
        internal List<string> Items = new List<string>(); // item IDs; empty = the game's usual items
        internal string Totem; // "Tribe:Sigil"; null = no totem

        internal bool HasExtras => Items.Count > 0 || Totem != null;
    }

    // Card ID lists as text: the config's "Stoat, Wolf" form, saved decks and the share codes. No game types here,
    // so the tests can compile this file on its own.
    internal static class DeckCode
    {
        // Share codes: "KCM:" + the deck in base64, so chat apps can't mangle names with underscores
        internal const string Prefix = "KCM:";

        // Cards, then items and totem after '|' (only when there are any, so a plain deck reads like before 2.2)
        internal static string ToText(DeckSpec spec, string listSeparator = ", ")
        {
            string cards = string.Join(listSeparator, spec.Cards.ToArray());
            if (!spec.HasExtras) return cards;
            return cards + "|" + string.Join(listSeparator, spec.Items.ToArray()) + "|" + (spec.Totem ?? "");
        }

        internal static DeckSpec Parse(string text)
        {
            var parts = (text ?? "").Split(new[] { '|' }, 3);
            var spec = new DeckSpec { Cards = ParseList(parts[0]) };
            if (parts.Length > 1) spec.Items = ParseList(parts[1]);
            if (parts.Length > 2 && parts[2].Trim().Length > 0) spec.Totem = parts[2].Trim();
            return spec;
        }

        internal static string Encode(DeckSpec spec) =>
            Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToText(spec, ","))).TrimEnd('=');

        internal static string Encode(IEnumerable<string> names) => Encode(new DeckSpec { Cards = names.ToList() });

        // The deck in a share code anywhere in the text, or null if there is none
        internal static DeckSpec DecodeSpec(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string b64 = new string(text.Substring(i + Prefix.Length).TakeWhile(IsBase64Char).ToArray()).TrimEnd('=');
            if (b64.Length == 0) return null;
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            try
            {
                var spec = Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                return spec.Cards.Count > 0 ? spec : null;
            }
            catch (FormatException) { return null; }
        }

        // Just the card IDs of a share code, or null
        internal static List<string> Decode(string text) => DecodeSpec(text)?.Cards;

        private static bool IsBase64Char(char ch) => ch < 128 && (char.IsLetterOrDigit(ch) || ch == '+' || ch == '/' || ch == '=');

        internal static List<string> ParseList(string s) =>
            (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        internal static string JoinList(IEnumerable<string> names) => string.Join(", ", names.ToArray());
    }
}
