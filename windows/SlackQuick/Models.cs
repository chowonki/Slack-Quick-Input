using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SlackQuick
{
    public class Template
    {
        public string Name { get; set; } = "";
        public string Keyword { get; set; } = "";
        public string Body { get; set; } = "";

        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(이름 없음)" : Name;
    }

    /// <summary>클립보드에서 가져온 서식 조각(멘션·이모지·링크 등 HTML 그대로). 이름이 @로 시작하면 멘션.</summary>
    public class Snippet
    {
        public string Name { get; set; } = "";
        public string Html { get; set; } = "";
        public string Plain { get; set; } = "";

        /// <summary>Slack 사용자/그룹 ID (U…, S…). 있으면 진짜 멘션으로 붙여넣을 수 있음.</summary>
        public string Id { get; set; }
    }

    /// <summary>클립보드 내용 (서식이 있으면 Html에 조각이 들어 있음)</summary>
    public class ClipData
    {
        public string Text = "";
        public string Html;

        /// <summary>Slack이 복사할 때 넣는 서식 데이터(slack/texty, Quill Delta JSON). 멘션 ID가 들어 있음.</summary>
        public string Texty;

        /// <summary>Chromium 웹 사용자 지정 데이터 원본 (클립보드 복원용)</summary>
        public byte[] Custom;
    }

    public class AppData
    {
        public int SchemaVersion { get; set; } = 0;
        public List<Template> Templates { get; set; } = new List<Template>();
        public List<Snippet> Snippets { get; set; } = new List<Snippet>();
        public bool RestoreClipboard { get; set; } = true;
        public bool SmartSymbols { get; set; } = true;
        public string PopupHotkey { get; set; } = "Ctrl+Shift+Space";
        public string FormatHotkey { get; set; } = "Ctrl+Alt+F";
    }

    public static class Store
    {
        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlackQuick");

        public static string FilePath => Path.Combine(Folder, "templates.json");

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static AppData Data { get; private set; } = new AppData();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
                    if (d != null)
                    {
                        d.Templates ??= new List<Template>();
                        d.Snippets ??= new List<Snippet>();
                        if (d.SchemaVersion < 2)
                        {
                            // 1.2: 멘션 + 서식 있는 클립보드용 기본 템플릿 추가
                            d.Templates.Insert(0, ShareTemplate());
                            d.SchemaVersion = 2;
                            Data = d;
                            try { Save(); } catch { }
                        }
                        d.PopupHotkey ??= "Ctrl+Shift+Space";
                        d.FormatHotkey ??= "Ctrl+Alt+F";
                        Data = d;
                        return;
                    }
                }
            }
            catch
            {
                try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            }
            Data = new AppData { Templates = Defaults(), SchemaVersion = 2 };
            Data.Templates.Insert(0, ShareTemplate());
            Save();
        }

        public static void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, Options));
        }

        public static Template ShareTemplate() => new Template
        {
            Name = "공유 (멘션 · 복사한 줄 · 전달내용)",
            Keyword = "공유",
            Body = "{{멘션}}\n{{클립보드}}\n{{전달내용}}",
        };

        public static List<Template> Defaults() => new List<Template>
        {
            new Template
            {
                Name = "공지 (태그·타이틀·전달내용)",
                Keyword = "공지",
                Body = "*[{{태그}}] {{타이틀}}*\n{{내용}}\n\n> {{전달내용}}",
            },
            new Template
            {
                Name = "링크 공유",
                Keyword = "링크",
                Body = ":link: *{{제목}}*\n[바로가기]({{주소}})",
            },
            new Template
            {
                Name = "클립보드 붙여넣기 + 메모",
                Keyword = "클립",
                Body = "{{클립보드}}\n_{{메모}}_",
            },
            new Template
            {
                Name = "확인 완료",
                Keyword = "확인",
                Body = ":white_check_mark: 확인했습니다!",
            },
            new Template
            {
                Name = "오늘 날짜 보고",
                Keyword = "보고",
                Body = "*{{날짜}} 업무 보고*\n• {{내용}}",
            },
        };
    }
}
