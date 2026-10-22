export const rtcConfig = {iceServers:[],bundlePolicy:'max-bundle'};
export const $ = id => document.getElementById(id);
export function send(ws, message) { if(ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify(message)); }
export function preferH264(pc) {
  const codecs = RTCRtpReceiver.getCapabilities?.('video')?.codecs;
  if(!codecs) return;
  const ordered = [...codecs.filter(c=>c.mimeType.toLowerCase()==='video/h264'),...codecs.filter(c=>c.mimeType.toLowerCase()!=='video/h264')];
  for(const t of pc.getTransceivers()) if(t.receiver.track.kind==='video' && t.setCodecPreferences) t.setCodecPreferences(ordered);
}
export async function applyBitrate(sender, bitrate) {
  if(!sender) return;
  const p=sender.getParameters();
  if(!p.encodings?.length) p.encodings=[{}];
  p.encodings[0].maxBitrate=bitrate;
  p.degradationPreference='maintain-framerate';
  await sender.setParameters(p);
}
export class IceQueue {
  pending=[];
  async add(pc,candidate) { if(pc.remoteDescription) await pc.addIceCandidate(candidate); else this.pending.push(candidate); }
  async flush(pc) { for(const c of this.pending.splice(0)) await pc.addIceCandidate(c); }
}
