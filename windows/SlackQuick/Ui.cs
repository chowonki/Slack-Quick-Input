using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace SlackQuick
{
    /// <summary>공통 UI 도우미</summary>
    static class Ui
    {
        public static IntPtr Hwnd(Window w) => WinRT.Interop.WindowNative.GetWindowHandle(w);

        public static Style Res(string key)
        {
            try
            {
                if (Application.Current.Resources.TryGetValue(key, out object v)) return v as Style;
            }
            catch { }
            return null;
        }

        public static TextBlock Text(string text, string style = null)
        {
            var tb = new TextBlock { Text = text ?? "" };
            var s = style == null ? null : Res(style);
            if (s != null) tb.Style = s;
            return tb;
        }

        public static FontIcon Icon(string glyph, double size = 16) => new FontIcon { Glyph = glyph, FontSize = size };

        public static Border Styled(string style, UIElement child = null)
        {
            var b = new Border { Child = child };
            var s = Res(style);
            if (s != null) b.Style = s;
            return b;
        }

        public static Border Card(UIElement child) => Styled("CardBorderStyle", child);

        public static Border Badge(string text) => Styled("BadgeBorderStyle", Text(text, "BadgeTextStyle"));

        public static Border KeyCap(string text) => Styled("KeyCapBorderStyle", Text(text, "CaptionTextBlockStyle"));

        public static Border IconTile(string glyph)
        {
            var icon = Icon(glyph, 16);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            return Styled("IconTileBorderStyle", icon);
        }

        /// <summary>"Ctrl+Enter 붙여넣기" 같은 안내 한 묶음</summary>
        public static StackPanel Hint(string keys, string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var k in keys.Split('+')) sp.Children.Add(KeyCap(k));
            var t = Text(label, "SecondaryTextStyle");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(2, 0, 10, 0);
            sp.Children.Add(t);
            return sp;
        }

        public static T At<T>(T element, int row, int column = 0, int columnSpan = 1) where T : FrameworkElement
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            if (columnSpan > 1) Grid.SetColumnSpan(element, columnSpan);
            return element;
        }

        public static bool IsDown(VirtualKey key) =>
            (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        public static bool Ctrl => IsDown(VirtualKey.Control);
        public static bool Shift => IsDown(VirtualKey.Shift);
        public static bool Alt => IsDown(VirtualKey.Menu);

        public static void SetIcon(Window w)
        {
            try
            {
                if (Shell.IconLarge != IntPtr.Zero)
                    w.AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(Shell.IconLarge));
            }
            catch { }
        }

        public static double Scale(Window w)
        {
            uint dpi = Native.GetDpiForWindow(Hwnd(w));
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }

        /// <summary>마우스가 있는 모니터 기준으로 크기·위치를 잡습니다 (크기는 DIP).</summary>
        public static void Place(Window w, double width, double height, double topFraction = -1)
        {
            double s = Scale(w);
            Native.GetCursorPos(out var pt);
            var area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest);
            var wa = area.WorkArea;
            int pw = Math.Min((int)(width * s), wa.Width - 20);
            int ph = Math.Min((int)(height * s), (int)(wa.Height * 0.92));
            int x = wa.X + (wa.Width - pw) / 2;
            int y = topFraction < 0 ? wa.Y + (wa.Height - ph) / 2 : wa.Y + (int)(wa.Height * topFraction);
            w.AppWindow.MoveAndResize(new RectInt32(x, y, pw, ph));
        }

        public static void RoundCorners(Window w)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                Native.DwmSetWindowAttribute(Hwnd(w), 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
            }
            catch { }
        }

        /// <summary>창 제목 표시줄(아이콘 + 제목)을 만들어 Mica 위에 얹습니다.</summary>
        public static Grid TitleBar(Window w, string title)
        {
            var bar = new Grid { Height = 44, Padding = new Thickness(16, 0, 0, 0), ColumnSpacing = 10 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Icon("", 15); // Message
            icon.VerticalAlignment = VerticalAlignment.Center;
            var text = Text(title, "CaptionTextBlockStyle");
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            bar.Children.Add(At(icon, 0, 0));
            bar.Children.Add(At(text, 0, 1));
            w.ExtendsContentIntoTitleBar = true;
            bar.Loaded += (o, e) => { try { w.SetTitleBar(bar); } catch { } };
            return bar;
        }

        public static string Normalize(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>Markup.ToHtml 결과(b,i,s,code,a,br,blockquote)를 Slack 느낌의 미리보기로 그립니다.</summary>
    static class Preview
    {
        static readonly Regex TagRx = new Regex(@"<(/?)(\w+)[^>]*>");
        static readonly Regex EmojiRx = new Regex(@":([a-z0-9_+\-]+):");

        static readonly Dictionary<string, string> Emoji = new Dictionary<string, string>
        {
            ["white_check_mark"] = "✅", ["heavy_check_mark"] = "✔️", ["x"] = "❌", ["link"] = "🔗", ["warning"] = "⚠️",
            ["tada"] = "🎉", ["pray"] = "🙏", ["fire"] = "🔥", ["eyes"] = "👀", ["+1"] = "👍", ["thumbsup"] = "👍",
            ["-1"] = "👎", ["bulb"] = "💡", ["memo"] = "📝", ["pushpin"] = "📌", ["rocket"] = "🚀", ["calendar"] = "📅",
            ["bell"] = "🔔", ["point_right"] = "👉", ["exclamation"] = "❗", ["question"] = "❓", ["smile"] = "😄",
            ["pencil"] = "✏️", ["mega"] = "📣", ["loudspeaker"] = "📢", ["star"] = "⭐", ["heart"] = "❤️",
            ["ok_hand"] = "👌", ["clap"] = "👏", ["wave"] = "👋", ["rotating_light"] = "🚨", ["red_circle"] = "🔴",
            ["large_blue_circle"] = "🔵", ["phone"] = "☎️", ["email"] = "✉️", ["clock3"] = "🕒", ["moneybag"] = "💰",
            ["car"] = "🚗", ["package"] = "📦", ["hourglass"] = "⌛", ["sparkles"] = "✨", ["speech_balloon"] = "💬",
        };

        static string WithEmoji(string s) =>
            EmojiRx.Replace(s, m => Emoji.TryGetValue(m.Groups[1].Value, out var e) ? e : m.Value);

        public static void Render(StackPanel panel, string markup) =>
            RenderHtml(panel, Markup.ToHtml(Ui.Normalize(markup)));

        public static void Render(StackPanel panel, Markup.Composed content) =>
            RenderHtml(panel, content.ToPreviewHtml());

        static void RenderHtml(StackPanel panel, string html)
        {
            panel.Children.Clear();
            bool dark = panel.ActualTheme == ElementTheme.Dark;
            var codeBrush = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(255, 0xE8, 0x91, 0x2D)
                : Windows.UI.Color.FromArgb(255, 0xC0, 0x17, 0x4F));

            var open = new Dictionary<string, int>();
            Paragraph para = null;
            bool inQuote = false;
            TextBlock pre = null;
            int listDepth = 0;
            string bullet = null;

            bool Has(string t) => open.TryGetValue(t, out var n) && n > 0;

            void EndBlock() => para = null;

            Paragraph Para()
            {
                if (para != null) return para;
                para = new Paragraph();
                var rtb = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
                rtb.Blocks.Add(para);
                if (listDepth > 0)
                {
                    rtb.Margin = new Thickness(6 + 20 * (listDepth - 1), 0, 0, 0);
                    if (bullet != null) para.Inlines.Add(new Run { Text = bullet + "  " });
                    bullet = null;
                }
                if (inQuote)
                {
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.Children.Add(Ui.At(Ui.Styled("QuoteBarStyle"), 0, 0));
                    g.Children.Add(Ui.At(rtb, 0, 1));
                    panel.Children.Add(g);
                }
                else panel.Children.Add(rtb);
                return para;
            }

            void AddText(string s)
            {
                if (s.Length == 0) return;
                if (pre != null)
                {
                    pre.Text += WebUtility.HtmlDecode(s);
                    return;
                }
                var run = new Run { Text = WithEmoji(WebUtility.HtmlDecode(s.Replace("&nbsp;", " "))) };
                if (Has("b")) run.FontWeight = FontWeights.Bold;
                if (Has("i")) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                if (Has("s")) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                if (Has("code"))
                {
                    run.FontFamily = new FontFamily("Consolas");
                    run.Foreground = codeBrush;
                }
                if (Has("a"))
                {
                    var link = new Hyperlink();
                    link.Inlines.Add(run);
                    Para().Inlines.Add(link);
                }
                else Para().Inlines.Add(run);
            }

            int pos = 0;
            foreach (Match m in TagRx.Matches(html))
            {
                AddText(html.Substring(pos, m.Index - pos));
                pos = m.Index + m.Length;

                bool closing = m.Groups[1].Value == "/";
                string tag = m.Groups[2].Value.ToLowerInvariant();

                if (tag == "br")
                {
                    Para().Inlines.Add(new LineBreak());
                    continue;
                }
                if (tag == "ul")
                {
                    EndBlock();
                    listDepth = Math.Max(0, listDepth + (closing ? -1 : 1));
                    continue;
                }
                if (tag == "li")
                {
                    EndBlock();
                    if (!closing) bullet = listDepth <= 1 ? "•" : listDepth == 2 ? "◦" : "▪";
                    continue;
                }
                if (tag == "pre")
                {
                    EndBlock();
                    if (closing) pre = null;
                    else
                    {
                        pre = new TextBlock { FontFamily = new FontFamily("Consolas, Malgun Gothic"), TextWrapping = TextWrapping.Wrap };
                        var box = Ui.Styled("CodeBorderStyle", pre);
                        box.HorizontalAlignment = HorizontalAlignment.Stretch;
                        box.Padding = new Thickness(10, 8, 10, 8);
                        panel.Children.Add(box);
                    }
                    continue;
                }
                if (tag == "blockquote")
                {
                    EndBlock();
                    inQuote = !closing;
                    continue;
                }
                open.TryGetValue(tag, out var c);
                open[tag] = closing ? Math.Max(0, c - 1) : c + 1;
            }
            AddText(html.Substring(pos));

            if (panel.Children.Count == 0)
                panel.Children.Add(Ui.Text("입력하면 여기에 Slack에 들어갈 모양이 보여요.", "SecondaryTextStyle"));
        }
    }

    /// <summary>목록 줄에서 Tab=들여쓰기, Shift+Tab=내어쓰기, Enter=다음 항목 이어 쓰기</summary>
    static class ListKeys
    {
        static readonly System.Text.RegularExpressions.Regex ItemRx =
            new System.Text.RegularExpressions.Regex(@"^(\t*)([-*]) (.*)$");

        public static bool Handle(TextBox box, VirtualKey key)
        {
            if (box == null || Ui.Ctrl || Ui.Alt) return false;
            if (key != VirtualKey.Tab && key != VirtualKey.Enter) return false;
            if (box.SelectionLength > 0) return false;

            string text = box.Text ?? "";
            int caret = Math.Min(box.SelectionStart, text.Length);
            int lineStart = caret == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, caret - 1) + 1;
            int lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, caret);
            if (lineEnd < 0) lineEnd = text.Length;
            var m = ItemRx.Match(text.Substring(lineStart, lineEnd - lineStart));
            if (!m.Success) return false;

            int tabs = m.Groups[1].Length;
            bool empty = m.Groups[3].Value.Trim().Length == 0;

            void Replace(int start, int length, string with, int newCaret)
            {
                box.SelectionStart = start;
                box.SelectionLength = length;
                box.SelectedText = with;
                box.SelectionStart = Math.Max(0, newCaret);
                box.SelectionLength = 0;
            }

            if (key == VirtualKey.Tab)
            {
                if (Ui.Shift)
                {
                    if (tabs > 0) Replace(lineStart, 1, "", caret - 1);
                }
                else if (tabs < 5) Replace(lineStart, 0, "\t", caret + 1);
                return true;
            }

            // Enter
            if (Ui.Shift) return false;
            if (empty)
            {
                // 빈 항목에서 Enter: 한 단계 내어쓰고, 맨 앞이면 목록을 끝냄
                if (tabs > 0) Replace(lineStart, 1, "", caret - 1);
                else Replace(lineStart, lineEnd - lineStart, "", lineStart);
                return true;
            }
            if (caret < lineStart + tabs + 2) return false;
            string next = "\r" + new string('\t', tabs) + m.Groups[2].Value + " ";
            Replace(caret, 0, next, caret + next.Length);
            return true;
        }
    }

    /// <summary>굵게·기울임·링크 등 서식 문법을 TextBox에 넣어 주는 도구 모음</summary>
    static class FormatBar
    {
        public static StackPanel Build(Func<TextBox> target, bool withFields)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            bar.Children.Add(Tool("", "굵게  *글자*  (Ctrl+B)", () => Wrap(target(), "*", "*")));
            bar.Children.Add(Tool("", "기울임  _글자_  (Ctrl+I)", () => Wrap(target(), "_", "_")));
            bar.Children.Add(Tool("", "링크  [글자](주소)  (Ctrl+K)", () => InsertLink(target())));
            bar.Children.Add(Tool("", "목록  줄 앞에 -", () => PrefixLine(target(), "- ")));

            // 덜 쓰는 서식은 '더 보기'에
            var extra = new MenuFlyout();
            void Extra(string label, Action action)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (s, e) => action();
                extra.Items.Add(item);
            }
            Extra("취소선   ~글자~", () => Wrap(target(), "~", "~"));
            Extra("코드   `글자`", () => Wrap(target(), "`", "`"));
            Extra("코드 블록   ```글자```", () => Wrap(target(), "```", "```"));
            Extra("인용   > 글자", () => PrefixLine(target(), "> "));
            var moreButton = new Button { Content = Ui.Icon("\uE712", 14), Flyout = extra, IsTabStop = false };
            var moreStyle = Ui.Res("ToolButtonStyle");
            if (moreStyle != null) moreButton.Style = moreStyle;
            ToolTipService.SetToolTip(moreButton, "서식 더 보기");
            bar.Children.Add(moreButton);

            if (withFields)
            {
                var flyout = new MenuFlyout();
                void Add(string label, string insert, bool select)
                {
                    var item = new MenuFlyoutItem { Text = label };
                    item.Click += (s, e) => Insert(target(), insert, select ? 2 : -1, select ? insert.Length - 4 : 0);
                    flyout.Items.Add(item);
                }
                Add("빈칸 (붙여넣을 때 물어봄)", "{{이름}}", true);
                flyout.Items.Add(new MenuFlyoutSeparator());
                Add("클립보드 내용", "{{클립보드}}", false);
                Add("오늘 날짜", "{{날짜}}", false);
                Add("현재 시간", "{{시간}}", false);

                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(Ui.Icon("", 12));
                content.Children.Add(new TextBlock { Text = "빈칸" });
                bar.Children.Add(new DropDownButton
                {
                    Content = content,
                    Flyout = flyout,
                    Margin = new Thickness(8, 0, 0, 0),
                    Height = 32,
                    IsTabStop = false,
                });
            }
            return bar;
        }

        static Button Tool(string glyph, string tip, Action action)
        {
            object content = glyph[0] >= '\uE000' && glyph[0] <= '\uF8FF'
                ? (object)Ui.Icon(glyph, 14)
                : new TextBlock { Text = glyph, FontSize = 15 };
            var b = new Button { Content = content, IsTabStop = false, AllowFocusOnInteraction = false };
            var s = Ui.Res("ToolButtonStyle");
            if (s != null) b.Style = s;
            ToolTipService.SetToolTip(b, tip);
            b.Click += (o, e) => action();
            return b;
        }

        public static void Insert(TextBox box, string text, int selectOffset, int selectLength)
        {
            if (box == null) return;
            int start = box.SelectionStart;
            box.SelectedText = text;
            if (selectOffset >= 0)
            {
                box.SelectionStart = start + selectOffset;
                box.SelectionLength = selectLength;
            }
            else
            {
                box.SelectionStart = start + text.Length;
                box.SelectionLength = 0;
            }
            box.Focus(FocusState.Programmatic);
        }

        public static void Wrap(TextBox box, string pre, string post)
        {
            if (box == null) return;
            int start = box.SelectionStart, len = box.SelectionLength;
            string sel = box.SelectedText ?? "";
            box.SelectedText = pre + sel + post;
            if (len == 0)
            {
                box.SelectionStart = start + pre.Length;
                box.SelectionLength = 0;
            }
            else
            {
                box.SelectionStart = start;
                box.SelectionLength = pre.Length + len + post.Length;
            }
            box.Focus(FocusState.Programmatic);
        }

        public static void InsertLink(TextBox box)
        {
            if (box == null) return;
            string sel = box.SelectedText ?? "";
            string label = sel.Length == 0 ? "글자" : sel;
            Insert(box, "[" + label + "](https://)", label.Length + 3, "https://".Length);
        }

        public static void PrefixLine(TextBox box, string prefix)
        {
            if (box == null) return;
            string text = box.Text ?? "";
            int caret = Math.Min(box.SelectionStart, text.Length);
            int lineStart = caret == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, caret - 1) + 1;
            box.SelectionStart = lineStart;
            box.SelectionLength = 0;
            box.SelectedText = prefix;
            box.SelectionStart = caret + prefix.Length;
            box.SelectionLength = 0;
            box.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// 입력칸에 Ctrl+V 할 때, 복사한 내용에 링크·굵게 같은 서식이 있으면
        /// [글자](주소) · *굵게* 같은 문법으로 바꿔 넣어 서식이 사라지지 않게 합니다.
        /// </summary>
        public static void EnableRichPaste(TextBox box)
        {
            box.Paste += (s, e) =>
            {
                try
                {
                    var clip = Paster.GetClip();
                    if (Markup.LearnMentions(clip)) Store.Save(); // Slack에서 복사한 멘션은 자동 등록
                    if (!Markup.HasRichHtml(clip.Html)) return; // 일반 글자는 기본 붙여넣기
                    string text = Markup.HtmlToMarkup(clip.Html);
                    if (text.Length == 0) return;
                    e.Handled = true;
                    int start = box.SelectionStart;
                    text = text.Replace("\n", "\r");
                    box.SelectedText = text;
                    box.SelectionStart = start + text.Length;
                    box.SelectionLength = 0;
                }
                catch { }
            };
        }

        /// <summary>서식 단축키 처리. 처리했으면 true.</summary>
        public static bool HandleKey(TextBox box, VirtualKey key)
        {
            if (box == null || !Ui.Ctrl || Ui.Alt) return false;
            bool shift = Ui.Shift;
            if (!shift && key == VirtualKey.B) { Wrap(box, "*", "*"); return true; }
            if (!shift && key == VirtualKey.I) { Wrap(box, "_", "_"); return true; }
            if (!shift && key == VirtualKey.K) { InsertLink(box); return true; }
            if (shift && key == VirtualKey.X) { Wrap(box, "~", "~"); return true; }
            if (shift && key == VirtualKey.C) { Wrap(box, "`", "`"); return true; }
            return false;
        }
    }
}
