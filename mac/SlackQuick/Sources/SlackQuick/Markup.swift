import Foundation

/// 붙여넣을 내용: 빈칸·자동 빈칸·서식 조각·멘션을 채운 결과.
/// 서식 조각(복사한 HTML, 멘션)은 text 안에 자리표시(TOKEN_OPEN 번호 TOKEN_CLOSE)로 들어 있고,
/// HTML로 바꾼 뒤 원본 HTML로 되돌려 끼웁니다. (Windows 버전 Markup.Composed와 같은 방식)
final class Composed {
    var text = ""
    var parts: [Snippet] = []

    private func resolve(_ s: String, _ pick: (Snippet) -> String) -> String {
        Markup.replace(Markup.tokenRx, s) { m in
            guard let n = Int(Markup.group(m, 1, s) ?? ""), n >= 0, n < parts.count else { return "" }
            return pick(parts[n])
        }
    }

    /// 붙여넣기용 HTML (서식 조각은 원본 HTML 그대로)
    func toHTML() -> String {
        resolve(Markup.toHTML(text)) { Markup.isMention($0) ? Markup.mentionHTML($0) : $0.html }
    }

    /// Slack 입력창이 읽는 서식 데이터(slack/texty). 멘션·목록·인용이 진짜로 들어감.
    func toTexty() -> String { Markup.toDelta(text, parts: parts) }

    func toPlain() -> String { resolve(Markup.toPlain(text)) { $0.plain } }

    /// 미리보기용 HTML (서식 조각은 글자만, 멘션은 링크 모양)
    func toPreviewHTML() -> String {
        resolve(Markup.toHTML(text)) { part in
            let plain = part.plain.replacingOccurrences(of: "\r\n", with: "\n")
                .trimmingCharacters(in: .whitespacesAndNewlines)
            let t = Markup.escape(plain).replacingOccurrences(of: "\n", with: "<br>")
            // ID를 아는 멘션만 파랗게
            if Markup.isMention(part) { return Markup.mentionId(part) != nil ? "<a>" + t + "</a>" : t }
            // 복사한 조각: 링크·굵게가 걸린 모양 그대로 보여 줌
            return Markup.hasRichHTML(part.html) ? Markup.toHTML(Markup.htmlToMarkup(part.html)) : t
        }
    }
}

/// 슬랙식 텍스트 서식(*굵게* _기울임_ ~취소선~ `코드` ```코드 블록``` [글자](주소) <주소|글자> > 인용 - 목록)을
/// HTML(서식 붙여넣기용)과 일반 텍스트로 바꿉니다. Windows 버전 Markup.cs와 같은 규칙입니다.
enum Markup {
    private static func rx(_ p: String, _ options: NSRegularExpression.Options = []) -> NSRegularExpression {
        try! NSRegularExpression(pattern: p, options: options)
    }

    // 자리표시용 사설 영역 문자 (코드값으로 만들어 소스에 특수 문자가 들어가지 않게 함)
    static let tokenOpen = String(UnicodeScalar(0xE000)!)
    static let tokenClose = String(UnicodeScalar(0xE001)!)

