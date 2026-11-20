import AppKit
import SwiftUI

enum PopupItem {
    case template(Template)
    case free
}

final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// 팝업 상태: 검색어 · 결과 · 선택
final class PopupModel: ObservableObject {
    @Published var query = "" {
        didSet { if query != oldValue { refresh() } }
    }
    @Published private(set) var items: [PopupItem] = []
    @Published var selection = 0
    weak var field: NSTextField?

    init() { refresh() }

    func refresh() {
        let q = query
        let ranked = Store.shared.data.templates.enumerated()
            .map { (i: $0.offset, t: $0.element, s: Markup.score($0.element, q)) }
            .filter { $0.s > 0 }
            .sorted { $0.s != $1.s ? $0.s > $1.s : $0.i < $1.i }
        items = ranked.map { PopupItem.template($0.t) } + [PopupItem.free]
        selection = 0
    }

    func move(_ d: Int) {
        guard !items.isEmpty else { return }
        selection = max(0, min(items.count - 1, selection + d))
    }

    var selected: PopupItem? {
        selection >= 0 && selection < items.count ? items[selection] : nil
    }
}

/// 검색 입력칸. 한글 조합 중에도 ↑↓·Enter·Esc가 올바로 처리되도록 NSTextField를 그대로 씁니다.
struct PopupSearchField: NSViewRepresentable {
    @ObservedObject var model: PopupModel
    var onSubmit: () -> Void
    var onCancel: () -> Void

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSTextField {
        let field = NSTextField()
        field.placeholderString = "템플릿 검색"
        field.font = .systemFont(ofSize: 20)
        field.isBordered = false
        field.focusRingType = .none
        field.drawsBackground = false
        field.delegate = context.coordinator
        model.field = field
        DispatchQueue.main.async { field.window?.makeFirstResponder(field) }
        return field
    }

    func updateNSView(_ field: NSTextField, context: Context) {
        context.coordinator.parent = self
        if field.stringValue != model.query { field.stringValue = model.query }
    }

    final class Coordinator: NSObject, NSTextFieldDelegate {
        var parent: PopupSearchField
        init(_ parent: PopupSearchField) { self.parent = parent }

        func controlTextDidChange(_ obj: Notification) {
            if let field = obj.object as? NSTextField { parent.model.query = field.stringValue }
        }

        func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
            switch selector {
            case #selector(NSResponder.moveDown(_:)):
                parent.model.move(1); return true
            case #selector(NSResponder.moveUp(_:)):
                parent.model.move(-1); return true
            case #selector(NSResponder.insertNewline(_:)):
                parent.onSubmit(); return true
            case #selector(NSResponder.cancelOperation(_:)):
                parent.onCancel(); return true
            default:
                return false
            }
        }
    }
}

struct PopupRow: View {
    let item: PopupItem
    let selected: Bool

    private var symbol: String {
        switch item {
        case .free: return "square.and.pencil"
        case .template(let t): return Markup.askFields(t.body).isEmpty ? "text.bubble" : "doc.text"
        }
    }

    private var title: String {
        switch item {
        case .free: return "서식 직접 입력"
        case .template(let t): return t.name.isEmpty ? "(이름 없음)" : t.name
        }
    }

    private var subtitle: String {
        switch item {
        case .free: return "*굵게*  _기울임_  ~취소선~  `코드`  [글자](주소) 로 직접 써서 붙여넣기"
        case .template(let t): return t.body.replacingOccurrences(of: "\n", with: " ⏎ ")
        }
    }

    private var keyword: String {
        if case .template(let t) = item { return t.keyword.trimmingCharacters(in: .whitespaces) }
        return ""
    }

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .medium))
                .frame(width: 34, height: 34)
                .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Color.primary.opacity(0.07)))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                Text(subtitle).font(.system(size: 11.5)).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 8)
            if !keyword.isEmpty {
                Text(keyword)
                    .font(.system(size: 11, weight: .medium, design: .rounded))
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(Color.primary.opacity(0.08)))
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous)
            .fill(selected ? Color.accentColor.opacity(0.22) : Color.clear))
        .contentShape(Rectangle())
    }
}

struct PopupView: View {
    @ObservedObject var model: PopupModel
    var onPick: () -> Void
    var onCancel: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 18, weight: .medium))
                    .foregroundStyle(.secondary)
                PopupSearchField(model: model, onSubmit: onPick, onCancel: onCancel)
                    .frame(height: 28)
            }
            .padding(.horizontal, 20)
            .padding(.top, 16)
            .padding(.bottom, 12)

            Divider().opacity(0.6)

            ScrollViewReader { proxy in
                ScrollView {
                    VStack(spacing: 2) {
                        ForEach(Array(model.items.enumerated()), id: \.offset) { pair in
                            PopupRow(item: pair.element, selected: pair.offset == model.selection)
                                .id(pair.offset)
                                .onTapGesture {
                                    model.selection = pair.offset
                                    onPick()
                                }
                        }
                    }
                    .padding(8)
                }
                .onChange(of: model.selection) { i in proxy.scrollTo(i) }
            }

            Divider().opacity(0.6)

            HStack(spacing: 14) {
                KeyHint(keys: "↑↓", label: "이동")
                KeyHint(keys: "Enter", label: "선택")
                KeyHint(keys: "Esc", label: "닫기")
                Spacer()
                Text("\(max(0, model.items.count - 1))개 템플릿")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 9)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// 검색해서 템플릿을 고르는 팝업 (유리판 위에 SwiftUI)
final class PopupController: NSObject, NSWindowDelegate {
    let panel: KeyPanel
    private let model = PopupModel()
    private var picked = false
    var onPick: ((PopupItem) -> Void)?
    var onClose: (() -> Void)?

    override init() {
        panel = KeyPanel(contentRect: NSRect(x: 0, y: 0, width: 620, height: 440),
                         styleMask: [.borderless, .fullSizeContentView],
                         backing: .buffered, defer: false)
        super.init()

        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.delegate = self

        let view = PopupView(model: model,
                             onPick: { [weak self] in self?.pickSelected() },
                             onCancel: { [weak self] in self?.close() })
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = [] // 창 크기는 직접 정함
        panel.contentView = GlassChrome.container(hosting, cornerRadius: 22)
        panel.setContentSize(NSSize(width: 620, height: 440))
    }

    func show() {
        panel.center()
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.contentView?.layoutSubtreeIfNeeded()
        focusSearch()
        DispatchQueue.main.async { [weak self] in self?.focusSearch() }
    }

    private func focusSearch() {
        guard let field = model.field, field.window === panel, field.currentEditor() == nil else { return }
        panel.makeFirstResponder(field)
    }

    private func pickSelected() {
        guard !picked, let item = model.selected else { return }
        picked = true
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
