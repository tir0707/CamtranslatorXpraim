import Foundation

struct QualityProfile: Identifiable, Equatable {
    let id: String
    let name: String
    let width: Int32
    let height: Int32
    let fps: Int
    let bitrate: Int
    static let all: [QualityProfile] = [
        .init(id: "economy", name: "Экономичный · 480p / 30", width: 640, height: 480, fps: 30, bitrate: 900_000),
        .init(id: "standard", name: "Стандартный · 720p / 30", width: 1280, height: 720, fps: 30, bitrate: 2_500_000),
        .init(id: "high", name: "Качество · 1080p / 30", width: 1920, height: 1080, fps: 30, bitrate: 5_000_000),
        .init(id: "smooth", name: "Плавность · 720p / 60", width: 1280, height: 720, fps: 60, bitrate: 4_000_000),
        .init(id: "maximum", name: "Максимальный · 1080p / 60", width: 1920, height: 1080, fps: 60, bitrate: 8_000_000)
    ]
}
