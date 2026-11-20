import AppKit
import SwiftUI

/// 빈칸 입력용 텍스트뷰: ⌘Enter 제출, Tab 다음 칸(목록 줄에서는 들여쓰기), ⌘B/⌘I/⌘K 서식
final class FieldTextView: NSTextView {
    var onSubmit: (() -> Void)?
    var onCancel: (() -> Void)?
    var onFormat: ((String) -> Void)?
    /// Tab(true) · Shift+Tab(false)으로 다음/이전 칸으로 이동
    var onTab: ((Bool) -> Void)?

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if flags == .command {
            switch key {
            case "\r": onSubmit?(); return true
            case "b": onFormat?("bold"); return true
            case "i": onFormat?("italic"); return true
            case "k": onFormat?("link"); return true
            default: break
            }
        }
        if flags == [.command, .shift] {
            switch key {
            case "x": onFormat?("strike"); return true
            case "c": onFormat?("code"); return true
            default: break
            }
        }
        return super.performKeyEquivalent(with: event)
    }

    override func keyDown(with event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if event.keyCode == 36 /* Return */ && flags.contains(.command) {
            onSubmit?()
            return
        }
        super.keyDown(with: event)
    }

    override func cancelOperation(_ sender: Any?) { onCancel?() }

    /// 붙여넣기: 복사한 내용에 링크·굵게 같은 서식이 있으면 [글자](주소) · *굵게* 문법으로 바꿔 넣음
    override func paste(_ sender: Any?) {
        Markup.learnMentions(Paster.getClip()) // Slack에서 복사한 멘션은 자동 등록
        if let raw = NSPasteboard.general.string(forType: .html) {
            let html = Markup.cleanHTML(raw)
            if Markup.hasRichHTML(html) {
                let text = Markup.htmlToMarkup(html)
                if !text.isEmpty {
                    insertText(text, replacementRange: selectedRange())
                    return
                }
            }
        }
        pasteAsPlainText(sender)
    }

    override func becomeFirstResponder() -> Bool {
        let ok = super.becomeFirstResponder()
        if ok { selectAll(nil) }
        return ok
    }

    // MARK: 글머리 목록 (Tab 들여쓰기 · Shift+Tab 내어쓰기 · Enter 다음 항목)

    private struct ListLine {
        var start: Int      // 줄 시작 위치
        var length: Int     // 줄 길이 (줄바꿈 제외)
        var tabs: Int
        var marker: String
        var isEmpty: Bool
    }

    private func listLine() -> ListLine? {
        let sel = selectedRange()
        if sel.length > 0 { return nil }
        let ns = string as NSString
        let lr = ns.lineRange(for: NSRange(location: sel.location, length: 0))
        var line = ns.substring(with: lr)
        while line.hasSuffix("\n") || line.hasSuffix("\r") { line.removeLast() }
        var tabs = 0
        var rest = Substring(line)
        while rest.hasPrefix("\t") {
            tabs += 1
            rest = rest.dropFirst()
        }
        guard rest.hasPrefix("- ") || rest.hasPrefix("* ") else { return nil }
        let content = rest.dropFirst(2).trimmingCharacters(in: .whitespaces)
        return ListLine(start: lr.location, length: (line as NSString).length, tabs: tabs,
                        marker: String(rest.prefix(1)), isEmpty: content.isEmpty)
    }

    private func edit(_ range: NSRange, _ replacement: String, caret: Int) {
        guard shouldChangeText(in: range, replacementString: replacement) else { return }
        textStorage?.replaceCharacters(in: range, with: replacement)
        didChangeText()
        setSelectedRange(NSRange(location: max(0, caret), length: 0))
    }

    override func insertTab(_ sender: Any?) {
        if let l = listLine() {
            if l.tabs < 5 {
                edit(NSRange(location: l.start, length: 0), "\t", caret: selectedRange().location + 1)
            }
            return
        }
        if let onTab = onTab { onTab(true) } else { window?.selectNextKeyView(self) }
    }

    override func insertBacktab(_ sender: Any?) {
        if let l = listLine() {
            if l.tabs > 0 {
                edit(NSRange(location: l.start, length: 1), "", caret: selectedRange().location - 1)
            }
            return
        }
        if let onTab = onTab { onTab(false) } else { window?.selectPreviousKeyView(self) }
    }

    override func insertNewline(_ sender: Any?) {
        if let l = listLine() {
            let caret = selectedRange().location
            if l.isEmpty {
                // 빈 항목에서 Enter: 한 단계 내어쓰고, 맨 앞이면 목록을 끝냄
                if l.tabs > 0 {
                    edit(NSRange(location: l.start, length: 1), "", caret: caret - 1)
                } else {
                    edit(NSRange(location: l.start, length: l.length), "", caret: l.start)
                }
                return
            }
            if caret >= l.start + l.tabs + 2 {
                let next = "\n" + String(repeating: "\t", count: l.tabs) + l.marker + " "
                edit(NSRange(location: caret, length: 0), next, caret: caret + (next as NSString).length)
                return
            }
        }
        super.insertNewline(sender)
    }
}

