import json
from pathlib import Path
# 根据音频的低音变化和起音逐点标记；没有将自由演奏量化到固定 BPM。
times=[.066,1.842,3.578,5.265,6.981,8.697,10.403,12.129,13.855,15.521,17.247,18.973,20.709,22.406,24.112,25.828,27.524,29.250]
roots=[2,9,11,6,7,2,7,9]
qualities=[1,1,2,2,1,1,1,1]
notes=[]
for i,t in enumerate(times):
 if i % 2 != 0: continue # 简单版：每隔一个和弦才需要操作，约 3.4 秒一次。
 notes.append(dict(time=round(t-.025,3),root=roots[i%8],quality=qualities[i%8],auto=False))
# 简单版只留四组灰色弧条，沿用刚才的和弦位置；站住即可，不额外换位。
for i,sub in {8:[14.703],10:[18.115],12:[21.528],14:[24.960]}.items():
 for t in sub:notes.append(dict(time=round(t-.025,3),root=roots[i%8],quality=qualities[i%8],auto=True))
notes.sort(key=lambda n:n['time'])
Path('Assets/Resources/CanonChart.json').write_text(json.dumps(dict(duration=30,notes=notes),indent=2))
print(len(notes),'targets,',sum(n['auto'] for n in notes),'automatic')
