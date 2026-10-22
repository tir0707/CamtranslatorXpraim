import {rtcConfig,$,send,preferH264,applyBitrate,IceQueue} from './common.js';
let ws,pc,stream,ice,heartbeat,statsTimer,wakeLock,front=false,stopping=false,negotiating=false,videoSender;
const status = text => $('status').textContent=text;
const error = e => { $('error').textContent=e.message||String(e); };
function quality() { const [width,height,frameRate,bitrate]=$('quality').value.split(',').map(Number);return {width,height,frameRate,bitrate}; }
async function capture() {
  const q=quality();
  const old=stream;
  // Safari needs the previous camera released before opening the other lens.
  old?.getTracks().forEach(t=>t.stop());
  // Exact constraints ensure a requested mode is never reported as supported after silent fallback.
  const next=await navigator.mediaDevices.getUserMedia({video:{facingMode:front?'user':'environment',width:{exact:q.width},height:{exact:q.height},frameRate:{exact:q.frameRate}},audio:$('mic').checked?{channelCount:1,echoCancellation:false,noiseSuppression:false}:false});
  stream=next;$('video').srcObject=stream;
  if(pc) {
    if(videoSender) await videoSender.replaceTrack(stream.getVideoTracks()[0]);
    else videoSender=pc.addTrack(stream.getVideoTracks()[0],stream);
    const a=pc.getSenders().find(s=>s.track?.kind==='audio');
    const track=stream.getAudioTracks()[0];
    if(a) { if(track) await a.replaceTrack(track); else {pc.removeTrack(a);} }
    else if(track) pc.addTrack(track,stream);
  }
  const s=stream.getVideoTracks()[0].getSettings();
  $('metrics').textContent=`Захват: ${s.width}×${s.height} · ${s.frameRate?.toFixed(0)||'—'} FPS`;
}
async function offer() {
  if(!pc||negotiating||pc.signalingState!=='stable')return;
  negotiating=true;
  try {preferH264(pc);await pc.setLocalDescription(await pc.createOffer());send(ws,{type:'offer',sdp:pc.localDescription.sdp});await applyBitrate(videoSender,quality().bitrate);}
  finally {negotiating=false;}
}
async function start() {
  stopping=false;$('error').textContent='';$('connect').disabled=true;
  try {
    if(!isSecureContext||!navigator.mediaDevices) throw Error('Нужен HTTPS с доверенным сертификатом. См. инструкцию сертификата.');
    if(!/^\d{6}$/.test($('code').value)) throw Error('Введите шестизначный код с компьютера.');
    await capture();
    ws=new WebSocket(`wss://${location.host}/signal`);
    ws.onopen=()=>{status('Подтвердите на компьютере');send(ws,{type:'hello',role:'publisher',code:$('code').value});};
    ws.onmessage=async e=>{
      try {
        const m=JSON.parse(e.data);
        if(m.type==='paired') { status(`Сопряжено: ${m.computer}`); $('stop').disabled=false; if(navigator.wakeLock)wakeLock=await navigator.wakeLock.request('screen').catch(()=>null); }
        if(m.type==='peer-ready') {
          pc?.close();pc=new RTCPeerConnection(rtcConfig);ice=new IceQueue();
          stream.getTracks().forEach(t=>{const s=pc.addTrack(t,stream);if(t.kind==='video')videoSender=s;});
          pc.onicecandidate=e=>{if(e.candidate)send(ws,{type:'ice',candidate:e.candidate});};
          pc.onconnectionstatechange=()=>{status(pc.connectionState==='connected'?'● Трансляция активна':`Соединение: ${pc.connectionState}`);if(pc.connectionState==='failed') {error(Error('Связь потеряна. Подключитесь с новым кодом.'));stop();}};
          await offer();
        }
        if(m.type==='answer') {await pc.setRemoteDescription({type:'answer',sdp:m.sdp});await ice.flush(pc);}
        if(m.type==='ice'&&pc)await ice.add(pc,m.candidate);
        if(m.type==='command') {
          if(m.action==='bitrate')await applyBitrate(videoSender,Math.min(quality().bitrate,Math.max(300000,m.value)));
          if(m.action==='mute') { $('mic').checked=false; stream?.getAudioTracks().forEach(t=>t.enabled=false); }
        }
        if(m.type==='peer-left'){pc?.close();pc=null;status('Ожидание приёмника');}
        if(m.type==='disconnect'){stop();}
        if(m.type==='error'){error(Error(m.message));stop();}
      }catch(e){error(e);stop();}
    };
    ws.onclose=()=>{if(!stopping){error(Error('Соединение закрыто. Для безопасности повторите сопряжение.'));stop();}};
    ws.onerror=()=>error(Error('Не удалось установить защищённое соединение. Проверьте сертификат и сеть.'));
    heartbeat=setInterval(()=>send(ws,{type:'ping'}),15000);
    statsTimer=setInterval(async()=>{
      if(!pc)return;
      const stats=await pc.getStats();
      for(const r of stats.values()) if(r.type==='outbound-rtp'&&r.kind==='video')send(ws,{type:'telemetry',width:r.frameWidth,height:r.frameHeight,fps:r.framesPerSecond,battery:null});
    },2000);
  } catch(e) {error(e);stop();}
}
function stop() {
  stopping=true;clearInterval(heartbeat);clearInterval(statsTimer);pc?.close();pc=null;ws?.close();ws=null;
  stream?.getTracks().forEach(t=>t.stop());stream=null;$('video').srcObject=null;wakeLock?.release();wakeLock=null;
  $('connect').disabled=false;$('stop').disabled=true;status('Не подключено');
}
$('connect').onclick=start;$('stop').onclick=stop;
$('mirror').onchange=()=>{$('video').style.transform=$('mirror').checked?'scaleX(-1)':'';};
async function changeCapture() {if(!stream)return;try{await capture();await offer();}catch(e){error(e);stop();}}
$('flip').onclick=async()=>{front=!front;await changeCapture();};
$('quality').onchange=changeCapture;$('mic').onchange=changeCapture;
document.addEventListener('visibilitychange',()=>{if(document.hidden&&stream){error(Error('Трансляция остановлена при сворачивании.'));stop();}});
window.addEventListener('pagehide',stop);
