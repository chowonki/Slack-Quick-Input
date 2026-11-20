using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace SlackQuick
{
    /// <summary>템플릿 관리 · 서식 안내 · 설정 (Windows 11 설정 앱 스타일)</summary>
    sealed class EditorWindow : Window
    {
        readonly NavigationView _nav;
        readonly FrameworkElement _templatesPage, _guidePage, _settingsPage, _mentionsPage;

        // 멘션 페이지
        StackPanel _mentionList;
        InfoBar _mentionInfo;

        // 템플릿 페이지
        ListView _list;
        TextBox _name, _keyword, _body;
        ContentDialog _help;
        StackPanel _preview = new StackPanel { Spacing = 4 };
        FrameworkElement _detail, _empty;
        bool _loading;

        // 설정 페이지
        InfoBar _info;
        bool _capturing; // 단축키 입력칸에 포커스가 있는 동안 (전역 단축키를 잠시 꺼 둠)

        readonly DispatcherTimer _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

        Template Selected
        {
            get
            {
                int i = _list.SelectedIndex;
                return i >= 0 && i < Store.Data.Templates.Count ? Store.Data.Templates[i] : null;
            }
        }

        public EditorWindow()
        {
            Title = "SlackQuick";
            SystemBackdrop = new MicaBackdrop();
            Ui.SetIcon(this);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 820;
                presenter.PreferredMinimumHeight = 560;
            }

            _templatesPage = BuildTemplatesPage();
            _mentionsPage = BuildMentionsPage();
            _guidePage = BuildGuidePage();
            _settingsPage = BuildSettingsPage();

            _nav = new NavigationView
            {
                PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
                IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                IsSettingsVisible = false,
            };
            _nav.MenuItems.Add(NavItem("템플릿", "templates", "\uE8A5"));
            _nav.MenuItems.Add(NavItem("멘션", "mentions", "\uE716"));
            _nav.MenuItems.Add(NavItem("설정", "settings", "\uE713"));
            _nav.SelectionChanged += (s, e) =>
            {
                if (!(e.SelectedItem is NavigationViewItem item)) return;
                switch (item.Tag as string)
                {
                    case "mentions": RebuildMentions(); _nav.Content = _mentionsPage; break;
                    case "settings": _nav.Content = _settingsPage; break;
                    default: _nav.Content = _templatesPage; break;
                }
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Ui.At(Ui.TitleBar(this, "SlackQuick — Slack 빠른 입력"), 0));
            root.Children.Add(Ui.At(_nav, 1));
            Content = root;

            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Save(); };
            Closed += (s, e) =>
            {
                _saveTimer.Stop();
                Save();
                // 단축키 입력칸에 포커스를 둔 채 닫으면 단축키가 꺼진 채로 남으므로 다시 켬
                if (_capturing) { _capturing = false; Shell.RegisterHotkeys(); }
            };
            Activated += (s, e) =>
            {
                // 입력칸에 포커스를 둔 채 다른 창으로 가도 단축키는 계속 동작해야 함
                if (!_capturing) return;
                if (e.WindowActivationState == WindowActivationState.Deactivated) Shell.RegisterHotkeys();
                else Shell.UnregisterHotkeys();
            };

            Ui.Place(this, 1000, 700);
            RebuildList(0);
        }

        static NavigationViewItem NavItem(string text, string tag, string glyph) =>
            new NavigationViewItem { Content = text, Tag = tag, Icon = Ui.Icon(glyph) };

        public void Navigate(string page)
        {
            foreach (var list in new[] { _nav.MenuItems, _nav.FooterMenuItems })
                foreach (var o in list)
                    if (o is NavigationViewItem item && (item.Tag as string) == page)
                    {
                        _nav.SelectedItem = item;
                        return;
                    }
        }

        void Save()
        {
            try { Store.Save(); } catch { }
        }

        void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        static Button IconButton(string glyph, string label, Action action, bool accent = false)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(Ui.Icon(glyph, 14));
            if (label != null) content.Children.Add(new TextBlock { Text = label });
            var b = new Button { Content = content };
            if (accent)
            {
                var s = Ui.Res("AccentButtonStyle");
                if (s != null) b.Style = s;
            }
            b.Click += (o, e) => action();
            return b;
        }

        static ScrollViewer MakePage(string title, string subtitle, StackPanel body)
        {
            var stack = new StackPanel { Padding = new Thickness(28, 16, 28, 28), Spacing = 4, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            if (title != null) stack.Children.Add(Ui.Text(title, "TitleTextBlockStyle"));
            if (subtitle != null)
            {
                var sub = Ui.Text(subtitle, "SecondaryBodyTextStyle");
                sub.TextWrapping = TextWrapping.Wrap;
                sub.Margin = new Thickness(0, 0, 0, 8);
                stack.Children.Add(sub);
            }
            stack.Children.Add(body);
            return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ═════════ 템플릿 ═════════

        FrameworkElement BuildTemplatesPage()
        {
            // ── 왼쪽: 목록
            _list = new ListView { SelectionMode = ListViewSelectionMode.Single };
            _list.SelectionChanged += (s, e) => LoadCurrent();

            var add = IconButton("\uE710", "새 템플릿", Add, true);
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.Margin = new Thickness(0, 0, 0, 10);

            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.Children.Add(Ui.At(add, 0));
            var listCard = Ui.Card(_list);
            listCard.Padding = new Thickness(4);
            left.Children.Add(Ui.At(listCard, 1));

            // ── 오른쪽: 편집
            _name = new TextBox { Header = "이름", PlaceholderText = "예: 공지" };
            _body = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 170,
                MaxHeight = 320,
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                IsSpellCheckEnabled = false,
                PlaceholderText = "*[{{태그}}] {{타이틀}}*\n{{내용}}",
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_body, ScrollBarVisibility.Auto);
            FormatBar.EnableRichPaste(_body);
            _keyword = new TextBox { Header = "키워드", PlaceholderText = "예: ㄱㅈ", Width = 150 };
            ToolTipService.SetToolTip(_keyword, "팝업에서 이 글자를 그대로 치면 이 템플릿이 맨 위로 와요");
            _name.TextChanged += (s, e) => Edited();
            _keyword.TextChanged += (s, e) => Edited();
            _body.TextChanged += (s, e) => Edited();
            _body.PreviewKeyDown += (s, e) =>
            {
                if (ListKeys.Handle(_body, e.Key) || FormatBar.HandleKey(_body, e.Key)) e.Handled = true;
            };

            // 이름 옆 '더 보기': 복제 · 순서 · 삭제
            var more = new MenuFlyout();
            void MoreItem(string text, string glyph, Action action)
            {
                var item = new MenuFlyoutItem { Text = text, Icon = Ui.Icon(glyph) };
                item.Click += (s, e) => action();
                more.Items.Add(item);
            }
            MoreItem("복제", "\uE8C8", Duplicate);
            MoreItem("위로 이동", "\uE70E", () => Move(-1));
            MoreItem("아래로 이동", "\uE70D", () => Move(1));
            more.Items.Add(new MenuFlyoutSeparator());
            MoreItem("삭제", "\uE74D", Delete);
            var moreButton = new Button { Content = Ui.Icon("\uE712", 16), Flyout = more, VerticalAlignment = VerticalAlignment.Bottom, Height = 32, Width = 40, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(moreButton, "복제 · 순서 · 삭제");

            var nameRow = new Grid { ColumnSpacing = 8 };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameRow.Children.Add(Ui.At(_name, 0, 0));
            nameRow.Children.Add(Ui.At(_keyword, 0, 1));
            nameRow.Children.Add(Ui.At(moreButton, 0, 2));

            var editStack = new StackPanel { Spacing = 10 };
            editStack.Children.Add(nameRow);
            var bodyLabel = Ui.Text("내용", "BodyTextBlockStyle");
            bodyLabel.Margin = new Thickness(0, 4, 0, -4);
            editStack.Children.Add(bodyLabel);
            editStack.Children.Add(FormatBar.Build(() => _body, true));
            editStack.Children.Add(_body);
            var tip = Ui.Text("{{이름}}처럼 쓰면 붙여넣을 때 물어봐요.", "SecondaryTextStyle");
            tip.VerticalAlignment = VerticalAlignment.Center;
            var helpLink = new HyperlinkButton { Content = "서식 도움말", Padding = new Thickness(6, 2, 6, 2) };
            helpLink.Click += (s, e) => ShowHelp();
            var tipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            tipRow.Children.Add(tip);
            tipRow.Children.Add(helpLink);
            editStack.Children.Add(tipRow);

            var detailStack = new StackPanel { Spacing = 8 };
            detailStack.Children.Add(Ui.Card(editStack));
            var previewLabel = Ui.Text("Slack 미리보기", "SectionHeaderTextStyle");
            detailStack.Children.Add(previewLabel);
            var previewCard = Ui.Card(_preview);
            previewCard.MinHeight = 64;
            detailStack.Children.Add(previewCard);
            _detail = new ScrollViewer { Content = detailStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };

            var emptyStack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var emptyIcon = Ui.Icon("", 40);
            emptyStack.Children.Add(emptyIcon);
            emptyStack.Children.Add(Ui.Text("템플릿이 없어요. '새 템플릿'으로 추가해 보세요.", "SecondaryBodyTextStyle"));
            _empty = emptyStack;
            _empty.Visibility = Visibility.Collapsed;

            var grid = new Grid { Padding = new Thickness(24, 16, 12, 20), ColumnSpacing = 20 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(Ui.At(left, 0, 0));
            grid.Children.Add(Ui.At(_detail, 0, 1));
            grid.Children.Add(Ui.At(_empty, 0, 1));
            return grid;
        }

        static T Tip<T>(T element, string tip) where T : DependencyObject
        {
            ToolTipService.SetToolTip(element, tip);
            return element;
        }

        static UIElement RowContent(Template t)
        {
            var title = Ui.Text(string.IsNullOrWhiteSpace(t.Name) ? "(이름 없음)" : t.Name, "BodyStrongTextBlockStyle");
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap;
            var sub = Ui.Text(Ui.Normalize(t.Body).Replace("\n", " ⏎ "), "SecondaryTextStyle");
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.TextWrapping = TextWrapping.NoWrap;
            sub.MaxLines = 1;
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            texts.Children.Add(title);
            texts.Children.Add(sub);

            var grid = new Grid { ColumnSpacing = 8, Height = 52 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.At(texts, 0, 0));
            return grid;
        }

        void RebuildList(int select)
        {
            _loading = true;
            _list.Items.Clear();
            foreach (var t in Store.Data.Templates)
                _list.Items.Add(new ListViewItem { Content = RowContent(t), HorizontalContentAlignment = HorizontalAlignment.Stretch });
            int n = Store.Data.Templates.Count;
            _list.SelectedIndex = n == 0 ? -1 : Math.Max(0, Math.Min(select, n - 1));
            _loading = false;
            LoadCurrent();
        }

        void LoadCurrent()
        {
            if (_loading) return;
            var t = Selected;
            _detail.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
            _empty.Visibility = t == null ? Visibility.Visible : Visibility.Collapsed;
            _loading = true;
            _name.Text = t?.Name ?? "";
            _keyword.Text = t?.Keyword ?? "";
            _body.Text = t?.Body ?? "";
            _loading = false;
            UpdatePreview();
        }

        void UpdatePreview()
        {
            // 빈칸은 이름 그대로 보여 줘서 완성된 모양을 가늠할 수 있게
            string body = Ui.Normalize(_body.Text);
            var values = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var name in Markup.AskFields(body)) values[name] = name;
            Preview.Render(_preview, Markup.Compose(body, values, new ClipData { Text = "(복사한 내용)" }));
        }

        void Edited()
        {
            if (_loading) return;
            var t = Selected;
            if (t == null) return;
            t.Name = _name.Text ?? "";
            t.Keyword = (_keyword.Text ?? "").Trim();
            t.Body = Ui.Normalize(_body.Text);
            if (_list.SelectedItem is ListViewItem row) row.Content = RowContent(t);
            UpdatePreview();
            QueueSave();
        }

        void Add()
        {
            Store.Data.Templates.Add(new Template { Name = "새 템플릿", Body = "*{{타이틀}}*\n{{내용}}" });
            RebuildList(Store.Data.Templates.Count - 1);
            if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
            _name.Focus(FocusState.Programmatic);
            _name.SelectAll();
            QueueSave();
        }

        void Duplicate()
        {
            var c = Selected;
            if (c == null) return;
            int i = _list.SelectedIndex + 1;
            Store.Data.Templates.Insert(i, new Template { Name = c.Name + " 복사본", Keyword = c.Keyword, Body = c.Body });
            RebuildList(i);
            QueueSave();
        }

        async void Delete()
        {
            var c = Selected;
            if (c == null) return;
            int i = _list.SelectedIndex;
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "템플릿 삭제",
                Content = $"'{c.Name}' 템플릿을 삭제할까요?",
                PrimaryButtonText = "삭제",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            Store.Data.Templates.Remove(c);
            RebuildList(i);
            QueueSave();
        }

        void Move(int delta)
        {
            int i = _list.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= Store.Data.Templates.Count) return;
            var c = Store.Data.Templates[i];
            Store.Data.Templates.RemoveAt(i);
            Store.Data.Templates.Insert(j, c);
            RebuildList(j);
            QueueSave();
        }

        // ═════════ 멘션 ═════════

        FrameworkElement BuildMentionsPage()
        {
            var body = new StackPanel { Spacing = 4 };

            _mentionInfo = new InfoBar { IsOpen = false, IsClosable = true, Margin = new Thickness(0, 0, 0, 4) };
            body.Children.Add(_mentionInfo);

            var steps = new StackPanel { Spacing = 6 };
            steps.Children.Add(StepText("1", "Slack 입력창에 등록할 사람·그룹을 @로 넣고(목록에서 선택), 그 글자를 선택해 복사하세요. 보낸 메시지에서 복사해도 돼요."));
            steps.Children.Add(StepText("2", "아래 '클립보드에서 가져오기'를 누르세요."));
            steps.Children.Add(StepText("3", "이제 빈칸에 @이름을 쓰면 실제 멘션으로 붙여넣어져요."));
            body.Children.Add(Ui.Card(steps));

            var import = IconButton("\uE77F", "클립보드에서 가져오기", ImportMentions, true);
            import.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(import);

            body.Children.Add(Ui.Text("등록된 멘션", "SectionHeaderTextStyle"));
            _mentionList = new StackPanel { Spacing = 4 };
            body.Children.Add(_mentionList);

            return MakePage(null, "붙여넣은 @이름은 Slack에서 알림이 가지 않아요. Slack에서 복사한 진짜 멘션을 한 번 등록해 두면, 그 뒤로는 이름만 써도 멘션으로 들어가요.", body);
        }

        static Grid StepText(string number, string text)
        {
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = Ui.Text(text, "BodyTextBlockStyle");
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(Ui.At(Ui.Badge(number), 0, 0));
            grid.Children.Add(Ui.At(t, 0, 1));
            return grid;
        }

        void MentionInfo(InfoBarSeverity severity, string title, string message)
        {
            _mentionInfo.Severity = severity;
            _mentionInfo.Title = title;
            _mentionInfo.Message = message;
            _mentionInfo.IsOpen = true;
        }

        void ImportMentions()
        {
            var clip = Paster.GetClip();
            var found = Markup.FindMentions(clip);
            if (found.Count == 0)
            {
                MentionInfo(InfoBarSeverity.Warning, "멘션을 찾지 못했어요",
                    "Slack 입력창에 @이름을 넣고(목록에서 선택) 그 글자를 선택해 복사(Ctrl+C)한 뒤 다시 눌러 주세요.");
                return;
            }
            int added = 0, updated = 0;
            foreach (var m in found)
            {
                var existing = Markup.FindSnippet(m.Name);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(m.Html)) existing.Html = m.Html;
                    if (!string.IsNullOrEmpty(m.Id)) existing.Id = m.Id;
                    existing.Plain = m.Plain;
                    updated++;
                }
                else { Store.Data.Snippets.Add(m); added++; }
            }
            Save();
            RebuildMentions();
            MentionInfo(InfoBarSeverity.Success, "가져왔어요",
                $"새로 {added}개, 갱신 {updated}개: " + string.Join(", ", found.ConvertAll(x => x.Name)) +
                (found.Exists(x => string.IsNullOrEmpty(x.Id)) ? " — 일부는 Slack ID를 찾지 못해 글자로만 들어가요." : ""));
        }

        void RebuildMentions()
        {
            _mentionList.Children.Clear();
            var mentions = new System.Collections.Generic.List<Snippet>(Markup.Mentions);
            if (mentions.Count == 0)
            {
                _mentionList.Children.Add(Ui.Card(Ui.Text("아직 등록된 멘션이 없어요.", "SecondaryBodyTextStyle")));
                return;
            }
            foreach (var m in mentions)
            {
                var snippet = m;
                var remove = Tip(IconButton("\uE74D", null, () =>
                {
                    Store.Data.Snippets.Remove(snippet);
                    Save();
                    RebuildMentions();
                }), "삭제");
                var name = Ui.Text(m.Name + (Markup.MentionId(m) == null ? "   (ID 없음 — 글자로만 들어가요. 다시 가져와 주세요)" : ""), "BodyStrongTextBlockStyle");
                name.VerticalAlignment = VerticalAlignment.Center;
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = Ui.Icon("\uE77B", 18);
                icon.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(Ui.At(icon, 0, 0));
                row.Children.Add(Ui.At(name, 0, 1));
                row.Children.Add(Ui.At(remove, 0, 2));
                var card = Ui.Card(row);
                card.Padding = new Thickness(16, 8, 10, 8);
                _mentionList.Children.Add(card);
            }
        }

        async void ShowHelp()
        {
            try
            {
                if (_help == null)
                {
                    _guidePage.MaxHeight = 520;
                    _help = new ContentDialog { Title = "서식 도움말", Content = _guidePage, CloseButtonText = "닫기" };
                    _help.Resources["ContentDialogMaxWidth"] = 820.0;
                }
                _help.XamlRoot = Content.XamlRoot;
                await _help.ShowAsync();
            }
            catch { }
        }

        public void ShowHelpForTest() => ShowHelp();

        // ═════════ 서식 안내 ═════════

        FrameworkElement BuildGuidePage()
        {
            var body = new StackPanel { Spacing = 4 };

            body.Children.Add(Ui.Text("글자 서식", "SectionHeaderTextStyle"));
            var table = new StackPanel { Spacing = 0 };
            string[][] rows =
            {
                new[] { "*굵게*", "*굵게*" },
                new[] { "_기울임_", "_기울임_" },
                new[] { "~취소선~", "~취소선~" },
                new[] { "`코드`", "`코드`" },
                new[] { "```코드 블록```", "```코드 블록```" },
                new[] { "[글자](https://주소)", "[글자](https://slack.com)" },
                new[] { "<https://주소|글자>", "<https://slack.com|글자>" },
                new[] { "> 인용", "> 인용" },
                new[] { "- 목록\n(Tab) - 하위 목록", "- 목록\n\t- 하위 목록 (목록 줄에서 Tab 들여쓰기, Shift+Tab 내어쓰기)" },
                new[] { "\"따옴표\"  --  ...  ->  <-", "\"따옴표\"  --  ...  ->  <-" },
                new[] { ":white_check_mark:", ":white_check_mark: (Slack이 이모지로 바꿔요)" },
            };
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) table.Children.Add(Ui.Styled("DividerStyle"));
                table.Children.Add(GuideRow(rows[i][0], rows[i][1]));
            }
            var tableCard = Ui.Card(table);
            tableCard.Padding = new Thickness(16, 4, 16, 4);
            body.Children.Add(tableCard);

            body.Children.Add(Ui.Text("빈칸", "SectionHeaderTextStyle"));
            var blanks = new StackPanel { Spacing = 0 };
            blanks.Children.Add(GuideText("{{이름}}", "붙여넣기 전에 입력창이 떠요. 같은 이름은 한 번만 묻고, 마지막 입력값을 기억해요."));
            blanks.Children.Add(Ui.Styled("DividerStyle"));
            blanks.Children.Add(GuideText("{{클립보드}}", "복사해 둔 내용이 링크·이모지 서식까지 그대로 들어가요. (사내 페이지의 링크 복사 등)"));
            blanks.Children.Add(Ui.Styled("DividerStyle"));
            blanks.Children.Add(GuideText("@이름", "'멘션'에 등록한 이름은 어디에 쓰든 실제 멘션으로 들어가요."));
            blanks.Children.Add(Ui.Styled("DividerStyle"));
            blanks.Children.Add(GuideText("{{날짜}}  {{시간}}", "오늘 날짜와 현재 시간이 자동으로 들어가요."));
            var blanksCard = Ui.Card(blanks);
            blanksCard.Padding = new Thickness(16, 4, 16, 4);
            body.Children.Add(blanksCard);

            body.Children.Add(Ui.Text("예시", "SectionHeaderTextStyle"));
            const string sample = "*[{{태그}}] {{타이틀}}*\n{{내용}} [바로가기](https://slack.com)\n\n> {{전달내용}}";
            var sampleGrid = new Grid { ColumnSpacing = 20 };
            sampleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sampleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var code = new TextBlock { Text = sample, FontFamily = new FontFamily("Consolas, Malgun Gothic"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            var rendered = new StackPanel { Spacing = 4 };
            rendered.Loaded += (s, e) => Preview.Render(rendered, sample
                .Replace("{{태그}}", "공지").Replace("{{타이틀}}", "점검 안내")
                .Replace("{{내용}}", "오늘 18시에 점검이 있어요.").Replace("{{전달내용}}", "담당자 확인 부탁드려요."));
            sampleGrid.Children.Add(Ui.At(code, 0, 0));
            sampleGrid.Children.Add(Ui.At(rendered, 0, 1));
            body.Children.Add(Ui.Card(sampleGrid));

            return MakePage(null, "Slack에서 쓰는 것과 같은 기호로 서식을 넣어요. 붙여넣을 때 굵게·링크가 적용된 상태로 들어가요.", body);
        }

        static Grid GuideRow(string syntax, string sample)
        {
            var grid = new Grid { ColumnSpacing = 16, MinHeight = 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var code = Ui.Styled("CodeBorderStyle", new TextBlock { Text = syntax, FontFamily = new FontFamily("Consolas, Malgun Gothic"), IsTextSelectionEnabled = true });
            var rendered = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            rendered.Loaded += (s, e) => Preview.Render(rendered, sample);
            grid.Children.Add(Ui.At(code, 0, 0));
            grid.Children.Add(Ui.At(rendered, 0, 1));
            return grid;
        }

        static Grid GuideText(string syntax, string description)
        {
            var grid = new Grid { ColumnSpacing = 16, MinHeight = 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var code = Ui.Styled("CodeBorderStyle", new TextBlock { Text = syntax, FontFamily = new FontFamily("Consolas, Malgun Gothic"), IsTextSelectionEnabled = true });
            var text = Ui.Text(description, "BodyTextBlockStyle");
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 8, 0, 8);
            grid.Children.Add(Ui.At(code, 0, 0));
            grid.Children.Add(Ui.At(text, 0, 1));
            return grid;
        }

        // ═════════ 설정 ═════════

        FrameworkElement BuildSettingsPage()
        {
            var body = new StackPanel { Spacing = 4 };

            _info = new InfoBar { IsOpen = false, IsClosable = true, Margin = new Thickness(0, 0, 0, 4) };
            body.Children.Add(_info);

            body.Children.Add(SettingRow("\uE721", "템플릿 팝업 단축키", "칸을 누른 뒤 원하는 키 조합을 누르면 바뀌어요.",
                HotkeyBox(() => Store.Data.PopupHotkey, v => Store.Data.PopupHotkey = v)));

            body.Children.Add(SettingRow("\uE713", "설정 화면 단축키", "어디서든 이 설정 화면을 바로 열어요. 칸을 누른 뒤 Backspace를 누르면 꺼져요.",
                HotkeyBox(() => Store.Data.SettingsHotkey, v => Store.Data.SettingsHotkey = v, true)));

            bool auto = false;
            try { auto = Shell.AutoStart; } catch { }
            var autostart = new ToggleSwitch { IsOn = auto, MinWidth = 0, OnContent = "켬", OffContent = "끔" };
            autostart.Toggled += (s, e) =>
            {
                try { Shell.AutoStart = autostart.IsOn; }
                catch (Exception ex) { ShowInfo(InfoBarSeverity.Error, "시작 프로그램 설정 실패", ex.Message); }
            };
            body.Children.Add(SettingRow("\uE768", "Windows 시작 시 실행", "로그인하면 트레이에서 자동으로 실행돼요.", autostart));

            // ── 고급: 평소엔 접어 둠
            var advanced = new StackPanel { Spacing = 4 };
            advanced.Children.Add(SettingRow("\uE70F", "서식 직접 입력 단축키", "템플릿 없이 바로 글을 써서 붙여넣는 창을 열어요.",
                HotkeyBox(() => Store.Data.FormatHotkey, v => Store.Data.FormatHotkey = v)));

            var restore = new ToggleSwitch { IsOn = Store.Data.RestoreClipboard, MinWidth = 0, OnContent = "켬", OffContent = "끔" };
            restore.Toggled += (s, e) => { Store.Data.RestoreClipboard = restore.IsOn; Save(); };
            advanced.Children.Add(SettingRow("\uE77F", "클립보드 복원", "붙여넣은 뒤 원래 복사해 둔 내용을 되돌려 놔요.", restore));

            var smart = new ToggleSwitch { IsOn = Store.Data.SmartSymbols, MinWidth = 0, OnContent = "켬", OffContent = "끔" };
            smart.Toggled += (s, e) => { Store.Data.SmartSymbols = smart.IsOn; Save(); };
            advanced.Children.Add(SettingRow("\uE8D2", "스마트 따옴표와 기호", "\" \" → “ ”,  -- → —,  ... → …,  -> → →,  <- → ←", smart));

            var folder = new Button { Content = "폴더 열기" };
            folder.Click += (s, e) => Shell.OpenFolder();
            advanced.Children.Add(SettingRow("\uE8B7", "저장 위치", Store.FilePath, folder));

            body.Children.Add(new Expander
            {
                Header = "고급 설정",
                Content = advanced,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 8),
            });

            var quit = new Button { Content = "SlackQuick 종료" };
            quit.Click += (s, e) => Shell.Exit();
            var quitNote = Ui.Text("창을 닫아도 트레이에서 계속 실행돼요.", "SecondaryTextStyle");
            quitNote.VerticalAlignment = VerticalAlignment.Center;
            var quitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
            quitRow.Children.Add(quit);
            quitRow.Children.Add(quitNote);
            body.Children.Add(quitRow);

            return MakePage(null, null, body);
        }

        void ShowInfo(InfoBarSeverity severity, string title, string message)
        {
            _info.Severity = severity;
            _info.Title = title;
            _info.Message = message;
            _info.IsOpen = true;
        }

        static Border SettingRow(string glyph, string title, string description, FrameworkElement control)
        {
            var icon = Ui.Icon(glyph, 20);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(4, 0, 4, 0);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            texts.Children.Add(Ui.Text(title, "BodyTextBlockStyle"));
            var desc = Ui.Text(description, "SecondaryTextStyle");
            desc.TextWrapping = TextWrapping.Wrap;
            texts.Children.Add(desc);

            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Right;

            var grid = new Grid { ColumnSpacing = 16, MinHeight = 40 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.At(icon, 0, 0));
            grid.Children.Add(Ui.At(texts, 0, 1));
            grid.Children.Add(Ui.At(control, 0, 2));
            return Ui.Card(grid);
        }

        /// <summary>누른 키 조합을 그대로 받아 적는 단축키 입력칸</summary>
        /// <param name="optional">true면 Backspace·Delete로 비워서 끌 수 있음</param>
        TextBox HotkeyBox(Func<string> get, Action<string> set, bool optional = false)
        {
            var box = new TextBox
            {
                Text = get(),
                IsReadOnly = true,
                Width = 200,
                TextAlignment = TextAlignment.Center,
                PlaceholderText = optional ? "사용 안 함" : "키 조합을 누르세요",
            };
            ToolTipService.SetToolTip(box, "누른 뒤 Ctrl·Alt·Shift·Win 과 함께 키를 누르세요" + (optional ? " (Backspace: 끄기)" : ""));

            box.GotFocus += (s, e) =>
            {
                _capturing = true;
                Shell.UnregisterHotkeys(); // 입력 중에는 기존 단축키가 반응하지 않게
            };
            box.LostFocus += (s, e) =>
            {
                _capturing = false;
                box.Text = get();
                Shell.RegisterHotkeys();
            };
            box.PreviewKeyDown += (s, e) =>
            {
                var key = e.Key;
                if (key == VirtualKey.Tab) return;
                e.Handled = true;
                switch (key)
                {
                    case VirtualKey.Control: case VirtualKey.LeftControl: case VirtualKey.RightControl:
                    case VirtualKey.Shift: case VirtualKey.LeftShift: case VirtualKey.RightShift:
                    case VirtualKey.Menu: case VirtualKey.LeftMenu: case VirtualKey.RightMenu:
                    case VirtualKey.LeftWindows: case VirtualKey.RightWindows:
                        return;
                }

                string combo = "";
                if (Ui.Ctrl) combo += "Ctrl+";
                if (Ui.Alt) combo += "Alt+";
                if (Ui.Shift) combo += "Shift+";
                if (Ui.IsDown(VirtualKey.LeftWindows) || Ui.IsDown(VirtualKey.RightWindows)) combo += "Win+";
                if (optional && combo.Length == 0 && (key == VirtualKey.Back || key == VirtualKey.Delete))
                {
                    set("");
                    box.Text = "";
                    Save();
                    ShowInfo(InfoBarSeverity.Success, "단축키를 껐어요", "다시 쓰려면 칸을 누른 뒤 원하는 키 조합을 누르세요.");
                    return;
                }
                if (combo.Length == 0)
                {
                    ShowInfo(InfoBarSeverity.Warning, "수정키가 필요해요", "Ctrl·Alt·Shift·Win 중 하나 이상과 함께 눌러 주세요.");
                    return;
                }
                string name = key.ToString();
                if (name.StartsWith("Number") && name.Length == 7) name = name.Substring(6);
                combo += name;

                if (!Shell.TryParseHotkey(combo, out _, out _))
                {
                    ShowInfo(InfoBarSeverity.Warning, "쓸 수 없는 키예요", combo);
                    return;
                }

                string before = get();
                set(combo);
                var failed = Shell.RegisterHotkeys();
                Shell.UnregisterHotkeys(); // 입력칸에 포커스가 있는 동안은 꺼 둠
                // 이번에 누른 조합이 실패했을 때만 되돌림 (다른 단축키의 실패 때문에 막히지 않게)
                if (failed != null && (", " + failed + ", ").Contains(", " + combo + ", "))
                {
                    set(before);
                    ShowInfo(InfoBarSeverity.Error, "단축키 등록 실패", $"{combo} 는 다른 프로그램이나 SlackQuick의 다른 기능이 쓰는 중이에요. 다른 조합을 눌러 주세요.");
                    return;
                }
                box.Text = combo;
                Save();
                ShowInfo(InfoBarSeverity.Success, "단축키를 바꿨어요", combo);
            };
            return box;
        }
    }
}
