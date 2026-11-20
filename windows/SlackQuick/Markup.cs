using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SlackQuick
{
    /// <summary>
    /// 슬랙식 텍스트 서식(*굵게* _기울임_ ~취소선~ `코드` [글자](주소) &lt;주소|글자&gt; &gt; 인용)을
    /// HTML(서식 붙여넣기용)과 일반 텍스트로 바꿉니다.
    /// </summary>
    public static class Markup
    {
        static readonly Regex LinkRx = new Regex(
            @"\[([^\]\n]+)\]\((https?://[^\s)]+)\)" +   // [글자](주소)
            @"|<(https?://[^|>\s]+)\|([^>\n]+)>" +       // <주소|글자>
            @"|<(https?://[^>\s]+)>" +                   // <주소>
            @"|(https?://[^\s<>]+)",                      // 주소
            RegexOptions.Compiled);

        static readonly Regex CodeRx = new Regex(@"`([^`\n]+)`", RegexOptions.Compiled);
        static readonly Regex BoldRx = new Regex(@"(?<![A-Za-z0-9*])\*(?=\S)([^*\n]+?)(?<=\S)\*(?![A-Za-z0-9*])", RegexOptions.Compiled);
        static readonly Regex ItalicRx = new Regex(@"(?<![A-Za-z0-9_])_(?=\S)([^_\n]+?)(?<=\S)_(?![A-Za-z0-9_])", RegexOptions.Compiled);
        static readonly Regex StrikeRx = new Regex(@"(?<![A-Za-z0-9~])~(?=\S)([^~\n]+?)(?<=\S)~(?![A-Za-z0-9~])", RegexOptions.Compiled);
        static readonly Regex PlaceholderRx = new Regex(@"\{\{([^{}\n]+)\}\}", RegexOptions.Compiled);

        public static readonly string[] AutoFields = { "클립보드", "날짜", "시간" };

        // ───────── 빈칸 ─────────

        public static List<string> Placeholders(string body)
        {
            var list = new List<string>();
            foreach (Match m in PlaceholderRx.Matches(body ?? ""))
            {
                var name = m.Groups[1].Value.Trim();
                if (!list.Contains(name)) list.Add(name);
            }
            return list;
        }

        public static Snippet FindSnippet(string name) =>
            Store.Data.Snippets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

        public static List<string> AskFields(string body) =>
            Placeholders(body).Where(p => !AutoFields.Contains(p) && FindSnippet(p) == null).ToList();

        // 서식 조각 자리표시:  + 번호 +  (HTML 변환 뒤 원본 HTML로 바꿔 끼움)
        const char TokenOpen = '', TokenClose = '';
        static readonly Regex TokenRx = new Regex("(\\d+)", RegexOptions.Compiled);

        /// <summary>빈칸·자동 빈칸·서식 조각·멘션을 채운 결과</summary>
        public class Composed
        {
            public string Text = "";
            public List<Snippet> Parts = new List<Snippet>();

            string Resolve(string s, Func<Snippet, string> pick) => TokenRx.Replace(s, m =>
            {
                int i = int.Parse(m.Groups[1].Value);
                if (i < 0 || i >= Parts.Count) return "";
                return pick(Parts[i]) ?? "";
            });

            /// <summary>붙여넣기용 HTML (서식 조각은 원본 HTML 그대로)</summary>
            public string ToHtml() => Resolve(Markup.ToHtml(Text), x => IsMention(x) ? MentionHtml(x) : x.Html);

            /// <summary>Slack 입력창이 읽는 서식 데이터(slack/texty). 멘션·목록·인용이 진짜로 들어감.</summary>
            public string ToTexty() => Markup.ToDelta(Text, Parts);

            public string ToPlain() => Resolve(Markup.ToPlain(Text), x => x.Plain);

            /// <summary>미리보기용 HTML (서식 조각은 글자만, 멘션은 링크 모양)</summary>
            public string ToPreviewHtml() => Resolve(Markup.ToHtml(Text), x =>
            {
                var t = WebUtility.HtmlEncode((x.Plain ?? "").Replace("\r\n", "\n").Trim()).Replace("\n", "<br>");
                if (IsMention(x)) return MentionId(x) != null ? "<a>" + t + "</a>" : t; // ID를 아는 멘션만 파랗게
                // 복사한 조각: 링크·굵게가 걸린 모양 그대로 보여 줌
                return HasRichHtml(x.Html) ? Markup.ToHtml(HtmlToMarkup(x.Html)) : t;
            });
        }

        static readonly Regex MentionIdRx = new Regex(
            @"data-(?:member-id|stringify-id|user-group-id|broadcast-id|id)\s*=\s*[""']([A-Za-z0-9]{4,})[""']", RegexOptions.Compiled);
        static readonly Regex MentionHrefRx = new Regex(@"/(?:team|usergroups)/([A-Z0-9]{6,})", RegexOptions.Compiled);

        /// <summary>등록한 멘션 HTML에서 Slack 사용자/그룹 ID를 찾습니다. 없으면 null.</summary>
        public static string MentionId(Snippet x)
        {
            if (!string.IsNullOrWhiteSpace(x?.Id)) return x.Id;
            string html = x?.Html ?? "";
            int end = html.IndexOf('>');
            string open = end > 0 ? html.Substring(0, end + 1) : html;
            var m = MentionIdRx.Match(open);
            if (m.Success) return m.Groups[1].Value;
            m = MentionHrefRx.Match(open);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// 붙여넣을 멘션 HTML. Slack 메시지에서 복사한 멘션은 링크(a) 모양이라 그대로 붙이면 링크로만 들어가므로,
        /// Slack 입력창이 멘션으로 알아보는 ts-mention 요소로 바꿔서 내보냅니다.
        /// </summary>
        public static string MentionHtml(Snippet x)
        {
            string html = x?.Html ?? "";
            if (html.TrimStart().StartsWith("<ts-mention", StringComparison.OrdinalIgnoreCase)) return html;
            string id = MentionId(x);
            if (id == null) return html.Length > 0 ? html : WebUtility.HtmlEncode(x?.Name ?? "");
            string label = WebUtility.HtmlEncode(x.Name ?? "");
            return "<ts-mention data-id=\"" + id + "\" data-label=\"" + label +
                   "\" spellcheck=\"false\" class=\"c-member_slug c-member_slug--link ts_tip_texty\" dir=\"ltr\">" +
                   label + "</ts-mention>";
        }

        public static bool IsMention(Snippet x) => x?.Name != null && x.Name.StartsWith("@") && x.Name.Length > 1;

        public static IEnumerable<Snippet> Mentions => Store.Data.Snippets.Where(IsMention);

        static string StripTokens(string s) => (s ?? "").Replace(TokenOpen.ToString(), "").Replace(TokenClose.ToString(), "");

        public static Composed Compose(string body, IDictionary<string, string> values, ClipData clip)
        {
            var result = new Composed();
            var now = DateTime.Now;
            clip ??= new ClipData();

            string Token(Snippet part)
            {
                result.Parts.Add(part);
                return TokenOpen + (result.Parts.Count - 1).ToString() + TokenClose;
            }

            string text = PlaceholderRx.Replace(StripTokens(body), m =>
            {
                var name = m.Groups[1].Value.Trim();
                switch (name)
                {
                    case "클립보드":
                        // 복사한 서식(링크·굵게·목록)은 이 앱의 문법으로 바꿔 넣음 → Slack 형식으로 다시 내보냄
                        return StripTokens(HasRichHtml(clip.Html) ? HtmlToMarkup(clip.Html) : clip.Text);
                    case "날짜": return now.ToString("yyyy-MM-dd (ddd)", new CultureInfo("ko-KR"));
                    case "시간": return now.ToString("HH:mm");
                }
                var snippet = FindSnippet(name);
                if (snippet != null) return Token(snippet);
                return values != null && values.TryGetValue(name, out var v) ? StripTokens(v) : "";
            });

            // 등록된 멘션(@이름)은 글자로 써도 진짜 멘션으로 바꿈 (긴 이름부터)
            foreach (var mention in Mentions.OrderByDescending(x => x.Name.Length).ToList())
            {
                var rx = new Regex(Regex.Escape(mention.Name) + @"(?![\w\-])", RegexOptions.IgnoreCase);
                text = rx.Replace(text, m => Token(mention));
            }

            result.Text = text.TrimEnd('\n', ' ');
            return result;
        }

        // ───────── 가져온 HTML 다루기 ─────────

        static readonly Regex JunkRx = new Regex(
            @"<!--.*?-->|<meta\b[^>]*>|<style\b.*?</style>|<script\b.*?</script>|</?(html|head|body)\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        static readonly Regex MentionRx = new Regex(
            @"<(a|ts-mention|span)\b[^>]*>\s*(@[^<\s][^<]*?)\s*</\1>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>클립보드 HTML 조각에서 주석·meta·style을 걷어냅니다.</summary>
        public static string CleanHtml(string html) => JunkRx.Replace(html ?? "", "").Trim();

        /// <summary>HTML 안의 멘션(@이름) 요소를 찾아 이름과 원본 HTML을 돌려줍니다.</summary>
        public static List<Snippet> FindMentions(string html)
        {
            var list = new List<Snippet>();
            foreach (Match m in MentionRx.Matches(html ?? ""))
            {
                var name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (name.Length < 2 || list.Any(x => x.Name == name)) continue;
                list.Add(new Snippet { Name = name, Html = m.Value, Plain = name });
            }
            return list;
        }

        /// <summary>Slack 서식 데이터(slack/texty)에서 멘션의 ID와 이름을 찾습니다.</summary>
        public static List<Snippet> MentionsFromTexty(string json)
        {
            var list = new List<Snippet>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("ops", out var ops) || ops.ValueKind != System.Text.Json.JsonValueKind.Array) return list;
                foreach (var op in ops.EnumerateArray())
                {
                    if (op.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!op.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!attrs.TryGetProperty("slackmention", out var sm) || sm.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    string id = sm.TryGetProperty("id", out var i) && i.ValueKind == System.Text.Json.JsonValueKind.String ? i.GetString() : null;
                    string label = sm.TryGetProperty("label", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.String ? l.GetString() : null;
                    if (string.IsNullOrWhiteSpace(label) && op.TryGetProperty("insert", out var ins) && ins.ValueKind == System.Text.Json.JsonValueKind.String)
                        label = ins.GetString();
                    label = (label ?? "").Trim();
                    if (string.IsNullOrEmpty(id) || label.Length < 2 || !label.StartsWith("@")) continue;
                    if (list.Any(x => x.Name == label)) continue;
                    list.Add(new Snippet { Name = label, Id = id, Plain = label, Html = "" });
                }
            }
            catch { }
            return list;
        }

        /// <summary>클립보드에서 멘션을 찾습니다: Slack 서식 데이터(ID 포함)가 먼저, 없으면 HTML.</summary>
        public static List<Snippet> FindMentions(ClipData clip)
        {
            var list = MentionsFromTexty(clip?.Texty);
            foreach (var h in FindMentions(clip?.Html))
            {
                if (list.Any(x => string.Equals(x.Name, h.Name, StringComparison.OrdinalIgnoreCase))) continue;
                h.Id = MentionId(h);
                list.Add(h);
            }
            return list;
        }

        /// <summary>클립보드에 ID가 있는 멘션이 있으면 조용히 등록/갱신합니다. 바뀐 게 있으면 true.</summary>
        public static bool LearnMentions(ClipData clip)
        {
            bool changed = false;
            foreach (var m in FindMentions(clip))
            {
                if (string.IsNullOrEmpty(m.Id)) continue;
                var existing = FindSnippet(m.Name);
                if (existing == null) { Store.Data.Snippets.Add(m); changed = true; }
                else if (existing.Id != m.Id) { existing.Id = m.Id; changed = true; }
            }
            return changed;
        }

        // ───────── Slack 서식 데이터 (slack/texty = Quill Delta) ─────────

        static readonly Regex StyleTagRx = new Regex("<(/?)(b|i|s)>", RegexOptions.Compiled);
        static readonly Regex DeltaEmojiRx = new Regex(@"(?<![\w:]):[a-z0-9_+\-]*[a-z][a-z0-9_+\-]*:(?![\w:])", RegexOptions.Compiled);

        /// <summary>
        /// 서식 문법을 Slack 입력창이 붙여넣을 때 읽는 Quill Delta JSON으로 바꿉니다.
        /// 굵게·기울임·취소선·코드·링크는 글자 속성으로, 목록·인용·코드 블록은 줄 끝 속성으로,
        /// 멘션은 slackmention(id, label)으로 들어갑니다.
        /// </summary>
        public static string ToDelta(string text, IList<Snippet> parts)
        {
            var ops = new List<Dictionary<string, object>>();

            void Add(object insert, Dictionary<string, object> attrs)
            {
                if (insert is string str && str.Length == 0) return;
                var op = new Dictionary<string, object> { ["insert"] = insert };
                if (attrs != null && attrs.Count > 0) op["attributes"] = attrs;
                ops.Add(op);
            }

            Dictionary<string, object> Attrs(bool b, bool i, bool s, bool code, string link)
            {
                var a = new Dictionary<string, object>();
                if (b) a["bold"] = true;
                if (i) a["italic"] = true;
                if (s) a["strike"] = true;
                if (code) a["code"] = true;
                if (link != null) a["link"] = link;
                return a;
            }

            // :이름: 은 Slack 이모지로 (Slack이 복사할 때 쓰는 것과 같은 slackemoji)
            void Plain(string t, Dictionary<string, object> attrs)
            {
                int pos = 0;
                foreach (Match m in DeltaEmojiRx.Matches(t))
                {
                    Add(t.Substring(pos, m.Index - pos), attrs);
                    Add(new Dictionary<string, object> { ["slackemoji"] = new Dictionary<string, object> { ["text"] = m.Value } },
                        attrs.Count > 0 ? new Dictionary<string, object>(attrs) : null);
                    pos = m.Index + m.Length;
                }
                Add(t.Substring(pos), attrs);
            }

            // 자리표시(멘션 등)를 풀면서 글자를 넣음
            void TextOps(string t, bool b, bool i, bool s, bool code, string link)
            {
                int pos = 0;
                foreach (Match m in TokenRx.Matches(t))
                {
                    Plain(t.Substring(pos, m.Index - pos), Attrs(b, i, s, code, link));
                    pos = m.Index + m.Length;
                    int n = int.Parse(m.Groups[1].Value);
                    if (parts == null || n < 0 || n >= parts.Count) continue;
                    var part = parts[n];
                    string id = IsMention(part) ? MentionId(part) : null;
                    if (id != null)
                    {
                        Add(part.Name, new Dictionary<string, object>
                        {
                            ["slackmention"] = new Dictionary<string, object>
                            {
                                ["id"] = id, ["label"] = part.Name, ["mention"] = id.StartsWith("BK"), ["unverified"] = false,
                            },
                        });
                    }
                    else Add(string.IsNullOrEmpty(part.Plain) ? part.Name ?? "" : part.Plain, Attrs(b, i, s, code, link));
                }
                Plain(t.Substring(pos), Attrs(b, i, s, code, link));
            }

            // *굵게* _기울임_ ~취소선~ : HTML 변환과 같은 규칙을 그대로 써서 결과가 어긋나지 않게 함
            void Styled(string seg, string link)
            {
                if (seg.Length == 0) return;
                string html = Styles(Enc(seg));
                bool b = false, i = false, s = false;
                int pos = 0;
                foreach (Match m in StyleTagRx.Matches(html))
                {
                    TextOps(WebUtility.HtmlDecode(html.Substring(pos, m.Index - pos)), b, i, s, false, link);
                    pos = m.Index + m.Length;
                    bool on = m.Groups[1].Value != "/";
                    switch (m.Groups[2].Value)
                    {
                        case "b": b = on; break;
                        case "i": i = on; break;
                        case "s": s = on; break;
                    }
                }
                TextOps(WebUtility.HtmlDecode(html.Substring(pos)), b, i, s, false, link);
            }

            void Inline(string t, string link)
            {
                int pos = 0;
                foreach (Match m in CodeRx.Matches(t))
                {
                    Styled(t.Substring(pos, m.Index - pos), link);
                    Add(m.Groups[1].Value, Attrs(false, false, false, true, link));
                    pos = m.Index + m.Length;
                }
                Styled(t.Substring(pos), link);
            }

            void LineOps(string line)
            {
                int pos = 0;
                foreach (Match m in LinkRx.Matches(line))
                {
                    Inline(line.Substring(pos, m.Index - pos), null);
                    string url, label;
                    if (m.Groups[1].Success) { label = m.Groups[1].Value; url = m.Groups[2].Value; }
                    else if (m.Groups[3].Success) { url = m.Groups[3].Value; label = m.Groups[4].Value; }
                    else if (m.Groups[5].Success) { url = label = m.Groups[5].Value; }
                    else { url = label = m.Groups[6].Value; }
                    Inline(label, url);
                    pos = m.Index + m.Length;
                }
                Inline(line.Substring(pos), null);
            }

            void NewLine(Dictionary<string, object> block) => Add("\n", block);

            var chunks = SplitCode(text);
            for (int c = 0; c < chunks.Count; c++)
            {
                if (chunks[c].Key)
                {
                    foreach (var line in chunks[c].Value.Split('\n'))
                    {
                        Add(line, null);
                        NewLine(new Dictionary<string, object> { ["code-block"] = true });
                    }
                    continue;
                }
                var p = chunks[c].Value;
                if (c > 0 && p.StartsWith("\n")) p = p.Substring(1);
                if (c < chunks.Count - 1 && p.EndsWith("\n")) p = p.Substring(0, p.Length - 1);
                if (p.Length == 0) continue;
                foreach (var raw in p.Split('\n'))
                {
                    if (raw.StartsWith(">"))
                    {
                        var q = raw.Substring(1);
                        if (q.StartsWith(" ")) q = q.Substring(1);
                        LineOps(q);
                        NewLine(new Dictionary<string, object> { ["blockquote"] = true });
                        continue;
                    }
                    var item = ListRx.Match(raw);
                    if (item.Success)
                    {
                        LineOps(item.Groups[2].Value);
                        var block = new Dictionary<string, object> { ["list"] = "bullet" };
                        int level = ListLevel(item.Groups[1].Value);
                        if (level > 0) block["indent"] = level;
                        NewLine(block);
                        continue;
                    }
                    LineOps(raw);
                    NewLine(null);
                }
            }
            if (ops.Count == 0) NewLine(null);
            return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["ops"] = ops });
        }

        // ───────── Chromium 웹 사용자 지정 데이터 (Slack 데스크톱 앱이 slack/texty를 주고받는 통) ─────────

        public const string TextyType = "slack/texty";

        /// <summary>
        /// 형식: uint32 payload 크기, uint32 항목 수, 항목마다 문자열 2개(종류, 값).
        /// 문자열은 uint32 UTF-16 글자 수 + UTF-16LE 데이터 + 4바이트 정렬 채움.
        /// </summary>
        public static byte[] WebCustomData(string type, string value)
        {
            var payload = new System.IO.MemoryStream();
            using (var w = new System.IO.BinaryWriter(payload, Encoding.Unicode, true))
            {
                void Str(string str)
                {
                    w.Write((uint)str.Length);
                    w.Write(Encoding.Unicode.GetBytes(str));
                    int pad = (4 - (str.Length * 2) % 4) % 4;
                    for (int k = 0; k < pad; k++) w.Write((byte)0);
                }
                w.Write((uint)1);
                Str(type);
                Str(value ?? "");
            }
            var body = payload.ToArray();
            var result = new byte[4 + body.Length];
            BitConverter.GetBytes((uint)body.Length).CopyTo(result, 0);
            body.CopyTo(result, 4);
            return result;
        }

        public static string ReadWebCustomData(byte[] data, string type)
        {
            try
            {
                if (data == null || data.Length < 8) return null;
                int size = (int)BitConverter.ToUInt32(data, 0);
                if (size < 4 || 4 + size > data.Length) return null;
                int pos = 4, end = 4 + size;
                uint count = BitConverter.ToUInt32(data, pos);
                pos += 4;
                string Str()
                {
                    int chars = (int)BitConverter.ToUInt32(data, pos);
                    pos += 4;
                    int bytes = chars * 2;
                    if (chars < 0 || pos + bytes > end) throw new InvalidOperationException();
                    string str = Encoding.Unicode.GetString(data, pos, bytes);
                    pos += bytes + (4 - bytes % 4) % 4;
                    return str;
                }
                for (uint k = 0; k < count && pos < end; k++)
                {
                    string key = Str();
                    string value = Str();
                    if (key == type) return value;
                }
            }
            catch { }
            return null;
        }

        // ───────── HTML ─────────

        // ───────── 복사한 HTML → 서식 문법 (입력칸에 붙여넣을 때 링크·굵게 등을 살림) ─────────

        static readonly Regex HtmlTagRx = new Regex(@"<(/?)([a-zA-Z][a-zA-Z0-9\-]*)([^>]*)>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex HrefRx = new Regex(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex EmojiAttrRx = new Regex(@"\bdata-stringify-emoji\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex AltRx = new Regex(@"\balt\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex RichRx = new Regex(@"<(a|b|strong|i|em|s|del|strike|code|pre|li|img)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>HTML 조각에 링크·굵게 같은 서식이 들어 있는지</summary>
        public static bool HasRichHtml(string html) => !string.IsNullOrEmpty(html) && RichRx.IsMatch(html);

        static string AttrValue(Match m)
        {
            if (!m.Success) return "";
            for (int i = 1; i < m.Groups.Count; i++)
                if (m.Groups[i].Success) return WebUtility.HtmlDecode(m.Groups[i].Value);
            return "";
        }

        /// <summary>
        /// 복사한 HTML을 이 앱의 서식 문법으로 바꿉니다: 링크 → [글자](주소), 굵게 → *글자*, 목록 → "- " 등.
        /// </summary>
        public static string HtmlToMarkup(string html)
        {
            html = JunkRx.Replace(html ?? "", "");
            var sb = new StringBuilder();
            var stack = new List<(string tag, int start, string href)>();
            int listDepth = 0;
            bool inPre = false;

            void Text(string raw)
            {
                if (raw.Length == 0) return;
                var t = WebUtility.HtmlDecode(raw).Replace(' ', ' ');
                if (!inPre)
                {
                    t = Regex.Replace(t, @"[ \t\r\n]+", " ");
                    if (sb.Length == 0 || sb[sb.Length - 1] == '\n' || sb[sb.Length - 1] == ' ') t = t.TrimStart(' ');
                }
                sb.Append(t);
            }

            void NewLine()
            {
                while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
            }

            void Close(string tag)
            {
                int idx = stack.FindLastIndex(x => x.tag == tag);
                if (idx < 0) return;
                var e = stack[idx];
                stack.RemoveRange(idx, stack.Count - idx);
                if (e.start > sb.Length) return;
                string inner = sb.ToString(e.start, sb.Length - e.start);
                sb.Length = e.start;
                string core = inner.Trim();
                if (core.Length == 0) { sb.Append(inner); return; }
                string lead = inner.Substring(0, inner.Length - inner.TrimStart().Length);
                string trail = inner.Substring(inner.TrimEnd().Length);
                bool plainOnly = core.Contains('\n');
                string result = core;
                switch (tag)
                {
                    case "a":
                        string url = (e.href ?? "").Trim().Replace(" ", "%20").Replace(")", "%29");
                        if (!plainOnly && (url.StartsWith("http://") || url.StartsWith("https://")))
                        {
                            string label = core.Replace("]", ")");
                            if (label == url || label.StartsWith("@")) result = label; // 멘션은 이름만 (등록돼 있으면 진짜 멘션으로 바뀜)
                            else result = "[" + label + "](" + url + ")";
                        }
                        break;
                    case "b": if (!plainOnly && !core.Contains("](") && !core.Contains("*")) result = "*" + core + "*"; break;
                    case "i": if (!plainOnly && !core.Contains("](") && !core.Contains("_")) result = "_" + core + "_"; break;
                    case "s": if (!plainOnly && !core.Contains("](") && !core.Contains("~")) result = "~" + core + "~"; break;
                    case "code": if (!plainOnly && !core.Contains("`")) result = "`" + core + "`"; break;
                }
                sb.Append(lead).Append(result).Append(trail);
            }

            int pos = 0;
            foreach (Match m in HtmlTagRx.Matches(html))
            {
                Text(html.Substring(pos, m.Index - pos));
                pos = m.Index + m.Length;
                bool closing = m.Groups[1].Value == "/";
                string tag = m.Groups[2].Value.ToLowerInvariant();
                string attrs = m.Groups[3].Value;
                switch (tag)
                {
                    case "strong": tag = "b"; break;
                    case "em": tag = "i"; break;
                    case "del": case "strike": tag = "s"; break;
                }
                switch (tag)
                {
                    case "br":
                        sb.Append('\n');
                        break;
                    case "p": case "div": case "tr": case "table": case "blockquote":
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        NewLine();
                        break;
                    case "ul": case "ol":
                        NewLine();
                        listDepth = Math.Max(0, listDepth + (closing ? -1 : 1));
                        break;
                    case "li":
                        NewLine();
                        if (!closing) sb.Append(new string('\t', Math.Max(0, listDepth - 1))).Append("- ");
                        break;
                    case "img":
                        string emoji = AttrValue(EmojiAttrRx.Match(attrs));
                        if (emoji.Length == 0) emoji = AttrValue(AltRx.Match(attrs));
                        sb.Append(emoji);
                        break;
                    case "pre":
                        NewLine();
                        sb.Append("```");
                        inPre = !closing;
                        if (closing) sb.Append('\n');
                        break;
                    case "a": case "b": case "i": case "s": case "code":
                        if (inPre) break;
                        if (closing) Close(tag);
                        else stack.Add((tag, sb.Length, tag == "a" ? AttrValue(HrefRx.Match(attrs)) : null));
                        break;
                }
            }
            Text(html.Substring(pos));

            string text = Regex.Replace(sb.ToString(), @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        static readonly Regex FenceRx = new Regex("```(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>```코드 블록```과 나머지를 나눕니다.</summary>
        static List<KeyValuePair<bool, string>> SplitCode(string raw)
        {
            var text = (raw ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            var parts = new List<KeyValuePair<bool, string>>();
            int pos = 0;
            foreach (Match m in FenceRx.Matches(text))
            {
                if (m.Groups[1].Value.Trim().Length == 0) continue;
                if (m.Index > pos) parts.Add(new KeyValuePair<bool, string>(false, text.Substring(pos, m.Index - pos)));
                parts.Add(new KeyValuePair<bool, string>(true, m.Groups[1].Value.Trim('\n')));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) parts.Add(new KeyValuePair<bool, string>(false, text.Substring(pos)));
            return parts;
        }

        public static string ToHtml(string text)
        {
            var parts = SplitCode(text);
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Key)
                {
                    sb.Append("<pre>").Append(WebUtility.HtmlEncode(parts[i].Value)).Append("</pre>");
                    continue;
                }
                var p = parts[i].Value;
                if (i > 0 && p.StartsWith("\n")) p = p.Substring(1);
                if (i < parts.Count - 1 && p.EndsWith("\n")) p = p.Substring(0, p.Length - 1);
                if (p.Length > 0) sb.Append(BlockHtml(p));
            }
            return sb.ToString();
        }

        public static string ToPlain(string text)
        {
            var sb = new StringBuilder();
            foreach (var part in SplitCode(text)) sb.Append(part.Key ? part.Value : PlainPart(part.Value));
            return sb.ToString().Replace("\n", Environment.NewLine);
        }

        static string BlockHtml(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            var quote = new List<string>();

            void FlushQuote()
            {
                if (quote.Count == 0) return;
                sb.Append("<blockquote>").Append(string.Join("<br>", quote)).Append("</blockquote>");
                quote.Clear();
            }

            // 글머리 목록: "- " 로 시작, 앞의 Tab 수만큼 들여쓰기 → 진짜 <ul><li> 로 내보냄
            int listDepth = 0;
            void CloseList()
            {
                while (listDepth > 0) { sb.Append("</li></ul>"); listDepth--; }
            }

            bool needBreak = false;
            foreach (var raw in lines)
            {
                if (raw.StartsWith(">"))
                {
                    if (listDepth > 0) { CloseList(); needBreak = false; }
                    var q = raw.Substring(1);
                    if (q.StartsWith(" ")) q = q.Substring(1);
                    quote.Add(Line(q));
                    needBreak = false;
                    continue;
                }
                if (quote.Count > 0) { FlushQuote(); needBreak = false; }

                var item = ListRx.Match(raw);
                if (item.Success)
                {
                    int depth = Math.Min(ListLevel(item.Groups[1].Value) + 1, listDepth + 1);
                    if (depth > listDepth) { sb.Append("<ul>"); listDepth++; }
                    else
                    {
                        sb.Append("</li>");
                        while (listDepth > depth) { sb.Append("</ul></li>"); listDepth--; }
                    }
                    sb.Append("<li>").Append(Line(item.Groups[2].Value));
                    needBreak = false;
                    continue;
                }
                if (listDepth > 0) { CloseList(); needBreak = false; }
                else if (needBreak) sb.Append("<br>");

                sb.Append(Line(raw));
                needBreak = true;
            }
            FlushQuote();
            CloseList();
            return sb.ToString();
        }

        public static readonly Regex ListRx = new Regex(@"^([\t ]*)[-*] (.*)$", RegexOptions.Compiled);

        /// <summary>들여쓰기 단계: Tab 1개 또는 공백 2칸이 한 단계 (최대 5단계)</summary>
        public static int ListLevel(string indent)
        {
            int tabs = 0, spaces = 0;
            foreach (var c in indent ?? "") { if (c == '\t') tabs++; else spaces++; }
            return Math.Min(5, tabs + spaces / 2);
        }

        // ───────── 스마트 따옴표와 기호 (Slack 설정과 같은 규칙) ─────────

        static readonly Regex QuoteRx = new Regex("\"([^\"\n]*)\"", RegexOptions.Compiled);

        static string Smart(string s)
        {
            if (string.IsNullOrEmpty(s) || !Store.Data.SmartSymbols) return s;
            s = s.Replace("<-", "←").Replace("->", "→").Replace("--", "—").Replace("...", "…");
            return QuoteRx.Replace(s, "“$1”");
        }

        static string Enc(string s) => WebUtility.HtmlEncode(Smart(s));

        static string Line(string line)
        {
            var sb = new StringBuilder();
            int pos = 0;
            foreach (Match m in LinkRx.Matches(line))
            {
                sb.Append(Inline(line.Substring(pos, m.Index - pos)));
                string url, label;
                if (m.Groups[1].Success) { label = m.Groups[1].Value; url = m.Groups[2].Value; }
                else if (m.Groups[3].Success) { url = m.Groups[3].Value; label = m.Groups[4].Value; }
                else if (m.Groups[5].Success) { url = label = m.Groups[5].Value; }
                else { url = label = m.Groups[6].Value; }
                sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">")
                  .Append(Inline(label)).Append("</a>");
                pos = m.Index + m.Length;
            }
            sb.Append(Inline(line.Substring(pos)));
            var s = sb.ToString();
            // 연속 공백 유지
            return s.Replace("  ", " &nbsp;");
        }

        static string Inline(string text)
        {
            var sb = new StringBuilder();
            int pos = 0;
            foreach (Match m in CodeRx.Matches(text))
            {
                sb.Append(Styles(Enc(text.Substring(pos, m.Index - pos))));
                sb.Append("<code>").Append(WebUtility.HtmlEncode(m.Groups[1].Value)).Append("</code>");
                pos = m.Index + m.Length;
            }
            sb.Append(Styles(Enc(text.Substring(pos))));
            return sb.ToString();
        }

        static string Styles(string s)
        {
            s = BoldRx.Replace(s, "<b>$1</b>");
            s = ItalicRx.Replace(s, "<i>$1</i>");
            s = StrikeRx.Replace(s, "<s>$1</s>");
            return s;
        }

        // ───────── 일반 텍스트 ─────────

        static string PlainPart(string text)
        {
            var s = (text ?? "").Replace("\r\n", "\n");
            s = LinkRx.Replace(s, m =>
            {
                if (m.Groups[1].Success) return m.Groups[1].Value == m.Groups[2].Value ? m.Groups[2].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})";
                if (m.Groups[3].Success) return $"{m.Groups[4].Value} ({m.Groups[3].Value})";
                if (m.Groups[5].Success) return m.Groups[5].Value;
                return m.Groups[6].Value;
            });
            s = CodeRx.Replace(s, "$1");
            s = BoldRx.Replace(s, "$1");
            s = ItalicRx.Replace(s, "$1");
            s = StrikeRx.Replace(s, "$1");
            s = PlainListRx.Replace(s, m => new string(' ', ListLevel(m.Groups[1].Value) * 4) + "• ");
            return s;
        }

        static readonly Regex PlainListRx = new Regex(@"^([\t ]*)[-*] ", RegexOptions.Compiled | RegexOptions.Multiline);

        public static string PreviewDocument(string text) =>
            "<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">" +
            "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><style>" +
            "body{font-family:'Segoe UI','Malgun Gothic',sans-serif;font-size:14px;margin:8px;color:#1d1c1d;line-height:1.45}" +
            "code{background:#f6f6f6;border:1px solid #ddd;border-radius:3px;padding:0 3px;color:#c0174f;font-family:Consolas,monospace}" +
            "blockquote{margin:4px 0;padding-left:10px;border-left:4px solid #ddd}a{color:#1264a3}" +
            "</style></head><body>" + ToHtml(text) + "</body></html>";

        // ───────── 초성 검색 ─────────

        static readonly char[] Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ".ToCharArray();

        public static string Chosung(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s ?? "")
            {
                if (c >= 0xAC00 && c <= 0xD7A3) sb.Append(Cho[(c - 0xAC00) / 588]);
                else sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        public static bool IsAllJamo(string s) => s.Length > 0 && s.All(c => c >= 0x3131 && c <= 0x314E);

        /// <summary>0 = 안 맞음, 클수록 잘 맞음</summary>
        public static int Score(Template t, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return 1;
            var q = query.Trim().ToLowerInvariant();
            var name = (t.Name ?? "").ToLowerInvariant();
            var key = (t.Keyword ?? "").ToLowerInvariant();
            var body = (t.Body ?? "").ToLowerInvariant();
            if (key.Length > 0 && key == q) return 100;
            if (key.StartsWith(q)) return 80;
            if (name.StartsWith(q)) return 70;
            if (name.Contains(q) || key.Contains(q)) return 60;
            if (IsAllJamo(q) && (Chosung(name).Contains(q) || Chosung(key).Contains(q))) return 50;
            if (body.Contains(q)) return 20;
            return 0;
        }
    }
}
