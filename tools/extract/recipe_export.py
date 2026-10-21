# Симуляция боевого движка Battle Chess (HANDLECOMBAT + CHECKTIME) и выгрузка кадров поединков.
import sys, os, pickle, json
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
import bcfmt
ex, out = sys.argv[1], sys.argv[2]
app = os.path.join(ex, 'app')
res = pickle.load(open(os.path.join(ex, 'chunks.pkl'), 'rb'))
tabs = pickle.load(open(os.path.join(app, 'combat_tables.pkl'), 'rb'))
G = 0x6c54; img = open(os.path.join(app, 'globals.bin'), 'rb').read()
def L(off): p = G + off; return int.from_bytes(img[p:p+4], 'big')
d1 = open(os.path.join(ex, 'vol', 'ALLCANM1.data'), 'rb').read()
sef0 = bcfmt.parse_sef(d1, 0)

def unpack(data, w, h):
    bpr = (w + 3) >> 2
    a = np.frombuffer(bytes(data[:bpr*h]), dtype=np.uint8).reshape(h, bpr)
    return np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(h, bpr*4)[:, :w]

def sef_frames(recs, data):
    fr = []; q = 0
    for w, h, hx, hy in recs:
        fr.append((unpack(data[q:], w, h), hx - 256 if hx > 127 else hx, hy - 256 if hy > 127 else hy)); q += ((w + 3) >> 2) * h
    return fr
POSES = sef_frames(sef0['recs'], sef0['data'])

def chunk_at(offset, file_hint):
    for n in ([file_hint] + [1, 2]):
        for c in res[n]:
            if c.get('start') == offset and c['kind'] in ('seq', 'sef'): return c
    raise KeyError(hex(offset))

def entry(v):
    k = v - 3
    return L(-0x27a2 + 8*k)

V_ATT = {0: 9, 1: -5, 2: 13, 3: 15, 4: -1, 5: 19}
V_DEF = {0: -7, 1: -3, 2: 25, 3: 27, 4: 1, 5: 31}

def load_side(v):
    fh = 2 if v >= 3 else 1
    seq = chunk_at(entry(v), fh); sef = chunk_at(entry(v + 1), fh)
    assert seq['kind'] == 'seq' and sef['kind'] == 'sef', (v, seq['kind'], sef['kind'])
    keys = sef_frames(sef['recs'], sef['data'])
    deltas = []
    for r, f in zip(seq['recs'], seq['frames']):
        if not f: deltas.append(None); continue
        hx, hy = r[2] - 256 if r[2] > 127 else r[2], r[3] - 256 if r[3] > 127 else r[3]
        deltas.append((unpack(f[0], r[0], r[1]), hx, hy))
    return seq['seqs'], keys, deltas

CW, CH, AX, AY = 520, 360, 260, 300   # холст с точкой привязки (клетка взятия)

def place(canvas, im, hx, hy, xor):
    h, w = im.shape; y0, x0 = AY - hy, AX - hx
    sub = canvas[y0:y0+h, x0:x0+w]
    if xor: sub ^= im
    else: sub[:] = 3; m = im != 3; sub[m] = im[m]

class Side:
    def __init__(self, v, playlist, script):
        self.seqs, self.keys, self.deltas = load_side(v)
        self.pl = list(playlist); self.pi = 0
        self.script = list(script) + [0xff]; self.sp = 0
        self.delay = 0; self.cur = None; self.ci = 0
        self.canvas = np.full((CH, CW), 3, np.uint8); self.done = False; self.shown = False
    def check_time(self, sounds, tick):
        if self.delay > 0: self.delay -= 1; return False
        while self.script[self.sp] > 0x7f and self.script[self.sp] != 0xff:
            sounds.append([tick, self.script[self.sp] & 0xf, self.script[self.sp + 1] - 0x40, self.side_id]); self.sp += 2
        b = self.script[self.sp]
        if b == 0xff: self.delay = 0
        else: self.delay = b; self.sp += 1
        return True
    def step(self):
        if self.done: return False
        if self.cur is None:
            if self.pi >= len(self.pl): self.done = True; return False
            sid = self.pl[self.pi]; self.pi += 1
            flag, frames = self.seqs[sid]
            if flag & 0x80: key = POSES[flag & 0x7f]
            else: key = self.keys[flag]
            self.canvas[:] = 3
            place(self.canvas, key[0], key[1], key[2], False)
            self.cur = frames; self.ci = 0
            if not self.cur: self.cur = None
            self.shown = True
            return True
        f = self.deltas[self.cur[self.ci]]
        if f is not None: place(self.canvas, f[0], f[1], f[2], True)
        self.ci += 1
        if self.ci >= len(self.cur): self.cur = None
        return True


# ---- новые бои по рецептам исследования: плейлисты и скрипты в формате оригинального движка ----
recipes = json.load(open(sys.argv[3], encoding='utf-8'))['recipes']
names = 'BKNPQR'
full = {'bishop': 'B', 'king': 'K', 'knight': 'N', 'pawn': 'P', 'queen': 'Q', 'rook': 'R'}
count = {}
for rc in recipes:
    vt = names.index(full[rc['victim']]); kt = names.index(full[rc['killer']])
    tag = names[vt] + names[kt]
    count[tag] = count.get(tag, 0) + 1
    att = Side(V_ATT[vt], rc['victim_playlist'], [b for b in rc['victim_script'] if b != 0xff])
    de = Side(V_DEF[kt], rc['killer_playlist'], [b for b in rc['killer_script'] if b != 0xff])
    att.side_id = 0; de.side_id = 1
    sounds = []; frames = {'att': [], 'def': []}; uniq = {}; imgs = []; tick = 0
    while not (att.done and de.done) and tick < 3000:
        for key, s in (('att', att), ('def', de)):
            if not s.done and s.check_time(sounds, tick): s.step()
            if s.shown:
                ys, xs = np.nonzero(s.canvas != 3)
                if len(ys) == 0: frames[key].append(-1); continue
                y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
                crop = s.canvas[y0:y1, x0:x1]
                h = (crop.tobytes(), crop.shape, x0, y0)
                if h not in uniq:
                    uniq[h] = len(imgs); imgs.append((crop.copy(), int(AX - x0), int(AY - y0)))
                frames[key].append(uniq[h])
            else: frames[key].append(-1)
        tick += 1
    dd = os.path.join(out, 'combat', f'{tag}_r{count[tag]}'); os.makedirs(dd, exist_ok=True)
    meta = []
    for i, (im, axx, ayy) in enumerate(imgs):
        Image.fromarray(im, 'L').save(os.path.join(dd, f'{i}.png'), optimize=True)
        meta.append([axx, ayy])
    # момент гибели/удара — как для оригинальных сцен
    vt_tops = [meta[i][1] if i >= 0 else 0 for i in frames['att']]
    ref = float(np.median([t for t in vt_tops[:12] if t > 0] or [1]))
    D = max(0, max([t for t, v in enumerate(vt_tops) if v >= 0.85 * ref] or [0]) - 3)
    cands = [s[0] for s in sounds if s[3] == 0 and D - 10 <= s[0] <= D + 2]
    json.dump({'ticks': tick, 'victim': frames['att'], 'killer': frames['def'], 'sounds': sounds, 'images': meta,
               'impact': min(cands) if cands else D, 'death': D, 'recipe': rc['id'], 'family': rc['family'],
               'confidence': rc['confidence'], 'description': rc['description']},
              open(os.path.join(dd, 'scene.json'), 'w'))
    print(rc['id'], '->', os.path.basename(dd), 'ticks', tick, 'images', len(imgs), rc['family'], rc['confidence'])
