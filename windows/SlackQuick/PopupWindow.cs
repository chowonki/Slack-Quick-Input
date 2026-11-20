using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace SlackQuick
{
    public class PopupItem
    {
        public Template Template;
        public bool IsFree;
    }

    /// <summary>검색해서 템플릿을 고르는 팝업 (아크릴 배경, 테두리 없는 창)</summary>
    sealed class PopupWindow : Window
    {
        const double PopupWidth = 660, HeaderHeight = 66, FooterHeight = 42, RowHeight = 62, MaxRows = 6;

        readonly TextBox _search;
        readonly ListView _list;
        readonly TextBlock _count;
        bool _closed;

        public PopupItem Result { get; private set; }

        /// <summary>Esc로 닫았는지 (다른 창을 눌러 닫힌 경우와 구분)</summary>
        public bool Cancelled { get; private set; }
        public event Action<PopupItem> Picked;
        public event Action Dismissed;

        public PopupWindow()
        {
            Title = "SlackQuick";
            SystemBackdrop = new DesktopAcrylicBackdrop();

            var presenter = OverlappedPresenter.Create();
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.IsShownInSwitchers = false;
            Ui.RoundCorners(this);

            // ── 검색
            _search = new TextBox
            {
                PlaceholderText = "템플릿 검색",
                FontSize = 18,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 6, 4, 6),
            };
            var clear = new SolidColorBrush(Colors.Transparent);
            _search.Resources["TextControlBackground"] = clear;
            _search.Resources["TextControlBackgroundPointerOver"] = clear;
            _search.Resources["TextControlBackgroundFocused"] = clear;
            _search.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
            _search.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
            _search.TextChanged += (s, e) => Refresh();
            _search.PreviewKeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case VirtualKey.Down: Move(1); e.Handled = true; break;
                    case VirtualKey.Up: Move(-1); e.Handled = true; break;
                    case VirtualKey.Enter: Pick(_list.SelectedItem as ListViewItem); e.Handled = true; break;
                    case VirtualKey.Escape: Cancelled = true; CloseOnce(); e.Handled = true; break;
                }
            };

            var searchIcon = Ui.Icon("", 18);
            searchIcon.VerticalAlignment = VerticalAlignment.Center;
            searchIcon.Margin = new Thickness(0, 0, 8, 0);

            var header = new Grid { Height = HeaderHeight, Padding = new Thickness(20, 0, 16, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Children.Add(Ui.At(searchIcon, 0, 0));
            header.Children.Add(Ui.At(_search, 0, 1));

            // ── 목록
            _list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                Padding = new Thickness(6, 6, 6, 6),
                IsTabStop = false,
            };

            // ── 아래 안내
            var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hints.Children.Add(Ui.Hint("↑+↓", "이동"));
            hints.Children.Add(Ui.Hint("Enter", "선택"));
            hints.Children.Add(Ui.Hint("Esc", "닫기"));
            _count = Ui.Text("", "SecondaryTextStyle");
            _count.VerticalAlignment = VerticalAlignment.Center;
            _count.HorizontalAlignment = HorizontalAlignment.Right;

            var footerGrid = new Grid { Padding = new Thickness(18, 0, 18, 0) };
            footerGrid.Children.Add(hints);
            footerGrid.Children.Add(_count);
            var footer = Ui.Styled("FooterBorderStyle", footerGrid);
            footer.Height = FooterHeight;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(Ui.At(header, 0));
            root.Children.Add(Ui.At(Ui.Styled("DividerStyle"), 1));
            root.Children.Add(Ui.At(_list, 2));
            root.Children.Add(Ui.At(footer, 3));
            root.Loaded += (s, e) => _search.Focus(FocusState.Programmatic);
            Content = root;

            Activated += (s, e) =>
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated)
                    DispatcherQueue.TryEnqueue(CloseOnce);
            };
            Closed += (s, e) =>
            {
                _closed = true;
                if (Result == null) Dismissed?.Invoke();
            };

            Ui.Place(this, PopupWidth, HeightFor(Store.Data.Templates.Count + 1), 0.2);
            Refresh();
        }

        public void ShowPopup()
        {
            Activate();
            Native.SetForegroundWindow(Ui.Hwnd(this));
            _search.Focus(FocusState.Programmatic);
        }

        static double HeightFor(int rows) =>
            HeaderHeight + 1 + 20 + Math.Min(Math.Max(rows, 1), MaxRows) * RowHeight + FooterHeight;

        void CloseOnce()
        {
            if (_closed) return;
            _closed = true;
            Close();
        }

        void Refresh()
        {
            string q = _search.Text ?? "";
            _list.Items.Clear();

            var matches = Store.Data.Templates
                .Select((t, i) => new { t, i, score = Markup.Score(t, q) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score).ThenBy(x => x.i)
                .Select(x => x.t)
                .ToList();

            foreach (var t in matches) _list.Items.Add(MakeRow(new PopupItem { Template = t }));
            _list.Items.Add(MakeRow(new PopupItem { IsFree = true }));
            _list.SelectedIndex = 0;
            _count.Text = matches.Count + "개 템플릿";

            // 결과 수에 맞춰 높이 조절 (위치는 유지)
            try
            {
                double s = Ui.Scale(this);
                var size = AppWindow.Size;
                int h = (int)(HeightFor(_list.Items.Count) * s);
                if (size.Height != h) AppWindow.Resize(new SizeInt32(size.Width, h));
            }
            catch { }
        }

        ListViewItem MakeRow(PopupItem item)
        {
            string glyph, title, sub, keyword = null;
            if (item.IsFree)
            {
                glyph = ""; // Edit
                title = "서식 직접 입력";
                sub = "*굵게*  _기울임_  ~취소선~  `코드`  [글자](주소) 로 바로 써서 붙여넣기";
            }
            else
            {
                var t = item.Template;
                var fields = Markup.AskFields(t.Body);
                glyph = fields.Count > 0 ? "" : ""; // Document : Message
                title = string.IsNullOrWhiteSpace(t.Name) ? "(이름 없음)" : t.Name;
                sub = Ui.Normalize(t.Body).Replace("\n", "  ⏎  ");
                keyword = string.IsNullOrWhiteSpace(t.Keyword) ? null : t.Keyword.Trim();
            }

            var titleText = Ui.Text(title, "BodyStrongTextBlockStyle");
            titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            titleText.TextWrapping = TextWrapping.NoWrap;
            var subText = Ui.Text(sub, "SecondaryTextStyle");
            subText.TextTrimming = TextTrimming.CharacterEllipsis;
            subText.TextWrapping = TextWrapping.NoWrap;
            subText.MaxLines = 1;

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            texts.Children.Add(titleText);
            texts.Children.Add(subText);

            var grid = new Grid { ColumnSpacing = 12, Height = RowHeight - 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.At(Ui.IconTile(glyph), 0, 0));
            grid.Children.Add(Ui.At(texts, 0, 1));

            var row = new ListViewItem
            {
                Content = grid,
                Tag = item,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 0, 12, 0),
                MinHeight = RowHeight,
            };
            row.Tapped += (s, e) => Pick(row);
            return row;
        }

        void Move(int delta)
        {
            int n = _list.Items.Count;
            if (n == 0) return;
            int i = Math.Max(0, Math.Min(n - 1, _list.SelectedIndex + delta));
            _list.SelectedIndex = i;
            _list.ScrollIntoView(_list.Items[i]);
        }

        void Pick(ListViewItem row)
        {
            if (row == null || !(row.Tag is PopupItem item) || _closed) return;
            Result = item;
            CloseOnce();
            Picked?.Invoke(item);
        }
    }
}
