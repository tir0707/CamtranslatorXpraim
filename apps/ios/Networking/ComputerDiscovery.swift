import Foundation
import Combine

final class ComputerDiscovery: NSObject, ObservableObject, NetServiceBrowserDelegate, NetServiceDelegate {
    @Published var computers: [String] = []
    private let browser = NetServiceBrowser()
    private var services: [NetService] = []
    override init() { super.init(); browser.delegate = self }
    func search() { browser.searchForServices(ofType: "_camxp._tcp.", inDomain: "local.") }
    func netServiceBrowser(_ browser: NetServiceBrowser, didFind service: NetService, moreComing: Bool) { services.append(service);service.delegate = self;service.resolve(withTimeout: 5) }
    func netServiceDidResolveAddress(_ sender: NetService) {
        guard let host = sender.hostName else { return }
        let url = "https://\(host):\(sender.port)"
        if !computers.contains(url) { computers.append(url) }
    }
    func netServiceBrowser(_ browser: NetServiceBrowser, didRemove service: NetService, moreComing: Bool) { services.removeAll { $0 == service }; if let host = service.hostName { computers.removeAll { $0.contains(host) } } }
    deinit { browser.stop() }
}
