import pickle, sys, os
R = os.environ.get('BC_WORK', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'work'))
res = pickle.load(open(os.path.join(R, 'bcextract', 'chunks.pkl'), 'rb'))
for t in range(6):
    c = res[2][t]
    print('chunk', t, c['kind'], 'start', hex(c['start']), 'nrecs', len(c['recs']), 'nseq', len(c['seqs']))
    for i, (k, fr) in enumerate(c['seqs']):
        print(f'  seq {i:2d} key={k:3d} ({"pose %d" % (k & 0x7f) if k & 0x80 else "sef %d" % k}) len={len(fr)} frames={fr[:3]}..{fr[-2:] if fr else ""}')
