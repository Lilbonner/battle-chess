# Combat simulator (HANDLECOMBAT + CHECKTIME semantics) producing per-tick canvases and seq annotations.
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from bclib import *

class Side:
    def __init__(self, v, playlist, script, dy=0):
        self.seq, self.keys, self.deltas = load_side(v)
        self.seqs = self.seq['seqs']
        self.pl = list(playlist); self.pi = 0
        self.script = list(script) + [0xff]; self.sp = 0
        self.delay = 0; self.cur = None; self.ci = 0; self.sid = None
        self.cv = Canvas(); self.done = False; self.shown = False; self.dy = dy
        self.log = []   # per tick: (sid, frame index within seq) or None
    def check_time(self, sounds, tick):
        if self.delay > 0: self.delay -= 1; return False
        while self.script[self.sp] > 0x7f and self.script[self.sp] != 0xff:
            sounds.append((tick, self.script[self.sp] & 0xf, self.script[self.sp + 1] - 0x40)); self.sp += 2
        b = self.script[self.sp]
        if b == 0xff: self.delay = 0
        else: self.delay = b; self.sp += 1
        return True
    def step(self):
        if self.done: return
        if self.cur is None:
            if self.pi >= len(self.pl): self.done = True; return
            self.sid = self.pl[self.pi]; self.pi += 1
            flag, frames = self.seqs[self.sid]
            key = POSES[flag & 0x7f] if flag & 0x80 else self.keys[flag]
            self.cv.c[:] = 3; self.cv.put(key[0], key[1], key[2], False, 0, self.dy)
            self.cur = frames; self.ci = 0
            if not self.cur: self.cur = None
            self.shown = True; self.fidx = 0
            return
        f = self.deltas[self.cur[self.ci]]
        if f is not None: self.cv.put(f[0], f[1], f[2], True, 0, self.dy)
        self.ci += 1; self.fidx = self.ci
        if self.ci >= len(self.cur): self.cur = None

def simulate(vt, kt, vpl=None, kpl=None, vsc=None, ksc=None, maxt=3000):
    T01, T23 = tabs['T01'], tabs['T23']
    vpl = T01[vt*6+kt] if vpl is None else vpl; kpl = T01[36+kt*6+vt] if kpl is None else kpl
    vsc = T23[vt*6+kt] if vsc is None else vsc; ksc = T23[36+kt*6+vt] if ksc is None else ksc
    V = Side(V_VICTIM[vt], vpl, vsc, dy=1); K = Side(V_KILLER[kt], kpl, ksc, dy=0)
    sounds = []; ticks = []
    t = 0
    while not (V.done and K.done) and t < maxt:
        rec = {}
        for name, s in (('V', V), ('K', K)):
            if not s.done and s.check_time(sounds, t): s.step()
            rec[name] = (s.sid, getattr(s, 'fidx', 0)) if (s.shown and not s.done) else (None, None)
        comp = np.full((CH, CW), 3, np.uint8)
        for s in (K, V):  # killer behind, victim (y+1) in front
            if s.shown:
                m = s.cv.c != 3; comp[m] = s.cv.c[m]
        ticks.append((rec, comp, K.cv.c.copy(), V.cv.c.copy()))
        t += 1
    return ticks, sounds
