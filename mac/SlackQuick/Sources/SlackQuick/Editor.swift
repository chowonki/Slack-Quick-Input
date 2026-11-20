import AppKit
import ServiceManagement
import SwiftUI

/// 관리 창 상태 (탭 · 도움말) — 밖에서도 바꿀 수 있게 따로 둠
final class EditorState: ObservableObject {
    @Published var tab = 0          // 0 템플릿, 1 멘션, 2 설정
    @Published var showHelp = false
}

/// 미리보기 (HTML → 서식 텍스트)
struct HTMLPreview: NSViewRepresentable {
    var bodyHTML: String

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSTextView.scrollableTextView()
        if let tv = scroll.documentView as? NSTextView {
            tv.isEditable = false
            tv.drawsBackground = false
            tv.textContainerInset = NSSize(width: 6, height: 6)
        }
        scroll.drawsBackground = false
        scroll.borderType = .lineBorder
        return scroll
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {
        (nsView.documentView as? NSTextView)?.textStorage?.setAttributedString(HTMLRender.attributed(bodyHTML))
    }
}

struct TemplateRow: View {
    @ObservedObject var t: Template
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(t.name.isEmpty ? "(이름 없음)" : t.name).fontWeight(.medium).lineLimit(1)
            Text(t.body.replacingOccurrences(of: "\n", with: " ⏎ "))
                .font(.caption).foregroundColor(.secondary).lineLimit(1)
        }
        .padding(.vertical, 2)
    }
}

struct TemplateDetail: View {
    @ObservedObject var t: Template
    @ObservedObject var state: EditorState
    var onDuplicate: () -> Void
    var onMove: (Int) -> Void
    var onDelete: () -> Void

    /// 빈칸은 이름 그대로 보여 줘서 완성된 모양을 가늠할 수 있게
    private var previewHTML: String {
        var values: [String: String] = [:]
        for name in Markup.askFields(t.body) { values[name] = name }
        return Markup.compose(t.body, values: values, clip: ClipData(text: "(복사한 내용)", html: nil)).toPreviewHTML()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                TextField("이름", text: $t.name).textFieldStyle(.roundedBorder)
                TextField("키워드", text: $t.keyword).textFieldStyle(.roundedBorder)
                    .frame(width: 130)
                    .help("팝업에서 이 글자를 그대로 치면 이 템플릿이 맨 위로 와요")
                Menu {
                    Button("복제", action: onDuplicate)
                    Button("위로 이동") { onMove(-1) }
                    Button("아래로 이동") { onMove(1) }
                    Divider()
                    Button("삭제", action: onDelete)
                } label: {
                    Image(systemName: "ellipsis")
                }
                .frame(width: 44)
                .help("복제 · 순서 · 삭제")
            }

            HStack(spacing: 8) {
                Text("내용").fontWeight(.semibold)
                Spacer()
                Menu("빈칸 넣기") {
                    Button("빈칸 (붙여넣을 때 물어봄)") { t.body += "{{이름}}" }
                    Divider()
                    Button("복사한 내용") { t.body += "{{클립보드}}" }
                    Button("오늘 날짜") { t.body += "{{날짜}}" }
                    Button("현재 시간") { t.body += "{{시간}}" }
                }
                .frame(width: 110)
            }
            TextEditor(text: $t.body)
                .font(.system(size: 13, design: .monospaced))
                .frame(minHeight: 150)
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.35)))

            HStack(spacing: 6) {
                Text("{{이름}}처럼 쓰면 붙여넣을 때 물어봐요.").font(.caption).foregroundColor(.secondary)
                Button("서식 도움말") { state.showHelp = true }.buttonStyle(.link).font(.caption)
            }

            Text("Slack 미리보기").fontWeight(.semibold).padding(.top, 4)
            HTMLPreview(bodyHTML: previewHTML).frame(minHeight: 110)
        }
        .onChange(of: t.name) { _ in Store.shared.save() }
        .onChange(of: t.keyword) { _ in Store.shared.save() }
        .onChange(of: t.body) { _ in Store.shared.save() }
    }
}

struct TemplatesTab: View {
    @ObservedObject var store = Store.shared
    @ObservedObject var state: EditorState
    @State private var selection: UUID?

    private var selected: Template? {
        store.data.templates.first { $0.id == selection }
    }

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            VStack(spacing: 8) {
                Button(action: add) {
                    Label("새 템플릿", systemImage: "plus").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)

                List(selection: $selection) {
                    ForEach(store.data.templates) { t in
                        TemplateRow(t: t).tag(Optional(t.id))
                    }
                }
            }
            .frame(width: 260)

