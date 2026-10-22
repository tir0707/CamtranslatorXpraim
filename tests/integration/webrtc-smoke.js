// Test fixture only: a changing canvas, never represented as real iPhone capture.
(async()=>{
  await new Promise(r=>setTimeout(r,1000));
  const ws=new WebSocket(`wss://${location.host}/signal`);
  const canvas=document.createElement('canvas');canvas.width=640;canvas.height=480;
  const c=canvas.getContext('2d');let frame=0;
  const draw=setInterval(()=>{c.fillStyle=frame++%2?'#18b6a0':'#173a6a';c.fillRect(0,0,640,480);c.fillStyle='white';c.font='28px sans-serif';c.fillText('SYNTHETIC TEST '+frame,30,100);},33);
  const stream=canvas.captureStream(30);
  let pc,queue=[],connected=false,failed;
  ws.onopen=()=>ws.send(JSON.stringify({type:'hello',role:'publisher',code:globalThis.SMOKE_CODE}));
  ws.onmessage=async e=>{
    try{
      const m=JSON.parse(e.data);
      if(m.type==='peer-ready'){
        pc=new RTCPeerConnection({iceServers:[]});stream.getTracks().forEach(t=>pc.addTrack(t,stream));
        const h264=RTCRtpReceiver.getCapabilities('video').codecs.filter(c=>c.mimeType==='video/H264');if(h264.length)pc.getTransceivers()[0].setCodecPreferences(h264);
        pc.onicecandidate=e=>{if(e.candidate)ws.send(JSON.stringify({type:'ice',candidate:e.candidate}));};
        await pc.setLocalDescription(await pc.createOffer());ws.send(JSON.stringify({type:'offer',sdp:pc.localDescription.sdp}));
      }
      if(m.type==='answer'){await pc.setRemoteDescription({type:'answer',sdp:m.sdp});for(const candidate of queue)await pc.addIceCandidate(candidate);queue=[];connected=true;}
      if(m.type==='ice'){if(pc.remoteDescription)await pc.addIceCandidate(m.candidate);else queue.push(m.candidate);}
      if(m.type==='error')failed=m.message;
    }catch(e){failed=e.stack||e.message;}
  };
  const deadline=Date.now()+25000;const video=document.querySelector('video');
  while(Date.now()<deadline){
    if(failed)throw Error(failed);
    const quality=video.getVideoPlaybackQuality();
    if(connected&&quality.totalVideoFrames>=30&&video.videoWidth===640){
      const stats=await pc.getStats();let codec;
      for(const s of stats.values())if(s.type==='outbound-rtp'&&s.kind==='video')codec=stats.get(s.codecId)?.mimeType;
      const received=video.srcObject;const mime='video/webm;codecs=vp8';
      const bytes=await new Promise((resolve,reject)=>{const recorder=new MediaRecorder(received,{mimeType:mime});const chunks=[];recorder.ondataavailable=e=>chunks.push(e.data);recorder.onerror=reject;recorder.onstop=()=>resolve(new Blob(chunks).size);recorder.start();setTimeout(()=>recorder.stop(),1200);});
      const sample=document.createElement('canvas');sample.width=1;sample.height=1;const pixelContext=sample.getContext('2d');pixelContext.drawImage(video,0,0,1,1);const pixel=[...pixelContext.getImageData(0,0,1,1).data];
      if(pixel[1]<10&&pixel[2]<10)throw Error('Decoded frame was black');
      // Leave media alive until the host has captured its verification image.
      clearInterval(draw);
      return {ok:true,fixture:'synthetic canvas, no iPhone',width:video.videoWidth,height:video.videoHeight,decodedFrames:quality.totalVideoFrames,codec,recordedBytes:bytes,pixel,secureContext:isSecureContext};
    }
    await new Promise(r=>setTimeout(r,250));
  }
  clearInterval(draw);stream.getTracks().forEach(t=>t.stop());pc?.close();ws.close();throw Error('No decoded frames within 25 seconds');
})().then(r=>chrome.webview.postMessage({type:'smoke-result',...r})).catch(e=>chrome.webview.postMessage({type:'smoke-result',ok:false,error:e.stack||e.message}));
