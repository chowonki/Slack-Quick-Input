import AppKit

/// 빈칸 입력용 텍스트뷰: ⌘Enter 제출, Tab 다음 칸(목록 줄에서는 들여쓰기), ⌘B/⌘I/⌘K 서식
final class FieldTextView: NSTextView {
    var onSubmit: (() -> Void)?
    var onCancel: (() -> Void)?
    var onFormat: ((String) -> Void)?

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
        window?.selectNextKeyView(self)
    }

    override func insertBacktab(_ sender: Any?) {
        if let l = listLine() {
            if l.tabs > 0 {
                edit(NSRange(location: l.start, length: 1), "", caret: selectedRange().location - 1)
            }
            return
        }
        window?.selectPreviousKeyView(self)
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

/// 템플릿의 {{빈칸}}을 채우는 창 (서식 버튼 + 멘션 버튼 + Slack 미리보기)
final class FillController: NSObject, NSWindowDelegate, NSTextViewDelegate {
    private static var lastValues: [String: String] = [:]

    let window: NSWindow
    private let body: String
    private let clip: ClipData
    private var fields: [(name: String, view: FieldTextView)] = []
    private let preview = NSTextView()
    private let note = NSTextField(wrappingLabelWithString: "검은색 @이름은 등록되지 않아 알림이 가지 않아요. 관리 창의 '멘션'에서 등록할 수 있어요.")
    private weak var lastFocused: FieldTextView?
    private var submitted = false

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

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        stack.edgeInsets = NSEdgeInsets(top: 14, left: 16, bottom: 14, right: 16)

        // 서식 버튼: 자주 쓰는 것만, 나머지는 ⋯
        let bar = NSStackView(views: [
            toolButton("B", "굵게  *글자*  (⌘B)", "bold"),
            toolButton("I", "기울임  _글자_  (⌘I)", "italic"),
            symbolButton("link", "링크  [글자](주소)  (⌘K)", #selector(formatClicked(_:)), "link"),
            symbolButton("list.bullet", "목록  줄 앞에 -  (Tab 들여쓰기)", #selector(formatClicked(_:)), "list"),
            symbolButton("ellipsis", "서식 더 보기", #selector(moreClicked(_:)), "more"),
        ])
        bar.spacing = 4
        stack.addArrangedSubview(bar)

        // 등록된 멘션: 누르면 지금 칸에 @이름이 들어감
        let mentions = Markup.mentions
        if !mentions.isEmpty {
            let chips = NSStackView()
            chips.spacing = 4
            for m in mentions.prefix(8) {
                let b = NSButton(title: m.name, target: self, action: #selector(chipClicked(_:)))
                b.bezelStyle = .rounded
                b.controlSize = .small
                b.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
                b.refusesFirstResponder = true
                chips.addArrangedSubview(b)
            }
            stack.addArrangedSubview(chips)
        }

        let names = Markup.askFields(body)
        let singleFree = names.count == 1 && body.trimmingCharacters(in: .whitespacesAndNewlines) == "{{" + names[0] + "}}"
        var previousView: NSView?
        for name in names {
            if !singleFree {
                let label = NSTextField(labelWithString: name)
                label.font = .systemFont(ofSize: 13, weight: .semibold)
                stack.addArrangedSubview(label)
            }

            let scroll = NSScrollView()
            scroll.borderType = .bezelBorder
            scroll.hasVerticalScroller = true
            scroll.autohidesScrollers = true
            let tv = FieldTextView(frame: NSRect(x: 0, y: 0, width: 560, height: 30))
            tv.isRichText = false
            tv.font = .systemFont(ofSize: 14)
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
            tv.textContainerInset = NSSize(width: 2, height: 4)
            tv.string = FillController.lastValues[name] ?? ""
            tv.delegate = self
            tv.onSubmit = { [weak self] in self?.submit() }
            tv.onCancel = { [weak self] in self?.window.close() }
            tv.onFormat = { [weak self] kind in self?.format(kind) }
            scroll.documentView = tv
            scroll.translatesAutoresizingMaskIntoConstraints = false
            scroll.heightAnchor.constraint(equalToConstant: singleFree ? 150 : 36).isActive = true
            stack.addArrangedSubview(scroll)
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true

            previousView?.nextKeyView = tv
            previousView = tv
            fields.append((name: name, view: tv))
        }

        let pl = NSTextField(labelWithString: "Slack 미리보기")
        pl.font = .systemFont(ofSize: 13, weight: .semibold)
        stack.addArrangedSubview(pl)

        preview.frame = NSRect(x: 0, y: 0, width: 560, height: 130)
        preview.minSize = NSSize(width: 0, height: 0)
        preview.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        preview.isEditable = false
        preview.isSelectable = true
        preview.drawsBackground = false
        preview.textContainerInset = NSSize(width: 6, height: 6)
        preview.isVerticallyResizable = true
        preview.autoresizingMask = [.width]
        preview.textContainer?.widthTracksTextView = true
        let pscroll = NSScrollView()
        pscroll.documentView = preview
        pscroll.hasVerticalScroller = true
        pscroll.autohidesScrollers = true
        pscroll.drawsBackground = false
        pscroll.borderType = .lineBorder
        pscroll.translatesAutoresizingMaskIntoConstraints = false
        pscroll.heightAnchor.constraint(equalToConstant: 130).isActive = true
        stack.addArrangedSubview(pscroll)
        pscroll.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true

        note.font = .systemFont(ofSize: 11)
        note.textColor = .secondaryLabelColor
        note.isHidden = true
        stack.addArrangedSubview(note)
        note.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true

        let hint = NSTextField(labelWithString: "⌘Enter 붙여넣기")
        hint.font = .systemFont(ofSize: 11)
        hint.textColor = .secondaryLabelColor
        let cancel = NSButton(title: "취소", target: self, action: #selector(cancelClicked))
        cancel.keyEquivalent = "\u{1b}"
        let ok = NSButton(title: "붙여넣기", target: self, action: #selector(okClicked))
        ok.keyEquivalent = "\r"
        ok.keyEquivalentModifierMask = [.command]
        ok.bezelColor = .controlAccentColor
        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        let bottom = NSStackView(views: [hint, spacer, cancel, ok])
        bottom.orientation = .horizontal
        stack.addArrangedSubview(bottom)
        bottom.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true

        window.contentView = stack
        let fit = stack.fittingSize
        window.setContentSize(NSSize(width: 600, height: max(fit.height, 240)))
        window.minSize = NSSize(width: 460, height: 240)
        updatePreview()
    }

    func show() {
        window.center()
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        if let first = fields.first?.view {
            window.makeFirstResponder(first)
            lastFocused = first
        }
    }

    /// 빌드 검증용: 칸을 차례로 채웁니다.
    func testFill(_ values: [String]) {
        for (i, f) in fields.enumerated() where i < values.count { f.view.string = values[i] }
        updatePreview()
    }

    // MARK: 버튼

    private func toolButton(_ title: String, _ tip: String, _ kind: String) -> NSButton {
        let b = NSButton(title: title, target: self, action: #selector(formatClicked(_:)))
        b.toolTip = tip
        b.identifier = NSUserInterfaceItemIdentifier(kind)
        b.bezelStyle = .rounded
        b.refusesFirstResponder = true
        if kind == "bold" { b.font = .boldSystemFont(ofSize: 13) }
        if kind == "italic" {
            b.font = NSFontManager.shared.convert(.systemFont(ofSize: 13), toHaveTrait: .italicFontMask)
        }
        return b
    }

    private func symbolButton(_ symbol: String, _ tip: String, _ action: Selector, _ kind: String) -> NSButton {
        let b: NSButton
        if let img = NSImage(systemSymbolName: symbol, accessibilityDescription: tip) {
            b = NSButton(image: img, target: self, action: action)
        } else {
            b = NSButton(title: kind, target: self, action: action)
        }
        b.toolTip = tip
        b.identifier = NSUserInterfaceItemIdentifier(kind)
        b.bezelStyle = .rounded
        b.refusesFirstResponder = true
        return b
    }

    @objc private func formatClicked(_ sender: NSButton) {
        format(sender.identifier?.rawValue ?? "")
    }

    @objc private func moreClicked(_ sender: NSButton) {
        let menu = NSMenu()
        for (title, kind) in [("취소선   ~글자~", "strike"), ("코드   `글자`", "code"),
                              ("코드 블록   ```글자```", "codeblock"), ("인용   > 글자", "quote")] {
            let item = NSMenuItem(title: title, action: #selector(menuFormat(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = kind
            menu.addItem(item)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.height + 4), in: sender)
    }

    @objc private func menuFormat(_ sender: NSMenuItem) {
        format(sender.representedObject as? String ?? "")
    }

    @objc private func chipClicked(_ sender: NSButton) {
        guard let tv = target else { return }
        tv.insertText(sender.title + " ", replacementRange: tv.selectedRange())
        window.makeFirstResponder(tv)
        updatePreview()
    }

    @objc private func okClicked() { submit() }
    @objc private func cancelClicked() { window.close() }

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
        window.makeFirstResponder(tv)
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
        preview.textStorage?.setAttributedString(HTMLRender.attributed(content.toPreviewHTML()))
        note.isHidden = !Markup.hasLooseMention(content.text)
    }

    func textDidChange(_ notification: Notification) {
        if let tv = notification.object as? FieldTextView { lastFocused = tv }
        updatePreview()
    }

    func textDidBeginEditing(_ notification: Notification) {
        if let tv = notification.object as? FieldTextView { lastFocused = tv }
    }

    private func submit() {
        for f in fields { FillController.lastValues[f.name] = f.view.string }
        submitted = true
        let content = compose()
        window.orderOut(nil)
        window.close()
        onDone?(content)
    }

    func windowWillClose(_ notification: Notification) {
        if !submitted { onDone?(nil) }
    }
}