            if let t = selected {
                TemplateDetail(t: t, state: state, onDuplicate: duplicate, onMove: move, onDelete: delete)
                    .id(t.id)
            } else {
                Text("왼쪽에서 템플릿을 고르거나 '새 템플릿'으로 추가해 주세요.")
                    .foregroundColor(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .onAppear { if selection == nil { selection = store.data.templates.first?.id } }
    }

    private func index() -> Int? {
        store.data.templates.firstIndex { $0.id == selection }
    }

    private func add() {
        let t = Template(name: "새 템플릿", body: "*{{타이틀}}*\n{{내용}}")
        store.data.templates.append(t)
        selection = t.id
        store.save()
    }

    private func duplicate() {
        guard let c = selected, let i = index() else { return }
        let t = Template(name: c.name + " 복사본", keyword: c.keyword, body: c.body)
        store.data.templates.insert(t, at: i + 1)
        selection = t.id
        store.save()
    }

    private func delete() {
        guard let c = selected, let i = index() else { return }
        let alert = NSAlert()
        alert.messageText = "'" + c.name + "' 템플릿을 삭제할까요?"
        alert.addButton(withTitle: "삭제")
        alert.addButton(withTitle: "취소")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        store.data.templates.remove(at: i)
        let n = store.data.templates.count
        selection = n == 0 ? nil : store.data.templates[min(i, n - 1)].id
        store.save()
    }

    private func move(_ d: Int) {
        guard let i = index() else { return }
        let j = i + d
        guard j >= 0, j < store.data.templates.count else { return }
        store.data.templates.swapAt(i, j)
        store.save()
    }
}

struct MentionsTab: View {
    @ObservedObject var store = Store.shared
    @State private var message = ""

    private var mentions: [Snippet] { store.data.snippets.filter { Markup.isMention($0) } }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("붙여넣은 @이름은 Slack에서 알림이 가지 않아요. Slack에서 복사한 진짜 멘션을 한 번 등록해 두면, 그 뒤로는 이름만 써도 멘션으로 들어가요.")
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            GroupBox {
                VStack(alignment: .leading, spacing: 6) {
                    Text("1.  Slack 입력창에 등록할 사람·그룹을 @로 넣고(목록에서 선택), 그 글자를 선택해 복사하세요. 보낸 메시지에서 복사해도 돼요.")
                    Text("2.  아래 '클립보드에서 가져오기'를 누르세요.")
                    Text("3.  이제 빈칸에 @이름을 쓰면 실제 멘션으로 붙여넣어져요.")
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(6)
            }

            HStack(spacing: 10) {
                Button(action: importMentions) {
                    Label("클립보드에서 가져오기", systemImage: "doc.on.clipboard")
                }
                .buttonStyle(.borderedProminent)
                Text(message).font(.caption).foregroundColor(.secondary).lineLimit(2)
            }

            Text("등록된 멘션").fontWeight(.semibold).padding(.top, 6)
            if mentions.isEmpty {
                Text("아직 등록된 멘션이 없어요.").foregroundColor(.secondary)
                Spacer()
            } else {
                List {
                    ForEach(mentions) { m in
                        HStack {
                            Image(systemName: "person.crop.circle")
                            Text(m.name).fontWeight(.medium)
                            if Markup.mentionId(m) == nil {
                                Text("ID 없음 — 글자로만 들어가요. 다시 가져와 주세요").font(.caption).foregroundColor(.secondary)
                            }
                            Spacer()
                            Button {
                                store.data.snippets.removeAll { $0.id == m.id }
                                store.save()
                            } label: {
                                Image(systemName: "trash")
                            }
                            .buttonStyle(.borderless)
                            .help("삭제")
                        }
                    }
                }
            }
        }
    }

    private func importMentions() {
        let clip = Paster.getClip()
        let found = Markup.findMentions(in: clip)
        if found.isEmpty {
            message = "멘션을 찾지 못했어요. Slack 입력창에 @이름을 넣고(목록에서 선택) 그 글자를 선택해 복사(⌘C)한 뒤 다시 눌러 주세요."
            return
        }
        var added = 0, updated = 0
        for m in found {
            if let i = store.data.snippets.firstIndex(where: { $0.name.caseInsensitiveCompare(m.name) == .orderedSame }) {
                if !m.html.isEmpty { store.data.snippets[i].html = m.html }
                if let id = m.slackId, !id.isEmpty { store.data.snippets[i].slackId = id }
                store.data.snippets[i].plain = m.plain
                updated += 1
            } else {
                store.data.snippets.append(m)
                added += 1
            }
        }
        store.save()
        message = "새로 \(added)개, 갱신 \(updated)개: " + found.map { $0.name }.joined(separator: ", ")
    }
}

