using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.System;

namespace SlackQuick
{
    /// <summary>트레이 아이콘, 전역 단축키, 창 흐름(팝업 → 빈칸 → 붙여넣기)</summary>
    static class Shell
    {
        const int HotkeyPopup = 1, HotkeyFormat = 2;
        const uint TrayMessage = Native.WM_APP + 1;
        const int CmdEditor = 1, CmdSettings = 2, CmdGuide = 3, CmdFolder = 4, CmdMentions = 5, CmdExit = 9;

        public static IntPtr Hwnd { get; private set; }
        public static IntPtr IconLarge { get; private set; }
        public static IntPtr IconSmall { get; private set; }

        static Native.WndProc _wndProc; // GC 방지
        static uint _taskbarCreated;
        static bool _busy;
        static EditorWindow _editor;

        public static void Start(bool firstRun)
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            try { Native.ExtractIconExW(Environment.ProcessPath, 0, large, small, 1); } catch { }
            IconLarge = large[0];
            IconSmall = small[0] != IntPtr.Zero ? small[0] : large[0];

            _wndProc = WndProc;
            var wc = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = Native.GetModuleHandleW(null),
                lpszClassName = "SlackQuickShellWindow",
            };
            Native.RegisterClassExW(ref wc);
            Hwnd = Native.CreateWindowExW(0, wc.lpszClassName, "SlackQuickShell", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");

            AddTray();
            if (Program.SelfTest) { RunSelfTest(); return; }
            var failed = RegisterHotkeys();

            if (failed != null)
            {
                Notify("단축키 등록 실패", $"{failed} 단축키를 다른 프로그램이 쓰는 중일 수 있어요. 설정에서 바꿔 주세요.");
                OpenEditor("settings");
            }
            else if (firstRun)
            {
                Notify("SlackQuick 실행 중", $"{Store.Data.PopupHotkey}: 템플릿 팝업\n{Store.Data.FormatHotkey}: 서식 직접 입력");
                OpenEditor("templates");
            }
        }

        // ───────── 트레이 ─────────

