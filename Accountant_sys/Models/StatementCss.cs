using System.Text;

namespace Accountant_sys.Models
{
    /// <summary>
    /// CSS-context escaping for the values that are emitted inside the
    /// <c>@page</c> margin-box strings of the printed statement.
    ///
    /// Razor HTML-encodes expressions inside a &lt;style&gt; block, which is the
    /// wrong context: Arabic text becomes <c>&amp;#x62D;...</c> and the printed
    /// page header shows raw entities. These helpers escape for a CSS string and
    /// also neutralise <c>&lt;</c>/<c>&gt;</c>/<c>&amp;</c> so a party name can
    /// never close the style element.
    /// </summary>
    public static class StatementCss
    {
        public static string EscapeString(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\D "); break;
                    case '\n': sb.Append("\\A "); break;
                    case '\f': sb.Append("\\C "); break;
                    case '<': sb.Append("\\3C "); break;
                    case '>': sb.Append("\\3E "); break;
                    case '&': sb.Append("\\26 "); break;
                    default:
                        // Other control characters have no useful place in a
                        // one-line printed header.
                        sb.Append(char.IsControl(ch) ? ' ' : ch);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string BuildPageHeaderContent(string title, string partyName, string accountCode)
            => $"{EscapeString(title)}: {EscapeString(partyName)} ({EscapeString(accountCode)})";
    }
}
