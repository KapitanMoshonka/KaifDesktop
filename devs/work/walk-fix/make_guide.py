from PIL import Image,ImageDraw
from pathlib import Path
import math,json
out=Path(__file__).resolve().parent
im=Image.new('RGB',(192*8,250),'white');d=ImageDraw.Draw(im)
legs=[('far hind',50,137,0,'#617b9b'),('far fore',119,134,.25,'#617b9b'),
      ('near hind',55,140,.5,'#c26020'),('near fore',124,136,.75,'#c26020')]
records=[]
for i in range(8):
 t=(i+.5)/8;xoff=i*192
 d.text((xoff+6,4),f'phase {i}',fill='black')
 d.line((xoff+20,226,xoff+182,226),fill='#aaa')
 d.line((xoff+50,161,xoff+122,158),fill='#444',width=22)
 d.line((xoff+122,158,xoff+141,119),fill='#444',width=16)
 d.ellipse((xoff+130,104,xoff+161,135),outline='#444',width=4)
 d.line((xoff+151,123,xoff+172,127),fill='#444',width=9)
 d.arc((xoff+26,133,xoff+59,163),20,340,fill='#444',width=7)
 row={}
 for name,hx,hy,start,color in legs:
  s=(t-start)%1
  if s<.25:
   fx=hx-20+40*s/.25;fy=202-15*math.sin(math.pi*s/.25);swing=True
  else:fx=hx+20-40*(s-.25)/.75;fy=202;swing=False
  knee_x=hx+(fx-hx)*.45+(-9 if 'hind' in name else 7)
  knee_y=(hy+fy)/2-3
  d.line([(xoff+hx,hy+24),(xoff+knee_x,knee_y+24),(xoff+fx,fy+24)],fill=color,width=6)
  d.ellipse((xoff+fx-5,fy+21,xoff+fx+5,fy+27),fill=color)
  row[name]={'paw':[round(fx,1),round(fy,1)],'swing':swing}
 records.append(row)
im.save(out/'phase-guide.png');(out/'phase-guide.json').write_text(json.dumps(records,indent=2))
