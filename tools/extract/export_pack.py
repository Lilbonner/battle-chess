# Экспорт оригинальных ресурсов Battle Chess (Mac, B&W 1.0.2) в папку игры — только для личного использования.
import sys, os, pickle, json, shutil, glob
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
import bcfmt
ex, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
res = pickle.load(open(os.path.join(ex, 'chunks.pkl'), 'rb'))
bases = pickle.load(open(os.path.join(ex, 'bases.pkl'), 'rb'))
d1 = open(os.path.join(ex, 'vol', 'ALLCANM1.data'), 'rb').read()
sef = bcfmt.parse_sef(d1, 0)

def unpack(data, w, h):
    bpr = (w + 3) >> 2
    a = np.frombuffer(bytes(data[:bpr*h]), dtype=np.uint8).reshape(h, bpr)
    return np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(h, bpr*4)[:, :w]

PALS = {
    'w': {0: (0, 0, 0, 255), 1: (255, 255, 255, 255), 2: (236, 226, 200, 255), 3: (0, 0, 0, 0)},
    'b': {0: (0, 0, 0, 255), 1: (172, 172, 184, 255), 2: (44, 40, 50, 255), 3: (0, 0, 0, 0)},
}
def save_idx(arr, path_base):
    for side, pal in PALS.items():
        lut = np.array([pal[i] for i in range(4)], np.uint8)
        Image.fromarray(lut[arr], 'RGBA').save(f'{path_base}_{side}.png')

man = {'poses': {}, 'walk': {}, 'sounds': {}}
# стоячие позы
os.makedirs(os.path.join(out, 'pose'), exist_ok=True)
q = 0
for i, (w, h, hx, hy) in enumerate(sef['recs']):
    arr = unpack(sef['data'][q:], w, h); q += ((w + 3) >> 2) * h
    save_idx(arr, os.path.join(out, 'pose', f'p{i}'))
    man['poses'][i] = {'ax': hx, 'ay': hy}

# циклы ходьбы: для каждой позы — самая длинная последовательность с надёжно найденной базой
W, H, X, Y = 240, 220, 120, 180
best_seq = {}
for (n, k, si), cand in bases.items():
    if n != 2 or not cand: continue
    sc, b = cand[0]
    c = res[n][k]; flag, idxs = c['seqs'][si]
    if sc > 30 or flag - 128 != b and not (k == 3): continue
    L = len(idxs)
    if L < 6: continue
    if b not in best_seq or L > best_seq[b][0]: best_seq[b] = (L, n, k, si)
os.makedirs(os.path.join(out, 'walk'), exist_ok=True)
q = 0; base_arr = []
for w, h, hx, hy in sef['recs']:
    base_arr.append((unpack(sef['data'][q:], w, h), hx, hy)); q += ((w + 3) >> 2) * h
for b, (L, n, k, si) in sorted(best_seq.items()):
    c = res[n][k]; flag, idxs = c['seqs'][si]
    canvas = np.full((H, W), 3, np.uint8)
    arr, hx, hy = base_arr[b]
    m = arr != 3; canvas[Y-hy:Y-hy+arr.shape[0], X-hx:X-hx+arr.shape[1]][m] = arr[m]
    frames = []
    for fi in idxs:
        if fi >= len(c['recs']) or not c['frames'][fi]: continue
        r = c['recs'][fi]; f = unpack(c['frames'][fi][0], r[0], r[1])
        canvas[Y-r[3]:Y-r[3]+f.shape[0], X-r[2]:X-r[2]+f.shape[1]] ^= f
        ys, xs = np.nonzero(canvas != 3)
        if len(ys) == 0: continue
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        j = len(frames)
        save_idx(canvas[y0:y1, x0:x1].copy(), os.path.join(out, 'walk', f'w{b}_{j}'))
        frames.append({'ax': int(X - x0), 'ay': int(Y - y0)})
    man['walk'][b] = frames
    print('walk pose', b, 'frames', len(frames), 'from', (n, k, si, flag))

# экраны
for n, k, name in ((1, 18, 'board3d'), (1, 19, 'board2d'), (1, 21, 'title')):
    a = np.frombuffer(res[n][k]['data'], np.uint8).reshape(340, 128)
    px = np.stack([(a >> 6) & 3, (a >> 4) & 3, (a >> 2) & 3, a & 3], axis=2).reshape(340, 512)
    lut = np.array([(0, 0, 0), (255, 255, 255), (150, 150, 150), (0, 0, 0)], np.uint8)
    Image.fromarray(lut[px], 'RGB').save(os.path.join(out, name + '.png'))

# звуки (роли подобраны по длительности и тембру)
roles = {'stab': 3, 'slash': 4, 'thunder': 5, 'die': 6, 'clang': 7, 'magic': 8, 'groan': 9, 'zap': 10,
         'charge': 11, 'crunch': 12, 'shatter': 13, 'hit': 14, 'burn': 15, 'smash': 16, 'thud': 17}
os.makedirs(os.path.join(out, 'sound'), exist_ok=True)
for role, idx in roles.items():
    src = glob.glob(os.path.join(ex, 'assets', f'f1_{idx:02d}_snd_*.wav'))[0]
    shutil.copy(src, os.path.join(out, 'sound', role + '.wav'))
    man['sounds'][role] = f'sound/{role}.wav'

# геометрия доски (центры клеток, ряд 0 — ближний)
man['grid'] = {'cx': 254.4,
               'rows': [{'y': y, 'pitch': p} for y, p in zip(
                   [301.1, 263.1, 228.1, 195.6, 165.6, 137.6, 111.5, 87.5],
                   [59.2, 56.8, 54.2, 52.1, 50.3, 48.4, 47.2, 45.7])]}
man['pieces'] = {
    'pawn':   {'dirs': 8, 'first': 16},
    'knight': {'dirs': 4, 'first': 12},
    'bishop': {'dirs': 4, 'first': 0, 'diagonal': True},
    'rook':   {'dirs': 4, 'first': 32, 'idle': 36},
    'queen':  {'dirs': 8, 'first': 24},
    'king':   {'dirs': 8, 'first': 4},
}
json.dump(man, open(os.path.join(out, 'manifest.json'), 'w'), indent=1)
open(os.path.join(out, 'README.txt'), 'w', encoding='utf-8').write(
    'Графика и звуки из оригинальной Battle Chess (c) Interplay, извлечены с личного диска пользователя.\n'
    'Только для личного использования. Не распространять.\n')
print('done')
