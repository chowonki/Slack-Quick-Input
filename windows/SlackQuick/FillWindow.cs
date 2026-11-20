using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace SlackQuick
{
    /// <summary>템플릿의 {{빈칸}}을 채우는 창 (서식 도구 + Slack 미리보기)</summary>
    sealed class FillWindow : Window
    {
        static readonly Dictionary<string, string> LastValues = new Dictionary<string, string>();

        readonly string _body;
        readonly ClipData _clipboard;
        readonly List<KeyValuePair<string, TextBox>> _fields = new List<KeyValuePair<string, TextBox>>();
        readonly StackPanel _preview = new StackPanel { Spacing = 4 };
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        TextBox _lastFocused;
        readonly TextBlock _note = Ui.Text("검은색 @이름은 등록되지 않아 알림이 가지 않아요. 관리 창의 '멘션'에서 등록할 수 있어요.", "SecondaryTextStyle");
        static readonly System.Text.RegularExpressions.Regex LooseMentionRx = new System.Text.RegularExpressions.Regex(@"(?<![\w@])@[^\s@]+");

        public Markup.Composed Result { get; private set; }

        public FillWindow(string title, string body, ClipData clipboard)
        {
            _body = body;
            _clipboard = clipboard;

            Title = "SlackQuick — " + title;
            SystemBackdrop = new MicaBackdrop();
            Ui.SetIcon(this);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            var names = Markup.AskFields(body);
            bool single = names.Count == 1 && body.Trim() == "{{" + names[0] + "}}";

            // ── 본문
            var stack = new StackPanel { Spacing = 10, Padding = new Thickness(24, 4, 24, 20) };

            var heading = Ui.Text(title, "SubtitleTextBlockStyle");
            heading.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(heading);
            stack.Children.Add(FormatBar.Build(() => _lastFocused ?? FirstBox, false));

            // 등록된 멘션: 누르면 지금 칸에 @이름이 들어감
            var mentions = Markup.Mentions.ToList();
            if (mentions.Count > 0)
            {
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var m in mentions)
                {
                    var chip = new Button { Content = m.Name, IsTabStop = false, AllowFocusOnInteraction = false, Padding = new Thickness(10, 3, 10, 4) };
                    string name = m.Name;
                    chip.Click += (s, e) => FormatBar.Insert(_lastFocused ?? FirstBox, name + " ", -1, 0);
                    chips.Children.Add(chip);
                }
                stack.Children.Add(new ScrollViewer
                {
                    Content = chips,
                    HorizontalScrollMode = ScrollMode.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollMode = ScrollMode.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });
            }

            foreach (var name in names)
            {
                var box = new TextBox
                {
                    Header = single ? null : name,
                    PlaceholderText = single ? "여기에 내용을 쓰세요. *굵게* _기울임_ [글자](주소) 를 쓸 수 있어요." : name,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = single ? 150 : 34,
                    MaxHeight = 240,
                    IsSpellCheckEnabled = false,
                };
                ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
                if (LastValues.TryGetValue(name, out var last)) box.Text = last;
                box.GotFocus += (s, e) =>
                {
                    if (_lastFocused != box) box.SelectAll();
                    _lastFocused = box;
                };
                box.TextChanged += (s, e) => { _timer.Stop(); _timer.Start(); };
                FormatBar.EnableRichPaste(box);
                _fields.Add(new KeyValuePair<string, TextBox>(name, box));
                stack.Children.Add(box);
            }

            var previewLabel = Ui.Text("Slack 미리보기", "BodyStrongTextBlockStyle");
            previewLabel.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(previewLabel);
            var card = Ui.Card(_preview);
            card.MinHeight = 56;
            stack.Children.Add(card);
            _note.TextWrapping = TextWrapping.Wrap;
            _note.Visibility = Visibility.Collapsed;
            stack.Children.Add(_note);

            var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // ── 아래 버튼
            var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hints.Children.Add(Ui.Hint("Ctrl+Enter", "붙여넣기"));
            hints.Children.Add(Ui.Hint("Esc", "취소"));

            var ok = new Button { Content = "붙여넣기", MinWidth = 110 };
            var accent = Ui.Res("AccentButtonStyle");
            if (accent != null) ok.Style = accent;
            ok.Click += (s, e) => Submit();
            var cancel = new Button { Content = "취소", MinWidth = 80 };
            cancel.Click += (s, e) => Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var footerGrid = new Grid { Padding = new Thickness(24, 14, 24, 14) };
            footerGrid.Children.Add(hints);
            footerGrid.Children.Add(buttons);
            var footer = Ui.Styled("FooterBorderStyle", footerGrid);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(Ui.At(Ui.TitleBar(this, "SlackQuick"), 0));
            root.Children.Add(Ui.At(scroll, 1));
            root.Children.Add(Ui.At(footer, 2));
            root.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == VirtualKey.Escape) { Close(); e.Handled = true; }
                else if (e.Key == VirtualKey.Enter && Ui.Ctrl) { Submit(); e.Handled = true; }
                else if (FocusManager.GetFocusedElement(root.XamlRoot) is TextBox focused && ListKeys.Handle(focused, e.Key)) e.Handled = true;
                else if (FormatBar.HandleKey(_lastFocused ?? FirstBox, e.Key)) e.Handled = true;
            };
            root.Loaded += (s, e) =>
            {
                UpdatePreview();
                FirstBox?.Focus(FocusState.Programmatic);
            };
            Content = root;

            _timer.Tick += (s, e) => { _timer.Stop(); UpdatePreview(); };
            Closed += (s, e) => _timer.Stop();

            double fieldsHeight = single ? 170 : names.Count * 72;
            Ui.Place(this, 640, 44 + 48 + 44 + (mentions.Count > 0 ? 40 : 0) + fieldsHeight + 40 + 150 + 64 + 40);
        }

        TextBox FirstBox => _fields.Count > 0 ? _fields[0].Value : null;

        public void ShowWindow()
        {
            Activate();
            Native.SetForegroundWindow(Ui.Hwnd(this));
        }

        /// <summary>빌드 검증용: 칸을 차례로 채웁니다.</summary>
        public void TestFill(params string[] values)
        {
            for (int i = 0; i < _fields.Count && i < values.Length; i++) _fields[i].Value.Text = values[i];
            UpdatePreview();
        }

        Markup.Composed Compose()
        {
            var values = new Dictionary<string, string>();
            foreach (var f in _fields) values[f.Key] = Ui.Normalize(f.Value.Text);
            return Markup.Compose(_body, values, _clipboard);
        }

        void UpdatePreview()
        {
            var content = Compose();
            Preview.Render(_preview, content);
            _note.Visibility = LooseMentionRx.IsMatch(content.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        void Submit()
        {
            foreach (var f in _fields) LastValues[f.Key] = Ui.Normalize(f.Value.Text);
            Result = Compose();
            Close();
        }
    }
}
