using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCMDeckBuilder
{
    // Card ID lists as text: the config's "Stoat, Wolf" form and the share codes. No game types here, so the tests can
    // compile this file on its own.
    internal static class DeckCode
    {
        // Share codes: "KCM:" + the card IDs in base64, so chat apps can't mangle names with underscores
        internal const string Prefix = "KCM:";

        internal static string Encode(IEnumerable<string> names) =>
            Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(",", names.ToArray()))).TrimEnd('=');

        // Card IDs from a share code anywhere in the text, or null if there is none
        internal static List<string> Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string b64 = new string(text.Substring(i + Prefix.Length).TakeWhile(IsBase64Char).ToArray()).TrimEnd('=');
            if (b64.Length == 0) return null;
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            try
            {
                var names = ParseList(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                return names.Count > 0 ? names : null;
            }
            catch (FormatException) { return null; }
        }

        private static bool IsBase64Char(char ch) => ch < 128 && (char.IsLetterOrDigit(ch) || ch == '+' || ch == '/' || ch == '=');

        internal static List<string> ParseList(string s) =>
            (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        internal static string JoinList(IEnumerable<string> names) => string.Join(", ", names.ToArray());
    }
}
