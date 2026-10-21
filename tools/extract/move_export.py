# Экспорт всех последовательностей движения (ходьба, повороты, превращение ладьи) из ALLCANM2, блоки 0..5 = B,K,N,P,Q,R.
import sys, os, pickle, json, shutil
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__)); import bcfmt
ex, out, mvjson = sys.argv[1], sys.argv[2], sys.argv[3]
res = pickle.load(open(os.path.join(ex, 'chunks.pkl'), 'rb'))
d1 = open(os.path.join(ex, 'vol', 'ALLCANM1.data'), 'rb').read()
sef0 = bcfmt.parse_sef(d1, 0)
def unpack(data, w, h):
    bpr = (w + 3) >> 2
    a = np.frombuffer(bytes(data[:bpr*h]), dtype=np.uint8).reshape(h, bpr)
    return np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(h, bpr*4)[:, :w]
sgn = lambda v: v - 256 if v > 127 else v
POSES = []; q = 0
for w, h, hx, hy in sef0['recs']:
    POSES.append((unpack(sef0['data'][q:], w, h), sgn(hx), sgn(hy))); q += ((w + 3) >> 2) * h
W, H, AX, AY = 360, 360, 180, 280
letters = 'BKNPQR'
for k, L in enumerate(letters):
    c = res[2][k]; assert c['kind'] == 'seq'
    dd = os.path.join(out, 'move', L); os.makedirs(dd, exist_ok=True)
    meta = {}
    for sid, (flag, idxs) in enumerate(c['seqs']):
        canvas = np.full((H, W), 3, np.uint8)
        key = POSES[flag & 0x7f] if flag & 0x80 else None
        if key is None: continue
        im, hx, hy = key
        m = im != 3; canvas[AY-hy:AY-hy+im.shape[0], AX-hx:AX-hx+im.shape[1]][m] = im[m]
        frames = []
        def snap():
            ys, xs = np.nonzero(canvas != 3)
            y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
            j = len(frames)
            Image.fromarray(canvas[y0:y1, x0:x1].copy(), 'L').save(os.path.join(dd, f'{sid}_{j}.png'), optimize=True)
            frames.append([int(AX - x0), int(AY - y0)])
        snap()
        for fi in idxs:
            r = c['recs'][fi]; f = c['frames'][fi]
            if f:
                dlt = unpack(f[0], r[0], r[1])
                canvas[AY-sgn(r[3]):AY-sgn(r[3])+dlt.shape[0], AX-sgn(r[2]):AX-sgn(r[2])+dlt.shape[1]] ^= dlt
            snap()
        meta[sid] = {'key_pose': flag & 0x7f, 'frames': frames}
    json.dump(meta, open(os.path.join(dd, 'seqs.json'), 'w'))
    print(L, 'seqs', len(meta), 'frames', sum(len(v['frames']) for v in meta.values()))
shutil.copy(mvjson, os.path.join(out, 'movement.json'))
