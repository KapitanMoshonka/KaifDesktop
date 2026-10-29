"""Extract/size imagegen artwork only; never writes master Frames or releases."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib, json, shutil

OUT = Path(__file__).resolve().parent
PROJECT = OUT.parents[2]
GENERATED = next(p / '.codex/generated_images/01a18713-e271-70d2-b9bb-c54d990cfa75'
                 for p in OUT.parents if (p / '.codex').is_dir())
SOURCES = OUT / 'sources'
SOURCES.mkdir(exist_ok=True)
NAMES = ['exec-d926c311-dfd7-45d0-89d8-d992e9734641.png',
         'exec-fe69b447-1e65-4e09-b3ca-45e3d8d666e1.png',
         'exec-7a01f972-86e7-49d7-962b-090bb8b85ea8.png']

def protected_hashes():
    return {p.relative_to(PROJECT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for folder in ['devs/Frames', 'devs/src', 'actual', 'backup']
            for p in sorted((PROJECT/folder).rglob('*')) if p.is_file()}

before = protected_hashes()
for name in NAMES:
    shutil.copy2(GENERATED/name, SOURCES/name)
sheet = Image.open(SOURCES/NAMES[1]).convert('RGBA')
poses, records = [], []
# Reject the near-duplicate, smaller second pose from the generated sheet.
selected = [0, 2, 3, 4, 5, 6, 7]
for i, source_index in enumerate(selected):
    col, row = source_index % 4, source_index // 4
    box = (col*384, row*512, (col+1)*384, (row+1)*512)
    cell = sheet.crop(box)
    # Generated alpha max is 254, with faint <=8 background residue.
    # Preserve edge antialiasing and normalize solid fur to full opacity.
    cell.putalpha(cell.getchannel('A').point(lambda a: 0 if a <= 8 else 255 if a >= 240 else a))
    bounds = cell.getbbox()
    assert bounds and bounds[0] > 0 and bounds[1] > 0 and bounds[2] < 384 and bounds[3] < 512, (i, bounds)
    poses.append(cell.crop(bounds))
    records.append({'frame': i, 'source_index':source_index, 'source_cell': box, 'source_bounds': bounds})

# One uniform isotropic scale for all poses; no stretching individual bodies.
scale = min(176/max(p.height for p in poses), 178/max(p.width for p in poses))
FRAMES = OUT/'frames'
FRAMES.mkdir(exist_ok=True)
frames = []
for i, pose in enumerate(poses):
    sprite = pose.resize((round(pose.width*scale), round(pose.height*scale)), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', (192,208))
    offset = ((192-sprite.width)//2, 202-sprite.height)
    frame.alpha_composite(sprite, offset)
    frame.save(FRAMES/f'{i:02d}.png')
    frames.append(frame)
    records[i].update({'offset': offset, 'scale': scale, 'bounds': frame.getbbox()})

for name, bg in [('contact-sheet.png','#f3f0e8'), ('contact-sheet-dark.png','#25282c')]:
    contact = Image.new('RGB', (4*216,2*240), bg)
    draw = ImageDraw.Draw(contact)
    for i,frame in enumerate(frames):
        x,y=(i%4)*216+12,(i//4)*240+26
        contact.paste(frame,(x,y),frame)
        draw.text((x,y-20),f'{i:02d}',fill='#151515' if 'dark' not in name else '#eeeeee')
    contact.save(OUT/name)

durations = [160]*len(frames)
gif_frames = []
for frame in frames:
    rgb = Image.new('RGB', frame.size, '#f3f0e8')
    rgb.paste(frame,mask=frame.getchannel('A'))
    gif_frames.append(rgb)
gif_frames[0].save(OUT/'preview.gif',save_all=True,append_images=gif_frames[1:],duration=durations,loop=0,disposal=2)

candidate = {'status':'candidate_pending_user_review','animation':'chase-tail',
             'frame_width':192,'frame_height':208,'format':'PNG RGBA',
             'frames':[f'frames/{i:02d}.png' for i in range(len(frames))],'durations_ms':durations,
             'loop':True,'cycle_ms':sum(durations),'ground_y':202,
             'registration':'shared isotropic scale; silhouette centered; lower support bound y=202',
             'source':f'sources/{NAMES[1]}'}
(OUT/'candidate.json').write_text(json.dumps(candidate,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
errors=[]
for i,frame in enumerate(frames):
    assert frame.mode=='RGBA' and frame.size==(192,208)
    assert frame.getchannel('A').getextrema()==(0,255)
    b=frame.getbbox()
    assert b[0]>0 and b[1]>0 and b[2]<192 and b[3]<208
    if i and frame.tobytes()==frames[i-1].tobytes():errors.append(f'duplicate neighbor {i}')
assert frames[0].tobytes()!=frames[-1].tobytes()
with Image.open(OUT/'preview.gif') as gif:
    assert gif.n_frames==len(frames) and gif.size==(192,208)
    for i,duration in enumerate(durations):
        gif.seek(i)
        assert gif.info['duration']==duration
after=protected_hashes()
assert before==after, 'Protected project files changed during packaging'
report={'passed':not errors,'errors':errors,'frame_count':len(frames),'duration_ms':sum(durations),
        'alpha_normalization':{'background_at_most':8,'solid_at_least':240},
        'registration':records,'protected_files_unchanged':True,'protected_file_count':len(before),
        'limitations':['Technical validation does not establish natural motion or user acceptance.',
                       'Not integrated or tested in desktop application.',
                       'Seven selected generated key poses; no interpolated frames.',
                       'Source pose 1 omitted: near-duplicate front angle and smaller silhouette.']}
(OUT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert not errors
print(json.dumps({'passed':True,'frames':len(frames),'scale':scale,'cycle_ms':sum(durations),'protected_files':len(before)}))
