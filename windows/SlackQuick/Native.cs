using System;
using System.Runtime.InteropServices;

namespace SlackQuick
{
    static class Native
    {
        // ───────── 창 / 메시지 ─────────

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessageW(string name);

        [DllImport("user32.dll")]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        // ───────── 단축키 / 포커스 ─────────

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const uint WM_DESTROY = 0x0002, WM_NULL = 0x0000, WM_HOTKEY = 0x0312, WM_APP = 0x8000;
        public const uint WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const uint KEYEVENTF_KEYUP = 0x2;
        const byte VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_V = 0x56;

        public static void Focus(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
            var fg = GetForegroundWindow();
            uint a = GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint b = GetCurrentThreadId();
            if (a != b && a != 0) AttachThreadInput(a, b, true);
            SetForegroundWindow(hwnd);
            if (a != b && a != 0) AttachThreadInput(a, b, false);
        }

        static bool Down(byte vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>눌려 있는 수정키를 떼고 Ctrl+V를 보냅니다.</summary>
        public static void SendPaste()
        {
            foreach (var vk in new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN, VK_CONTROL })
                if (Down(vk)) keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ───────── 트레이 ─────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr menu);

        public const uint MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800;
        public const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;

        // ───────── 클립보드 ─────────

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint format, IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterClipboardFormatW(string name);

        /// <summary>클립보드 내용이 바뀔 때마다 올라가는 번호</summary>
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalFree(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern UIntPtr GlobalSize(IntPtr handle);

        public const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x2;
    }
}
