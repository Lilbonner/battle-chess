import sys, pickle
sys.path.insert(0, '.')
import bcfmt
import numpy as np
res = pickle.load(open(sys.argv[1], 'rb'))
d1 = open(sys.argv[2], 'rb').read()
sef = bcfmt.parse_sef(d1, 0)
def unpack(data, w, h):
    bpr = (w + 3) >> 2
    a = np.frombuffer(data[:bpr*h], dtype=np.uint8).reshape(h, bpr)
    px = np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(h, bpr*4)
    return px[:, :w]
base_imgs = []; q = 0
for w, h, hx, hy in sef['recs']:
    base_imgs.append((unpack(sef['data'][q:], w, h), hx, hy)); q += ((w + 3) >> 2) * h
W, H, X, Y = 200, 200, 100, 160
def place(canvas, img, hx, hy, xor):
    h, w = img.shape
    y0, x0 = Y - hy, X - hx
    if xor: canvas[y0:y0+h, x0:x0+w] ^= img
    else:
        m = img != 3; canvas[y0:y0+h, x0:x0+w][m] = img[m]
def score(canvas):
    t = canvas == 3
    hole = t[1:-1, 1:-1] & ~t[:-2, 1:-1] & ~t[2:, 1:-1] & ~t[1:-1, :-2] & ~t[1:-1, 2:]
    return int(hole.sum())
out = {}
for n, chunks in res.items():
    for k, c in enumerate(chunks):
        if c['kind'] != 'seq' or not all(s[0] >= 128 for s in c['seqs']): continue
        frames = [unpack(f[0], r[0], r[1]) if f else None for r, f in zip(c['recs'], c['frames'])]
        for si, (flag, idxs) in enumerate(c['seqs']):
            best = []
            for b, (bi, bhx, bhy) in enumerate(base_imgs):
                canvas = np.full((H, W), 3, np.uint8); place(canvas, bi, bhx, bhy, False)
                sc = 0; ok = True
                for fi in idxs:
                    if fi >= len(frames) or frames[fi] is None: continue
                    r = c['recs'][fi]
                    try: place(canvas, frames[fi], r[2], r[3], True)
                    except Exception: ok = False; break
                    sc += score(canvas)
                if ok: best.append((sc, b))
            best.sort()
            out[(n, k, si)] = best[:3]
            print(f'F{n}#{k} seq{si} flag {flag} len {len(idxs)} best {best[:3]}')
pickle.dump(out, open(sys.argv[3], 'wb'))
