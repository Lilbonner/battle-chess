import sys, json, os
import numpy as np
from PIL import Image
d, outp, step = sys.argv[1], sys.argv[2], int(sys.argv[3])
sc = json.load(open(os.path.join(d, 'scene.json')))
PW = np.array([(0,0,0,255),(255,255,255,255),(236,226,200,255),(0,0,0,0)], np.uint8)
PB = np.array([(0,0,0,255),(172,172,184,255),(44,40,50,255),(0,0,0,0)], np.uint8)
imgs = [np.array(Image.open(os.path.join(d, f'{i}.png'))) for i in range(len(sc['images']))]
W, H, AX, AY = 300, 230, 150, 200
tiles = []
for t in range(0, sc['ticks'], step):
    can = Image.new('RGBA', (W, H), (90, 90, 90, 255))
    for key, pal in (('killer', PB), ('victim', PW)):
        i = sc[key][t] if t < len(sc[key]) else -1
        if i < 0: continue
        ax, ay = sc['images'][i]
        im = Image.fromarray(pal[imgs[i]], 'RGBA')
        can.paste(im, (AX - ax, AY - ay), im)
    tiles.append(can)
cols = 6; rows = (len(tiles) + cols - 1) // cols
sh = Image.new('RGBA', (W*cols, H*rows), (0,0,0,255))
for k, t in enumerate(tiles): sh.paste(t, ((k % cols)*W, (k//cols)*H))
sh.save(outp); print(len(tiles), 'tiles', [s for s in sc['sounds']][:10])
