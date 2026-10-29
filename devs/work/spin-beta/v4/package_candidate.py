"""Package single-frame revision; nine other PNGs are byte-for-byte v3 copies."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,shutil,hashlib

OUT=Path(__file__).resolve().parent
BASE=OUT.parent/'v3'
PROJECT=OUT.parents[3]
NAME='exec-a24cbb76-a203-4a32-9ef5-d705135ffdf8.png'
GENERATED=next(p/'.codex/generated_images/01a18713-e271-70d2-b9bb-c54d990cfa75'
               for p in OUT.parents if (p/'.codex').is_dir())
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def snapshot():
 return {p.relative_to(PROJECT).as_posix():digest(p)
         for name in ['devs/Frames','devs/src','actual','backup']
         for p in (PROJECT/name).rglob('*') if p.is_file()}
before=snapshot()
(OUT/'sources').mkdir(exist_ok=True)
(OUT/'frames').mkdir(exist_ok=True)
source=OUT/'sources'/NAME
if not source.exists():shutil.copy2(GENERATED/NAME,source)
pose=Image.open(source).convert('RGBA')
pose.putalpha(pose.getchannel('A').point(lambda a:0 if a<=8 else 255 if a>=240 else a))
source_bounds=pose.getbbox()
assert source_bounds
pose=pose.crop(source_bounds)
old=Image.open(BASE/'frames/06.png').convert('RGBA')
old_bounds=old.getbbox()
scale=(old_bounds[3]-old_bounds[1])/pose.height
pose=pose.resize((round(pose.width*scale),old_bounds[3]-old_bounds[1]),Image.Resampling.LANCZOS)
new=Image.new('RGBA',(192,208))
new.alpha_composite(pose,(0,old_bounds[1]))
# Register the lower torso and feet, rather than recentering around the wider head.
old_body=old.getchannel('A').crop((0,110,192,208)).point(lambda a:255 if a>128 else 0).getbbox()
new_body=new.getchannel('A').crop((0,110,192,208)).point(lambda a:255 if a>128 else 0).getbbox()
shift=round((old_body[0]+old_body[2]-new_body[0]-new_body[2])/2)
registered=Image.new('RGBA',(192,208));registered.alpha_composite(new,(shift,0))
registered.save(OUT/'frames/06.png')
for i in range(10):
 if i!=6:shutil.copy2(BASE/'frames'/f'{i:02d}.png',OUT/'frames'/f'{i:02d}.png')
frames=[Image.open(OUT/'frames'/f'{i:02d}.png').convert('RGBA') for i in range(10)]
candidate=json.loads((BASE/'candidate.json').read_text(encoding='utf-8'))
candidate.update({'revision':4,'changed_frames':[6],'unchanged_frames':[i for i in range(10) if i!=6],
                  'source':f'sources/{NAME}','registration':'06: lower-body horizontal registration to v3, same height/baseline; other frames copied unchanged'})
(OUT/'candidate.json').write_text(json.dumps(candidate,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
durations=candidate['durations_ms']
for filename,bg,ink in [('contact-sheet.png','#f3f0e8','#222222'),('contact-sheet-dark.png','#25282c','#eeeeee')]:
 sheet=Image.new('RGB',(1080,480),bg);draw=ImageDraw.Draw(sheet)
 for i,frame in enumerate(frames):
  x,y=i%5*216+12,i//5*240+26
  sheet.paste(frame,(x,y),frame);draw.text((x,y-20),f'{i:02d}',fill=ink)
 sheet.save(OUT/filename)
for filename,size in [('preview.gif',(192,208)),('preview-2x.gif',(384,416))]:
 rgb=[]
 for frame in frames:
  bg=Image.new('RGB',(192,208),'#f3f0e8');bg.paste(frame,mask=frame.getchannel('A'))
  if size!=bg.size:bg=bg.resize(size,Image.Resampling.LANCZOS)
  rgb.append(bg)
 rgb[0].save(OUT/filename,save_all=True,append_images=rgb[1:],duration=durations,loop=0,disposal=2)
 with Image.open(OUT/filename) as gif:
  assert gif.n_frames==10
  for i,duration in enumerate(durations):gif.seek(i);gif.load();assert gif.info['duration']==duration
sheet=Image.new('RGB',(2*384,416),'#f3f0e8');draw=ImageDraw.Draw(sheet)
for i,frame in enumerate([old,registered]):
 img=frame.resize((384,416),Image.Resampling.LANCZOS);sheet.paste(img,(384*i,0),img)
 draw.text((384*i+8,8),'BEFORE 06' if i==0 else 'AFTER 06',fill='#222222')
sheet.save(OUT/'frame-06-before-after.png')
for i,frame in enumerate(frames):
 assert frame.size==(192,208) and frame.mode=='RGBA' and frame.getchannel('A').getextrema()==(0,255)
 b=frame.getbbox();assert b[0]>0 and b[1]>0 and b[2]<192 and b[3]<208
 if i!=6:assert digest(OUT/'frames'/f'{i:02d}.png')==digest(BASE/'frames'/f'{i:02d}.png')
assert frames[0].tobytes()!=frames[-1].tobytes()
assert snapshot()==before
report={'passed':True,'frames':10,'changed_frames':[6],'nine_other_pngs_byte_identical_to_v3':True,
        'protected_file_count':len(before),'protected_files_unchanged':True,'cycle_ms':sum(durations),
        'frame06_registration':{'source_bounds':source_bounds,'scale':scale,'horizontal_shift':shift,
                                'old_bounds':old_bounds,'new_bounds':registered.getbbox()},
        'limitations':['Single-frame imagegen edit may slightly redraw body details.',
                       'Visual approval and transition 05-06-07 still require user review.',
                       'Not integrated or tested in application.']}
(OUT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report))
