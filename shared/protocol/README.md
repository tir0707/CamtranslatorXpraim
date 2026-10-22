# CamtranslatorXpraim protocol 1

TLS HTTPS/WSS 47990; HTTP 47991 serves **only** the public CA at `/ca.cer`.
DNS-SD `_camxp._tcp.local` advertises the desktop. An iPhone discovers the computer, rather than the reverse; manual IP works independently of multicast. No internet signaling, STUN or TURN.

WebSocket `/signal`: UTF-8 JSON text, at most 64 KiB, JSON depth <=16, first message within 60 seconds. One publisher, one loopback receiver. Origin, when present, must equal the HTTPS server origin. Never log tokens, codes or SDP.

1. Receiver: `{"type":"hello","role":"receiver","token":"<256-bit process token>"}`. Server requires loopback and token, replies `ready`.
2. iPhone: `{"type":"hello","role":"publisher","code":"123456"}`. Code is one-use, expires after five minutes, maximum five attempts per IP per five minutes. Desktop confirmation is additionally required. No remembered identity yet; every new signaling session requires pairing.
3. Server replies `paired` with computer name, then `peer-ready` when the receiver is present.
4. Publisher makes `offer` with SDP; receiver sends `answer`. Both send `ice` with an `RTCIceCandidateInit` in `candidate`. Queue ICE until remote description is applied.
5. Media travels directly using WebRTC ICE + DTLS-SRTP. H.264 preferred; native uses VideoToolbox through libwebrtc. Audio negotiated by WebRTC, normally Opus. No media bytes in signaling.
6. Publisher `telemetry`: capture resolution/FPS/battery when known. Receiver `command`: `bitrate` (bps) or `mute`. No arbitrary command execution.
7. Both can send `ping`, server replies `pong`. `peer-left` tears down media, `disconnect` stops publisher capture. Receiver reconnects on signaling loss, publisher re-pairs explicitly.

Transport state must not be inferred solely from IP. USB candidate detection is advisory. A chosen ICE candidate pair and isolated interface test are needed to confirm USB.

RTT, jitter buffer delay and decode time are separate metrics. They do not constitute end-to-end latency. See `tests/performance/README.md`.
