using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TimberNet;

namespace BeaverBuddies.Panel
{
    /// <summary>
    /// How a chat line is written on the panel. Plain code with no game types, so it is checked without the game.
    /// </summary>
    internal static class ChatFormat
    {
        // The panel is dark, so a name must not be: a color darker than this is lightened toward white.
        const double MinBrightness = .5;
        // The panel's ordinary text color, used when a color is not six hex digits.
        const string FallbackHex = "F2E8D0";

        /// <summary>"Name: message" with the name in the sender's color and the message in the panel's own text color. Only the color is markup.</summary>
        public static string Line(string name, string colorHex, string text) =>
            "<color=#" + ReadableHex(colorHex) + ">" + Plain(name) + "</color>: " + Plain(text);

        // Rich text is on so the name can be colored, so nothing a player types may carry a tag of its own.
        // (Chat is already cleaned when it arrives; this keeps the line safe whatever calls it.)
        static string Plain(string value) => (value ?? "").Replace("<", "").Replace(">", "");

        /// <summary>
        /// Whether new messages chime: one from another player that arrived live. Your own never do, nor do those of a player whose sound is muted (<paramref name="isMuted"/>), and nor does the
        /// history a guest is sent as it joins (up to <paramref name="historyThrough"/>): it was said before they came.
        /// </summary>
        public static bool Chimes(IEnumerable<ChatMessage> fresh, int myPlayerId, int historyThrough, Func<int, bool> isMuted = null) =>
            fresh.Any(message => message.Sequence > historyThrough && message.PlayerId != myPlayerId && !(isMuted?.Invoke(message.PlayerId) ?? false));

        /// <summary>
        /// Whether a chat line counts as seen: at least half of it was inside the messages' visible area (for a line
        /// taller than that area, most of the area was taken by it). Tops and bottoms in the same units, downward.
        /// </summary>
        public static bool IsSeen(float lineTop, float lineBottom, float viewTop, float viewBottom)
        {
            float line = lineBottom - lineTop, view = viewBottom - viewTop;
            if (float.IsNaN(line) || float.IsNaN(view) || line <= 0 || view <= 0) return false;
            float inside = Math.Min(lineBottom, viewBottom) - Math.Max(lineTop, viewTop);
            return inside >= Math.Min(line, view) / 2;
        }

        /// <summary>The color as six hex digits, lightened if it would be hard to read on the panel's dark background.</summary>
        public static string ReadableHex(string hex)
        {
            if (hex == null || hex.Length != 6 || !hex.All(Uri.IsHexDigit)) return FallbackHex;
            int rgb = int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            double r = ((rgb >> 16) & 255) / 255.0, g = ((rgb >> 8) & 255) / 255.0, b = (rgb & 255) / 255.0;
            double brightness = .2126 * r + .7152 * g + .0722 * b;
            if (brightness < MinBrightness)
            {
                double toward = (MinBrightness - brightness) / (1 - brightness);
                r += (1 - r) * toward; g += (1 - g) * toward; b += (1 - b) * toward;
            }
            return Hex2(r) + Hex2(g) + Hex2(b);
        }

        static string Hex2(double channel) =>
            ((int)Math.Round(Math.Min(1, Math.Max(0, channel)) * 255)).ToString("X2", CultureInfo.InvariantCulture);
    }
}
