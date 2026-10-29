from pathlib import Path
from PIL import Image,ImageFilter,ImageOps,ImageDraw
import json,importlib.util,shutil
root=Path(__file__).resolve().parents[2];out=Path(__file__).resolve().parent
skill_rel=Path('.codex/plugins/cache/openai-curated-remote/work-pets/0.1.6/skills/create-pet/scripts')
skill=next((parent/skill_rel for parent in Path(__file__).resolve().parents
            if (parent/skill_rel/'extract_strip_frames.py').is_file()),None)
if skill is None:
 raise FileNotFoundError('Existing work-pets extraction scripts not found in an ancestor profile .codex directory')
spec=importlib.util.spec_from_file_location('extract',skill/'extract_strip_frames.py')
extract=importlib.util.module_from_spec(spec);spec.loader.exec_module(extract)
inputs=json.loads((out/'single-sources.json').read_text(encoding='utf-8'))
frames=[];reports=[]
for i,item in enumerate(inputs):
 # The JSON retains historical provenance; use the archived local source.
 path=out/f"single-source-{item['phase']:02d}.png"
 im=Image.open(path).convert('RGBA');px=im.load();alpha=im.getchannel('A');eroded=alpha.filter(ImageFilter.MinFilter(9))
 for y in range(im.height):
  for x in range(im.width):
   r,g,b,a=px[x,y]
   if a<=16 or (eroded.getpixel((x,y))<220 and ((r>170 and g>160 and b<110 and abs(r-g)<60) or (r>170 and g<90 and b<100))):px[x,y]=(0,0,0,0)
 groups=extract.component_frame_groups(im,1);assert groups
 sprite=extract.component_group_image(im,groups[0],padding=0)
 # Body extent excludes swinging paws. Match existing upper-body width and
 # keep original frame size/baseline; this is registration, not screen scaling.
 a=sprite.getchannel('A');headbody=a.crop((0,0,sprite.width,round(sprite.height*.62)))
 hb=headbody.point(lambda v:255 if v>128 else 0).getbbox()
 sx=151/(hb[2]-hb[0]);sy=141/sprite.height
 sprite=sprite.resize((round(sprite.width*sx),141),Image.Resampling.LANCZOS)
 f=Image.new('RGBA',(192,208));left=173-round(hb[2]*sx);f.alpha_composite(sprite,(left,61))
 frames.append(f);reports.append({'phase':i,'source':str(path),'registration_scale_xy':[sx,sy],'bounds':f.getbbox()})
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
sheet=Image.new('RGB',(192*8,240),'#262626');draw=ImageDraw.Draw(sheet)
for j,i in enumerate([6,7,0,1,2,3,4,5]):
 f=frames[i];sheet.paste(f,(192*j,24),f);draw.text((192*j+4,4),f'phase {i}',fill='white')
sheet.save(out/'seam-dark.png')
(out/'single-extraction.json').write_text(json.dumps(reports,indent=2))
print(json.dumps(reports,indent=2))
