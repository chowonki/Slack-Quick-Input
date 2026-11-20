using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SlackQuick
{
    public static class Program
    {
        static Mutex _mutex;

        /// <summary>--selftest: 모든 창을 차례로 띄워 보고 결과를 selftest.log에 남긴 뒤 종료 (빌드 검증용)</summary>
        public static bool SelfTest { get; private set; }

        public static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Store.Folder);
                File.AppendAllText(Path.Combine(Store.Folder, "selftest.log"), text + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            SelfTest = Array.IndexOf(args, "--selftest") >= 0;
            _mutex = new Mutex(true, SelfTest ? "SlackQuick.SelfTest" : "SlackQuick.SingleInstance", out bool first);
            if (!first)
            {
                Native.MessageBoxW(IntPtr.Zero,
                    "SlackQuick이 이미 실행 중이에요.\n작업 표시줄 오른쪽 트레이 아이콘을 확인해 주세요.", "SlackQuick", 0x40);
                return 0;
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }
    }

    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // 트레이에 상주: 창을 모두 닫아도 계속 실행
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
            UnhandledException += (s, e) =>
            {
                e.Handled = true;
                if (Program.SelfTest) Program.Log("ERROR " + e.Exception);
                Shell.Notify("SlackQuick 오류", e.Message);
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            bool firstRun = !File.Exists(Store.FilePath);
            Store.Load();
            Shell.Start(firstRun);
        }
    }
}
