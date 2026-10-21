# Shared helpers: load poses, movement chunks, combat chunks; composite sequences; contact sheets.
import sys, os, pickle
import numpy as np
from PIL import Image, ImageDraw
R = os.environ.get('BC_WORK', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'work'))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'extract'))
import bcfmt
EX = os.path.join(R, 'bcextract'); APP = os.path.join(EX, 'app')
res = pickle.load(open(os.path.join(EX, 'chunks.pkl'), 'rb'))
tabs = pickle.load(open(os.path.join(APP, 'combat_tables.pkl'), 'rb'))
G = 0x6c54; GIMG = open(os.path.join(APP, 'globals.bin'), 'rb').read()
def L(off): p = G + off; return int.from_bytes(GIMG[p:p+4], 'big')
def W(off, signed=True): p = G + off; return int.from_bytes(GIMG[p:p+2], 'big', signed=signed)
d1 = open(os.path.join(EX, 'vol', 'ALLCANM1.data'), 'rb').read()
sef0 = bcfmt.parse_sef(d1, 0)
NAMES = 'BKNPQR'
FULL = {'B': 'bishop', 'K': 'king', 'N': 'knight', 'P': 'pawn', 'Q': 'queen', 'R': 'rook'}

def s8(v): return v - 256 if v > 127 else v

def unpack(data, w, h):
    bpr = (w + 3) >> 2
    a = np.frombuffer(bytes(data[:bpr*h]), dtype=np.uint8).reshape(h, bpr)
    return np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(h, bpr*4)[:, :w]

def sef_frames(recs, data):
    fr = []; q = 0
    for w, h, hx, hy in recs:
        fr.append((unpack(data[q:], w, h), s8(hx), s8(hy))); q += ((w + 3) >> 2) * h
    return fr
POSES = sef_frames(sef0['recs'], sef0['data'])

def seq_deltas(seq):
    out = []
    for r, f in zip(seq['recs'], seq['frames']):
        if not f: out.append(None); continue
        out.append((unpack(f[0], r[0], r[1]), s8(r[2]), s8(r[3])))
    return out

def chunk_at(offset, file_hint):
    for n in ([file_hint] + [1, 2]):
        for c in res[n]:
            if c.get('start') == offset and c['kind'] in ('seq', 'sef'): return c
    raise KeyError(hex(offset))

def entry(v): return L(-0x27a2 + 8*(v - 3))
V_VICTIM = {0: 9, 1: -5, 2: 13, 3: 15, 4: -1, 5: 19}   # "attacker" table side = victim
V_KILLER = {0: -7, 1: -3, 2: 25, 3: 27, 4: 1, 5: 31}

def load_side(v):
    fh = 2 if v >= 3 else 1
    seq = chunk_at(entry(v), fh); sef = chunk_at(entry(v + 1), fh)
    return seq, sef_frames(sef['recs'], sef['data']), seq_deltas(seq)

CW, CH, AX, AY = 560, 400, 280, 320

class Canvas:
    def __init__(self):
        self.c = np.full((CH, CW), 3, np.uint8)
    def put(self, im, hx, hy, xor, dx=0, dy=0):
        h, w = im.shape; y0, x0 = AY + dy - hy, AX + dx - hx
        sub = self.c[y0:y0+h, x0:x0+w]
        if xor: sub ^= im
        else: sub[:] = 3; m = im != 3; sub[m] = im[m]

def play(seqs, keys, deltas, sid, dx=0, dy=0, canvas=None):
    """Returns list of canvases (copies) for key + each delta of sequence sid."""
    flag, frames = seqs[sid]
    key = POSES[flag & 0x7f] if flag & 0x80 else keys[flag]
    cv = canvas or Canvas()
    if canvas is None: cv.c[:] = 3
    cv.put(key[0], key[1], key[2], False, dx, dy)
    out = [cv.c.copy()]
    for fi in frames:
        f = deltas[fi]
        if f is not None: cv.put(f[0], f[1], f[2], True, dx, dy)
        out.append(cv.c.copy())
    return out

PAL = {0: (0, 0, 0), 1: (255, 255, 255), 2: (150, 150, 150), 3: (40, 110, 60)}
def to_rgb(a):
    rgb = np.zeros(a.shape + (3,), np.uint8)
    for k, v in PAL.items(): rgb[a == k] = v
    return rgb

def bbox(arrs):
    ys0, ys1, xs0, xs1 = CH, 0, CW, 0
    for a in arrs:
        ys, xs = np.nonzero(a != 3)
        if len(ys):
            ys0 = min(ys0, ys.min()); ys1 = max(ys1, ys.max() + 1); xs0 = min(xs0, xs.min()); xs1 = max(xs1, xs.max() + 1)
    if ys1 == 0: return 0, 1, 0, 1
    return ys0, ys1, xs0, xs1

def sheet(rows, path, cols=12, scale=1, label_rows=None):
    """rows: list of (label, [canvas arrays]). Frames cropped to common bbox per row."""
    tiles = []
    for label, frames in rows:
        y0, y1, x0, x1 = bbox(frames)
        y0 = max(0, y0 - 2); x0 = max(0, x0 - 2); y1 += 2; x1 += 2
        crops = [to_rgb(f[y0:y1, x0:x1]) for f in frames]
        # mark anchor
        tiles.append((label, crops, (AX - x0, AY - y0)))
    W_ = 0; H_ = 0; lines = []
    for label, crops, anc in tiles:
        h, w = crops[0].shape[:2]
        n = len(crops); c = min(cols, n); r = (n + c - 1) // c
        lines.append((label, crops, anc, w, h, c, r))
        W_ = max(W_, c * (w + 2) + 2); H_ += 14 + r * (h + 2)
    img = Image.new('RGB', (max(W_, 300), H_ + 2), (230, 230, 230)); dr = ImageDraw.Draw(img)
    y = 0
    for label, crops, anc, w, h, c, r in lines:
        dr.text((2, y + 1), label, fill=(0, 0, 0)); y += 14
        for i, cr in enumerate(crops):
            xx = 2 + (i % c) * (w + 2); yy = y + (i // c) * (h + 2)
            img.paste(Image.fromarray(cr), (xx, yy))
            ax, ay = anc
            if 0 <= ax < w and 0 <= ay < h:
                dr.point((xx + ax, yy + ay), fill=(255, 0, 0))
            dr.text((xx + 1, yy + 1), str(i), fill=(255, 255, 0))
        y += r * (h + 2)
    if scale != 1: img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    img.save(path)
