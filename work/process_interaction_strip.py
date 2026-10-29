from pathlib import Path
from PIL import Image, ImageDraw
import importlib.util, json, sys, shutil
import statistics

ROOT=Path(__file__).resolve().parent/'interactions';ROOT.mkdir(exist_ok=True)
SKILL=Path(r'C:/Users/SirxM/.codex/plugins/cache/openai-curated-remote/work-pets/0.1.6/skills/create-pet/scripts')
def module(name,file):
    spec=importlib.util.spec_from_file_location(name,SKILL/file); m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
extract=module('extract','extract_strip_frames.py')
despill=module('despill','despill_chroma_edges.py')
def process(state,path,count,key=(0,255,255)):
    source=Path(path);(ROOT/'sources').mkdir(exist_ok=True);shutil.copy2(source,ROOT/'sources'/f'{state}.png')
    im=Image.open(source).convert('RGBA')
    im=extract.remove_chroma_background(im,key,96)
    groups=extract.component_frame_groups(im,count)
    if groups is None:raise RuntimeError(f'{state}: could not recover {count} complete pose groups')
    bounds=[extract.component_bounds(g) for g in groups]
    widths=[b[2]-b[0] for b in bounds];heights=[b[3]-b[1] for b in bounds]
    if state in ['grab-right','pull-right']: scale=min(145/max(heights),178/max(widths))
    elif state=='sit-down':scale=min(174/heights[0],182/max(widths),196/max(heights))
    elif state in ['watch-idle','watch-look','watch-look-up-left','ear-scratch-source']:
        neutral=Image.open(ROOT/'frames/sit-down/05.png').getbbox();targetheight=neutral[3]-neutral[1]
        scale=min((neutral[2]-neutral[0])/widths[0],182/max(widths),196/max(heights)) if state in ['watch-look','watch-look-up-left'] else min(targetheight/heights[0],182/max(widths),196/max(heights))
    else:scale=min(174/heights[0],182/max(widths),196/max(heights))
    folder=ROOT/'frames'/state;folder.mkdir(parents=True,exist_ok=True)
    frames=[];reports=[]
    for i,(g,b) in enumerate(zip(groups,bounds)):
        sprite=extract.component_group_image(im,g,padding=0)
        sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
        f=Image.new('RGBA',(192,208));f.alpha_composite(sprite,((192-sprite.width)//2,202-sprite.height))
        if state in ['watch-idle','watch-look','watch-look-up-left','ear-scratch-source']:
            def paw_center(image):
                alpha=image.getchannel('A');box=alpha.point(lambda a:255 if a>128 else 0).getbbox();bottom=box[3]
                return statistics.median(x for y in range(max(box[1],bottom-10),bottom) for x in range(192) if alpha.getpixel((x,y))>128)
            neutral_img=Image.open(ROOT/'frames/sit-down/05.png').convert('RGBA')
            shift=round(paw_center(neutral_img)-paw_center(f))
            aligned=Image.new('RGBA',(192,208));aligned.alpha_composite(f,(shift,0));f=aligned
        f,report=despill.decontaminate_image(f,chroma_key=key,strength=1,edge_radius=5,spill_tolerance=.15,minimum_saturation=.1)
        f.save(folder/f'{i:02d}.png');frames.append(f);reports.append(report)
    sheet=Image.new('RGB',(192*count,232),'#f7f3eb');draw=ImageDraw.Draw(sheet)
    for i,f in enumerate(frames):sheet.paste(f,(192*i,24),f);draw.text((192*i+4,4),f'{state} {i}',fill='black')
    sheet.save(ROOT/f'{state}-contact.png')
    frames[0].save(ROOT/f'{state}.gif',save_all=True,append_images=frames[1:],duration=180,loop=0,disposal=2)
    report={'state':state,'frames':count,'scale':scale,'source_bounds':bounds,'frame_bounds':[f.getbbox() for f in frames],'despill_changed_pixels':[r['changed_pixels'] for r in reports]}
    (ROOT/f'{state}-qa.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))
if __name__=='__main__':
    key=tuple(int(sys.argv[4].lstrip('#')[i:i+2],16) for i in (0,2,4)) if len(sys.argv)>4 else (0,255,255)
    process(sys.argv[1],sys.argv[2],int(sys.argv[3]),key)
