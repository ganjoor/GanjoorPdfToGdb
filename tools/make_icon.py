from PIL import Image, ImageDraw
S=1024
img=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(img)
# background: rounded square, teal gradient
bg=Image.new('RGBA',(S,S))
for y in range(S):
    t=y/S; c=(int(14+10*t),int(92-22*t),int(96-18*t),255)
    ImageDraw.Draw(bg).line([(0,y),(S,y)],fill=c)
mask=Image.new('L',(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([32,32,S-32,S-32],radius=210,fill=255)
img.paste(bg,(0,0),mask)
d=ImageDraw.Draw(img)
cream=(250,243,224,255); shade=(226,214,184,255); gold=(232,176,60,255); ink=(18,80,84,255)
# open book: two pages with slight perspective
L=[(512,300),(150,350),(150,760),(512,720)]
R=[(512,300),(874,350),(874,760),(512,720)]
d.polygon(L,fill=cream); d.polygon(R,fill=shade)
# book spine highlight and cover edge
d.polygon([(512,720),(150,760),(150,800),(512,764)],fill=gold)
d.polygon([(512,720),(874,760),(874,800),(512,764)],fill=gold)
d.line([(512,300),(512,722)],fill=(190,176,140,255),width=6)
# verse lines on the left page: two columns of hemistichs (a bayt per row), right page: scan lines
def bar(x0,y0,x1,y1,col): d.rounded_rectangle([x0,y0,x1,y1],radius=(y1-y0)//2,fill=col)
for i in range(5):
    y=400+i*64
    bar(190,y,330,y+26,ink); bar(352,y,470,y+26,ink)
for i in range(5):
    y=400+i*64
    w=[330,300,340,280,320][i]
    bar(560,y,560+w*0.9,y+26,(150,138,104,255))
# gold sparkle (a verse star) above the book
cx,cy,r=512,205,92
pts=[]
import math
for k in range(8):
    ang=math.pi/4*k-math.pi/2; rad=r if k%2==0 else r*0.42
    pts.append((cx+rad*math.cos(ang),cy+rad*math.sin(ang)))
d.polygon(pts,fill=gold)
img.save('icon/app-1024.png')
sizes=[16,24,32,48,64,128,256]
imgs=[img.resize((s,s),Image.LANCZOS) for s in sizes]
imgs[-1].save('icon/app.ico',format='ICO',sizes=[(s,s) for s in sizes],append_images=imgs[:-1])
print('ok')
