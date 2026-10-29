from pathlib import Path
from PIL import Image,ImageDraw,ImageOps,ImageFilter
import json,importlib.util,shutil,hashlib,sys,statistics
root=Path(__file__).resolve().parents[2];out=root/'work/walk-fix'
path=Path(r'C:/Users/SirxM/.codex/generated_images/01a14e06-df72-7603-859d-e8fb86255c4f/exec-cf37ea3f-5bd3-40d7-ad0d-1dc5413b0939.png')
spec=importlib.util.spec_from_file_location('extract',r'C:/Users/SirxM/.codex/plugins/cache/openai-curated-remote/work-pets/0.1.6/skills/create-pet/scripts/extract_strip_frames.py')
extract=importlib.util.module_from_spec(spec);spec.loader.exec_module(extract)
shutil.copy2(path,out/'approved-source.png')
im=Image.open(path).convert('RGBA');pixels=im.load();removed=0
# Generated alpha contains saturated yellow/red matte specks. Remove only
# those key-like hues at the alpha boundary; retain interior coat colors.
alpha=im.getchannel('A');eroded=alpha.filter(ImageFilter.MinFilter(9))
for y in range(im.height):
 for x in range(im.width):
  r,g,b,a=pixels[x,y]
  yellow=r>170 and g>160 and b<110 and abs(r-g)<60
  red=r>170 and g<90 and b<100
  if a and eroded.getpixel((x,y))<220 and (yellow or red):
   pixels[x,y]=(0,0,0,0);removed+=1
groups=extract.component_frame_groups(im,8);assert groups and len(groups)==8
bounds=[extract.component_bounds(g) for g in groups]
top=min(b[1] for b in bounds);bottom=max(b[3] for b in bounds)
scale=min(141/(bottom-top),160/max(b[2]-b[0] for b in bounds))
frames=[]
for g,b in zip(groups,bounds):
 sprite=extract.component_group_image(im,g,padding=0)
 sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
 # Head registration uses upper half of silhouette, avoiding moving paws.
 head=Image.new('L',im.size)
 head.paste(im.getchannel('A').crop((b[0],top,b[2],top+int((bottom-top)*.52))),(b[0],top))
 hb=head.point(lambda a:255 if a>128 else 0).getbbox()
 left=173-round((hb[2]-b[0])*scale)
 y=202-round((bottom-b[1])*scale)
 f=Image.new('RGBA',(192,208));f.alpha_composite(sprite,(left,y))
 # Keep lowest supporting paw on the existing common baseline.
 base=f.getchannel('A').point(lambda a:255 if a>128 else 0).getbbox()[3]
 aligned=Image.new('RGBA',(192,208));aligned.alpha_composite(f,(0,202-base));frames.append(aligned)
for side,fs in [('right',frames),('left',[ImageOps.mirror(f) for f in frames])]:
 folder=out/f'candidate-{side}';folder.mkdir(exist_ok=True)
 for i,f in enumerate(fs):f.save(folder/f'{i:02d}.png')
 sheet=Image.new('RGB',(192*8,240),'#efe9df');draw=ImageDraw.Draw(sheet)
 for i,f in enumerate(fs):sheet.paste(f,(192*i,24),f);draw.text((192*i+4,4),str(i),fill='black')
 sheet.save(out/f'candidate-{side}.png')
 rgb=[]
 for f in fs:
  bg=Image.new('RGB',(192,208),'#efe9df');bg.paste(f,mask=f.getchannel('A'));rgb.append(bg)
 rgb[0].save(out/f'candidate-{side}.gif',save_all=True,append_images=rgb[1:],duration=180,loop=0,disposal=2)
# Inspect lower limbs at native and double scale, including cyclic seam.
fs=[frames[i] for i in [6,7,0,1,2,3,4,5]]
sheet=Image.new('RGB',(192*8,240),'#262626');draw=ImageDraw.Draw(sheet)
for j,f in enumerate(fs):
 sheet.paste(f,(192*j,24),f);draw.text((192*j+4,4),f'phase {[6,7,0,1,2,3,4,5][j]}',fill='white')
sheet.save(out/'seam-dark.png')
report={'source':str(path),'removed_edge_matte_pixels':removed,'shared_scale':scale,'source_bounds':bounds,'frame_bounds':[f.getbbox() for f in frames]}
(out/'extraction.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if '--promote' not in sys.argv:raise SystemExit(0)
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