enum HTMLRender {
    static var isDark: Bool {
        NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
    }

    /// 본문 HTML → 서식 있는 글자 (미리보기용)
    static func attributed(_ bodyHTML: String) -> NSAttributedString {
        if bodyHTML.isEmpty {
            return NSAttributedString(string: "입력하면 여기에 Slack에 들어갈 모양이 보여요.",
                                      attributes: [.foregroundColor: NSColor.secondaryLabelColor,
                                                   .font: NSFont.systemFont(ofSize: 12)])
        }
        let html = Markup.previewDocument(Markup.flattenForPreview(bodyHTML), dark: isDark)
        guard let data = html.data(using: .utf8),
              let s = try? NSAttributedString(
                data: data,
                options: [.documentType: NSAttributedString.DocumentType.html,
                          .characterEncoding: String.Encoding.utf8.rawValue],
                documentAttributes: nil)
        else { return NSAttributedString(string: "") }
        return s
    }
}

/// 빈칸 창의 바뀌는 값 (미리보기 · 안내)
final class FillModel: ObservableObject {
    @Published var previewHTML = ""
    @Published var showNote = false
}

struct FillField: Identifiable {
    let id: Int
    let name: String
    let view: FieldTextView
}

/// FieldTextView(AppKit)를 SwiftUI 안에 넣는 틀
struct FieldHost: NSViewRepresentable {
    let view: FieldTextView

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.borderType = .noBorder
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        scroll.documentView = view
        return scroll
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {}
}

struct FillView: View {
    @ObservedObject var model: FillModel
    let fields: [FillField]
    let single: Bool
    let mentions: [String]
    var onFormat: (String) -> Void
    var onMention: (String) -> Void
    var onSubmit: () -> Void
    var onCancel: () -> Void

    private func tool(_ kind: String, _ tip: String, @ViewBuilder label: () -> some View) -> some View {
        Button { onFormat(kind) } label: {
            label().frame(width: 30, height: 24).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(tip)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            // 서식 버튼: 자주 쓰는 것만, 나머지는 ⋯
            HStack(spacing: 2) {
                tool("bold", "굵게  *글자*  (⌘B)") { Text("B").font(.system(size: 13, weight: .bold)) }
                tool("italic", "기울임  _글자_  (⌘I)") { Text("I").font(.system(size: 13, design: .serif)).italic() }
                tool("link", "링크  [글자](주소)  (⌘K)") { Image(systemName: "link") }
                tool("list", "목록  줄 앞에 -  (Tab 들여쓰기)") { Image(systemName: "list.bullet") }
                Menu {
                    Button("취소선   ~글자~") { onFormat("strike") }
                    Button("코드   `글자`") { onFormat("code") }
                    Button("코드 블록   ```글자```") { onFormat("codeblock") }
                    Button("인용   > 글자") { onFormat("quote") }
                } label: {
                    Image(systemName: "ellipsis")
                }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .frame(width: 30, height: 24)
                .help("서식 더 보기")
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .glassCapsule()

            // 등록된 멘션: 누르면 지금 칸에 @이름이 들어감
            if !mentions.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(mentions, id: \.self) { name in
                            Button(name) { onMention(name) }
                                .controlSize(.small)
                                .glassButton()
                        }
                    }
                    .padding(.vertical, 2)
                }
            }

            ForEach(fields) { f in
                VStack(alignment: .leading, spacing: 4) {
                    if !single {
                        Text(f.name).font(.system(size: 13, weight: .semibold))
                    }
                    FieldHost(view: f.view)
                        .frame(height: single ? 150 : 38)
                        .contentCard(cornerRadius: 9)
                }
            }