struct SettingsTab: View {
    @ObservedObject var store = Store.shared
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var message = ""
    @State private var showAdvanced = false
    var onApplyHotkeys: () -> String?

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        Text("템플릿 팝업 단축키")
                        Spacer()
                        TextField("Ctrl+Alt+Space", text: $store.data.popupHotkey)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 170)
                            .onSubmit(apply)
                        Button("적용", action: apply)
                    }
                    Text("예: Ctrl+Alt+Space, Cmd+Shift+K  (Ctrl · Alt · Shift · Cmd + 글자/숫자/Space/F1~F12)")
                        .font(.caption).foregroundColor(.secondary)
                    HStack {
                        Text("설정 화면 단축키")
                        Spacer()
                        TextField("사용 안 함", text: $store.data.settingsHotkey)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 170)
                            .onSubmit(apply)
                        Button("적용", action: apply)
                    }
                    Text("어디서든 이 설정 화면을 바로 열어요. 비워 두고 적용하면 꺼져요.")
                        .font(.caption).foregroundColor(.secondary)
                    Divider()
                    Toggle("로그인 시 실행", isOn: $launchAtLogin)
                        .onChange(of: launchAtLogin) { on in
                            do {
                                if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                            } catch {
                                message = "로그인 시 실행 설정 실패: " + error.localizedDescription
                            }
                        }
                }
                .padding(6)
            }

            DisclosureGroup("고급 설정", isExpanded: $showAdvanced) {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        Text("서식 직접 입력 단축키")
                        Spacer()
                        TextField("Ctrl+Alt+F", text: $store.data.formatHotkey)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 170)
                            .onSubmit(apply)
                        Button("적용", action: apply)
                    }
                    Toggle("붙여넣은 뒤 원래 복사해 둔 내용 되돌리기", isOn: $store.data.restoreClipboard)
                        .onChange(of: store.data.restoreClipboard) { _ in store.save() }
                    Toggle("스마트 따옴표와 기호  (\" \" → “ ”,  -- → —,  ... → …,  -> → →,  <- → ←)", isOn: $store.data.smartSymbols)
                        .onChange(of: store.data.smartSymbols) { _ in store.save() }
                    HStack {
                        Button("저장 폴더 열기") { NSWorkspace.shared.open(store.folder) }
                        Button("손쉬운 사용 권한 확인") {
                            if Paster.isTrusted {
                                message = "손쉬운 사용 권한이 허용돼 있어요. 자동 붙여넣기가 가능해요."
                            } else {
                                Paster.askAccessibility()
                            }
                        }
                    }
                }
                .padding(.top, 8)
                .padding(.leading, 4)
            }

            if !message.isEmpty {
                Text(message).font(.callout).foregroundColor(.secondary)
            }

            HStack(spacing: 12) {
                Button("SlackQuick 종료") { NSApp.terminate(nil) }
                Text("창을 닫아도 메뉴 막대에서 계속 실행돼요.").font(.caption).foregroundColor(.secondary)
            }
            Spacer()
        }
        .frame(maxWidth: 620, alignment: .leading)
    }

    private func apply() {
        if let err = onApplyHotkeys() { message = err } else { message = "단축키를 적용했어요." }
    }
}

struct HelpView: View {
    @ObservedObject var state: EditorState

    private let rows: [(String, String)] = [
        ("*굵게*", "굵게"),
        ("_기울임_", "기울임"),
        ("~취소선~", "취소선"),
        ("`코드`", "코드"),
        ("```코드 블록```", "코드 블록 (여러 줄 가능)"),
        ("[글자](https://주소)", "글자에 링크"),
        ("> 인용", "인용"),
        ("- 목록", "글머리 목록 · 목록 줄에서 Tab 들여쓰기, Shift+Tab 내어쓰기"),
        ("\"따옴표\"  --  ...  ->  <-", "“따옴표”  —  …  →  ←"),
        (":white_check_mark:", "Slack 이모지 (보내면 Slack이 바꿔요)"),
        ("{{이름}}", "빈칸 · 붙여넣기 전에 물어봐요"),
        ("{{클립보드}}", "복사해 둔 내용이 링크·이모지 서식까지 그대로 들어가요"),
        ("{{날짜}}  {{시간}}", "오늘 날짜와 현재 시간"),
        ("@이름", "'멘션'에 등록한 이름은 실제 멘션으로 들어가요"),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("서식 도움말").font(.title2).fontWeight(.semibold)
            VStack(alignment: .leading, spacing: 0) {
                ForEach(0..<rows.count, id: \.self) { i in
                    HStack(alignment: .firstTextBaseline, spacing: 16) {
                        Text(rows[i].0)
                            .font(.system(size: 12, design: .monospaced))
                            .frame(width: 210, alignment: .leading)
                        Text(rows[i].1)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .padding(.vertical, 6)
                    if i < rows.count - 1 { Divider() }
                }
            }
            HStack {
                Spacer()
                Button("닫기") { state.showHelp = false }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 600)
    }
}

struct EditorView: View {
    @ObservedObject var state: EditorState
    var onApplyHotkeys: () -> String?

    var body: some View {
        VStack(spacing: 14) {
            Picker("", selection: $state.tab) {
                Text("템플릿").tag(0)
                Text("멘션").tag(1)
                Text("설정").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(width: 300)

            Group {
                if state.tab == 0 {
                    TemplatesTab(state: state)
                } else if state.tab == 1 {
                    MentionsTab()
                } else {
                    SettingsTab(onApplyHotkeys: onApplyHotkeys)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .padding(16)
        .frame(minWidth: 780, minHeight: 560)
        .sheet(isPresented: $state.showHelp) { HelpView(state: state) }
    }
}
