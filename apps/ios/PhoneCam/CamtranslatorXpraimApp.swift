import SwiftUI
import WebRTC

@main
struct CamtranslatorXpraimApp: App {
    init() { RTCInitializeSSL() }
    var body: some Scene { WindowGroup { CameraScreen() } }
}
