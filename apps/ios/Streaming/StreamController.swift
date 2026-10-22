import Foundation
import Combine
import AVFoundation
import UIKit
import WebRTC

final class StreamController: NSObject, ObservableObject, RTCPeerConnectionDelegate {
    @Published var status = "Не подключено"
    @Published var error = ""
    @Published var active = false
    @Published var paired = false
    @Published var computer = ""
    @Published var fps: Double = 0
    @Published var rtt: Double?
    @Published var thermal = ""
    @Published var profiles: [QualityProfile] = []
    @Published var battery: Int = 0
    @Published var profileID = "standard"
    @Published var microphone = false
    @Published var front = false
    @Published var mirror = false
    @Published var energySaving = false
    let factory: RTCPeerConnectionFactory
    let camera: CameraController
    private var socket: URLSessionWebSocketTask?
    private var session: URLSession?
    private var peer: RTCPeerConnection?
    private var audio: RTCAudioTrack?
    private var video: RTCVideoTrack?
    private var pendingIce: [RTCIceCandidate] = []
    private var timer: Timer?
    private var oldBrightness: CGFloat?
    private var offerBusy = false
    private var currentBitrate = 2_500_000
    var profile: QualityProfile { profiles.first { $0.id == profileID } ?? profiles.first ?? QualityProfile.all[0] }
    override init() {
        let encoder = RTCDefaultVideoEncoderFactory()
        if let h264 = encoder.supportedCodecs().first(where: { $0.name == "H264" }) { encoder.preferredCodec = h264 }
        factory = RTCPeerConnectionFactory(encoderFactory: encoder, decoderFactory: RTCDefaultVideoDecoderFactory())
        camera = CameraController(factory: factory)
        super.init()
        profiles = camera.profiles
        UIDevice.current.isBatteryMonitoringEnabled = true
    }
    func fail(_ e: Error) { DispatchQueue.main.async { self.error = e.localizedDescription;self.stop() } }
    func start(address: String, code: String) {
        guard !active else { return }
        guard code.count == 6, code.allSatisfy(\.isNumber), var parts = URLComponents(string: address), parts.scheme == "https", parts.host != nil, parts.user == nil, parts.password == nil else { error = "Введите HTTPS-адрес компьютера и код из шести цифр.";return }
        parts.scheme = "wss";parts.path = "/signal";parts.query = nil;parts.fragment = nil
        guard let url = parts.url else { return }
        error = ""; active = true;status = "Разрешение камеры…"
        AVCaptureDevice.requestAccess(for: .video) { granted in
            DispatchQueue.main.async {
                guard self.active else { return }
                guard granted else { self.error = "Разрешите доступ к камере в настройках iOS.";self.stop();return }
                self.camera.start(profile: self.profile) { error in if let error { self.fail(error) } }
                self.session = URLSession(configuration: .ephemeral)
                self.socket = self.session?.webSocketTask(with: url)
                self.socket?.resume()
                self.send(["type":"hello","role":"publisher","code":code])
                self.status = "Подтвердите на компьютере"
                self.receive()
                UIApplication.shared.isIdleTimerDisabled = true
                self.timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.measure() }
            }
        }
    }
    func stop() {
        active = false;paired = false;offerBusy = false
        timer?.invalidate();timer = nil
        socket?.cancel(with: .normalClosure, reason: nil);socket = nil;session?.invalidateAndCancel();session = nil
        peer?.close();peer = nil;pendingIce = [];audio = nil;video = nil
        camera.stop();UIApplication.shared.isIdleTimerDisabled = false
        if let oldBrightness { UIScreen.main.brightness = oldBrightness;self.oldBrightness = nil }
        status = "Не подключено";fps = 0;rtt = nil
    }
    private func send(_ message: [String:Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: message), let text = String(data: data, encoding: .utf8) else { return }
        socket?.send(.string(text)) { [weak self] e in if let e { self?.fail(e) } }
    }
    private func receive() {
        socket?.receive { [weak self] result in
            guard let self else { return }
            switch result {
            case .failure(let e): if self.active { self.fail(e) }
            case .success(let message):
                if case .string(let text) = message, let data = text.data(using: .utf8), let m = (try? JSONSerialization.jsonObject(with: data)) as? [String:Any] {
                    DispatchQueue.main.async { self.handle(m) }
                }
                self.receive()
            }
        }
    }
    private func handle(_ m: [String:Any]) {
        guard active, let type = m["type"] as? String else { return }
        switch type {
        case "paired": paired = true;computer = m["computer"] as? String ?? "Компьютер";status = "Сопряжено"
        case "peer-ready": createPeer()
        case "answer":
            guard let sdp = m["sdp"] as? String else { return }
            peer?.setRemoteDescription(RTCSessionDescription(type: .answer, sdp: sdp)) { [weak self] e in
                if let e { self?.fail(e);return }
                DispatchQueue.main.async { guard let self else { return };for candidate in self.pendingIce { self.peer?.add(candidate, completionHandler: { _ in }) };self.pendingIce = [] }
            }
        case "ice":
            guard let c = m["candidate"] as? [String:Any], let sdp = c["candidate"] as? String, let index = c["sdpMLineIndex"] as? Int else { return }
            let candidate = RTCIceCandidate(sdp: sdp, sdpMLineIndex: Int32(index), sdpMid: c["sdpMid"] as? String)
            if peer?.remoteDescription != nil { peer?.add(candidate, completionHandler: { [weak self] e in if let e { self?.fail(e) } }) } else { pendingIce.append(candidate) }
        case "command":
            if m["action"] as? String == "bitrate", let value = m["value"] as? Int { setBitrate(value) }
            if m["action"] as? String == "mute" { microphone = false;audio?.isEnabled = false }
        case "peer-left": peer?.close();peer = nil;status = "Ожидание приёмника"
        case "disconnect": stop()
        case "error": error = m["message"] as? String ?? "Ошибка сопряжения";stop()
        default: break
        }
    }
    private func createPeer() {
        peer?.close();pendingIce = []
        let config = RTCConfiguration();config.iceServers = [];config.sdpSemantics = .unifiedPlan
        guard let connection = factory.peerConnection(with: config, constraints: RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil), delegate: self) else { error = "Не удалось создать WebRTC";stop();return }
        peer = connection
        video = factory.videoTrack(with: camera.source, trackId: "camera")
        if let video { connection.add(video, streamIds: ["camxp"]) }
        if microphone {
            audio = factory.audioTrack(withTrackId: "microphone")
            if let audio { connection.add(audio, streamIds: ["camxp"]) }
        }
        makeOffer()
    }
    private func makeOffer() {
        guard let peer, !offerBusy else { return };offerBusy = true
        peer.offer(for: RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)) { [weak self] sdp, e in
            if let e { self?.fail(e);return };guard let sdp else { return }
            peer.setLocalDescription(sdp) { e in
                if let e { self?.fail(e);return }
                DispatchQueue.main.async { self?.offerBusy = false;self?.send(["type":"offer","sdp":sdp.sdp]);if let self { self.setBitrate(self.profile.bitrate) } }
            }
        }
    }
    func setMicrophone(_ enabled: Bool) {
        if !enabled { microphone = false;audio?.isEnabled = false;return }
        PhoneAudio.authorize { [weak self] allowed in DispatchQueue.main.async {
            guard let self else { return }
            self.microphone = allowed
            if allowed { do {
                try PhoneAudio.prepare()
                if self.audio == nil, let peer = self.peer {
                    self.audio = self.factory.audioTrack(withTrackId: "microphone")
                    if let audio = self.audio { peer.add(audio, streamIds: ["camxp"]);self.makeOffer() }
                }
                self.audio?.isEnabled = true
            } catch { self.error = error.localizedDescription;self.microphone = false } }
            else { self.error = "Доступ к микрофону не разрешён." }
        } }
    }
    func changeProfile() {
        guard active else { return }
        camera.stop { [weak self] in DispatchQueue.main.async { guard let self, self.active else { return };self.camera.start(profile: self.profile) { if let e = $0 { self.fail(e) } };self.setBitrate(self.profile.bitrate) } }
    }
    func flip() {
        front.toggle();camera.select(front: front);profiles = camera.profiles
        if !profiles.contains(where: { $0.id == profileID }) { profileID = profiles.first?.id ?? "" }
        changeProfile()
    }
    private func setBitrate(_ requested: Int) {
        currentBitrate = min(profile.bitrate, max(300_000, requested))
        for sender in peer?.senders ?? [] where sender.track?.kind == "video" {
            let p = sender.parameters
            for encoding in p.encodings { encoding.maxBitrateBps = NSNumber(value: currentBitrate);encoding.maxFramerate = NSNumber(value: profile.fps) }
            sender.parameters = p
        }
    }
    func setEnergySaving(_ enabled: Bool) {
        energySaving = enabled
        if enabled { if oldBrightness == nil { oldBrightness = UIScreen.main.brightness };UIScreen.main.brightness = 0.15;if profiles.contains(where: { $0.id == "economy" }) { profileID = "economy";changeProfile() } }
        else if let oldBrightness { UIScreen.main.brightness = oldBrightness;self.oldBrightness = nil }
    }
    func configure(_ body: @escaping (AVCaptureDevice) throws -> Void) { camera.configure(body, onError: { [weak self] e in DispatchQueue.main.async { self?.error = e.localizedDescription } }) }
    private func measure() {
        battery = max(0, Int(UIDevice.current.batteryLevel * 100))
        switch ProcessInfo.processInfo.thermalState {
        case .serious: thermal = "Нагрев: снижаем нагрузку";if !energySaving { setEnergySaving(true) }
        case .critical: error = "Критический нагрев: трансляция остановлена";stop();return
        default: thermal = ""
        }
        send(["type":"ping"])
        peer?.statistics { [weak self] report in DispatchQueue.main.async {
            guard let self else { return }
            for stat in report.statistics.values {
                if stat.type == "outbound-rtp", stat.values["kind"] as? String == "video" { self.fps = (stat.values["framesPerSecond"] as? NSNumber)?.doubleValue ?? 0 }
                if stat.type == "candidate-pair", (stat.values["nominated"] as? NSNumber)?.boolValue == true { self.rtt = (stat.values["currentRoundTripTime"] as? NSNumber).map { $0.doubleValue * 1000 } }
            }
            self.send(["type":"telemetry","fps":self.fps,"width":self.profile.width,"height":self.profile.height,"battery":self.battery])
        } }
    }
    func peerConnection(_ peerConnection: RTCPeerConnection, didGenerate candidate: RTCIceCandidate) { send(["type":"ice","candidate":["candidate":candidate.sdp,"sdpMid":candidate.sdpMid ?? "0","sdpMLineIndex":candidate.sdpMLineIndex]]) }
    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceConnectionState) { DispatchQueue.main.async { if newState == .connected || newState == .completed { self.status = "● Трансляция активна" };if newState == .failed { self.error = "Связь потеряна. Подключитесь с новым кодом.";self.stop() } } }
    func peerConnection(_ peerConnection: RTCPeerConnection, didChange stateChanged: RTCSignalingState) {}
    func peerConnection(_ peerConnection: RTCPeerConnection, didAdd stream: RTCMediaStream) {}
    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove stream: RTCMediaStream) {}
    func peerConnectionShouldNegotiate(_ peerConnection: RTCPeerConnection) {}
    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceGatheringState) {}
    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove candidates: [RTCIceCandidate]) {}
    func peerConnection(_ peerConnection: RTCPeerConnection, didOpen dataChannel: RTCDataChannel) {}
}
