using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Yuqiao
{
    /// <summary>
    /// Target-language typography only. No translation, network, files or UI.
    /// Markdown code, obvious standalone code, URLs, addresses and numeric
    /// punctuation are copied verbatim. Ambiguous ASCII commas are left alone.
    /// </summary>
    public static class TranslationPunctuation
    {
        private static readonly Regex ProtectedToken = new Regex(
            @"\G(?:(?:https?://|ftp://|www\.)[^\s<>""'`，。、！？：；（）「」『』“”‘’]+" +
            @"|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}" +
            @"|\b(?:[A-Za-z]\.){2,}" +
            @"|\b(?:Mr|Mrs|Ms|Dr|Prof|Sr|Jr|St|vs|etc)\." +
            @"|\d+(?:[.,:]\d+)+" +
            @"|!==?|===?|<=|>=|=>|&&|\|\|)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

        private static readonly Regex CodeLine = new Regex(
            @"^(?:(?://|/\*|\*/|#include\b|<!|</?[A-Za-z][^>]*>)" +
            @"|(?:using|import|package|namespace|public|private|protected|class|def|function|const|let|var|return|throw)\s" +
            @"|(?:if|for|while|switch|catch)\s*\(" +
            @"|[A-Za-z_$][\w.$\[\]]*\s*(?:=(?!=)|\+=|-=|:=)" +
            @"|[A-Za-z_$][\w.$]*\s*\(.*\)\s*;?\s*$" +
            @"|[""'][^""']+[""']\s*:" +
            @"|[{}\[\]]\s*[,;]?\s*$)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

        public static string Normalize(string text, string target)
        {
            if (String.IsNullOrEmpty(text)) return text;
            bool japanese = String.Equals(target, "ja", StringComparison.OrdinalIgnoreCase);
            bool english = String.Equals(target, "en", StringComparison.OrdinalIgnoreCase);
            if (!japanese && !english) return text;

            StringBuilder result = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; )
            {
                int end;
                if (TryCodeSpan(text, i, out end) || TryCodeLine(text, i, out end))
                { result.Append(text, i, end - i); i = end; continue; }
                Match protectedMatch = ProtectedToken.Match(text, i);
                if (protectedMatch.Success && protectedMatch.Index == i)
                { result.Append(protectedMatch.Value); i += protectedMatch.Length; continue; }

                char current = text[i];
                if (japanese) result.Append(JapaneseCharacter(text, i));
                else
                {
                    char converted = EnglishCharacter(current);
                    if (converted != current)
                    {
                        if (IsClosingPunctuation(converted)) TrimTrailingSpaces(result);
                        if (IsOpeningBracket(converted) && NeedsSpaceBeforeOpening(result)) result.Append(' ');
                        result.Append(converted);
                        if (NeedsEnglishSpaceAfter(text, i, converted)) result.Append(' ');
                    }
                    else result.Append(current);
                }
                i++;
            }
            return result.ToString();
        }

        private static char JapaneseCharacter(string text, int index)
        {
            switch (text[index])
            {
                case '，': return '、';
                case '?': return '？';
                case '!': return '！';
                case ':': return '：';
                case ';': return '；';
                case '“': return '「';
                case '”': return '」';
                case '‘': return '『';
                case '’': return '』';
                case '.': return IsSentencePeriod(text, index) ? '。' : '.';
                default: return text[index];
            }
        }

        private static bool IsSentencePeriod(string text, int index)
        {
            if (index > 0 && text[index - 1] == '.') return false;
            if (index + 1 < text.Length && text[index + 1] == '.') return false;
            if (index + 1 == text.Length) return true;
            char next = text[index + 1];
            return Char.IsWhiteSpace(next) || ")]\"'”’」』）］!?！？".IndexOf(next) >= 0
                || (next >= '\u3040' && next <= '\u30ff') || (next >= '\u3400' && next <= '\u9fff');
        }

        private static char EnglishCharacter(char value)
        {
            switch (value)
            {
                case '，': case '、': return ',';
                case '。': case '．': return '.';
                case '？': return '?';
                case '！': return '!';
                case '：': return ':';
                case '；': return ';';
                case '（': return '(';
                case '）': return ')';
                case '［': case '【': return '[';
                case '］': case '】': return ']';
                case '｛': return '{';
                case '｝': return '}';
                case '“': case '”': case '「': case '」': return '"';
                case '‘': case '’': case '『': case '』': return '\'';
                default: return value;
            }
        }

        private static bool NeedsEnglishSpaceAfter(string text, int index, char converted)
        {
            if (index + 1 >= text.Length) return false;
            char next = text[index + 1];
            if (Char.IsWhiteSpace(next)) return false;
            if (",.;:!?，。．、；：！？)]}）］｝】\"'”’」』".IndexOf(next) >= 0) return false;
            if (index > 0 && Char.IsDigit(text[index - 1]) && Char.IsDigit(next)
                && (converted == ',' || converted == '.' || converted == ':')) return false;
            // Curly apostrophes inside English words are not closing quotations.
            if (converted == '\'' && index > 0 && Char.IsLetter(text[index - 1]) && Char.IsLetter(next)) return false;
            bool originalClosingQuote = "”’」』".IndexOf(text[index]) >= 0;
            return ",.;:!?)]}".IndexOf(converted) >= 0 || originalClosingQuote;
        }

        private static bool IsClosingPunctuation(char value) { return ",.;:!?)]}".IndexOf(value) >= 0; }
        private static bool IsOpeningBracket(char value) { return "([{".IndexOf(value) >= 0; }
        private static bool NeedsSpaceBeforeOpening(StringBuilder result)
        {
            if (result.Length == 0) return false;
            char previous = result[result.Length - 1];
            return Char.IsLetterOrDigit(previous) || ")]}\"'".IndexOf(previous) >= 0;
        }
        private static void TrimTrailingSpaces(StringBuilder result)
        {
            while (result.Length > 0 && (result[result.Length - 1] == ' ' || result[result.Length - 1] == '\t')) result.Length--;
        }

        private static bool TryCodeLine(string text, int index, out int end)
        {
            end = index;
            if (index != 0 && text[index - 1] != '\n') return false;
            int lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text.Substring(index, lineEnd - index).TrimStart(' ', '\t');
            if (!CodeLine.IsMatch(line)) return false;
            end = lineEnd < text.Length ? lineEnd + 1 : lineEnd;
            return true;
        }

        private static bool TryCodeSpan(string text, int index, out int end)
        {
            end = index;
            char marker = text[index];
            if (marker != '`' && marker != '~') return false;
            int count = 1;
            while (index + count < text.Length && text[index + count] == marker) count++;
            bool lineStart = true;
            for (int before = index - 1; before >= 0 && text[before] != '\n'; before--)
                if (text[before] != ' ' && text[before] != '\t') { lineStart = false; break; }
            bool fence = count >= 3 && lineStart;
            if (marker == '~' && !fence) return false;
            string delimiter = new string(marker, count);
            int search = index + count;
            while (search < text.Length)
            {
                int closing = text.IndexOf(delimiter, search, StringComparison.Ordinal);
                if (closing < 0) break;
                int closingCount = count;
                while (closing + closingCount < text.Length && text[closing + closingCount] == marker) closingCount++;
                if (!fence)
                {
                    if (closingCount == count) { end = closing + count; return true; }
                }
                else
                {
                    bool closingAtStart = true;
                    for (int before = closing - 1; before >= 0 && text[before] != '\n'; before--)
                        if (text[before] != ' ' && text[before] != '\t') { closingAtStart = false; break; }
                    if (closingAtStart) { end = closing + closingCount; return true; }
                }
                search = closing + closingCount;
            }
            // Preserve unfinished code rather than guessing where prose resumes.
            end = text.Length;
            return true;
        }
    }
}
