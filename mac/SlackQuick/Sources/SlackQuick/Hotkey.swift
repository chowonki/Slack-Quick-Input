import AppKit
import Carbon.HIToolbox

/// Carbon RegisterEventHotKey 기반 전역 단축키
final class HotkeyCenter {
    static let shared = HotkeyCenter()

    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var handlers: [UInt32: () -> Void] = [:]
    private var installed = false

    private func installHandler() {
        if installed { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ -> OSStatus in
            var hk = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            let id = hk.id
            DispatchQueue.main.async { HotkeyCenter.shared.handlers[id]?() }
            return noErr
        }, 1, &spec, nil, nil)
    }

    func unregisterAll() {
        for (_, ref) in refs { UnregisterEventHotKey(ref) }
        refs.removeAll()
        handlers.removeAll()
    }

    /// - Returns: 성공 여부
    @discardableResult
    func register(id: UInt32, _ text: String, handler: @escaping () -> Void) -> Bool {
        installHandler()
        guard let parsed = HotkeyCenter.parse(text) else { return false }
        let mods = parsed.0, key = parsed.1
        var ref: EventHotKeyRef?
        let hkID = EventHotKeyID(signature: OSType(0x53514B31) /* 'SQK1' */, id: id)
        let status = RegisterEventHotKey(key, mods, hkID, GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let r = ref else { return false }
        refs[id] = r
        handlers[id] = handler
        return true
    }

    static let keyCodes: [String: Int] = [
        "a": kVK_ANSI_A, "b": kVK_ANSI_B, "c": kVK_ANSI_C, "d": kVK_ANSI_D, "e": kVK_ANSI_E, "f": kVK_ANSI_F,
        "g": kVK_ANSI_G, "h": kVK_ANSI_H, "i": kVK_ANSI_I, "j": kVK_ANSI_J, "k": kVK_ANSI_K, "l": kVK_ANSI_L,
        "m": kVK_ANSI_M, "n": kVK_ANSI_N, "o": kVK_ANSI_O, "p": kVK_ANSI_P, "q": kVK_ANSI_Q, "r": kVK_ANSI_R,
        "s": kVK_ANSI_S, "t": kVK_ANSI_T, "u": kVK_ANSI_U, "v": kVK_ANSI_V, "w": kVK_ANSI_W, "x": kVK_ANSI_X,
        "y": kVK_ANSI_Y, "z": kVK_ANSI_Z,
        "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4,
        "5": kVK_ANSI_5, "6": kVK_ANSI_6, "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9,
        "space": kVK_Space, "return": kVK_Return, "enter": kVK_Return, "tab": kVK_Tab,
        "f1": kVK_F1, "f2": kVK_F2, "f3": kVK_F3, "f4": kVK_F4, "f5": kVK_F5, "f6": kVK_F6,
        "f7": kVK_F7, "f8": kVK_F8, "f9": kVK_F9, "f10": kVK_F10, "f11": kVK_F11, "f12": kVK_F12,
        ";": kVK_ANSI_Semicolon, "'": kVK_ANSI_Quote, ",": kVK_ANSI_Comma, ".": kVK_ANSI_Period,
        "/": kVK_ANSI_Slash, "`": kVK_ANSI_Grave,
    ]

    /// "Ctrl+Alt+Space", "Cmd+Shift+K" 같은 문자열 → (Carbon 수정키, 키코드)
    static func parse(_ text: String) -> (UInt32, UInt32)? {
        var mods: UInt32 = 0
        var key: UInt32?
        for raw in text.split(separator: "+") {
            let p = raw.trimmingCharacters(in: .whitespaces).lowercased()
            switch p {
            case "ctrl", "control", "⌃": mods |= UInt32(controlKey)
            case "alt", "option", "opt", "⌥": mods |= UInt32(optionKey)
            case "shift", "⇧": mods |= UInt32(shiftKey)
            case "cmd", "command", "⌘": mods |= UInt32(cmdKey)
            default:
                guard let k = keyCodes[p] else { return nil }
                key = UInt32(k)
            }
        }
        guard let k = key, mods != 0 else { return nil }
        return (mods, k)
    }
}
