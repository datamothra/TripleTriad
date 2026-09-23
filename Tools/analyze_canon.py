import numpy as np, subprocess, imageio_ffmpeg
p=r'C:/Users/24245/Desktop/clavier-music-pachelbelx27s-canon-canon-in-d-307319.mp3'
f=imageio_ffmpeg.get_ffmpeg_exe()
subprocess.run([f,'-y','-i',p,'-t','30','-af','afade=t=out:st=29.5:d=0.5','-ar','44100','Assets/Resources/Canon30.wav'],capture_output=True,check=True)
x=np.frombuffer(subprocess.run([f,'-i',p,'-t','31','-f','f32le','-ac','1','-ar','22050','-'],capture_output=True,check=True).stdout,dtype=np.float32)
n=2048; hop=220
frames=np.lib.stride_tricks.sliding_window_view(x,n)[::hop]
spec=np.abs(np.fft.rfft(frames*np.hanning(n)))
flux=np.maximum(np.diff(np.log1p(spec*10),axis=0),0).sum(axis=1)
peaks=[i for i in range(2,len(flux)-2) if flux[i]==max(flux[i-2:i+3]) and flux[i]>np.percentile(flux,65)]
peaks=sorted(peaks,key=lambda i:flux[i],reverse=True); selected=[]
for i in peaks:
 if all(abs(i-j)>18 for j in selected): selected.append(i)
print('ONSETS',[(round((i*hop+n/2)/22050,3),round(float(flux[i]),1)) for i in sorted(selected)])
freq=np.fft.rfftfreq(8192,1/22050)
for t in np.arange(.2,30,1):
 a=x[int(t*22050):int(t*22050)+8192]; s=np.abs(np.fft.rfft(a*np.hanning(len(a))))
 ids=np.where((freq>65)&(freq<500))[0]; ids=sorted(ids,key=lambda i:s[i],reverse=True); notes=[]
 for i in ids:
  midi=round(69+12*np.log2(freq[i]/440))
  if midi not in notes:notes.append(midi)
  if len(notes)==4:break
 print(round(t,2),notes)

