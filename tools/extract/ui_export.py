# Экспорт элементов интерфейса оригинальной Battle Chess для пиксельной панели:
# логотип (из заставки), картинки диалога превращения пешки (PICT 400..403).
import sys, os
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
import macresources
from pict_dump import decode

vol, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

title = Image.open(os.path.join(os.path.dirname(out.rstrip('/\\')), 'title.png')).convert('L')
for name, box in (('logo_battle', (14, 0, 256, 79)), ('logo_chess', (258, 0, 490, 79))):
    w = title.crop(box)
    W, H = round(w.width * 0.6), round(w.height * 0.6)
    a = np.array(w.resize((W, H), Image.BOX)).astype(int)
    yy, xx = np.mgrid[0:H, 0:W]
    o = np.where(a < 75, 0, np.where(a < 200, np.where((xx + yy) % 2 == 0, 0, 255), 255)).astype(np.uint8)
    img = Image.fromarray(o)
    # обрезать пустые поля
    bb = Image.fromarray(255 - o).getbbox()
    img.crop(bb).save(os.path.join(out, name + '.png'))

names = {400: 'promo_B', 401: 'promo_N', 402: 'promo_Q', 403: 'promo_R'}
for r in macresources.parse_file(open(os.path.join(vol, 'Battle Chess.rsrc'), 'rb').read()):
    if r.type == b'PICT' and r.id in names:
        _, img = decode(bytes(r))[0]
        img.convert('L').save(os.path.join(out, names[r.id] + '.png'))
print('ok')