            Text("Slack 미리보기").font(.system(size: 13, weight: .semibold)).padding(.top, 2)
            HTMLPreview(bodyHTML: model.previewHTML)
                .frame(minHeight: 60, maxHeight: .infinity)
                .contentCard(cornerRadius: 9)

            if model.showNote {
                Text("검은색 @이름은 등록되지 않아 알림이 가지 않아요. 관리 창의 '멘션'에서 등록할 수 있어요.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack(spacing: 12) {
                KeyHint(keys: "⌘Enter", label: "붙여넣기")
                KeyHint(keys: "Esc", label: "취소")
                Spacer()
                Button("취소", action: onCancel)
                    .keyboardShortcut(.cancelAction)
                    .glassButton()
                Button("붙여넣기", action: onSubmit)
                    .keyboardShortcut(.return, modifiers: .command)
                    .glassButton(prominent: true)
            }
            .controlSize(.large)
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// 템플릿의 {{빈칸}}을 채우는 창 (서식 버튼 + 멘션 버튼 + Slack 미리보기)
final class FillController: NSObject, NSWindowDelegate, NSTextViewDelegate {
    private static var lastValues: [String: String] = [:]

    let window: NSWindow
    private let body: String
    private let clip: ClipData
    private var fields: [(name: String, view: FieldTextView)] = []
    private let model = FillModel()
    private weak var lastFocused: FieldTextView?
    private var submitted = false
    private var cancelled = false

    var onDone: ((Composed?) -> Void)?

    init(title: String, body: String, clip: ClipData) {
        self.body = body
        self.clip = clip
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 600, height: 300),
                          styleMask: [.titled, .closable, .resizable],
                          backing: .buffered, defer: false)
        super.init()
        window.title = title
        window.level = .floating
        window.isReleasedWhenClosed = false
        window.delegate = self

        let names = Markup.askFields(body)
        let single = names.count == 1 && body.trimmingCharacters(in: .whitespacesAndNewlines) == "{{" + names[0] + "}}"
        for name in names {
            let tv = FieldTextView(frame: NSRect(x: 0, y: 0, width: 560, height: 30))
            tv.isRichText = false
            tv.font = .systemFont(ofSize: 14)
            tv.drawsBackground = false
            tv.isAutomaticQuoteSubstitutionEnabled = false
            tv.isAutomaticDashSubstitutionEnabled = false
            tv.isAutomaticTextReplacementEnabled = false
            tv.isAutomaticSpellingCorrectionEnabled = false
            tv.allowsUndo = true
            tv.minSize = NSSize(width: 0, height: 0)
            tv.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
            tv.isVerticallyResizable = true
            tv.isHorizontallyResizable = false
            tv.autoresizingMask = [.width]
            tv.textContainer?.widthTracksTextView = true
            tv.textContainerInset = NSSize(width: 4, height: 8)
            tv.string = FillController.lastValues[name] ?? ""
            tv.delegate = self
            tv.onSubmit = { [weak self] in self?.submit() }
            tv.onCancel = { [weak self] in self?.window.close() }
            tv.onFormat = { [weak self] kind in self?.format(kind) }
            tv.onTab = { [weak self, weak tv] forward in
                guard let tv = tv else { return }
                self?.moveFocus(from: tv, forward: forward)
            }
            fields.append((name: name, view: tv))
        }

        let mentions = Markup.mentions.map { $0.name }
        let view = FillView(
            model: model,
            fields: fields.enumerated().map { FillField(id: $0.offset, name: $0.element.name, view: $0.element.view) },
            single: single,
            mentions: mentions,
            onFormat: { [weak self] kind in self?.format(kind) },
            onMention: { [weak self] name in self?.insertMention(name) },
            onSubmit: { [weak self] in self?.submit() },
            onCancel: { [weak self] in self?.window.close() })
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = [] // 창 크기는 직접 정함
        window.contentView = hosting

        // 도구 모음 + 멘션 + 칸 + 미리보기 + 버튼
        var height: CGFloat = 16 + 42 + (mentions.isEmpty ? 0 : 36) + 28 + 140 + 56 + 16
        height += single ? 160 : CGFloat(names.count) * 72
        let limit = (NSScreen.main?.visibleFrame.height ?? 800) - 80
        window.setContentSize(NSSize(width: 600, height: min(max(height, 300), limit)))
        window.minSize = NSSize(width: 460, height: 300)
        updatePreview()
    }

