import AppKit
import ApplicationServices

/// 서식 있는 HTML + 일반 텍스트를 클립보드에 넣고 이전 앱에 ⌘V로 붙여넣습니다.
enum Paster {
    /// 클립보드의 글자와 서식(HTML 조각)을 함께 읽습니다.
    static func getClip() -> ClipData {
        let pb = NSPasteboard.general
        var clip = ClipData()
        clip.text = pb.string(forType: .string) ?? ""
        if let raw = pb.string(forType: .html) {
            let cleaned = Markup.cleanHTML(raw)
            if !cleaned.isEmpty { clip.html = cleaned }
        }
        // Slack에서 복사한 내용이면 멘션 ID가 든 서식 데이터가 같이 있음
        let custom = pb.data(forType: NSPasteboard.PasteboardType(Markup.webCustomDataType))
        clip.texty = Markup.readWebCustomData(custom, type: Markup.textyType)
        return clip
    }

    static var isTrusted: Bool { AXIsProcessTrusted() }

    static func askAccessibility() {
        let key = "AXTrustedCheckOptionPrompt"
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func paste(_ content: Composed, into app: NSRunningApplication?) {
        let pb = NSPasteboard.general

        // 이전 클립보드 백업 (모든 항목·형식 → 서식까지 되돌림)
        var backup: [NSPasteboardItem] = []
        if Store.shared.data.restoreClipboard {
            for item in pb.pasteboardItems ?? [] {
                let copy = NSPasteboardItem()
                for type in item.types {
                    if let d = item.data(forType: type) { copy.setData(d, forType: type) }
                }
                backup.append(copy)
            }
        }

        pb.clearContents()
        let item = NSPasteboardItem()
        item.setString(Markup.htmlDocument(content.toHTML()), forType: .html)
        item.setString(content.toPlain(), forType: .string)
        // Slack은 자기 서식 데이터(slack/texty)를 먼저 읽음 → 멘션·목록·인용이 진짜로 들어감.
        // HTML과 글자는 Slack이 아닌 앱에 붙일 때 쓰임.
        item.setData(Markup.webCustomData(type: Markup.textyType, value: content.toTexty()),
                     forType: NSPasteboard.PasteboardType(Markup.webCustomDataType))
        pb.writeObjects([item])
        let changeCount = pb.changeCount

        app?.activate(options: [.activateIgnoringOtherApps])

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) {
            if !isTrusted {
                askAccessibility()
                let alert = NSAlert()
                alert.messageText = "손쉬운 사용 권한이 필요해요"
                alert.informativeText = "자동 붙여넣기(⌘V)를 하려면 시스템 설정 > 개인정보 보호 및 보안 > 손쉬운 사용에서 SlackQuick을 켜 주세요.\n\n지금 내용은 클립보드에 복사돼 있으니 ⌘V로 직접 붙여넣을 수 있어요."
                alert.runModal()
                return
            }
            sendCommandV()

            if !backup.isEmpty {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    // 그 사이 사용자가 다른 걸 복사했다면 건드리지 않음
                    guard pb.changeCount == changeCount else { return }
                    pb.clearContents()
                    pb.writeObjects(backup)
                }
            }
        }
    }

    static func sendCommandV() {
        let src = CGEventSource(stateID: .combinedSessionState)
        let vKey: CGKeyCode = 0x09 // kVK_ANSI_V
        let down = CGEvent(keyboardEventSource: src, virtualKey: vKey, keyDown: true)
        let up = CGEvent(keyboardEventSource: src, virtualKey: vKey, keyDown: false)
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }
}