    static let linkRx = rx(
        #"\[([^\]\n]+)\]\((https?://[^\s)]+)\)"# +
        #"|<(https?://[^|>\s]+)\|([^>\n]+)>"# +
        #"|<(https?://[^>\s]+)>"# +
        #"|(https?://[^\s<>]+)"#)
    static let codeRx = rx(#"`([^`\n]+)`"#)
    static let boldRx = rx(#"(?<![A-Za-z0-9*])\*(?=\S)([^*\n]+?)(?<=\S)\*(?![A-Za-z0-9*])"#)
    static let italicRx = rx(#"(?<![A-Za-z0-9_])_(?=\S)([^_\n]+?)(?<=\S)_(?![A-Za-z0-9_])"#)
    static let strikeRx = rx(#"(?<![A-Za-z0-9~])~(?=\S)([^~\n]+?)(?<=\S)~(?![A-Za-z0-9~])"#)
    static let placeholderRx = rx(#"\{\{([^{}\n]+)\}\}"#)
    static let fenceRx = rx("```(.*?)```", [.dotMatchesLineSeparators])
    static let listRx = rx(#"^([\t ]*)[-*] (.*)$"#)
    static let plainListRx = rx(#"^([\t ]*)[-*] "#, [.anchorsMatchLines])
    static let quoteRx = rx(#""([^"\n]*)""#)
    static let tokenRx = rx(tokenOpen + #"(\d+)"# + tokenClose)
    static let junkRx = rx(
        #"<!--.*?-->|<meta\b[^>]*>|<style\b.*?</style>|<script\b.*?</script>|</?(html|head|body)\b[^>]*>"#,
        [.dotMatchesLineSeparators, .caseInsensitive])
    static let mentionRx = rx(
        #"<(a|ts-mention|span)\b[^>]*>\s*(@[^<\s][^<]*?)\s*</\1>"#,
        [.dotMatchesLineSeparators, .caseInsensitive])
    static let listTagRx = rx("</?(ul|li)>")
    static let blockquoteRx = rx("<blockquote>(.*?)</blockquote>", [.dotMatchesLineSeparators])
    static let htmlTagRx = rx(#"<(/?)([a-zA-Z][a-zA-Z0-9\-]*)([^>]*)>"#, [.dotMatchesLineSeparators])
    static let hrefRx = rx(#"\bhref\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))"#, [.caseInsensitive])
    static let emojiAttrRx = rx(#"\bdata-stringify-emoji\s*=\s*(?:"([^"]*)"|'([^']*)')"#, [.caseInsensitive])
    static let altRx = rx(#"\balt\s*=\s*(?:"([^"]*)"|'([^']*)')"#, [.caseInsensitive])
    static let richRx = rx(#"<(a|b|strong|i|em|s|del|strike|code|pre|li|img)\b"#, [.caseInsensitive])
    static let spaceRunRx = rx(#"[ \t\r\n]+"#)
    static let trailingSpaceRx = rx(#"[ \t]+\n"#)
    static let manyNewlinesRx = rx(#"\n{3,}"#)
    static let numericEntityRx = rx(#"&#(x?)([0-9a-fA-F]+);"#, [.caseInsensitive])
    static let mentionIdRx = rx(#"data-(?:member-id|stringify-id|user-group-id|broadcast-id|id)\s*=\s*["']([A-Za-z0-9]{4,})["']"#)
    static let mentionHrefRx = rx(#"/(?:team|usergroups)/([A-Z0-9]{6,})"#)
    static let styleTagRx = rx("<(/?)(b|i|s)>")
    static let deltaEmojiRx = rx(#"(?<![\w:]):[a-z0-9_+\-]*[a-z][a-z0-9_+\-]*:(?![\w:])"#)
    static let looseMentionRx = rx(#"(?<![\w@])@[^\s@]+"#)

    static let autoFields = ["클립보드", "날짜", "시간"]

    // MARK: 정규식 도우미

    static func matches(_ re: NSRegularExpression, _ s: String) -> [NSTextCheckingResult] {
        re.matches(in: s, options: [], range: NSRange(s.startIndex..., in: s))
    }

    static func group(_ m: NSTextCheckingResult, _ i: Int, _ s: String) -> String? {
        let r = m.range(at: i)
        guard r.location != NSNotFound, let rr = Range(r, in: s) else { return nil }
        return String(s[rr])
    }

    static func replace(_ re: NSRegularExpression, _ s: String, _ template: String) -> String {
        re.stringByReplacingMatches(in: s, options: [], range: NSRange(s.startIndex..., in: s), withTemplate: template)
    }

    static func replace(_ re: NSRegularExpression, _ s: String, _ f: (NSTextCheckingResult) -> String) -> String {
        var out = ""
        var pos = s.startIndex
        for m in matches(re, s) {
            guard let r = Range(m.range, in: s) else { continue }
            out += s[pos..<r.lowerBound]
            out += f(m)
            pos = r.upperBound
        }
        out += s[pos...]
        return out
    }

    static func normalize(_ s: String) -> String {
        s.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
    }

    // MARK: 빈칸 · 서식 조각 · 멘션

    static func placeholders(_ body: String) -> [String] {
        var list: [String] = []
        for m in matches(placeholderRx, body) {
            if let name = group(m, 1, body)?.trimmingCharacters(in: .whitespaces), !list.contains(name) {
                list.append(name)
            }
        }
        return list
    }

    static func findSnippet(_ name: String) -> Snippet? {
        Store.shared.data.snippets.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
    }

    static func isMention(_ s: Snippet) -> Bool { s.name.hasPrefix("@") && s.name.count > 1 }

    /// 등록한 멘션 HTML에서 Slack 사용자/그룹 ID를 찾습니다.
    static func mentionId(_ s: Snippet) -> String? {
        if let id = s.slackId, !id.isEmpty { return id }
        var open = s.html
        if let end = open.firstIndex(of: ">") { open = String(open[...end]) }
        if let m = matches(mentionIdRx, open).first, let id = group(m, 1, open) { return id }
        if let m = matches(mentionHrefRx, open).first, let id = group(m, 1, open) { return id }
        return nil
    }

    /// 붙여넣을 멘션 HTML. Slack 메시지에서 복사한 멘션은 링크(a) 모양이라 그대로 붙이면 링크로만 들어가므로,
    /// Slack 입력창이 멘션으로 알아보는 ts-mention 요소로 바꿔서 내보냅니다.
    static func mentionHTML(_ s: Snippet) -> String {
        let html = s.html
        if html.trimmingCharacters(in: .whitespacesAndNewlines).lowercased().hasPrefix("<ts-mention") { return html }
        guard let id = mentionId(s) else { return html.isEmpty ? escape(s.name) : html }
        let label = escape(s.name)
        return "<ts-mention data-id=\"" + id + "\" data-label=\"" + label +
            "\" spellcheck=\"false\" class=\"c-member_slug c-member_slug--link ts_tip_texty\" dir=\"ltr\">" +
            label + "</ts-mention>"
    }

    static var mentions: [Snippet] { Store.shared.data.snippets.filter(isMention) }

    static func askFields(_ body: String) -> [String] {
        placeholders(body).filter { !autoFields.contains($0) && findSnippet($0) == nil }
    }

    private static func stripTokens(_ s: String) -> String {
        s.replacingOccurrences(of: tokenOpen, with: "").replacingOccurrences(of: tokenClose, with: "")
    }

    static func compose(_ body: String, values: [String: String], clip: ClipData) -> Composed {
        let result = Composed()
        let now = Date()
        let df = DateFormatter()
        df.locale = Locale(identifier: "ko_KR")

        func token(_ part: Snippet) -> String {
            result.parts.append(part)
            return tokenOpen + String(result.parts.count - 1) + tokenClose
        }

        let source = stripTokens(body)
        var text = replace(placeholderRx, source) { (m: NSTextCheckingResult) -> String in
            let name = (group(m, 1, source) ?? "").trimmingCharacters(in: .whitespaces)
            switch name {
            case "클립보드":
                // 복사한 서식(링크·굵게·목록)은 이 앱의 문법으로 바꿔 넣음 → Slack 형식으로 다시 내보냄
                if let html = clip.html, hasRichHTML(html) { return stripTokens(htmlToMarkup(html)) }
                return stripTokens(clip.text)
            case "날짜":
                df.dateFormat = "yyyy-MM-dd (E)"
                return df.string(from: now)
            case "시간":
                df.dateFormat = "HH:mm"
                return df.string(from: now)
            default:
                if let snippet = findSnippet(name) { return token(snippet) }
                return stripTokens(values[name] ?? "")
            }
        }

        // 등록된 멘션(@이름)은 글자로 써도 진짜 멘션으로 바꿈 (긴 이름부터)
        for mention in mentions.sorted(by: { $0.name.count > $1.name.count }) {
            let pattern = NSRegularExpression.escapedPattern(for: mention.name) + #"(?![\w\-])"#
            guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { continue }
            text = replace(re, text) { (_: NSTextCheckingResult) -> String in token(mention) }
        }

        while let last = text.last, last == "\n" || last == " " { text.removeLast() }
        result.text = text
        return result
    }

    /// 클립보드 HTML 조각에서 주석·meta·style을 걷어냅니다.
    static func cleanHTML(_ html: String) -> String {
        replace(junkRx, html, "").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// HTML 안의 멘션(@이름) 요소를 찾아 이름과 원본 HTML을 돌려줍니다.
    static func findMentions(_ html: String) -> [Snippet] {
        var list: [Snippet] = []
        for m in matches(mentionRx, html) {
            guard let raw = group(m, 2, html), let whole = group(m, 0, html) else { continue }
            let name = unescape(raw).trimmingCharacters(in: .whitespacesAndNewlines)
            if name.count < 2 || list.contains(where: { $0.name == name }) { continue }
            list.append(Snippet(name: name, html: whole, plain: name))
        }
        return list
    }

    /// Slack 서식 데이터(slack/texty)에서 멘션의 ID와 이름을 찾습니다.
    static func mentionsFromTexty(_ json: String?) -> [Snippet] {
        guard let json = json, let data = json.data(using: .utf8),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let ops = root["ops"] as? [[String: Any]] else { return [] }
        var list: [Snippet] = []
        for op in ops {
            guard let attrs = op["attributes"] as? [String: Any],
                  let sm = attrs["slackmention"] as? [String: Any],
                  let id = sm["id"] as? String, !id.isEmpty else { continue }
            var label = (sm["label"] as? String) ?? ""
            if label.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, let ins = op["insert"] as? String {
                label = ins
            }
            label = label.trimmingCharacters(in: .whitespacesAndNewlines)
            if label.count < 2 || !label.hasPrefix("@") || list.contains(where: { $0.name == label }) { continue }
            var snippet = Snippet(name: label, html: "", plain: label)
            snippet.slackId = id
            list.append(snippet)
        }
        return list
    }

    /// 클립보드에서 멘션을 찾습니다: Slack 서식 데이터(ID 포함)가 먼저, 없으면 HTML.
    static func findMentions(in clip: ClipData) -> [Snippet] {
        var list = mentionsFromTexty(clip.texty)
        for found in findMentions(clip.html ?? "") {
            if list.contains(where: { $0.name.caseInsensitiveCompare(found.name) == .orderedSame }) { continue }
            var h = found
            h.slackId = mentionId(h)
            list.append(h)
        }
        return list
    }

    /// 클립보드에 ID가 있는 멘션이 있으면 조용히 등록/갱신합니다.
    @discardableResult
    static func learnMentions(_ clip: ClipData) -> Bool {
        var changed = false
        for m in findMentions(in: clip) {
            guard let id = m.slackId, !id.isEmpty else { continue }
            if let i = Store.shared.data.snippets.firstIndex(where: { $0.name.caseInsensitiveCompare(m.name) == .orderedSame }) {
                if Store.shared.data.snippets[i].slackId != id {
                    Store.shared.data.snippets[i].slackId = id
                    changed = true
                }
            } else {
                Store.shared.data.snippets.append(m)
                changed = true
            }
        }
        if changed { Store.shared.save() }
        return changed
    }

    // MARK: Slack 서식 데이터 (slack/texty = Quill Delta)

    /// 서식 문법을 Slack 입력창이 붙여넣을 때 읽는 Quill Delta JSON으로 바꿉니다.
    /// 굵게·기울임·취소선·코드·링크는 글자 속성으로, 목록·인용·코드 블록은 줄 끝 속성으로,
    /// 멘션은 slackmention(id, label)으로 들어갑니다.
    static func toDelta(_ text: String, parts: [Snippet]) -> String {
        var ops: [[String: Any]] = []

        func dAdd(_ insert: Any, _ attrs: [String: Any]) {
            if let str = insert as? String, str.isEmpty { return }
            var op: [String: Any] = ["insert": insert]
            if !attrs.isEmpty { op["attributes"] = attrs }
            ops.append(op)
        }

        func dAttrs(_ b: Bool, _ i: Bool, _ s: Bool, _ code: Bool, _ link: String?) -> [String: Any] {
            var a: [String: Any] = [:]
            if b { a["bold"] = true }
            if i { a["italic"] = true }
            if s { a["strike"] = true }
            if code { a["code"] = true }
            if let link = link { a["link"] = link }
            return a
        }

        // :이름: 은 Slack 이모지로 (Slack이 복사할 때 쓰는 것과 같은 slackemoji)
        func dPlain(_ t: String, _ attrs: [String: Any]) {
            var pos = t.startIndex
            for m in matches(deltaEmojiRx, t) {
                guard let r = Range(m.range, in: t) else { continue }
                dAdd(String(t[pos..<r.lowerBound]), attrs)
                let emoji: [String: Any] = ["slackemoji": ["text": String(t[r])]]
                dAdd(emoji, attrs)
                pos = r.upperBound
            }
            dAdd(String(t[pos...]), attrs)
        }

        // 자리표시(멘션 등)를 풀면서 글자를 넣음
        func dText(_ t: String, _ attrs: [String: Any]) {
            var pos = t.startIndex
            for m in matches(tokenRx, t) {
                guard let r = Range(m.range, in: t) else { continue }
                dPlain(String(t[pos..<r.lowerBound]), attrs)
                pos = r.upperBound
                guard let n = Int(group(m, 1, t) ?? ""), n >= 0, n < parts.count else { continue }
                let part = parts[n]
                if isMention(part), let id = mentionId(part) {
                    let sm: [String: Any] = ["id": id, "label": part.name, "mention": id.hasPrefix("BK"), "unverified": false]
                    dAdd(part.name, ["slackmention": sm])
                } else {
                    dAdd(part.plain.isEmpty ? part.name : part.plain, attrs)
                }
            }
            dPlain(String(t[pos...]), attrs)
        }

        // *굵게* _기울임_ ~취소선~ : HTML 변환과 같은 규칙을 그대로 써서 결과가 어긋나지 않게 함
        func dStyled(_ seg: String, _ link: String?) {
            if seg.isEmpty { return }
            let html = styles(escape(smart(seg)))
            var b = false, i = false, s = false
            var pos = html.startIndex
            for m in matches(styleTagRx, html) {
                guard let r = Range(m.range, in: html) else { continue }
                dText(unescape(String(html[pos..<r.lowerBound])), dAttrs(b, i, s, false, link))
                pos = r.upperBound
                let on = (group(m, 1, html) ?? "") != "/"
                switch group(m, 2, html) ?? "" {
                case "b": b = on
                case "i": i = on
                case "s": s = on
                default: break
                }
            }
            dText(unescape(String(html[pos...])), dAttrs(b, i, s, false, link))
        }

        func dInline(_ t: String, _ link: String?) {
            var pos = t.startIndex
            for m in matches(codeRx, t) {
                guard let r = Range(m.range, in: t) else { continue }
                dStyled(String(t[pos..<r.lowerBound]), link)
                dAdd(group(m, 1, t) ?? "", dAttrs(false, false, false, true, link))
                pos = r.upperBound
            }
            dStyled(String(t[pos...]), link)
        }

        func dLine(_ lineText: String) {
            var pos = lineText.startIndex
            for m in matches(linkRx, lineText) {
                guard let r = Range(m.range, in: lineText) else { continue }
                dInline(String(lineText[pos..<r.lowerBound]), nil)
                var url = "", label = ""
                if let l = group(m, 1, lineText) {
                    label = l
                    url = group(m, 2, lineText) ?? ""
                } else if let u = group(m, 3, lineText) {
                    url = u
                    label = group(m, 4, lineText) ?? u
                } else if let u = group(m, 5, lineText) {
                    url = u
                    label = u
                } else {
                    url = group(m, 6, lineText) ?? ""
                    label = url
                }
                dInline(label, url)
                pos = r.upperBound
            }
            dInline(String(lineText[pos...]), nil)
        }

        func dNewLine(_ block: [String: Any]) { dAdd("\n", block) }

        let chunks = splitCode(text)
        for (c, chunk) in chunks.enumerated() {
            if chunk.code {
                for codeLine in chunk.text.components(separatedBy: "\n") {
                    dAdd(codeLine, [:])
                    dNewLine(["code-block": true])
                }
                continue
            }
            var p = chunk.text
            if c > 0, p.hasPrefix("\n") { p.removeFirst() }
            if c < chunks.count - 1, p.hasSuffix("\n") { p.removeLast() }
            if p.isEmpty { continue }
            for raw in p.components(separatedBy: "\n") {
                if raw.hasPrefix(">") {
                    var q = String(raw.dropFirst())
                    if q.hasPrefix(" ") { q = String(q.dropFirst()) }
                    dLine(q)
                    dNewLine(["blockquote": true])
                    continue
                }
                if let m = matches(listRx, raw).first {
                    dLine(group(m, 2, raw) ?? "")
                    var block: [String: Any] = ["list": "bullet"]
                    let level = listLevel(group(m, 1, raw) ?? "")
                    if level > 0 { block["indent"] = level }
                    dNewLine(block)
                    continue
                }
                dLine(raw)
                dNewLine([:])
            }
        }
        if ops.isEmpty { dNewLine([:]) }

        let root: [String: Any] = ["ops": ops]
        guard let data = try? JSONSerialization.data(withJSONObject: root, options: [.withoutEscapingSlashes]),
              let json = String(data: data, encoding: .utf8) else { return "{\"ops\":[{\"insert\":\"\\n\"}]}" }
        return json
    }

    // MARK: Chromium 웹 사용자 지정 데이터 (Slack 데스크톱 앱이 slack/texty를 주고받는 통)

    static let textyType = "slack/texty"
    static let webCustomDataType = "org.chromium.web-custom-data"

    /// 형식: uint32 payload 크기, uint32 항목 수, 항목마다 문자열 2개(종류, 값).
    /// 문자열은 uint32 UTF-16 글자 수 + UTF-16LE 데이터 + 4바이트 정렬 채움.
    static func webCustomData(type: String, value: String) -> Data {
        var payload = Data()

        func put32(_ v: Int) {
            payload.append(UInt8(v & 0xFF))
            payload.append(UInt8((v >> 8) & 0xFF))
            payload.append(UInt8((v >> 16) & 0xFF))
            payload.append(UInt8((v >> 24) & 0xFF))
        }

        func putString(_ str: String) {
            let units = Array(str.utf16)
            put32(units.count)
            for u in units {
                payload.append(UInt8(u & 0xFF))
                payload.append(UInt8(u >> 8))
            }
            let pad = (4 - (units.count * 2) % 4) % 4
            for _ in 0..<pad { payload.append(0) }
        }

        put32(1)
        putString(type)
        putString(value)

        let size = payload.count
        var out = Data([UInt8(size & 0xFF), UInt8((size >> 8) & 0xFF), UInt8((size >> 16) & 0xFF), UInt8((size >> 24) & 0xFF)])
        out.append(payload)
        return out
    }

    static func readWebCustomData(_ data: Data?, type: String) -> String? {
        guard let data = data, data.count >= 8 else { return nil }
        let bytes = [UInt8](data)

        func get32(_ p: Int) -> Int? {
            guard p >= 0, p + 4 <= bytes.count else { return nil }
            return Int(bytes[p]) | (Int(bytes[p + 1]) << 8) | (Int(bytes[p + 2]) << 16) | (Int(bytes[p + 3]) << 24)
        }

        guard let size = get32(0), size >= 4, 4 + size <= bytes.count, let count = get32(4) else { return nil }
        let end = 4 + size
        var pos = 8

        func getString() -> String? {
            guard let chars = get32(pos) else { return nil }
            pos += 4
            let n = chars * 2
            guard chars >= 0, pos + n <= end else { return nil }
            var units: [UInt16] = []
            units.reserveCapacity(chars)
            var i = pos
            while i < pos + n {
                units.append(UInt16(bytes[i]) | (UInt16(bytes[i + 1]) << 8))
                i += 2
            }
            pos += n + (4 - n % 4) % 4
            return String(decoding: units, as: UTF16.self)
        }

        var k = 0
        while k < count && pos < end {
            guard let key = getString(), let value = getString() else { return nil }
            if key == type { return value }
            k += 1
        }
        return nil
    }

    /// 등록되지 않은 @이름이 남아 있는지 (자리표시로 바뀐 멘션은 제외됨)
    static func hasLooseMention(_ text: String) -> Bool {
        !matches(looseMentionRx, text).isEmpty
    }

    // MARK: 복사한 HTML → 서식 문법 (입력칸에 붙여넣을 때 링크·굵게 등을 살림)

    /// HTML 조각에 링크·굵게 같은 서식이 들어 있는지
    static func hasRichHTML(_ html: String?) -> Bool {
        guard let html = html, !html.isEmpty else { return false }
        return !matches(richRx, html).isEmpty
    }

    private static func attrValue(_ re: NSRegularExpression, _ attrs: String) -> String {
        guard let m = matches(re, attrs).first else { return "" }
        for i in 1..<m.numberOfRanges {
            if let v = group(m, i, attrs) { return unescape(v) }
        }
        return ""
    }

    /// 복사한 HTML을 이 앱의 서식 문법으로 바꿉니다: 링크 → [글자](주소), 굵게 → *글자*, 목록 → "- " 등.
    static func htmlToMarkup(_ rawHTML: String) -> String {
        let html = normalize(replace(junkRx, rawHTML, ""))
        var out = ""
        var stack: [(tag: String, start: Int, href: String)] = []
        var listDepth = 0
        var inPre = false

        func isSpace(_ c: Character) -> Bool { c == " " || c == "\n" || c == "\t" }

        func addText(_ raw: String) {
            if raw.isEmpty { return }
            var t = unescape(raw)
            if !inPre {
                t = replace(spaceRunRx, t, " ")
                if out.isEmpty || out.hasSuffix("\n") || out.hasSuffix(" ") {
                    while t.hasPrefix(" ") { t.removeFirst() }
                }
            }
            out += t
        }

        func newLine() {
            while out.hasSuffix(" ") { out.removeLast() }
            if !out.isEmpty && !out.hasSuffix("\n") { out += "\n" }
        }

        func close(_ tag: String) {
            guard let idx = stack.lastIndex(where: { $0.tag == tag }) else { return }
            let e = stack[idx]
            stack.removeSubrange(idx...)
            let n = out.count - e.start
            if n < 0 { return }
            let inner = String(out.suffix(n))
            out.removeLast(n)
            let core = inner.trimmingCharacters(in: .whitespacesAndNewlines)
            if core.isEmpty {
                out += inner
                return
            }
            let lead = String(inner.prefix(while: isSpace))
            let trail = String(String(inner.reversed()).prefix(while: isSpace).reversed())
            let plainOnly = core.contains("\n")
            var result = core
            switch tag {
            case "a":
                let url = e.href.trimmingCharacters(in: .whitespaces)
                    .replacingOccurrences(of: " ", with: "%20")
                    .replacingOccurrences(of: ")", with: "%29")
                if !plainOnly && (url.hasPrefix("http://") || url.hasPrefix("https://")) {
                    let label = core.replacingOccurrences(of: "]", with: ")")
                    // 멘션은 이름만 (등록돼 있으면 진짜 멘션으로 바뀜)
                    if label == url || label.hasPrefix("@") {
                        result = label
                    } else {
                        result = "[" + label + "](" + url + ")"
                    }
                }
            case "b":
                if !plainOnly && !core.contains("](") && !core.contains("*") { result = "*" + core + "*" }
            case "i":
                if !plainOnly && !core.contains("](") && !core.contains("_") { result = "_" + core + "_" }
            case "s":
                if !plainOnly && !core.contains("](") && !core.contains("~") { result = "~" + core + "~" }
            case "code":
                if !plainOnly && !core.contains("`") { result = "`" + core + "`" }
            default:
                break
            }
            out += lead + result + trail
        }

        var pos = html.startIndex
        for m in matches(htmlTagRx, html) {
            guard let r = Range(m.range, in: html) else { continue }
            addText(String(html[pos..<r.lowerBound]))
            pos = r.upperBound
            let closing = group(m, 1, html) == "/"
            var tag = (group(m, 2, html) ?? "").lowercased()
            let attrs = group(m, 3, html) ?? ""
            switch tag {
            case "strong": tag = "b"
            case "em": tag = "i"
            case "del", "strike": tag = "s"
            default: break
            }
            switch tag {
            case "br":
                out += "\n"
            case "p", "div", "tr", "table", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6":
                newLine()
            case "ul", "ol":
                newLine()
                listDepth = max(0, listDepth + (closing ? -1 : 1))
            case "li":
                newLine()
                if !closing { out += String(repeating: "\t", count: max(0, listDepth - 1)) + "- " }
            case "img":
                var emoji = attrValue(emojiAttrRx, attrs)
                if emoji.isEmpty { emoji = attrValue(altRx, attrs) }
                out += emoji
            case "pre":
                newLine()
                out += "```"
                inPre = !closing
                if closing { out += "\n" }
            case "a", "b", "i", "s", "code":
                if inPre { break }
                if closing {
                    close(tag)
                } else {
                    stack.append((tag: tag, start: out.count, href: tag == "a" ? attrValue(hrefRx, attrs) : ""))
                }
            default:
                break
            }
        }
        addText(String(html[pos...]))

        var text = replace(trailingSpaceRx, out, "\n")
        text = replace(manyNewlinesRx, text, "\n\n")
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: HTML

    static func escape(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
    }

    static func unescape(_ s: String) -> String {
        if !s.contains("&") { return s }
        let numeric = replace(numericEntityRx, s) { (m: NSTextCheckingResult) -> String in
            let hex = !(group(m, 1, s) ?? "").isEmpty
            guard let v = UInt32(group(m, 2, s) ?? "", radix: hex ? 16 : 10), let u = UnicodeScalar(v) else { return "" }
            return v == 0xA0 ? " " : String(Character(u))
        }
        return numeric.replacingOccurrences(of: "&lt;", with: "<")
            .replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&apos;", with: "'")
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .replacingOccurrences(of: "&amp;", with: "&")
    }

    /// 스마트 따옴표와 기호 (Slack 설정과 같은 규칙)
    private static func smart(_ s: String) -> String {
        guard !s.isEmpty, Store.shared.data.smartSymbols else { return s }
        let r = s.replacingOccurrences(of: "<-", with: "←")
            .replacingOccurrences(of: "->", with: "→")
            .replacingOccurrences(of: "--", with: "—")
            .replacingOccurrences(of: "...", with: "…")
        return replace(quoteRx, r, "“$1”")
    }

    /// ```코드 블록```과 나머지를 나눕니다.
    private static func splitCode(_ raw: String) -> [(code: Bool, text: String)] {
        let text = normalize(raw)
        var out: [(code: Bool, text: String)] = []
        var pos = text.startIndex
        for m in matches(fenceRx, text) {
            guard let r = Range(m.range, in: text), let inner = group(m, 1, text) else { continue }
            if inner.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { continue }
            if r.lowerBound > pos { out.append((code: false, text: String(text[pos..<r.lowerBound]))) }
            out.append((code: true, text: inner.trimmingCharacters(in: .newlines)))
            pos = r.upperBound
        }
        if pos < text.endIndex { out.append((code: false, text: String(text[pos...]))) }
        return out
    }

    static func toHTML(_ text: String) -> String {
        let parts = splitCode(text)
        var out = ""
        for (i, part) in parts.enumerated() {
            if part.code {
                out += "<pre>" + escape(part.text) + "</pre>"
                continue
            }
            var p = part.text
            if i > 0, p.hasPrefix("\n") { p.removeFirst() }
            if i < parts.count - 1, p.hasSuffix("\n") { p.removeLast() }
            if !p.isEmpty { out += blockHTML(p) }
        }
        return out
    }

    static func toPlain(_ text: String) -> String {
        splitCode(text).map { $0.code ? $0.text : plainPart($0.text) }.joined()
    }

    /// 들여쓰기 단계: Tab 1개 또는 공백 2칸이 한 단계 (최대 5단계)
    static func listLevel(_ indent: String) -> Int {
        var tabs = 0, spaces = 0
        for c in indent {
            if c == "\t" { tabs += 1 } else { spaces += 1 }
        }
        return min(5, tabs + spaces / 2)
    }

    private static func blockHTML(_ text: String) -> String {
        let lines = normalize(text).components(separatedBy: "\n")
        var out = ""
        var quote: [String] = []
        var needBreak = false
        var listDepth = 0

        func flushQuote() {
            if quote.isEmpty { return }
            out += "<blockquote>" + quote.joined(separator: "<br>") + "</blockquote>"
            quote.removeAll()
        }

        func closeList() {
            while listDepth > 0 {
                out += "</li></ul>"
                listDepth -= 1
            }
        }

        for raw in lines {
            if raw.hasPrefix(">") {
                if listDepth > 0 { closeList() }
                var q = String(raw.dropFirst())
                if q.hasPrefix(" ") { q = String(q.dropFirst()) }
                quote.append(line(q))
                needBreak = false
                continue
            }
            if !quote.isEmpty {
                flushQuote()
                needBreak = false
            }

            // 글머리 목록: "- " 로 시작, 앞의 Tab 수만큼 들여쓰기 → 진짜 <ul><li>
            if let m = matches(listRx, raw).first {
                let indent = group(m, 1, raw) ?? ""
                let content = group(m, 2, raw) ?? ""
                let depth = min(listLevel(indent) + 1, listDepth + 1)
                if depth > listDepth {
                    out += "<ul>"
                    listDepth += 1
                } else {
                    out += "</li>"
                    while listDepth > depth {
                        out += "</ul></li>"
                        listDepth -= 1
                    }
                }
                out += "<li>" + line(content)
                needBreak = false
                continue
            }
            if listDepth > 0 {
                closeList()
                needBreak = false
            } else if needBreak {
                out += "<br>"
            }

            out += line(raw)
            needBreak = true
        }
        flushQuote()
        closeList()
        return out
    }

    private static func line(_ s: String) -> String {
        var out = ""
        var pos = s.startIndex
        for m in matches(linkRx, s) {
            guard let r = Range(m.range, in: s) else { continue }
            out += inline(String(s[pos..<r.lowerBound]))
            var url = "", label = ""
            if let l = group(m, 1, s) {
                label = l
                url = group(m, 2, s) ?? ""
            } else if let u = group(m, 3, s) {
                url = u
                label = group(m, 4, s) ?? u
            } else if let u = group(m, 5, s) {
                url = u
                label = u
            } else {
                url = group(m, 6, s) ?? ""
                label = url
            }
            out += "<a href=\"" + escape(url) + "\">" + inline(label) + "</a>"
            pos = r.upperBound
        }
        out += inline(String(s[pos...]))
        return out.replacingOccurrences(of: "  ", with: " &nbsp;")
    }

    private static func inline(_ text: String) -> String {
        var out = ""
        var pos = text.startIndex
        for m in matches(codeRx, text) {
            guard let r = Range(m.range, in: text) else { continue }
            out += styles(escape(smart(String(text[pos..<r.lowerBound]))))
            out += "<code>" + escape(group(m, 1, text) ?? "") + "</code>"
            pos = r.upperBound
        }
        out += styles(escape(smart(String(text[pos...]))))
        return out
    }

    private static func styles(_ s: String) -> String {
        var s = replace(boldRx, s, "<b>$1</b>")
        s = replace(italicRx, s, "<i>$1</i>")
        s = replace(strikeRx, s, "<s>$1</s>")
        return s
    }

    /// 붙여넣기용 전체 HTML 문서
    static func htmlDocument(_ bodyHTML: String) -> String {
        "<html><head><meta charset=\"utf-8\"></head><body>" + bodyHTML + "</body></html>"
    }

    /// 미리보기 전용: 목록과 인용을 글머리 기호·막대 글자로 풀어 씁니다.
    /// (macOS의 HTML → 서식 글자 변환이 중첩 목록과 인용 막대를 제대로 그리지 못해서)
    static func flattenForPreview(_ html: String) -> String {
        let src = replace(blockquoteRx, html) { (m: NSTextCheckingResult) -> String in
            let inner = (group(m, 1, html) ?? "").replacingOccurrences(of: "<br>", with: "<br>▎&nbsp;")
            return "<div class=\"q\">▎&nbsp;" + inner + "</div>"
        }
        var out = ""
        var depth = 0
        var pos = src.startIndex
        for m in matches(listTagRx, src) {
            guard let r = Range(m.range, in: src) else { continue }
            out += src[pos..<r.lowerBound]
            pos = r.upperBound
            switch String(src[r]) {
            case "<ul>":
                depth += 1
            case "</ul>":
                depth = max(0, depth - 1)
                if depth == 0 { out += "<br>" }
            case "<li>":
                if !out.isEmpty && !out.hasSuffix("<br>") && !out.hasSuffix("</div>") && !out.hasSuffix("</pre>") {
                    out += "<br>"
                }
                let bullet = depth <= 1 ? "•" : (depth == 2 ? "◦" : "▪")
                out += String(repeating: "&nbsp;", count: 2 + 4 * max(0, depth - 1)) + bullet + "&nbsp;&nbsp;"
            default:
                break
            }
        }
        out += src[pos...]
        while out.hasSuffix("<br>") { out.removeLast(4) }
        return out
    }

    /// 미리보기용 HTML 문서 (글꼴/색 포함, 어두운 테마 대응)
    static func previewDocument(_ bodyHTML: String, dark: Bool) -> String {
        let text = dark ? "#e6e6e6" : "#1d1c1d"
        let link = dark ? "#4aa8e8" : "#1264a3"
        let code = dark ? "#e8912d" : "#c0174f"
        let box = dark ? "#2c2c2e" : "#f2f2f2"
        let quote = dark ? "#b0b0b0" : "#616061"
        var css = "body{font-family:-apple-system,'Apple SD Gothic Neo',sans-serif;font-size:14px;color:" + text + "}"
        css += "code{font-family:Menlo,monospace;color:" + code + ";background:" + box + "}"
        css += "pre{font-family:Menlo,monospace;font-size:12px;background:" + box + ";padding:6px;margin:2px 0}"
        css += "blockquote{margin:2px 0;padding-left:8px;border-left:4px solid #999;color:" + quote + "}"
        css += ".q{color:" + quote + "}"
        css += "a{color:" + link + "}"
        return "<html><head><meta charset=\"utf-8\"><style>" + css + "</style></head><body>" + bodyHTML + "</body></html>"
    }

    // MARK: 일반 텍스트

    private static func plainPart(_ text: String) -> String {
        let src = normalize(text)
        var s = replace(linkRx, src) { (m: NSTextCheckingResult) -> String in
            if let l = group(m, 1, src) {
                let u = group(m, 2, src) ?? ""
                return l == u ? u : l + " (" + u + ")"
            }
            if let u = group(m, 3, src) { return (group(m, 4, src) ?? "") + " (" + u + ")" }
            if let u = group(m, 5, src) { return u }
            return group(m, 6, src) ?? ""
        }
        s = replace(codeRx, s, "$1")
        s = replace(boldRx, s, "$1")
        s = replace(italicRx, s, "$1")
        s = replace(strikeRx, s, "$1")
        let listed = s
        s = replace(plainListRx, listed) { (m: NSTextCheckingResult) -> String in
            String(repeating: " ", count: listLevel(group(m, 1, listed) ?? "") * 4) + "• "
        }
        return s
    }

    // MARK: 검색 (초성 포함)

    private static let cho: [Character] = Array("ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ")

    static func chosung(_ s: String) -> String {
        var out = ""
        for u in s.unicodeScalars {
            if u.value >= 0xAC00 && u.value <= 0xD7A3 {
                out.append(cho[Int((u.value - 0xAC00) / 588)])
            } else {
                out += String(u).lowercased()
            }
        }
        return out
    }

    static func isAllJamo(_ s: String) -> Bool {
        !s.isEmpty && s.unicodeScalars.allSatisfy { $0.value >= 0x3131 && $0.value <= 0x314E }
    }

    static func score(_ t: Template, _ query: String) -> Int {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        if q.isEmpty { return 1 }
        let name = t.name.lowercased(), key = t.keyword.lowercased(), body = t.body.lowercased()
        if !key.isEmpty && key == q { return 100 }
        if key.hasPrefix(q) { return 80 }
        if name.hasPrefix(q) { return 70 }
        if name.contains(q) || key.contains(q) { return 60 }
        if isAllJamo(q) && (chosung(name).contains(q) || chosung(key).contains(q)) { return 50 }
        if body.contains(q) { return 20 }
        return 0
    }
}
