from pathlib import Path
from PIL import Image,ImageDraw
root=Path(__file__).parent/'interactions'
sheet=Image.new('RGB',(4*384,3*336),'#f7f3eb');d=ImageDraw.Draw(sheet)
items=[('grab-right',i) for i in range(4)]+[('pull-right',i) for i in range(8)]
for k,(state,i) in enumerate(items):
    im=Image.open(root/'frames'/state/f'{i:02d}.png').convert('RGBA')
    crop=im.crop((96,48,192,132)).resize((384,336),Image.Resampling.NEAREST)
    x=(k%4)*384;y=(k//4)*336
    sheet.paste(crop,(x,y),crop);d.text((x+3,y+3),f'{state} {i}',fill='black')
    for gx in range(96,192,8):
        px=x+(gx-96)*4;d.line((px,y,px,y+335),fill='#b7b7b7',width=1);d.text((px+1,y+18),str(gx),fill='black')
    for gy in range(48,132,8):
        py=y+(gy-48)*4;d.line((x,py,x+383,py),fill='#b7b7b7',width=1);d.text((x+2,py+1),str(gy),fill='black')
sheet.save(root/'mouth-anchor-grid.png')
