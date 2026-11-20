import AppKit

enum PopupItem {
    case template(Template)
    case free
}

final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// 검색해서 템플릿을 고르는 팝업
final class PopupController: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSTextFieldDelegate, NSWindowDelegate {
    let panel: KeyPanel
    private let search = NSTextField()
    private let table = NSTableView()
    private var items: [PopupItem] = []
    private var picked = false
    var onPick: ((PopupItem) -> Void)?
    var onClose: (() -> Void)?

    override init() {
        panel = KeyPanel(contentRect: NSRect(x: 0, y: 0, width: 560, height: 450),
                         styleMask: [.titled, .fullSizeContentView],
                         backing: .buffered, defer: false)
        super.init()

        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.delegate = self
        panel.standardWindowButton(.closeButton)?.isHidden = true
        panel.standardWindowButton(.miniaturizeButton)?.isHidden = true
        panel.standardWindowButton(.zoomButton)?.isHidden = true

        search.placeholderString = "템플릿 검색"
        search.font = .systemFont(ofSize: 18)
        search.isBordered = false
        search.focusRingType = .none
        search.drawsBackground = false
        search.delegate = self

        let col = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("c"))
        table.addTableColumn(col)
        table.headerView = nil
        table.rowHeight = 46
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.doubleAction = #selector(pickSelected)
        table.style = .inset

        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false

        let hint = NSTextField(labelWithString: "↑↓ 이동 · Enter 선택 · Esc 닫기")
        hint.font = .systemFont(ofSize: 11)
        hint.textColor = .secondaryLabelColor

        let sep = NSBox()
        sep.boxType = .separator

        let root = NSView()
        for v in [search, sep, scroll, hint] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(v)
        }
        NSLayoutConstraint.activate([
            search.topAnchor.constraint(equalTo: root.topAnchor, constant: 30),
            search.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            search.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            sep.topAnchor.constraint(equalTo: search.bottomAnchor, constant: 8),
            sep.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            sep.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: sep.bottomAnchor, constant: 4),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            hint.topAnchor.constraint(equalTo: scroll.bottomAnchor, constant: 4),
            hint.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            hint.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -8),
        ])
        panel.contentView = root
        refresh()
    }

    func show() {
        panel.center()
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(search)
    }

    private func refresh() {
        let q = search.stringValue
        let ranked = Store.shared.data.templates.enumerated()
            .map { (i: $0.offset, t: $0.element, s: Markup.score($0.element, q)) }
            .filter { $0.s > 0 }
            .sorted { $0.s != $1.s ? $0.s > $1.s : $0.i < $1.i }
        items = ranked.map { PopupItem.template($0.t) } + [PopupItem.free]
        table.reloadData()
        if !items.isEmpty { table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false) }
    }

    // MARK: 표

    func numberOfRows(in tableView: NSTableView) -> Int { items.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let title: String, sub: String
        switch items[row] {
        case .free:
            title = "서식 직접 입력"
            sub = "*굵게*  _기울임_  ~취소선~  `코드`  [글자](주소) 로 직접 써서 붙여넣기"
        case .template(let t):
            title = t.name.isEmpty ? "(이름 없음)" : t.name
            sub = t.body.replacingOccurrences(of: "\n", with: " ⏎ ")
        }
        let a = NSTextField(labelWithString: title)
        a.font = .systemFont(ofSize: 14, weight: .semibold)
        a.lineBreakMode = .byTruncatingTail
        let b = NSTextField(labelWithString: sub)
        b.font = .systemFont(ofSize: 11.5)
        b.textColor = .secondaryLabelColor
        b.lineBreakMode = .byTruncatingTail
        let stack = NSStackView(views: [a, b])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 4, bottom: 4, right: 4)
        a.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        b.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return stack
    }

    // MARK: 검색창 키 처리

    func controlTextDidChange(_ obj: Notification) { refresh() }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        switch selector {
        case #selector(NSResponder.moveDown(_:)):
            move(1); return true
        case #selector(NSResponder.moveUp(_:)):
            move(-1); return true
        case #selector(NSResponder.insertNewline(_:)):
            pickSelected(); return true
        case #selector(NSResponder.cancelOperation(_:)):
            close(); return true
        default:
            return false
        }
    }

    private func move(_ d: Int) {
        guard !items.isEmpty else { return }
        let r = max(0, min(items.count - 1, table.selectedRow + d))
        table.selectRowIndexes(IndexSet(integer: r), byExtendingSelection: false)
        table.scrollRowToVisible(r)
    }

    @objc func pickSelected() {
        let r = table.selectedRow
        guard r >= 0, r < items.count else { return }
        picked = true
        let item = items[r]
        panel.orderOut(nil)
        panel.close()
        onPick?(item)
    }

    func close() {
        panel.close()
    }

    // MARK: 창

    func windowDidResignKey(_ notification: Notification) {
        if !picked { close() }
    }

    func windowWillClose(_ notification: Notification) {
        if !picked { onClose?() }
    }
}
