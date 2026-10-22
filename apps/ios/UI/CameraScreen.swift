import SwiftUI
import AVFoundation
import WebRTC

struct NativePreview: UIViewRepresentable {
    let session: AVCaptureSession
    let mirror: Bool
    func makeUIView(context: Context) -> RTCCameraPreviewView { let view = RTCCameraPreviewView();view.captureSession = session;return view }
    func updateUIView(_ view: RTCCameraPreviewView, context: Context) { view.transform = CGAffineTransform(scaleX: mirror ? -1 : 1, y: 1) }
}

struct CameraScreen: View {
    @StateObject private var model = StreamController()
    @StateObject private var discovery = ComputerDiscovery()
    @AppStorage("computerAddress") private var address = "https://192.168.1.2:47990"
    @State private var code = ""
    @State private var settings = false
    @State private var focus = 0.5
    @State private var exposure = 0.0
    @State private var iso = 100.0
    @State private var temperature = 5000.0
    @Environment(\.scenePhase) private var scenePhase
    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    NativePreview(session: model.camera.capturer.captureSession, mirror: model.mirror).frame(height: 340).background(.black).clipShape(RoundedRectangle(cornerRadius: 20))
                    Text(model.status).font(.headline).foregroundStyle(model.paired ? .green : .secondary)
                    if !model.computer.isEmpty { Text(model.computer) }
                    HStack { Text("\(model.profile.width)×\(model.profile.height)");Text("\(model.fps, specifier: "%.0f") FPS");Text("Батарея: \(model.battery)%") }.font(.caption)
                    Text(model.rtt.map { "Сетевой RTT: \(Int($0)) мс · полная задержка не измерена" } ?? "Задержка камера → экран не измерена").font(.caption)
                    if !model.thermal.isEmpty { Text(model.thermal).foregroundStyle(.orange) }
                    if !model.error.isEmpty { Text(model.error).foregroundStyle(.red) }
                    TextField("HTTPS-адрес компьютера", text: $address).textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL).textFieldStyle(.roundedBorder).disabled(model.active)
                    if !discovery.computers.isEmpty { Menu("Компьютеры в сети") { ForEach(discovery.computers, id: \.self) { url in Button(url) { address = url } } }.disabled(model.active) }
                    TextField("Код с компьютера", text: $code).keyboardType(.numberPad).textFieldStyle(.roundedBorder).disabled(model.active)
                    Button(model.active ? "Остановить трансляцию" : "Подключить") { if model.active { model.stop() } else { model.start(address: address, code: code) } }.buttonStyle(.borderedProminent)
                    HStack { Button("Сменить камеру") { model.flip() };Button("Настройки") { settings = true } }.buttonStyle(.bordered)
                    Text("Установите сертификат компьютера и включите полное доверие в настройках iOS. Камера работает только при открытом приложении и разблокированном экране.").font(.footnote).foregroundStyle(.secondary)
                }.padding()
            }.navigationTitle("CamtranslatorXpraim").navigationBarTitleDisplayMode(.inline)
                .sheet(isPresented: $settings) { settingsView }
                .onAppear { discovery.search() }
                .onChange(of: scenePhase) { _, phase in if phase != .active { model.stop() } }
        }
    }
    private var settingsView: some View {
        NavigationStack {
            Form {
                Section("Видео · аппаратный H.264 через WebRTC") {
                    Picker("Режим", selection: $model.profileID) { ForEach(model.profiles) { Text($0.name).tag($0.id) } }.onChange(of: model.profileID) { _, _ in model.changeProfile() }
                    Text("Битрейт: до \(Double(model.profile.bitrate) / 1_000_000, specifier: "%.1f") Мбит/с, адаптивный")
                    Toggle("Зеркало предпросмотра", isOn: $model.mirror)
                    Toggle("Микрофон", isOn: Binding(get: { model.microphone }, set: { model.setMicrophone($0) }))
                    Toggle("Экономия энергии", isOn: Binding(get: { model.energySaving }, set: { model.setEnergySaving($0) }))
                }
                if let d = model.camera.device {
                    Section("Фокус и экспозиция") {
                        if d.isFocusModeSupported(.continuousAutoFocus) { Button("Автофокус") { model.configure { $0.focusMode = .continuousAutoFocus } } }
                        if d.isLockingFocusWithCustomLensPositionSupported { Slider(value: $focus, in: 0...1, onEditingChanged: { editing in if !editing { let value = Float(focus);model.configure { $0.setFocusModeLocked(lensPosition: value, completionHandler: nil) } } });Text("Ручной фокус (фиксируется)") }
                        if d.isExposureModeSupported(.continuousAutoExposure) { Button("Автоэкспозиция") { model.configure { $0.exposureMode = .continuousAutoExposure } } }
                        if d.isExposureModeSupported(.locked) { Button("Зафиксировать экспозицию") { model.configure { $0.exposureMode = .locked } } }
                        Slider(value: $exposure, in: Double(d.minExposureTargetBias)...Double(d.maxExposureTargetBias), onEditingChanged: { editing in if !editing { let value = Float(exposure);model.configure { $0.setExposureTargetBias(value, completionHandler: nil) } } });Text("Коррекция экспозиции: \(exposure, specifier: "%.1f")")
                        if d.isExposureModeSupported(.custom), d.activeFormat.minISO < d.activeFormat.maxISO {
                            Slider(value: $iso, in: Double(d.activeFormat.minISO)...Double(d.activeFormat.maxISO), onEditingChanged: { editing in if !editing { let value = Float(iso);model.configure { $0.setExposureModeCustom(duration: AVCaptureDevice.currentExposureDuration, iso: min($0.activeFormat.maxISO, max($0.activeFormat.minISO, value)), completionHandler: nil) } } });Text("ISO: \(iso, specifier: "%.0f")")
                        }
                    }
                    Section("Свет и баланс белого") {
                        if d.hasTorch && d.isTorchModeSupported(.on) { Button(d.torchMode == .on ? "Выключить фонарик" : "Включить фонарик") { model.configure { $0.torchMode = $0.torchMode == .on ? .off : .on } } }
                        if d.isWhiteBalanceModeSupported(.continuousAutoWhiteBalance) { Button("Автоматический баланс белого") { model.configure { $0.whiteBalanceMode = .continuousAutoWhiteBalance } } }
                        if d.isWhiteBalanceModeSupported(.locked) {
                            Slider(value: $temperature, in: 2500...8000, onEditingChanged: { editing in if !editing { let t = Float(temperature);model.configure { device in var gains = device.deviceWhiteBalanceGains(for: .init(temperature: t, tint: 0));gains.redGain = min(device.maxWhiteBalanceGain, max(1, gains.redGain));gains.greenGain = min(device.maxWhiteBalanceGain, max(1, gains.greenGain));gains.blueGain = min(device.maxWhiteBalanceGain, max(1, gains.blueGain));device.setWhiteBalanceModeLocked(with: gains, completionHandler: nil) } } });Text("Температура: \(Int(temperature)) K")
                        }
                    }
                }
                Section("Безопасность") { Text("Каждый сеанс требует нового кода и подтверждения на компьютере. Постоянное доверие не сохраняется. Нет облачных серверов и телеметрии.") }
            }.navigationTitle("Настройки").toolbar { Button("Готово") { settings = false } }
        }
    }
}
