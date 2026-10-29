"""Package approved imagegen poses; no app code or network access."""
from pathlib import Path
from PIL import Image, ImageOps, ImageDraw
import json

ROOT=Path(__file__).resolve().parents[1]
DEST=ROOT/'Frames'; SRC=ROOT/'work/interactions/frames'
manifest_path=DEST/'animations.json'
m=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
def load(folder):
    return [Image.open(p).convert('RGBA') for p in sorted((SRC/folder).glob('*.png'))]
idle=Image.open(DEST/'idle/00.png').convert('RGBA')
sit=load('sit-down'); sit[0]=idle.copy(); neutral=sit[-1].copy()
watch=load('watch-idle');watch[0]=neutral.copy();watch[-1]=neutral.copy()
stretch=load('stretch-yawn');stretch[0]=idle.copy();stretch[-1]=idle.copy()
scratch=load('ear-scratch-source');scratch[0]=neutral.copy();scratch[-1]=neutral.copy()
grab=load('grab-right');pull=load('pull-right')
mirror=lambda frames:[ImageOps.mirror(f) for f in frames]
reverse=lambda frames:list(reversed(frames))
grab_points=[[166,96],[176,97],[175,96],[175,96]]
pull_points=[[165,91],[167,91],[159,90],[166,91],[164,91],[164,91],[163,90],[163,91]]
mirror_points=lambda points:[[191-x,y] for x,y in points]
new={}
def add(name,frames,durations,loop=False,next_state=None,anchors=None,events=None):
    folder=DEST/name;folder.mkdir(exist_ok=True)
    for i,f in enumerate(frames):f.save(folder/f'{i:02d}.png')
    data={'frames':[f'{name}/{i:02d}.png' for i in range(len(frames))],
          'durations_ms':durations,'loop':loop}
    if next_state:data['next']=next_state
    if anchors:data['mouth_anchors_px']=anchors
    if events:data['frame_events']=events
    m['animations'][name]=data;new[name]=frames
for side,frames,points in [('right',grab,grab_points),('left',mirror(grab),mirror_points(grab_points))]:
    add('grab-'+side,frames,[140,120,140,160],next_state='pull-'+side,anchors=points,
        events=[{'frame':3,'event':'attach_to_mouth'}])
    add('release-'+side,reverse(frames),[120,140,120,180],next_state='idle',anchors=list(reversed(points)),
        events=[{'frame':1,'event':'detach_from_mouth'}])
for side,frames,points in [('right',pull,pull_points),('left',mirror(pull),mirror_points(pull_points))]:
    add('pull-'+side,frames,[140]*8,True,anchors=points)
add('chase-tail',load('chase-tail'),[140]*8,True)
add('stretch-yawn',stretch,[180,200,220,300,200,520,220,240],next_state='idle')
add('sit-down',sit,[180,160,180,180,180,220],next_state='watch-idle')
add('stand-up',reverse(sit),[160,160,180,180,180,220],next_state='idle')
add('watch-idle',watch,[360,240,240,160,240,360],True)
add('ear-scratch-enter',[scratch[i] for i in [0,1,2]],[180,180,160],next_state='ear-scratch-loop')
add('ear-scratch-loop',[scratch[i] for i in [2,3,4,5]],[110]*4,True)
add('ear-scratch-exit',[scratch[i] for i in [2,6,7]],[160,180,200],next_state='stand-up')
directions=load('watch-look')[:7]+load('watch-look-up-left')
folder=DEST/'watch-look';folder.mkdir(exist_ok=True)
m['watch_look_directions']=[]
for i,f in enumerate(directions):
    filename=f'watch-look/{i*45:03d}.png';f.save(DEST/filename)
    m['watch_look_directions'].append({'degrees_clockwise_from_up':i*45,'file':filename})
m['anchors']['mouth']='Per-frame mouth_anchors_px: [x,y] in the 192x208 PNG, origin top-left. Mirror x=191-x. Runtime must apply sprite scale and screen position.'
m['transitions']['sleep_trigger']='Behavior controller: random interaction after 10 seconds of calm standing/walking; not system input inactivity. Sleep lasts 60 seconds by default; click wakes early.'
m['behavior_sequences']={
    'sleep':['lie-down','sleep','wake-up','idle'],
    'meme-right':['walk-right','grab-right','pull-right','release-right','idle'],
    'meme-left':['walk-left','grab-left','pull-left','release-left','idle'],
    'cursor-right':['running-right','grab-right','pull-right','release-right','idle'],
    'cursor-left':['running-left','grab-left','pull-left','release-left','idle'],
    'tail':['chase-tail','idle'],
    'stretch':['stretch-yawn','idle'],
    'scratch':['sit-down','ear-scratch-enter','ear-scratch-loop','ear-scratch-exit','stand-up','idle'],
    'watch':['walk-right','sit-down','watch-idle','stand-up','idle']}
m['interaction_notes']={
    'controller':'Sequences are descriptive. Controller overrides sit-down.next for scratch, exits loops, chooses direction and duration, and resets calm timer after interaction.',
    'carry':'Same empty-mouth graphics serve cursor and Photos window. No cursor/window baked into PNG. Attach at grab frame 3, detach at release frame 1. Mouth anchors require runtime verification.',
    'watch':'Use watch_look_directions while seated; standing look directions have a different body pose.',
    'timing':'New frame durations are graphics playback defaults, not approved interaction durations or probabilities.',
    'limits':'Side-view locomotion only. Window dragging, pointer control, DPI and seamless runtime transitions not tested.'}
