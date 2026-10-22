import AVFoundation
import WebRTC

final class CameraController {
    let source: RTCVideoSource
    let capturer: RTCCameraVideoCapturer
    private(set) var device: AVCaptureDevice?
    private let queue = DispatchQueue(label: "CamXP.camera")
    init(factory: RTCPeerConnectionFactory) {
        source = factory.videoSource()
        capturer = RTCCameraVideoCapturer(delegate: source)
        select(front: false)
    }
    func select(front: Bool) {
        device = RTCCameraVideoCapturer.captureDevices().first { $0.position == (front ? .front : .back) }
    }
    var profiles: [QualityProfile] {
        guard let device else { return [] }
        return QualityProfile.all.filter { p in format(device: device, profile: p) != nil }
    }
    private func format(device: AVCaptureDevice, profile: QualityProfile) -> AVCaptureDevice.Format? {
        RTCCameraVideoCapturer.supportedFormats(for: device).first { f in
            let d = CMVideoFormatDescriptionGetDimensions(f.formatDescription)
            return d.width == profile.width && d.height == profile.height && f.videoSupportedFrameRateRanges.contains { $0.minFrameRate <= Double(profile.fps) && $0.maxFrameRate >= Double(profile.fps) }
        }
    }
    func start(profile: QualityProfile, completion: @escaping (Error?) -> Void) {
        guard let device, let format = format(device: device, profile: profile) else { completion(CameraError.unsupported); return }
        source.adaptOutputFormat(toWidth: profile.width, height: profile.height, fps: Int32(profile.fps))
        capturer.startCapture(with: device, format: format, fps: profile.fps, completionHandler: completion)
    }
    func stop(completion: @escaping () -> Void = {}) { capturer.stopCapture(completionHandler: completion) }
    func configure(_ body: @escaping (AVCaptureDevice) throws -> Void, onError: @escaping (Error) -> Void) {
        queue.async { [weak self] in
            guard let device = self?.device else { return }
            do { try device.lockForConfiguration(); defer { device.unlockForConfiguration() }; try body(device) }
            catch { onError(error) }
        }
    }
    enum CameraError: LocalizedError {
        case unsupported
        var errorDescription: String? { "Выбранный режим не поддерживается этой камерой." }
    }
}
