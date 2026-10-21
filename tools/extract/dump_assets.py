import sys, os, pickle, wave, json
from PIL import Image
ex, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
res = pickle.load(open(os.path.join(ex, 'chunks.pkl'), 'rb'))
PAL = {0: (0, 0, 0, 255), 1: (255, 255, 255, 255), 2: (150, 150, 150, 255), 3: (0, 0, 0, 0)}

def img2bpp(data, w, h, bpr=None):
    bpr = bpr or (w + 3) >> 2
    im = Image.new('RGBA', (w, h))
    px = im.load()
    for y in range(h):
        row = data[y*bpr:(y+1)*bpr]
        for x in range(w):
            v = (row[x >> 2] >> (6 - 2*(x & 3))) & 3 if (x >> 2) < len(row) else 3
            px[x, y] = PAL[v]
    return im

def sheet(frames, path, cols=12):
    if not frames: return
    cw = max(f.width for f in frames) + 2; ch = max(f.height for f in frames) + 12
    rows = (len(frames) + cols - 1) // cols
    sh = Image.new('RGBA', (cw*cols, ch*rows), (60, 110, 60, 255))
    for i, f in enumerate(frames): sh.paste(f, ((i % cols)*cw, (i//cols)*ch + 10), f)
    sh.save(path)

meta = {}
for n, chunks in res.items():
    for k, c in enumerate(chunks):
        tag = f'f{n}_{k:02d}'
        if c['kind'] == 'sef':
            q = 0; frames = []
            for w, h, hx, hy in c['recs']:
                sz = ((w + 3) >> 2) * h; frames.append(img2bpp(c['data'][q:q+sz], w, h)); q += sz
            sheet(frames, os.path.join(out, tag + '_sef.png'))
            meta[tag] = {'kind': 'sef', 'recs': c['recs']}
        elif c['kind'] == 'seq':
            frames = [img2bpp(f[0], r[0], r[1]) if f else Image.new('RGBA', (1, 1)) for r, f in zip(c['recs'], c['frames'])]
            sheet(frames, os.path.join(out, tag + '_seq.png'))
            meta[tag] = {'kind': 'seq', 'recs': c['recs'], 'seqs': c['seqs']}
        elif c['kind'] == 'raw':
            dd = c['data']
            if len(dd) == 43520:
                img2bpp(dd, 512, 340, 128).save(os.path.join(out, tag + '_screen.png'))
                meta[tag] = {'kind': 'screen'}
            elif len(dd) == 43280:
                meta[tag] = {'kind': 'sef0'}
            elif dd[4:8] in (b'\x01\x02\x03\x04',) or dd[4:6] in (b'\x80\x80', b'\x86\x86') or True:
                w0 = int.from_bytes(dd[0:2], 'big'); hl = int.from_bytes(dd[2:4], 'big')
                if 20 <= w0 <= 2000 and 4 + hl*2 <= len(dd) + 64:
                    rate = int(4005800 / w0)
                    smp = dd[4:4 + hl*2]
                    with wave.open(os.path.join(out, tag + f'_snd_{rate}hz.wav'), 'wb') as wv:
                        wv.setnchannels(1); wv.setsampwidth(1); wv.setframerate(rate); wv.writeframes(smp)
                    meta[tag] = {'kind': 'sound', 'rate': rate, 'len': len(smp), 'w0': w0}
                else:
                    open(os.path.join(out, tag + '_raw.bin'), 'wb').write(dd)
                    meta[tag] = {'kind': 'raw', 'len': len(dd)}
json.dump(meta, open(os.path.join(out, 'meta.json'), 'w'), indent=1)
for k, v in meta.items(): print(k, v['kind'], {kk: vv for kk, vv in v.items() if kk in ('rate', 'len')}, len(v.get('recs', [])))
