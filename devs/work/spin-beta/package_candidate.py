"""Package generated spin poses in this candidate folder only.

No artwork is redrawn. Cropping, common isotropic resizing and alpha cleanup
prepare the generated sprite sheet for the existing 192x208 resource contract.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib, json, shutil

OUT = Path(__file__).resolve().parent
PROJECT = OUT.parents[2]
SOURCE_NAME = 'exec-09b16cd6-bd04-4143-9e14-073b6dc6b4c5.png'
GENERATED = next(p / '.codex/generated_images/01a18713-e271-70d2-b9bb-c54d990cfa75'
                 for p in OUT.parents if (p / '.codex').is_dir())

def snapshot():
    return {p.relative_to(PROJECT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
            for folder in ['devs/Frames','devs/src','actual','backup']
            for p in sorted((PROJECT/folder).rglob('*')) if p.is_file()}

def runs(values):
    result=[]
    start=None
    for i,active in enumerate(values+[False]):
        if active and start is None:start=i
        if not active and start is not None:
            result.append((start,i));start=None
    return result

before=snapshot()
(OUT/'sources').mkdir(exist_ok=True)
source=OUT/'sources'/SOURCE_NAME
if not source.exists():shutil.copy2(GENERATED/SOURCE_NAME,source)
sheet=Image.open(source).convert('RGBA')
# The generator leaves RGB matte colors in near-transparent exterior pixels.
# Normalize only alpha; do not remove red/orange coat by color.
sheet.putalpha(sheet.getchannel('A').point(lambda a:0 if a<=8 else 255 if a>=240 else a))
mask=sheet.getchannel('A').point(lambda a:255 if a>128 else 0)
row_runs=[(a,b) for a,b in runs([mask.crop((0,y,sheet.width,y+1)).getbbox() is not None
                                for y in range(sheet.height)]) if b-a>80]
assert len(row_runs)==2,row_runs
poses=[];records=[]
for top,bottom in row_runs:
    rowmask=mask.crop((0,top,sheet.width,bottom))
    columns=runs([rowmask.crop((x,0,x+1,bottom-top)).getbbox() is not None
                  for x in range(sheet.width)])
    columns=[(a,b) for a,b in columns if b-a>60]
    assert len(columns)==5,columns
    for left,right in columns:
        # Include antialiased outline but stay between neighboring sprites.
        box=(max(0,left-3),max(0,top-3),min(sheet.width,right+3),min(sheet.height,bottom+3))
        crop=sheet.crop(box)
        b=crop.getbbox()
        assert b
        pose=crop.crop(b)
        poses.append(pose)
        records.append({'source_crop':box,'local_bounds':b})
assert len(poses)==10

scale=min(176/max(p.height for p in poses),178/max(p.width for p in poses))
(OUT/'frames').mkdir(exist_ok=True)
frames=[]
for i,pose in enumerate(poses):
    sprite=pose.resize((round(pose.width*scale),round(pose.height*scale)),Image.Resampling.LANCZOS)
    frame=Image.new('RGBA',(192,208))
    offset=((192-sprite.width)//2,202-sprite.height)
    frame.alpha_composite(sprite,offset)
    frame.save(OUT/'frames'/f'{i:02d}.png')
    frames.append(frame)
    records[i].update({'frame':i,'scale':scale,'offset':offset,'bounds':frame.getbbox()})

for filename,bg,ink in [('contact-sheet.png','#f3f0e8','#222222'),
                        ('contact-sheet-dark.png','#25282c','#eeeeee')]:
    contact=Image.new('RGB',(5*216,2*240),bg);draw=ImageDraw.Draw(contact)
    for i,frame in enumerate(frames):
        x,y=i%5*216+12,i//5*240+26
        contact.paste(frame,(x,y),frame);draw.text((x,y-20),f'{i:02d}',fill=ink)
    contact.save(OUT/filename)

reference_indices=[0,3,5,7,9,11,13,16,18,21]
durations=[(b-a)*50 for a,b in zip(reference_indices,reference_indices[1:]+[23])]
def gif(filename,size):
    images=[]
    for frame in frames:
        rgb=Image.new('RGB',frame.size,'#f3f0e8');rgb.paste(frame,mask=frame.getchannel('A'))
        if size!=frame.size:rgb=rgb.resize(size,Image.Resampling.LANCZOS)
        images.append(rgb)
    images[0].save(OUT/filename,save_all=True,append_images=images[1:],duration=durations,loop=0,disposal=2)
gif('preview.gif',(192,208))
gif('preview-2x.gif',(384,416))

candidate={'display_name':'Крутись','status':'candidate_pending_user_review',
           'internal_animation_key':None,'integration_note':'Master chase-tail key is unchanged; decide key only during integration.',
           'frame_width':192,'frame_height':208,'format':'PNG RGBA',
           'frames':[f'frames/{i:02d}.png' for i in range(10)],
           'durations_ms':durations,'loop':True,'cycle_ms':sum(durations),'ground_y':202,
           'registration':'shared isotropic scale; silhouette centered; lower bound y=202',
           'reference':{'file':'reference/golden-retriever-dog.gif','frames':23,'duration_ms':1150,
                        'selected_frame_indices':reference_indices},
           'source':f'sources/{SOURCE_NAME}'}
(OUT/'candidate.json').write_text(json.dumps(candidate,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

errors=[]
for i,frame in enumerate(frames):
    assert frame.mode=='RGBA' and frame.size==(192,208)
    assert frame.getchannel('A').getextrema()==(0,255)
    b=frame.getbbox()
    assert b[0]>0 and b[1]>0 and b[2]<192 and b[3]<208
    if i and frames[i-1].tobytes()==frame.tobytes():errors.append(f'duplicate neighbor {i}')
assert frames[0].tobytes()!=frames[-1].tobytes()
for filename,size in [('preview.gif',(192,208)),('preview-2x.gif',(384,416))]:
    with Image.open(OUT/filename) as im:
        assert im.n_frames==10 and im.size==size
        for i,duration in enumerate(durations):
            im.seek(i);im.load();assert im.info['duration']==duration
assert snapshot()==before,'Protected project files changed during packaging'
report={'passed':not errors,'errors':errors,'frames':10,'cycle_ms':sum(durations),
        'source_size':sheet.size,'source_pose_count':len(poses),'registration':records,
        'alpha_cleanup':{'zero_at_most':8,'opaque_at_least':240,'rgb_unchanged_before_resizing':True},
        'protected_files_unchanged':True,'protected_file_count':len(before),
        'checks':['RGBA 192x208','alpha includes 0 and 255','canvas margins','positive frame durations',
                  'distinct neighbors','nonduplicate loop endpoint','GIF frame counts and exact durations',
                  'protected files SHA-256 unchanged'],
        'limitations':['Technical validation does not establish natural motion or user acceptance.',
                       'Not integrated or tested in desktop application.',
                       'Ten generated key poses, no motion interpolation; stepping and seam need user evaluation.']}
(OUT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert not errors
print(json.dumps({'passed':True,'frames':10,'scale':scale,'cycle_ms':sum(durations),'protected_files':len(before)}))
