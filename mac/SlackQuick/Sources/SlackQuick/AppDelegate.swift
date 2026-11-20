import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var statusItem: NSStatusItem!
    private var settingsMenuItem: NSMenuItem?
    private var popup: PopupController?
    private var fill: FillController?
    private var editorWindow: NSWindow?
    private let editorState = EditorState()
    private var busy = false

    private let selfTest = CommandLine.arguments.contains("--selftest")
    private var testStep = 0
    private var testTimer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        Store.shared.load()
        buildMainMenu()
        buildMenu()

        if selfTest {
            runSelfTest()
            return
        }

        if let failed = registerHotkeys() {
            alert("단축키 등록 실패", failed + " 단축키를 등록하지 못했어요. 다른 앱이 쓰는 중일 수 있어요. 설정에서 바꿔 주세요.")
            openEditor(tab: 2)
        } else if Store.shared.isFirstRun {
            alert("SlackQuick 실행 중",
                  "메뉴 막대의 아이콘에서 쓸 수 있어요.\n\n" + Store.shared.data.popupHotkey + ": 템플릿 팝업\n"
                  + Store.shared.data.settingsHotkey + ": 설정\n\n자동 붙여넣기를 위해 손쉬운 사용 권한을 허용해 주세요.")
            Paster.askAccessibility()
            openEditor(tab: 0)
        }
    }

    /// 메뉴 막대 전용 앱이라도 ⌘C/⌘V/⌘X/⌘A/⌘Z가 입력칸에서 동작하도록 편집 메뉴를 둡니다.
    private func buildMainMenu() {
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "SlackQuick 종료", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "편집")
        edit.addItem(withTitle: "실행 취소", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "다시 실행", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "오려두기", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "복사", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "붙여넣기", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "모두 선택", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        NSApp.mainMenu = main
    }

    // MARK: 메뉴 막대

    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            let image = NSImage(systemSymbolName: "apple.terminal", accessibilityDescription: "SlackQuick")
                ?? NSImage(systemSymbolName: "terminal", accessibilityDescription: "SlackQuick")
            if let img = image {
                img.isTemplate = true
                button.image = img
            } else {
                button.title = ">_"
            }
        }
        let menu = NSMenu()
        menu.addItem(withTitle: "템플릿 팝업 열기", action: #selector(menuPopup), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "템플릿 관리", action: #selector(menuEditor), keyEquivalent: "").target = self
        let settings = menu.addItem(withTitle: "설정", action: #selector(menuSettings), keyEquivalent: ",")
        settings.target = self
        settingsMenuItem = settings
        menu.addItem(.separator())
        menu.addItem(withTitle: "SlackQuick 종료", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        statusItem.menu = menu
    }

    @objc private func menuPopup() {
        let target = NSWorkspace.shared.frontmostApplication
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { self.openPopup(target: target) }
    }

    @objc private func menuEditor() { openEditor(tab: 0) }
    @objc private func menuSettings() { openEditor(tab: 2) }

    // MARK: 단축키

    /// - Returns: 실패한 단축키, 모두 성공하면 nil
    func registerHotkeys() -> String? {
        let hc = HotkeyCenter.shared
        hc.unregisterAll()
        var failed: [String] = []
        let d = Store.shared.data
        if !hc.register(id: 1, d.popupHotkey, handler: { [weak self] in
            self?.openPopup(target: NSWorkspace.shared.frontmostApplication)
        }) { failed.append(d.popupHotkey) }
        if !hc.register(id: 2, d.formatHotkey, handler: { [weak self] in
            self?.openFill(title: "서식 직접 입력", body: "{{내용}}", target: NSWorkspace.shared.frontmostApplication)
        }) { failed.append(d.formatHotkey) }
        // 설정 화면 단축키: 비워 두면 쓰지 않음
        let settingsKey = d.settingsHotkey.trimmingCharacters(in: .whitespaces)
        var settingsOn = false
        if !settingsKey.isEmpty {
            settingsOn = hc.register(id: 3, settingsKey, handler: { [weak self] in self?.openEditor(tab: 2) })
            if !settingsOn { failed.append(settingsKey) }
        }
        settingsMenuItem?.title = settingsOn ? "설정  (" + settingsKey + ")" : "설정"
        return failed.isEmpty ? nil : failed.joined(separator: ", ")
    }

    // MARK: 흐름

    private func openPopup(target: NSRunningApplication?) {
        if busy { return }
        busy = true
        let p = PopupController()
        p.onClose = { [weak self] in
            self?.busy = false
            self?.popup = nil
        }
        p.onPick = { [weak self] item in
            guard let self = self else { return }
            self.popup = nil
            self.busy = false
            switch item {
            case .free: self.openFill(title: "서식 직접 입력", body: "{{내용}}", target: target)
            case .template(let t): self.openFill(title: t.name, body: t.body, target: target)
            }
        }
        popup = p
        p.show()
    }

    private func openFill(title: String, body: String, target: NSRunningApplication?) {
        if busy { return }
        let clip = Paster.getClip()
        Markup.learnMentions(clip) // Slack에서 복사한 멘션은 자동 등록
        if Markup.askFields(body).isEmpty {
            Paster.paste(Markup.compose(body, values: [:], clip: clip), into: target)
            return
        }
        busy = true
        let f = FillController(title: title, body: body, clip: clip)
        f.onDone = { [weak self] content in
            self?.busy = false
            self?.fill = nil
            if let content = content {
                Paster.paste(content, into: target)
            } else {
                target?.activate(options: [.activateIgnoringOtherApps])
            }
        }
        fill = f
        f.show()
    }

    func openEditor(tab: Int) {
        editorState.tab = tab
        if let w = editorWindow {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        let view = EditorView(state: editorState, onApplyHotkeys: { [weak self] in
            let d = Store.shared.data
            let settingsKey = d.settingsHotkey.trimmingCharacters(in: .whitespaces)
            var keys = [d.popupHotkey, d.formatHotkey]
            if !settingsKey.isEmpty { keys.append(settingsKey) } // 비워 두면 끔
            for hk in keys where HotkeyCenter.parse(hk) == nil {
                return "단축키 형식이 올바르지 않아요: " + hk
            }
            Store.shared.save()
            if let failed = self?.registerHotkeys() { return failed + " 단축키 등록에 실패했어요. 다른 조합을 써 주세요." }
            return nil
        })
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 880, height: 640),
                         styleMask: [.titled, .closable, .resizable, .miniaturizable],
                         backing: .buffered, defer: false)
        w.title = "SlackQuick"
        w.contentView = NSHostingView(rootView: view)
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.center()
        editorWindow = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        if (notification.object as? NSWindow) === editorWindow {
            Store.shared.save()
            editorWindow = nil
        }
    }

    private func alert(_ title: String, _ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        a.runModal()
    }

    // MARK: 빌드 검증 (--selftest): 모든 창을 차례로 열어 보고 selftest.log에 기록한 뒤 종료

    private func log(_ text: String) {
        let url = Store.shared.folder.appendingPathComponent("selftest.log")
        let line = text + "\n"
        if let handle = try? FileHandle(forWritingTo: url) {
            handle.seekToEndOfFile()
            handle.write(line.data(using: .utf8) ?? Data())
            try? handle.close()
        } else {
            try? line.write(to: url, atomically: true, encoding: .utf8)
        }
    }

    private func runSelfTest() {
        try? FileManager.default.removeItem(at: Store.shared.folder.appendingPathComponent("selftest.log"))
        testStepRun()
        testTimer = Timer.scheduledTimer(withTimeInterval: 4, repeats: true) { [weak self] _ in
            self?.testStepRun()
        }
    }

    private func testStepRun() {
        let step = testStep
        testStep += 1
        switch step {
        case 0:
            openEditor(tab: 0)
            let sample = "<div>12가3456 | <a href=\"https://ext.example.com/a?b=1&amp;c=2\">외부링크</a><img data-stringify-emoji=\":thread:\" alt=\":thread:\"></div><div><a href='https://car.example.com/cars/98765'><b>차량링크</b></a> (98765)</div><ul><li>하나<ul><li>둘</li></ul></li></ul>"
            log("h2m: " + Markup.htmlToMarkup(sample).replacingOccurrences(of: "\n", with: " / "))
            let texty = "{\"ops\":[{\"insert\":\"@테스트\",\"attributes\":{\"slackmention\":{\"id\":\"U0TEST12345\",\"label\":\"@테스트\"}}},{\"insert\":\"\\n\"}]}"
            let blob = Markup.webCustomData(type: Markup.textyType, value: texty)
            var testClip = ClipData(text: "x", html: nil)
            testClip.texty = Markup.readWebCustomData(blob, type: Markup.textyType)
            log("pickle: " + String(blob.count) + " roundtrip=" + String(testClip.texty == texty))
            Markup.learnMentions(testClip)
            let composed = Markup.compose("@테스트 *확인* :thread:\n- 하나\n\t- 둘\n> 인용", values: [:], clip: testClip)
            log("texty: " + composed.toTexty().replacingOccurrences(of: "\n", with: " "))
            log("editor ok")
        case 1:
            Store.shared.data.snippets.append(Snippet(name: "@홍길동", html: "<a>@홍길동</a>", plain: "@홍길동"))
            Store.shared.data.snippets.append(Snippet(name: "@운영팀", html: "<a>@운영팀</a>", plain: "@운영팀"))
            openEditor(tab: 1)
            log("mentions ok")
        case 2:
            openEditor(tab: 2)
            log("settings ok")
        case 3:
            openEditor(tab: 0)
            editorState.showHelp = true
            log("help ok")
        case 4:
            editorState.showHelp = false
            log("templates ok")
        case 5:
            editorWindow?.close()
            let p = PopupController()
            popup = p
            p.show()
            log("popup ok")
        case 6:
            popup?.close()
            popup = nil
            let f = FillController(title: "공지 (태그·타이틀·전달내용)",
                                   body: "{{멘션}}\n*[{{태그}}] {{타이틀}}*\n{{내용}}\n\n> {{전달내용}}",
                                   clip: ClipData(text: "clip", html: nil))
            fill = f
            f.show()
            f.testFill(["@홍길동 @없는사람", "공지", "점검 안내", "오늘 18시 \"서버\" 점검 -> [자세히](https://slack.com)", "확인 부탁드려요"])
            log("fill ok")
        case 7:
            fill?.window.close()
            let f = FillController(title: "서식 직접 입력", body: "{{내용}}", clip: ClipData(text: "clip", html: nil))
            fill = f
            f.show()
            f.testFill(["@홍길동 \"서버\" 점검 -> *완료*...\n- 순서\n\t- DB 백업\n\t\t- 확인 -- 끝\n- 마무리\n```sudo restart```\n> 인용"])
            log("free ok")
        default:
            testTimer?.invalidate()
            log("OK")
            NSApp.terminate(nil)
        }
    }
}
