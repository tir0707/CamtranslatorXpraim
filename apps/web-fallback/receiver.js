import {rtcConfig,$,send,IceQueue} from './common.js';
const token=new URLSearchParams(location.hash.slice(1)).get('token');history.replaceState(null,'',location.pathname);
let ws,pc,ice=new IceQueue(),previous,recording,recordChunks=[],recordBytes=0,adaptation=2500000,stopped=false,statsBusy=false,audioContext,meter;
const video=$('video');
function host(message){window.chrome?.webview?.postMessage(message);}
function reset(){pc?.close();pc=null;video.srcObject=null;$('empty').hidden=false;previous=null;audioContext?.close();audioContext=null;meter=null;}
function connect(){
  if(stopped)return;
  ws=new WebSocket(`wss://${location.host}/signal`);
  ws.onopen=()=>send(ws,{type:'hello',role:'receiver',token});
  ws.onclose=()=>{reset();host({type:'status',text:'Приёмник отключён'});if(!stopped)setTimeout(connect,2000);};
  ws.onmessage=async e=>{
    try {
      const m=JSON.parse(e.data);
      if(m.type==='offer') {
        if(!pc) { pc=new RTCPeerConnection(rtcConfig);
        pc.ontrack=e=>{const s=video.srcObject||new MediaStream();if(!s.getTracks().some(t=>t.id===e.track.id))s.addTrack(e.track);video.srcObject=s;$('empty').hidden=true;video.play().catch(()=>host({type:'status',text:'Для звука нажмите на видео'}));};
        pc.onicecandidate=e=>{if(e.candidate)send(ws,{type:'ice',candidate:e.candidate});};
        pc.onconnectionstatechange=()=>host({type:'status',text:pc.connectionState==='connected'?'● Видео подключено':`WebRTC: ${pc.connectionState}`});
        }
        await pc.setRemoteDescription({type:'offer',sdp:m.sdp});await ice.flush(pc);await pc.setLocalDescription(await pc.createAnswer());send(ws,{type:'answer',sdp:pc.localDescription.sdp});
      }
      if(m.type==='ice'){if(pc)await ice.add(pc,m.candidate);else ice.pending.push(m.candidate);}
      if(m.type==='peer-left') {reset();ice=new IceQueue();host({type:'status',text:'iPhone отключён'});}
      if(m.type==='telemetry')host(m);
    }catch(e){host({type:'error',text:e.message});}
  };
}
video.onclick=()=>video.play();
async function stats(){
  if(!pc||statsBusy)return;statsBusy=true;
  try{
    const all=await pc.getStats();let rtt=null;
    for(const s of all.values())if(s.type==='candidate-pair'&&s.state==='succeeded'&&s.nominated)rtt=s.currentRoundTripTime*1000;
    for(const s of all.values())if(s.type==='inbound-rtp'&&s.kind==='video'){
      const dt=previous?(s.timestamp-previous.timestamp)/1000:0;
      const bitrate=dt>0?(s.bytesReceived-previous.bytesReceived)*8/dt:0;
      const deltaFrames=previous?s.framesDecoded-previous.framesDecoded:0;
      const fps=dt>0?deltaFrames/dt:0;
      const lost=previous?Math.max(0,s.packetsLost-previous.packetsLost):0;
      const received=previous?s.packetsReceived-previous.packetsReceived:0;
      const loss=received+lost>0?lost/(received+lost):0;
      const emitted=previous?s.jitterBufferEmittedCount-previous.jitterBufferEmittedCount:0;
      const jitterMs=emitted>0?(s.jitterBufferDelay-previous.jitterBufferDelay)*1000/emitted:null;
      const decodeMs=deltaFrames>0?(s.totalDecodeTime-previous.totalDecodeTime)*1000/deltaFrames:null;
      if(dt>0){
        adaptation=loss>.03||rtt>120?Math.max(400000,adaptation*.8):Math.min(8000000,adaptation+100000);
        send(ws,{type:'command',action:'bitrate',value:Math.round(adaptation)});
      }
      host({type:'stats',width:s.frameWidth,height:s.frameHeight,fps,bitrate,rtt,jitterMs,decodeMs,dropped:s.framesDropped,loss,decoder:s.decoderImplementation||'Не сообщён',level:audioLevel()});
      previous=s;
    }
  }catch(e){host({type:'error',text:e.message});}finally{statsBusy=false;}
}
function audioLevel(){
  if(!video.srcObject?.getAudioTracks().length)return null;
  if(!audioContext){audioContext=new AudioContext();meter=audioContext.createAnalyser();audioContext.createMediaStreamSource(video.srcObject).connect(meter);meter.fftSize=256;}
  const data=new Float32Array(meter.fftSize);meter.getFloatTimeDomainData(data);return Math.sqrt(data.reduce((s,v)=>s+v*v,0)/data.length);
}
setInterval(stats,1000);
function download(blob,extension){const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`CamXP-${new Date().toISOString().replace(/[:.]/g,'-')}.${extension}`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),10000);}
window.chrome?.webview?.addEventListener('message',async e=>{
  const m=e.data;
  try{
    if(m.action==='mirror')video.style.transform=m.value?'scaleX(-1)':'';
    if(m.action==='rotate')video.style.transform=`rotate(${m.value}deg)`;
    if(m.action==='mute')send(ws,{type:'command',action:'mute'});
    if(m.action==='sink'){if(!video.setSinkId)throw Error('Выбор аудиовыхода не поддерживается');await video.setSinkId(m.value);}
    if(m.action==='snapshot'){
      if(!video.videoWidth)throw Error('Нет видеопотока');const c=document.createElement('canvas');c.width=video.videoWidth;c.height=video.videoHeight;c.getContext('2d').drawImage(video,0,0);c.toBlob(b=>download(b,'png'),'image/png');
    }
    if(m.action==='record'){
      if(recording?.state==='recording'){recording.stop();return;}
      if(!video.srcObject)throw Error('Нет видеопотока');
      const mime=['video/webm;codecs=vp8,opus','video/webm'].find(v=>MediaRecorder.isTypeSupported(v));if(!mime)throw Error('Запись WebM не поддерживается');
      recordChunks=[];recordBytes=0;recording=new MediaRecorder(video.srcObject,{mimeType:mime});
      recording.ondataavailable=e=>{if(e.data.size){recordChunks.push(e.data);recordBytes+=e.data.size;if(recordBytes>256*1024*1024&&recording.state==='recording')recording.stop();}};
      recording.onstop=()=>{download(new Blob(recordChunks,{type:mime}),'webm');recordChunks=[];host({type:'recording',active:false});};
      recording.start(1000);host({type:'recording',active:true});
    }
    if(m.action==='reconnect'){ws.close();}
    if(m.action==='shutdown'){stopped=true;ws.close();reset();}
  }catch(e){host({type:'error',text:e.message});}
});
connect();
