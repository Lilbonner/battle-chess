import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from bclib import *
T01, T23 = tabs['T01'], tabs['T23']
for side, V in (('victim', V_VICTIM), ('killer', V_KILLER)):
    for t in range(6):
        seq, keys, dl = load_side(V[t])
        uses = {}
        for o in range(6):
            pl = T01[t*6+o] if side == 'victim' else T01[36 + t*6 + o]
            for s in pl: uses.setdefault(s, []).append(NAMES[o])
        print(f'== {side} {NAMES[t]} v={V[t]} nseq={len(seq["seqs"])} nkeys={len(keys)} nrecs={len(seq["recs"])}')
        for i, (k, fr) in enumerate(seq['seqs']):
            print(f'   seq {i:2d} key {"pose %d" % (k&0x7f) if k & 0x80 else "sef %d" % k:8s} len {len(fr):3d} used-vs {"".join(uses.get(i, []))}')
print()
for v in range(6):
    for k in range(6):
        A = T01[v*6+k]; D = T01[36+k*6+v]
        print(f'{NAMES[v]}{NAMES[k]} victim pl {list(A)} script {list(T23[v*6+k])}')
        print(f'   killer pl {list(D)} script {list(T23[36+k*6+v])}')
