import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from bclib import *
def posecv(p):
    cv = Canvas(); k = POSES[p]; cv.put(k[0], k[1], k[2], False); return cv.c
def diff(a, b): return int(np.sum(a != b))
for t in range(6):
    c = res[2][t]; dl = seq_deltas(c); seqs = c['seqs']
    keys = [k & 0x7f for k, _ in seqs]
    print('==', NAMES[t])
    for i in range(len(seqs)):
        fr = play(seqs, None, dl, i)
        last = fr[-1]
        best = sorted(((diff(last, posecv(p)), p) for p in range(47)))[:2]
        print(f'  seq {i:2d} key {keys[i]:2d} len {len(seqs[i][1]):2d} -> last frame closest pose {best[0][1]} (diff {best[0][0]}), next {best[1][1]} ({best[1][0]}); diff to own key {diff(last, posecv(keys[i]))}')
