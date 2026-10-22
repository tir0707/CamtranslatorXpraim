import AVFoundation
import WebRTC

enum PhoneAudio {
    static func prepare() throws {
        let audio = RTCAudioSession.sharedInstance()
        audio.lockForConfiguration()
        defer { audio.unlockForConfiguration() }
        try audio.setCategory(.playAndRecord, with: [.defaultToSpeaker, .allowBluetooth])
        try audio.setMode(.videoChat)
    }
    static func authorize(_ completion: @escaping (Bool) -> Void) { AVCaptureDevice.requestAccess(for: .audio, completionHandler: completion) }
}
