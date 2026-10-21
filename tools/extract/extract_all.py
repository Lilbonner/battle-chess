import sys, os, pickle
sys.path.insert(0, sys.argv[1]); import bcfmt
ex = sys.argv[2]
data = open(os.path.join(ex, 'app', 'DATA_0.bin'), 'rb').read()
offs = [int.from_bytes(data[o:o+4], 'big') for o in range(0xed8, 0x1090, 4)]
split = offs.index(0x23f)
lists = {1: offs[:split], 2: [0] + offs[split:]}
result = {}
for n in (1, 2):
    d = open(os.path.join(ex, 'vol', f'ALLCANM{n}.data'), 'rb').read()
    bounds = sorted(set(lists[n])) + [len(d)]
    chunks = []; i = 0
    while i < len(bounds) - 1:
        a = bounds[i]; b = bounds[i + 1]
        c = None
        for parser in (bcfmt.parse_seq, bcfmt.parse_sef):
            try:
                t = parser(d, a)
                if t and t['stream'] == b: c = t; break
            except Exception: pass
        if c:
            c['chunk_end'] = bounds[i + 2] if i + 2 < len(bounds) else len(d)
            chunks.append(c); i += 2; continue
        raw = d[a:b]
        if raw.startswith(b'this is just a space holder'):
            chunks.append({'kind': 'pad', 'start': a}); i += 1; continue
        try:
            dec = bcfmt.Dec(d, a); out = dec.read(dec.size)
            chunks.append({'kind': 'raw', 'start': a, 'data': out, 'extra': dec.extra, 'end_bit': dec.bitpos, 'next': b})
        except Exception as e:
            chunks.append({'kind': 'unknown', 'start': a, 'len': b - a, 'head': d[a:a+16].hex()})
        i += 1
    result[n] = chunks
    for k, c in enumerate(chunks):
        if c['kind'] == 'seq':
            info = f"recs {len(c['recs'])} seqs {len(c['seqs'])} flags {[s[0] for s in c['seqs']][:12]}"
        elif c['kind'] == 'sef': info = f"recs {len(c['recs'])}"
        elif c['kind'] == 'raw': info = f"size {len(c['data'])} extra {c['extra']:#x} first {c['data'][:8].hex()} endbyte {(c['end_bit']+7)//8:#x}/{c['next']:#x}"
        else: info = str({k2: v for k2, v in c.items() if k2 != 'kind'})
        print(f"F{n} #{k:2} {c['kind']:7} {c['start']:#8x} {info}")
pickle.dump(result, open(os.path.join(ex, 'chunks.pkl'), 'wb'))