        static Native.NOTIFYICONDATA TrayData() => new Native.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = Hwnd,
            uID = 1,
            szTip = "SlackQuick — Slack 빠른 입력",
            szInfo = "",
            szInfoTitle = "",
        };

        static void AddTray()
        {
            var d = TrayData();
            d.uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP;
            d.uCallbackMessage = TrayMessage;
            d.hIcon = IconSmall;
            Native.Shell_NotifyIconW(Native.NIM_ADD, ref d);
        }

        static void RemoveTray()
        {
            var d = TrayData();
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref d);
        }

        public static void Notify(string title, string text)
        {
            if (Hwnd == IntPtr.Zero) return;
            var d = TrayData();
            d.uFlags = Native.NIF_INFO;
            d.szInfoTitle = title ?? "";
            d.szInfo = text ?? "";
            d.dwInfoFlags = 1; // NIIF_INFO
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref d);
        }

        static void ShowTrayMenu()
        {
            var menu = Native.CreatePopupMenu();
            Native.AppendMenuW(menu, Native.MF_STRING | Native.MF_GRAYED, UIntPtr.Zero, $"팝업 열기    {Store.Data.PopupHotkey}");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)CmdEditor, "템플릿 관리");
            Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)CmdSettings, "설정");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)CmdExit, "종료");

            Native.GetCursorPos(out var pt);
            Native.SetForegroundWindow(Hwnd);
            int cmd = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN,
                pt.X, pt.Y, Hwnd, IntPtr.Zero);
            Native.PostMessageW(Hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            Native.DestroyMenu(menu);

            switch (cmd)
            {
                case CmdEditor: OpenEditor("templates"); break;
                case CmdGuide: OpenEditor("guide"); break;
                case CmdMentions: OpenEditor("mentions"); break;
                case CmdSettings: OpenEditor("settings"); break;
                case CmdFolder: OpenFolder(); break;
                case CmdExit: Exit(); break;
            }
        }

        public static void OpenFolder()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Store.Folder + "\"") { UseShellExecute = true }); } catch { }
        }

        public static void Exit()
        {
            try { Store.Save(); } catch { }
            UnregisterHotkeys();
            RemoveTray();
            Application.Current.Exit();
        }

        static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == Native.WM_HOTKEY)
                {
                    var target = Native.GetForegroundWindow();
                    int id = wParam.ToInt32();
                    if (id == HotkeyPopup) OpenPopup(target);
                    else if (id == HotkeyFormat) OpenFree(target);
                    return IntPtr.Zero;
                }
                if (msg == TrayMessage)
                {
                    uint m = (uint)(lParam.ToInt64() & 0xFFFF);
                    if (m == Native.WM_RBUTTONUP || m == Native.WM_CONTEXTMENU) ShowTrayMenu();
                    else if (m == Native.WM_LBUTTONUP || m == Native.WM_LBUTTONDBLCLK) OpenEditor("templates");
                    return IntPtr.Zero;
                }
                if (_taskbarCreated != 0 && msg == _taskbarCreated)
                {
                    AddTray();
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                try { Notify("SlackQuick 오류", ex.Message); } catch { }
            }
            return Native.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // ───────── 단축키 ─────────

        public static bool TryParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var part in text.Split('+'))
            {
                var p = part.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; continue;
                    case "alt": mods |= Native.MOD_ALT; continue;
                    case "shift": mods |= Native.MOD_SHIFT; continue;
                    case "win": mods |= Native.MOD_WIN; continue;
                }
                if (p.Length == 0) return false;
                if (p.Length == 1 && char.IsDigit(p[0])) p = "Number" + p;
                else if (char.IsDigit(p[0])) return false;
                if (!Enum.TryParse(p, true, out VirtualKey key)) return false;
                vk = (uint)key;
            }
            return vk != 0 && mods != 0;
        }

        public static void UnregisterHotkeys()
        {
            if (Hwnd == IntPtr.Zero) return;
            Native.UnregisterHotKey(Hwnd, HotkeyPopup);
            Native.UnregisterHotKey(Hwnd, HotkeyFormat);
        }

        /// <returns>실패한 단축키 문자열, 모두 성공하면 null</returns>
        public static string RegisterHotkeys()
        {
            UnregisterHotkeys();
            string failed = null;
            if (!TryParseHotkey(Store.Data.PopupHotkey, out var m1, out var k1) ||
                !Native.RegisterHotKey(Hwnd, HotkeyPopup, m1 | Native.MOD_NOREPEAT, k1))
                failed = Store.Data.PopupHotkey;
            if (!TryParseHotkey(Store.Data.FormatHotkey, out var m2, out var k2) ||
                !Native.RegisterHotKey(Hwnd, HotkeyFormat, m2 | Native.MOD_NOREPEAT, k2))
                failed = failed == null ? Store.Data.FormatHotkey : failed + ", " + Store.Data.FormatHotkey;
            return failed;
        }

        // ───────── 흐름 ─────────

        static void OpenPopup(IntPtr target)
        {
            if (_busy) return;
            _busy = true;
            var popup = new PopupWindow();
            popup.Dismissed += () =>
            {
                _busy = false;
                Native.Focus(target);
            };
            popup.Picked += item =>
            {
                _busy = false;
                if (item.IsFree) OpenFill("서식 직접 입력", "{{내용}}", target);
                else OpenFill(item.Template.Name, item.Template.Body, target);
            };
            popup.ShowPopup();
        }

        static void OpenFree(IntPtr target)
        {
            if (_busy) return;
            OpenFill("서식 직접 입력", "{{내용}}", target);
        }

        static void OpenFill(string title, string body, IntPtr target)
        {
            var clip = Paster.GetClip();
            try { if (Markup.LearnMentions(clip)) Store.Save(); } catch { } // Slack에서 복사한 멘션은 자동 등록
            if (Markup.AskFields(body).Count == 0)
            {
                Paster.Paste(Markup.Compose(body, null, clip), target);
                return;
            }
            _busy = true;
            var fill = new FillWindow(title, body, clip);
            fill.Closed += (s, e) =>
            {
                _busy = false;
                if (fill.Result != null) Paster.Paste(fill.Result, target);
                else Native.Focus(target);
            };
            fill.ShowWindow();
        }

        public static void OpenEditor(string page)
        {
            if (_editor == null)
            {
                _editor = new EditorWindow();
                _editor.Closed += (s, e) => _editor = null;
            }
            _editor.Navigate(page);
            _editor.Activate();
            Native.SetForegroundWindow(Ui.Hwnd(_editor));
        }

        // ───────── 빌드 검증 ─────────

        static void RunSelfTest()
        {
            int step = 0;
            PopupWindow popup = null;
            FillWindow fill = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

            void Step()
            {
                try
                {
                    switch (step++)
                    {
                        case 0: OpenEditor("templates"); Program.Log("editor ok"); break;
                        case 1: OpenEditor("settings"); Program.Log("settings ok"); break;
                        case 2:
                            Store.Data.Snippets.Add(new Snippet { Name = "@홍길동", Html = "<a>@홍길동</a>", Plain = "@홍길동" });
                            Store.Data.Snippets.Add(new Snippet { Name = "@운영팀", Html = "<a>@운영팀</a>", Plain = "@운영팀" });
                            OpenEditor("mentions");
                            Program.Log("mentions ok");
                            break;
                        case 3: OpenEditor("templates"); _editor?.ShowHelpForTest(); Program.Log("help ok"); break;
                        case 4:
                            _editor?.Close();
                            popup = new PopupWindow();
                            popup.ShowPopup();
                            Program.Log("popup ok");
                            break;
                        case 5:
                            fill = new FillWindow("공지 (태그·타이틀·전달내용)", "{{멘션}}\n*[{{태그}}] {{타이틀}}*\n{{내용}}\n\n> {{전달내용}}", new ClipData { Text = "clip" });
                            fill.ShowWindow();
                            fill.TestFill("@홍길동 @없는사람", "공지", "점검 안내", "오늘 18시 \"서버\" 점검 -> [자세히](https://slack.com)\r- 순서\r\t- DB 백업\r\t\t- 확인...\r- 끝\r```sudo restart```", "확인 부탁드려요 :pray:");
                            Program.Log("fill ok");
                            break;
                        case 6:
                            fill?.Close();
                            fill = new FillWindow("서식 직접 입력", "{{내용}}", new ClipData { Text = "clip" });
                            fill.ShowWindow();
                            fill.TestFill("@홍길동 \"서버\" 점검 -> *완료*...\r- 순서\r\t- DB 백업\r\t\t- 확인 -- 끝\r- 마무리\r```sudo restart```\r> 인용");
                            Program.Log("free ok");
                            break;
                        default:
                            timer.Stop();
                            fill?.Close();
                            Program.Log("OK");
                            Exit();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("ERROR step " + step + ": " + ex);
                    timer.Stop();
                    Exit();
                }
            }

            timer.Tick += (s, e) => Step();
            timer.Start();
            Step();
        }

        // ───────── 시작 프로그램 ─────────

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool AutoStart
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue("SlackQuick") != null;
            }
            set
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue("SlackQuick", "\"" + Environment.ProcessPath + "\"");
                else k.DeleteValue("SlackQuick", false);
            }
        }
    }
}