    func show() {
        window.center()
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.contentView?.layoutSubtreeIfNeeded()
        focusFirst()
        DispatchQueue.main.async { [weak self] in self?.focusFirst() }
    }

    private func focusFirst() {
        guard let first = fields.first?.view, first.window === window else { return }
        if window.firstResponder === first { return }
        window.makeFirstResponder(first)
        lastFocused = first
    }

    private func moveFocus(from tv: FieldTextView, forward: Bool) {
        guard fields.count > 1, let i = fields.firstIndex(where: { $0.view === tv }) else { return }
        let next = fields[(i + (forward ? 1 : fields.count - 1)) % fields.count].view
        window.makeFirstResponder(next)
        lastFocused = next
    }

    /// 빌드 검증용: 칸을 차례로 채웁니다.
    func testFill(_ values: [String]) {
        for (i, f) in fields.enumerated() where i < values.count { f.view.string = values[i] }
        updatePreview()
    }

    // MARK: 버튼

    private func insertMention(_ name: String) {
        guard let tv = target else { return }
        tv.insertText(name + " ", replacementRange: tv.selectedRange())
        if window.firstResponder !== tv { window.makeFirstResponder(tv) }
        updatePreview()
    }

    private var target: FieldTextView? {
        if let fr = window.firstResponder as? FieldTextView { return fr }
        return lastFocused ?? fields.first?.view
    }

    private func format(_ kind: String) {
        guard let tv = target else { return }
        let ns = tv.string as NSString
        let sel = tv.selectedRange()
        let selected = ns.substring(with: sel)

        func wrap(_ pre: String, _ post: String) {
            let text = pre + selected + post
            tv.insertText(text, replacementRange: sel)
            if sel.length == 0 {
                tv.setSelectedRange(NSRange(location: sel.location + (pre as NSString).length, length: 0))
            } else {
                tv.setSelectedRange(NSRange(location: sel.location, length: (text as NSString).length))
            }
        }

        func prefixLine(_ prefix: String) {
            let lineStart = ns.lineRange(for: NSRange(location: sel.location, length: 0)).location
            tv.insertText(prefix, replacementRange: NSRange(location: lineStart, length: 0))
            tv.setSelectedRange(NSRange(location: sel.location + (prefix as NSString).length, length: 0))
        }

        switch kind {
        case "bold": wrap("*", "*")
        case "italic": wrap("_", "_")
        case "strike": wrap("~", "~")
        case "code": wrap("`", "`")
        case "codeblock": wrap("```", "```")
        case "link":
            let label = selected.isEmpty ? "글자" : selected
            let text = "[" + label + "](https://)"
            tv.insertText(text, replacementRange: sel)
            let start = sel.location + (label as NSString).length + 3
            tv.setSelectedRange(NSRange(location: start, length: 8))
        case "quote": prefixLine("> ")
        case "list": prefixLine("- ")
        default: break
        }
        if window.firstResponder !== tv { window.makeFirstResponder(tv) }
        updatePreview()
    }

    // MARK: 내용

    private func compose() -> Composed {
        var values: [String: String] = [:]
        for f in fields { values[f.name] = f.view.string }
        return Markup.compose(body, values: values, clip: clip)
    }

    private func updatePreview() {
        let content = compose()
        model.previewHTML = content.toPreviewHTML()
        model.showNote = Markup.hasLooseMention(content.text)
    }

    func textDidChange(_ notification: Notification) {
        if let tv = notification.object as? FieldTextView { lastFocused = tv }
        updatePreview()
    }

    func textDidBeginEditing(_ notification: Notification) {
        if let tv = notification.object as? FieldTextView { lastFocused = tv }
    }

    private func submit() {
        if submitted || cancelled { return }
        for f in fields { FillController.lastValues[f.name] = f.view.string }
        submitted = true
        let content = compose()
        window.orderOut(nil)
        window.close()
        onDone?(content)
    }

    func windowWillClose(_ notification: Notification) {
        if submitted || cancelled { return }
        cancelled = true
        onDone?(nil)
    }
}