m['total_frame_count']=sum(len(a['frames']) for a in m['animations'].values())+len(m['look_directions'])+len(m['watch_look_directions'])
m['generation']='Built-in imagegen artwork; deterministic chroma extraction, registration, mirroring and reversed transition frames. New desktop interaction package, 2026-10-18.'
manifest_path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

preview=DEST/'Previews';preview.mkdir(exist_ok=True)
def gif(name,frames,times):
    # Composite onto a known matte to avoid GIF partial-alpha artifacts.
    rgb=[]
    for f in frames:
        bg=Image.new('RGB',f.size,'#f3f0e8');bg.paste(f,mask=f.getchannel('A'));rgb.append(bg)
    rgb[0].save(preview/(name+'.gif'),save_all=True,append_images=rgb[1:],duration=times,loop=0,disposal=2)
for name,frames in new.items():gif(name,frames,m['animations'][name]['durations_ms'])
for name,states in {'sequence-carry-right':['grab-right','pull-right','pull-right','release-right'],
                    'sequence-carry-left':['grab-left','pull-left','pull-left','release-left'],
                    'sequence-scratch':['sit-down','ear-scratch-enter','ear-scratch-loop','ear-scratch-loop','ear-scratch-exit','stand-up'],
                    'sequence-watch':['sit-down','watch-idle','stand-up']}.items():
    gif(name,[f for s in states for f in new[s]],[d for s in states for d in m['animations'][s]['durations_ms']])
gif('watch-look',directions,[400]*8)
qa=DEST/'QA';qa.mkdir(exist_ok=True)
# Native-size contact sheets; no resampling of the delivered sprites.
for name,frames in {**new,'watch-look':directions}.items():
    sheet=Image.new('RGB',(192*len(frames),232),'#f3f0e8');draw=ImageDraw.Draw(sheet)
    for i,f in enumerate(frames):
        sheet.paste(f,(192*i,24),f);draw.text((192*i+4,4),f'{name} {i}',fill='black')
    sheet.save(qa/(name+'.png'))
sheet=Image.new('RGB',(192*8,232*6),'#252525');draw=ImageDraw.Draw(sheet)
for row,name in enumerate(['grab-right','pull-right','release-right','grab-left','pull-left','release-left']):
    for i,f in enumerate(new[name]):
        x=i*192;y=row*232;sheet.paste(f,(x,y+24),f)
        ax,ay=m['animations'][name]['mouth_anchors_px'][i]
        draw.ellipse((x+ax-2,y+24+ay-2,x+ax+2,y+24+ay+2),fill='#ff4444')
        draw.text((x+4,y+4),f'{name} {i}',fill='white')
sheet.save(qa/'mouth-anchors.png')

errors=[];bounds=[];references=[]
for name,a in m['animations'].items():
    if len(a['frames'])!=len(a['durations_ms']) or any(d<=0 for d in a['durations_ms']):errors.append(name+': duration mismatch')
    if a.get('next') and a['next'] not in m['animations']:errors.append(name+': invalid next')
    if 'mouth_anchors_px' in a and len(a['mouth_anchors_px'])!=len(a['frames']):errors.append(name+': anchor mismatch')
    references.extend(a['frames'])
for key in ['look_directions','watch_look_directions']:references.extend(d['file'] for d in m[key])
for file in references:
    path=(DEST/file).resolve()
    if not path.is_relative_to(DEST.resolve()):errors.append(file+': path escapes Frames');continue
    if not path.is_file():errors.append(file+': missing');continue
    with Image.open(path) as im:
        if im.mode!='RGBA' or im.size!=(192,208):errors.append(file+': invalid format')
        b=im.getchannel('A').point(lambda v:255 if v>128 else 0).getbbox()
        if not b:errors.append(file+': empty')
        elif b[0]<=0 or b[1]<=0 or b[2]>=192 or b[3]>=208:errors.append(file+': opaque content touches canvas edge')
        if im.getchannel('A').getextrema()!=(0,255):errors.append(file+': missing transparency/opaque pixels')
        bounds.append({'file':file,'opaque_bounds':b})
if len(set(references))!=m['total_frame_count']:errors.append('total count mismatch')
report={'passed':not errors,'errors':errors,'animation_count':len(m['animations']),
        'animation_frames':sum(len(a['frames']) for a in m['animations'].values()),
        'standing_look_frames':16,'seated_look_frames':8,'total_frame_count':m['total_frame_count'],
        'checks':['Referenced file exists and stays inside Frames','RGBA 192x208','Alpha includes 0 and 255','Opaque silhouette has canvas margins','Positive duration for each frame','Valid next references','Mouth anchor array lengths','Unique frame count'],
        'limitations':['Automated validation does not verify anatomy or temporal smoothness','Runtime, DPI, cursor and Photos window attachment not tested'],
        'bounds':bounds}
(DEST/'frames-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['bounds','checks']},ensure_ascii=False,indent=2))
if errors:raise SystemExit(1)
