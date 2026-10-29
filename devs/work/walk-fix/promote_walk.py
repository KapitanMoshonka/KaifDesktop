from pathlib import Path
from PIL import Image,ImageOps
import json,shutil,hashlib
root=Path(__file__).resolve().parents[2]
out=root/'work/walk-fix'
dest=root/'Frames';mp=dest/'animations.json';m=json.loads(mp.read_text(encoding='utf-8-sig'))
for side in ['right','left']:
 for p in (out/f'candidate-{side}').glob('*.png'):shutil.copy2(p,dest/f'walk-{side}'/p.name)
 shutil.copy2(out/f'candidate-{side}.gif',dest/'Previews'/f'walk-{side}.gif')
 shutil.copy2(out/f'candidate-{side}.png',dest/'QA'/f'walk-{side}.png')
 m['animations'][f'walk-{side}']['durations_ms']=[180]*8
 m['animations'][f'walk-{side}']['loop']=True
 m['animations'][f'walk-{side}']['revision']='walk-v2-2026-10-18'
 m['animations'][f'walk-{side}']['gait_note']='Eight successive four-beat walk phases; far hind, far fore, near hind, near fore. No duplicate endpoint. Mirrored left preserves chronological order. Runtime visual acceptance pending.'
mp.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
before=json.loads((out/'before-hashes.json').read_text());changed=[]
for name,old in before.items():
 p=dest/name
 if hashlib.sha256(p.read_bytes()).hexdigest()!=old:changed.append(name)
assert len([n for n in changed if not n.startswith(('walk-right\\','walk-left\\','QA\\walk-'))])==0,changed
errors=[];references=[];boxes=[]
for name,a in m['animations'].items():
 assert len(a['frames'])==len(a['durations_ms']) and all(v>0 for v in a['durations_ms'])
 assert not a.get('next') or a['next'] in m['animations']
 references+=a['frames']
for key in ['look_directions','watch_look_directions']:references += [d['file'] for d in m[key]]
for name in references:
 p=(dest/name).resolve();assert p.is_relative_to(dest.resolve())
 with Image.open(p) as f:
  if f.size!=(192,208) or f.mode!='RGBA':errors.append(name+': format')
  alpha=f.getchannel('A');b=alpha.point(lambda a:255 if a>128 else 0).getbbox()
  if not b or b[0]<=0 or b[1]<=0 or b[2]>=192 or b[3]>=208:errors.append(name+': margins')
  if alpha.getextrema()!=(0,255):errors.append(name+': alpha')
  boxes.append({'file':name,'opaque_bounds':b})
for side in ['right','left']:
 fs=[Image.open(dest/f'walk-{side}/{i:02d}.png').convert('RGBA') for i in range(8)]
 if fs[0].tobytes()==fs[-1].tobytes():errors.append(side+': repeated endpoint')
 for i,f in enumerate(fs):
  other=Image.open(dest/f'walk-{"left" if side=="right" else "right"}/{i:02d}.png').convert('RGBA')
  if ImageOps.mirror(f).tobytes()!=other.tobytes():errors.append(side+': mirror mismatch')
report={'passed':not errors,'errors':errors,'animation_count':len(m['animations']),'animation_frames':sum(len(a['frames']) for a in m['animations'].values()),'standing_look_frames':16,'seated_look_frames':8,'total_frame_count':len(set(references)),
 'checks':['All manifest references exist inside Frames','RGBA 192x208 and alpha range 0..255','Opaque canvas margins','Positive frame durations and valid next references','Walk frames mirror exactly with temporal order preserved','Walk endpoint is not duplicated','SHA256 comparison: other animation PNG unchanged'],
 'limitations':['Automated checks do not establish natural gait','Visual review of contact sheets is not runtime acceptance','New gait not run in desktop application'],
 'walk_revision':'walk-v2-2026-10-18','changed_png':changed,'bounds':boxes}
assert report['total_frame_count']==199
(dest/'frames-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
shutil.copy2(out/'seam-dark.png',dest/'QA/walk-seam-dark.png')
(dest/'QA/walk-v2-validation.json').write_text(json.dumps({k:v for k,v in report.items() if k!='bounds'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['bounds','changed_png']},ensure_ascii=False,indent=2))
assert not errors,errors
