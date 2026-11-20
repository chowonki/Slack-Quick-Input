import Foundation

final class Template: Codable, Identifiable, ObservableObject {
    let id = UUID()
    @Published var name: String
    @Published var keyword: String
    @Published var body: String

    init(name: String, keyword: String = "", body: String) {
        self.name = name
        self.keyword = keyword
        self.body = body
    }

    // Windows 버전과 같은 JSON 키 (파일을 그대로 옮겨 쓸 수 있게)
    enum CodingKeys: String, CodingKey { case name = "Name", keyword = "Keyword", body = "Body" }

    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        keyword = (try? c.decode(String.self, forKey: .keyword)) ?? ""
        body = (try? c.decode(String.self, forKey: .body)) ?? ""
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(keyword, forKey: .keyword)
        try c.encode(body, forKey: .body)
    }
}

/// 클립보드에서 가져온 서식 조각(멘션 등 HTML 그대로). 이름이 @로 시작하면 멘션.
struct Snippet: Codable, Identifiable {
    var id = UUID()
    var name: String
    var html: String
    var plain: String
    /// Slack 사용자/그룹 ID (U…, S…). 있으면 진짜 멘션으로 붙여넣을 수 있음.
    var slackId: String? = nil

    enum CodingKeys: String, CodingKey { case name = "Name", html = "Html", plain = "Plain", slackId = "Id" }

    init(name: String, html: String, plain: String) {
        self.name = name
        self.html = html
        self.plain = plain
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        html = (try? c.decode(String.self, forKey: .html)) ?? ""
        plain = (try? c.decode(String.self, forKey: .plain)) ?? ""
        slackId = try? c.decode(String.self, forKey: .slackId)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(html, forKey: .html)
        try c.encode(plain, forKey: .plain)
        try c.encodeIfPresent(slackId, forKey: .slackId)
    }
}

/// 클립보드 내용 (서식이 있으면 html에 조각이 들어 있음)
struct ClipData {
    var text: String = ""
    var html: String? = nil
    /// Slack이 복사할 때 넣는 서식 데이터(slack/texty, Quill Delta JSON). 멘션 ID가 들어 있음.
    var texty: String? = nil
}

struct AppData: Codable {
    var schemaVersion: Int = 0
    var templates: [Template] = []
    var snippets: [Snippet] = []
    var restoreClipboard: Bool = true
    var smartSymbols: Bool = true
    var popupHotkey: String = "Ctrl+Alt+Space"
    var formatHotkey: String = "Ctrl+Alt+F"
    /// 설정 화면을 여는 전역 단축키. 비어 있으면 쓰지 않음.
    var settingsHotkey: String = "Ctrl+Alt+S"

    enum CodingKeys: String, CodingKey {
        case schemaVersion = "SchemaVersion", templates = "Templates", snippets = "Snippets"
        case restoreClipboard = "RestoreClipboard", smartSymbols = "SmartSymbols"
        case popupHotkey = "MacPopupHotkey", formatHotkey = "MacFormatHotkey", settingsHotkey = "MacSettingsHotkey"
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = (try? c.decode(Int.self, forKey: .schemaVersion)) ?? 0
        templates = (try? c.decode([Template].self, forKey: .templates)) ?? []
        snippets = (try? c.decode([Snippet].self, forKey: .snippets)) ?? []
        restoreClipboard = (try? c.decode(Bool.self, forKey: .restoreClipboard)) ?? true
        smartSymbols = (try? c.decode(Bool.self, forKey: .smartSymbols)) ?? true
        popupHotkey = (try? c.decode(String.self, forKey: .popupHotkey)) ?? "Ctrl+Alt+Space"
        formatHotkey = (try? c.decode(String.self, forKey: .formatHotkey)) ?? "Ctrl+Alt+F"
        settingsHotkey = (try? c.decode(String.self, forKey: .settingsHotkey)) ?? "Ctrl+Alt+S"
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(schemaVersion, forKey: .schemaVersion)
        try c.encode(templates, forKey: .templates)
        try c.encode(snippets, forKey: .snippets)
        try c.encode(restoreClipboard, forKey: .restoreClipboard)
        try c.encode(smartSymbols, forKey: .smartSymbols)
        try c.encode(popupHotkey, forKey: .popupHotkey)
        try c.encode(formatHotkey, forKey: .formatHotkey)
        try c.encode(settingsHotkey, forKey: .settingsHotkey)
    }
}

final class Store: ObservableObject {
    static let shared = Store()

    @Published var data = AppData()

    var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("SlackQuick", isDirectory: true)
    }
    var fileURL: URL { folder.appendingPathComponent("templates.json") }

    private(set) var isFirstRun = false

    func load() {
        if let d = try? Data(contentsOf: fileURL) {
            if var decoded = try? JSONDecoder().decode(AppData.self, from: d) {
                if decoded.schemaVersion < 2 {
                    // 멘션 + 서식 있는 클립보드용 기본 템플릿 추가
                    decoded.templates.insert(Store.shareTemplate(), at: 0)
                    decoded.schemaVersion = 2
                    data = decoded
                    save()
                } else {
                    data = decoded
                }
                return
            }
            try? FileManager.default.copyItem(at: fileURL, to: fileURL.appendingPathExtension("bak"))
        } else {
            isFirstRun = true
        }
        var fresh = AppData()
        fresh.schemaVersion = 2
        fresh.templates = [Store.shareTemplate()] + Store.defaults()
        data = fresh
        save()
    }

    func save() {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        if let d = try? enc.encode(data) { try? d.write(to: fileURL, options: .atomic) }
    }

    static func shareTemplate() -> Template {
        Template(name: "공유 (멘션 · 복사한 줄 · 전달내용)", keyword: "공유",
                 body: "{{멘션}}\n{{클립보드}}\n{{전달내용}}")
    }

    static func defaults() -> [Template] {
        [
            Template(name: "공지 (태그·타이틀·전달내용)", keyword: "공지",
                     body: "*[{{태그}}] {{타이틀}}*\n{{내용}}\n\n> {{전달내용}}"),
            Template(name: "링크 공유", keyword: "링크",
                     body: ":link: *{{제목}}*\n[바로가기]({{주소}})"),
            Template(name: "클립보드 붙여넣기 + 메모", keyword: "클립",
                     body: "{{클립보드}}\n_{{메모}}_"),
            Template(name: "확인 완료", keyword: "확인",
                     body: ":white_check_mark: 확인했습니다!"),
            Template(name: "오늘 날짜 보고", keyword: "보고",
                     body: "*{{날짜}} 업무 보고*\n• {{내용}}"),
        ]
    }
}
