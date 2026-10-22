import AVFoundation
import WebRTC

enum PhoneAudio {
    static func prepare() throws {
        let audio = RTCAudioSession.sharedInstance()
        audio.lockForConfiguration()
        defer { audio.unlockForConfiguration() }
        try audio.setCategory(AVAudioSession.Category.playAndRecord.rawValue, with: [.defaultToSpeaker, .allowBluetooth])
        try audio.setMode(AVAudioSession.Mode.videoChat.rawValue)
    }
    static func authorize(_ completion: @escaping (Bool) -> Void) { AVCaptureDevice.requestAccess(for: .audio, completionHandler: completion) }
}
