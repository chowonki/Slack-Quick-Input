import AppKit
import SwiftUI

// Liquid Glass (macOS 26+) 도우미.
// macOS 26 SDK(Swift 6.2 이상)로 빌드하고 macOS 26에서 실행할 때만 유리 효과를 쓰고,
// 그 밖에는 기존 머티리얼(반투명 흐림)로 대신합니다.

extension View {
    /// 떠 있는 조작 영역(도구 모음 등)에 쓰는 캡슐 모양 유리
    @ViewBuilder
    func glassCapsule() -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            self.glassEffect()
        } else {
            self.legacyCapsule()
        }
        #else
        self.legacyCapsule()
        #endif
    }

    func legacyCapsule() -> some View {
        self.background(Capsule().fill(.regularMaterial))
            .overlay(Capsule().strokeBorder(Color.primary.opacity(0.1)))
    }

    /// 유리 버튼 (prominent: 강조 색)
    @ViewBuilder
    func glassButton(prominent: Bool = false) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            if prominent {
                self.buttonStyle(.glassProminent)
            } else {
                self.buttonStyle(.glass)
            }
        } else {
            self.legacyButton(prominent: prominent)
        }
        #else
        self.legacyButton(prominent: prominent)
        #endif
    }

    @ViewBuilder
    func legacyButton(prominent: Bool) -> some View {
        if prominent {
            self.buttonStyle(.borderedProminent)
        } else {
            self.buttonStyle(.bordered)
        }
    }

    /// 내용(입력칸·미리보기·설정 줄)을 담는 은은한 카드. 내용 영역에는 유리를 쓰지 않습니다.
    func contentCard(cornerRadius: CGFloat = 10) -> some View {
        let shape = RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
        return self
            .background(shape.fill(Color.primary.opacity(0.045)))
            .overlay(shape.strokeBorder(Color.primary.opacity(0.09)))
            .clipShape(shape)
    }
}

enum GlassChrome {
    /// 테두리 없는 창 전체를 유리판으로 감쌉니다 (팝업용).
    static func container(_ content: NSView, cornerRadius: CGFloat) -> NSView {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            let glass = NSGlassEffectView()
            glass.cornerRadius = cornerRadius
            glass.contentView = content
            return glass
        }
        #endif
        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.blendingMode = .behindWindow
        effect.state = .active
        effect.wantsLayer = true
        effect.layer?.cornerRadius = cornerRadius
        effect.layer?.masksToBounds = true
        content.translatesAutoresizingMaskIntoConstraints = false
        effect.addSubview(content)
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: effect.topAnchor),
            content.bottomAnchor.constraint(equalTo: effect.bottomAnchor),
            content.leadingAnchor.constraint(equalTo: effect.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: effect.trailingAnchor),
        ])
        return effect
    }
}

/// 키보드 키 모양 안내 ("⌘Enter" 등)
struct KeyHint: View {
    let keys: String
    let label: String

    var body: some View {
        HStack(spacing: 5) {
            Text(keys)
                .font(.system(size: 10.5, weight: .medium, design: .rounded))
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(RoundedRectangle(cornerRadius: 5, style: .continuous).fill(Color.primary.opacity(0.08)))
            Text(label).font(.system(size: 11))
        }
        .foregroundStyle(.secondary)
    }
}
