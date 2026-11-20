using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.UI.Xaml;

namespace SlackQuick
{
    /// <summary>서식 있는 HTML + 일반 텍스트를 클립보드에 넣고 이전 창에 붙여넣습니다.</summary>
    static class Paster
    {
        static bool Open()
        {
            for (int i = 0; i < 10; i++)
            {
                if (Native.OpenClipboard(Shell.Hwnd)) return true;
                Thread.Sleep(40);
            }
            return false;
        }

        public static string GetClipboardText()
        {
            if (!Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT)) return "";
            if (!Open()) return "";
            try
            {
                var h = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (h == IntPtr.Zero) return "";
                var p = Native.GlobalLock(h);
                if (p == IntPtr.Zero) return "";
                try { return Marshal.PtrToStringUni(p) ?? ""; }
                finally { Native.GlobalUnlock(h); }
            }
            finally { Native.CloseClipboard(); }
        }

        /// <summary>클립보드의 "HTML Format"에서 복사된 조각만 꺼냅니다. 없으면 null.</summary>
        static string GetClipboardHtml()
        {
            uint format = Native.RegisterClipboardFormatW("HTML Format");
            if (format == 0 || !Native.IsClipboardFormatAvailable(format)) return null;
            if (!Open()) return null;
            byte[] bytes;
            try
            {
                var h = Native.GetClipboardData(format);
                if (h == IntPtr.Zero) return null;
                int size = (int)Native.GlobalSize(h).ToUInt64();
                if (size <= 0 || size > 8 * 1024 * 1024) return null;
                var p = Native.GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try
                {
                    bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, size);
                }
                finally { Native.GlobalUnlock(h); }
            }
            finally { Native.CloseClipboard(); }

            int len = Array.IndexOf(bytes, (byte)0);
            if (len < 0) len = bytes.Length;
            string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(len, 1024));
            int start = HeaderNumber(head, "StartFragment:"), end = HeaderNumber(head, "EndFragment:");
            if (start > 0 && end > start && end <= len) return Encoding.UTF8.GetString(bytes, start, end - start);

            string all = Encoding.UTF8.GetString(bytes, 0, len);
            int a = all.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            int b = all.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (a >= 0 && b > a) return all.Substring(a + 20, b - a - 20);
            return null;
        }

        static int HeaderNumber(string head, string key)
        {
            int i = head.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            i += key.Length;
            int j = i;
            while (j < head.Length && char.IsDigit(head[j])) j++;
            return j > i && int.TryParse(head.Substring(i, j - i), out int n) ? n : -1;
        }

        /// <summary>Chromium(Slack·Chrome)이 웹 사용자 지정 데이터를 넣는 클립보드 형식</summary>
        const string WebCustomFormat = "Chromium Web Custom MIME Data Format";

        static byte[] GetClipboardBytes(string formatName)
        {
            uint format = Native.RegisterClipboardFormatW(formatName);
            if (format == 0 || !Native.IsClipboardFormatAvailable(format)) return null;
            if (!Open()) return null;
            try
            {
                var h = Native.GetClipboardData(format);
                if (h == IntPtr.Zero) return null;
                int size = (int)Native.GlobalSize(h).ToUInt64();
                if (size <= 0 || size > 8 * 1024 * 1024) return null;
                var p = Native.GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, size);
                    return bytes;
                }
                finally { Native.GlobalUnlock(h); }
            }
            finally { Native.CloseClipboard(); }
        }

        /// <summary>클립보드의 글자와 서식(HTML 조각)을 함께 읽습니다.</summary>
        public static ClipData GetClip()
        {
            var clip = new ClipData { Text = GetClipboardText() };
            try
            {
                var html = Markup.CleanHtml(GetClipboardHtml());
                if (!string.IsNullOrWhiteSpace(html)) clip.Html = html;
            }
            catch { }
            try
            {
                // Slack에서 복사한 내용이면 멘션 ID가 든 서식 데이터가 같이 있음
                clip.Custom = GetClipboardBytes(WebCustomFormat);
                clip.Texty = Markup.ReadWebCustomData(clip.Custom, Markup.TextyType);
            }
            catch { }
            return clip;
        }

        public static byte[] BuildCfHtml(string fragment)
        {
            const string header =
                "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string pre = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
            const string post = "<!--EndFragment-->\r\n</body></html>";

            var enc = new UTF8Encoding(false);
            int headerLen = enc.GetByteCount(string.Format(header, 0, 0, 0, 0));
            int startHtml = headerLen;
            int startFrag = startHtml + enc.GetByteCount(pre);
            int endFrag = startFrag + enc.GetByteCount(fragment);
            int endHtml = endFrag + enc.GetByteCount(post);
            var all = string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + fragment + post;
            return enc.GetBytes(all + "\0");
        }

        static void Put(uint format, byte[] bytes)
        {
            var h = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)(uint)bytes.Length);
            if (h == IntPtr.Zero) return;
            var p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) { Native.GlobalFree(h); return; }
            Marshal.Copy(bytes, 0, p, bytes.Length);
            Native.GlobalUnlock(h);
            if (Native.SetClipboardData(format, h) == IntPtr.Zero) Native.GlobalFree(h);
        }

        static bool SetClipboard(string plain, string htmlFragment, byte[] webCustom)
        {
            if (!Open()) return false;
            try
            {
                Native.EmptyClipboard();
                Put(Native.CF_UNICODETEXT, Encoding.Unicode.GetBytes((plain ?? "") + "\0"));
                if (htmlFragment != null)
                    Put(Native.RegisterClipboardFormatW("HTML Format"), BuildCfHtml(htmlFragment));
                if (webCustom != null && webCustom.Length > 0)
                    Put(Native.RegisterClipboardFormatW(WebCustomFormat), webCustom);
                return true;
            }
            finally { Native.CloseClipboard(); }
        }

        public static void Paste(Markup.Composed content, IntPtr target)
        {
            ClipData old = Store.Data.RestoreClipboard ? GetClip() : null;

            // Slack은 자기 서식 데이터(slack/texty)를 먼저 읽음 → 멘션·목록·인용이 진짜로 들어감.
            // HTML과 글자는 Slack이 아닌 앱에 붙일 때 쓰임.
            byte[] texty = null;
            try { texty = Markup.WebCustomData(Markup.TextyType, content.ToTexty()); } catch { }
            if (!SetClipboard(content.ToPlain(), content.ToHtml(), texty))
            {
                Shell.Notify("붙여넣기 실패", "클립보드를 열지 못했어요. 잠시 뒤 다시 시도해 주세요.");
                return;
            }

            Native.Focus(target);
            Thread.Sleep(120);
            Native.SendPaste();

            if (old != null && !string.IsNullOrEmpty(old.Text))
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    SetClipboard(old.Text, old.Html, old.Custom); // 서식까지 되돌림
                };
                timer.Start();
            }
        }
    }
}
