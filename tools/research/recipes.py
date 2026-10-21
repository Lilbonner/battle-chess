# Generate new capture choreographies that follow the original engine rules, validate them and render sheets.
import sys, os, json, pickle
sys.path.insert(0, os.path.dirname(__file__))
from sim import *
from scene_labels import SCENES, ATTACK, KILLER_PORTABLE, SIZE, DEATH_FITS, SIZE_RANK
CONT = pickle.load(open(os.path.join(R, 'research', 'continuity.pkl'), 'rb'))
T01, T23 = tabs['T01'], tabs['T23']
OUT = os.path.join(R, 'research', 'sheets', 'recipes'); os.makedirs(OUT, exist_ok=True)
SEQS = {}
def seqs(side, t):
    k = (side, t)
    if k not in SEQS: SEQS[k] = load_side((V_VICTIM if side == 'victim' else V_KILLER)[t])[0]['seqs']
    return SEQS[k]
def dur(side, t, sid): return 1 + len(seqs(side, t)[sid][1])
def pl_ticks(side, t, pl): return sum(dur(side, t, s) for s in pl)

# ---- scripts as per-step lists: step k = (sound pairs fired before step k, delay byte read at step k)
def script_steps(script, nsteps):
    s = list(script) + [0xff]; p = 0; out = []
    while len(out) < nsteps:
        snd = []
        while s[p] > 0x7f and s[p] != 0xff: snd.append((s[p], s[p+1])); p += 2
        if s[p] != 0xff: d = s[p]; p += 1
        else: d = 0
        out.append((snd, d))
    return out
def build_script(steps):
    while steps and not steps[-1][0] and steps[-1][1] == 0: steps = steps[:-1]
    b = []
    for snd, d in steps:
        for c, pch in snd: b += [c, pch]
        b.append(d)
    return b + [0xff]
def compress(script):
    """Inverse of EXPANDDATA (GOOD): run of n+1 zeros -> byte n (<0x3c); sound pair (c,p) -> (c+1,p). Delays>0 not storable."""
    out = []; i = 0; s = list(script)
    while i < len(s):
        if s[i] == 0xff: out.append(0xff); i += 1; continue
        if s[i] == 0:
            n = 0
            while i + n < len(s) and s[i+n] == 0 and n < 0x3c: n += 1
            out.append(n - 1); i += n; continue
        if s[i] > 0x7f: out += [s[i] + 1, s[i+1]]; i += 2; continue
        raise ValueError('nonzero delay byte')
    return out

def orig(tag):
    vt, kt = NAMES.index(tag[0]), NAMES.index(tag[1])
    return dict(vt=vt, kt=kt, vpl=list(T01[vt*6+kt]), kpl=list(T01[36+kt*6+vt]), vsc=list(T23[vt*6+kt]), ksc=list(T23[36+kt*6+vt]))
def starts(side, t, pl):
    out = []; x = 0
    for s in pl: out.append(x); x += dur(side, t, s)
    return out, x
def react(tag): return SCENES[tag].get('react', SCENES[tag]['imp'])
def impact_tick(tag):
    o = orig(tag); st, _ = starts('victim', o['vt'], o['vpl'])
    return st[(SCENES[tag].get('death_idx') or [react(tag)])[0]]

# ---- continuity
ATTESTED = {}
THR = {}
def attested(side, t):
    if (side, t) not in ATTESTED:
        A = set()
        for o in range(6):
            pl0 = T01[t*6+o] if side == 'victim' else T01[36+t*6+o]
            A |= set(zip(pl0, pl0[1:]))
        ATTESTED[(side, t)] = A
        M = CONT[(side, NAMES[t])]['M0']
        vals = [M[a, b] for a, b in A]
        THR[(side, t)] = max(0.2, float(np.percentile(vals, 75)))
    return ATTESTED[(side, t)]
def thr(side, t): attested(side, t); return THR[(side, t)]
def edge_ok(side, t, a, b):
    return (a, b) in attested(side, t) or CONT[(side, NAMES[t])]['M0'][a, b] <= thr(side, t)
def transitions_check(side, t, pl):
    M = CONT[(side, NAMES[t])]['M0']; A = attested(side, t)
    res = [dict(a=a, b=b, mismatch=round(float(M[a, b]), 3), attested=(a, b) in A) for a, b in zip(pl, pl[1:])]
    bad = [r for r in res if not r['attested'] and r['mismatch'] > thr(side, t)]
    worst = max([r['mismatch'] for r in res if not r['attested']] + [0.0])
    return res, bad, worst

def segment_steps(tag, side, i0, i1):
    o = orig(tag); t = o['vt'] if side == 'victim' else o['kt']
    pl = o['vpl'] if side == 'victim' else o['kpl']; sc = o['vsc'] if side == 'victim' else o['ksc']
    st, tot = starts(side, t, pl)
    allsteps = script_steps(sc, tot + 1)
    a = st[i0]; b = st[i1] if i1 < len(pl) else tot + 1
    return pl[i0:i1], allsteps[a:b]

# sequences allowed as bridges: pre-impact entries of scenes with a generic/semi death (exchange / stance moves), per victim type
def death_block(src):
    """victim death block of scene src: entries react..end, or the explicit 'death_idx' override (e.g. one struggle loop only)."""
    idx = SCENES[src].get('death_idx') or list(range(react(src), len(orig(src)['vpl'])))
    pl, steps = [], []
    for i in idx:
        a, b = segment_steps(src, 'victim', i, i + 1); pl += a; steps += b
    if idx[-1] == len(orig(src)['vpl']) - 1:   # keep the trailing 'done' step (its sounds)
        _, tail = segment_steps(src, 'victim', idx[-1], len(orig(src)['vpl'])); steps = steps[:-len(b)] + tail
    return pl, steps

def bridge_pool(v):
    pool = set()
    for tag in SCENES:
        if tag[0] == v and SCENES[tag]['death'] in ('generic', 'semi') and not SCENES[tag].get('vpre_specific'):
            pool |= set(orig(tag)['vpl'][:react(tag)])
    return pool

def join_victim(prefix_pl, prefix_steps, death_pl, death_steps, vt, T, allow_drop=True, intro_only=False):
    """Cut the victim pre-impact prefix (keeping the intro), optionally bridge + loop-fill, so that the death block starts at tick T
    with continuous transitions. Returns (playlist, steps, info) or None."""
    side = 'victim'; best = None
    pool = sorted(bridge_pool(NAMES[vt]))
    if intro_only: prefix_pl, prefix_steps = prefix_pl[:1], prefix_steps[:dur(side, vt, prefix_pl[0])]
    st, _ = starts(side, vt, prefix_pl)
    st.append(pl_ticks(side, vt, prefix_pl))
    d0 = death_pl[0]
    pmin = 1 if allow_drop else len(prefix_pl)
    for p in range(len(prefix_pl), pmin - 1, -1):
        base = st[p]
        if base > T: continue
        last = prefix_pl[p-1]
        # bridges: shortest (in ticks) paths last -> ... -> d0 through the pool, up to 3 intermediate sequences
        import heapq
        cands = []
        if edge_ok(side, vt, last, d0): cands.append([])
        heap = [(0, [])]; seenb = {}
        while heap and len(cands) < 6:
            c, path = heapq.heappop(heap)
            node = path[-1] if path else last
            if path and edge_ok(side, vt, node, d0): cands.append(path); continue
            if len(path) >= 3: continue
            for b in pool:
                if b in path or not edge_ok(side, vt, node, b): continue
                nc = c + dur(side, vt, b)
                if seenb.get((b, len(path)), 1e9) <= nc: continue
                seenb[(b, len(path))] = nc
                heapq.heappush(heap, (nc, path + [b]))
        for br in cands:
            bt = pl_ticks(side, vt, br)
            if base + bt > T: continue
            hold = T - base - bt
            # loop filler: repeat a block prefix[i:p] that can loop (prefix[p-1] -> prefix[i]) to absorb the hold
            fill_i, reps = None, 0
            if hold > 6:
                for i in range(1, p):
                    blk = pl_ticks(side, vt, prefix_pl[i:p])
                    if blk <= hold and edge_ok(side, vt, prefix_pl[p-1], prefix_pl[i]):
                        r = hold // blk
                        if fill_i is None or hold - r*blk < hold - reps * pl_ticks(side, vt, prefix_pl[fill_i:p]):
                            fill_i, reps = i, r
            fill_t = reps * pl_ticks(side, vt, prefix_pl[fill_i:p]) if reps else 0
            res_hold = hold - fill_t
            cost = res_hold + 3 * (len(prefix_pl) - p) + bt // 2
            if best is None or cost < best[0]:
                best = (cost, p, br, fill_i, reps, res_hold)
    if best is None: return None
    cost, p, br, fill_i, reps, res_hold = best
    pl = list(prefix_pl[:p]); steps = list(prefix_steps[:st[p]])
    if reps:
        a = st[fill_i]; b = st[p]
        for _ in range(reps): pl += prefix_pl[fill_i:p]; steps += prefix_steps[a:b]
    pl += br; steps += [([], 0)] * pl_ticks(side, vt, br)
    if res_hold > 0x7f: return None
    if res_hold:
        snd, d = steps[-1]; steps[-1] = (snd, d + res_hold)
    pl += death_pl; steps += death_steps
    return pl, steps, dict(entries_dropped=len(prefix_pl) - p, bridge=br, loop_block=prefix_pl[fill_i:p] if reps else [], loop_reps=reps, hold_ticks=res_hold, death_start_tick=T)

def finalize(rec):
    vt, kt = NAMES.index(rec['pair'][0]), NAMES.index(rec['pair'][1])
    rec['victim_script'] = build_script(rec.pop('vsteps')); rec['killer_script'] = build_script(rec.pop('ksteps'))
    for side in ('victim', 'killer'):
        try: rec[side + '_script_stored'] = compress(rec[side + '_script'])
        except ValueError: rec[side + '_script_stored'] = None
    _, bv, wv = transitions_check('victim', vt, rec['victim_playlist'])
    _, bk, wk = transitions_check('killer', kt, rec['killer_playlist'])
    rec['check'] = dict(victim_unattested_worst=wv, killer_unattested_worst=wk, victim_threshold=round(thr('victim', vt), 3), killer_threshold=round(thr('killer', kt), 3),
                        victim_bad=[(r['a'], r['b'], r['mismatch']) for r in bv], killer_bad=[(r['a'], r['b'], r['mismatch']) for r in bk])
    ticks, sounds = simulate(vt, kt, rec['victim_playlist'], rec['killer_playlist'], rec['victim_script'], rec['killer_script'])
    rec['ticks'] = len(ticks)
    return rec, ticks, sounds

# ---------------- families
def fam_window_repeat(tag):
    """Repeat a pre-impact time window [a,b) in which both sides have sequence boundaries and each side can loop b->a."""
    o = orig(tag); vt, kt = o['vt'], o['kt']
    vs, vtot = starts('victim', vt, o['vpl']); ks, ktot = starts('killer', kt, o['kpl'])
    T = impact_tick(tag); best = None
    vidx = {t: i for i, t in enumerate(vs)}; kidx = {t: i for i, t in enumerate(ks)}
    common = sorted(set(vs) & set(ks))
    for a in common:
        for b in common:
            if not (0 < a < b <= T): continue
            iva, ivb, ika, ikb = vidx[a], vidx[b], kidx[a], kidx[b]
            if iva < 1 or ika < 1: continue
            if edge_ok('victim', vt, o['vpl'][ivb-1], o['vpl'][iva]) and edge_ok('killer', kt, o['kpl'][ikb-1], o['kpl'][ika]):
                if best is None or b - a > best[1] - best[0]: best = (a, b, iva, ivb, ika, ikb)
    if not best or best[1] - best[0] < 8: return []
    a, b, iva, ivb, ika, ikb = best
    def dup(side, pl, i0, i1):
        _, s0 = segment_steps(tag, side, 0, i1); _, sb = segment_steps(tag, side, i0, i1); _, s1 = segment_steps(tag, side, i1, len(pl))
        return pl[:i1] + pl[i0:i1] + pl[i1:], s0 + sb + s1
    vpl, vst = dup('victim', o['vpl'], iva, ivb); kpl, kst = dup('killer', o['kpl'], ika, ikb)
    return [dict(family='window_repeat', description=f'original {tag} fight with the ticks {a}..{b} (victim {o["vpl"][iva:ivb]}, killer {o["kpl"][ika:ikb]}) played twice, both sides in lock-step',
                 victim_playlist=vpl, killer_playlist=kpl, vsteps=vst, ksteps=kst, sources=dict(scene=tag, window=[a, b]), confidence='high')]

def fam_death_swap(tag):
    """Original killer track and victim pre-impact; victim death block from another scene of the same victim type."""
    if SCENES[tag]['kpost'] or ATTACK[tag] == 'gag': return []
    o = orig(tag); out = []; T = impact_tick(tag)
    pv, sv = segment_steps(tag, 'victim', 0, react(tag))
    for y in NAMES:
        src = tag[0] + y
        if src == tag or src not in SCENES: continue
        d = SCENES[src]['death']
        if d not in DEATH_FITS or ATTACK[tag] not in DEATH_FITS[d]: continue
        dv, sd = death_block(src)
        j = join_victim(pv, sv, dv, sd, o['vt'], T)
        if not j: continue
        pl, steps, info = j
        _, ks = segment_steps(tag, 'killer', 0, len(o['kpl']))
        out.append(dict(family='death_swap', description=f'original {tag} fight; at the impact the {FULL[tag[0]]} dies as in {src} ({SCENES[src]["story"]})',
                        victim_playlist=pl, killer_playlist=o['kpl'], vsteps=steps, ksteps=ks,
                        sources=dict(victim_prefix=tag, victim_death=src, killer=tag, **info), confidence='medium'))
    return out

def fam_attack_swap(tag):
    """Killer track from (X,K) (portable; X not bigger than V unless magic), victim track from (V,Y) whose death fits the attack,
    re-timed so its death block starts at the killer's impact tick."""
    v, k = tag[0], tag[1]; out = []
    for x in NAMES:
        ksrc = x + k
        if ksrc == tag or ksrc not in KILLER_PORTABLE: continue
        if SIZE_RANK[SIZE[x]] > SIZE_RANK[SIZE[v]] and ATTACK[ksrc] != 'magic': continue
        Tk = impact_tick(ksrc); ok_ = orig(ksrc)
        for y in NAMES:
            vsrc = v + y
            if vsrc not in SCENES: continue
            d = SCENES[vsrc]['death']
            if d not in DEATH_FITS or ATTACK[ksrc] not in DEATH_FITS[d]: continue
            pv, sv = segment_steps(vsrc, 'victim', 0, react(vsrc))
            dv, sd = death_block(vsrc)
            j = join_victim(pv, sv, dv, sd, NAMES.index(v), Tk, intro_only=SCENES[vsrc].get('vpre_specific', False))
            if not j: continue
            pl, steps, info = j
            _, ks = segment_steps(ksrc, 'killer', 0, len(ok_['kpl']))
            size_note = '' if SIZE[x] == SIZE[v] else f' (killer moves were drawn for a {SIZE[x]} {FULL[x]}, victim is {SIZE[v]})'
            conf = 'medium' if (info['hold_ticks'] <= 8 and info['entries_dropped'] <= 2 and SIZE[x] == SIZE[v] and ATTACK[ksrc] != 'magic') else 'low'
            if ATTACK[ksrc] == 'magic': size_note += ' (note: the bolt/flash of the original impact was drawn in the original victim sprites, so no bolt is visible on the new victim)'
            out.append(dict(family='attack_swap', description=f'{FULL[k]} uses its {ATTACK[ksrc]} attack from {ksrc} ({SCENES[ksrc]["story"]}); '
                                                              f'{FULL[v]} fights and dies as in {vsrc} ({SCENES[vsrc]["story"]}), re-timed to the new impact' + size_note,
                            victim_playlist=pl, killer_playlist=list(ok_['kpl']), vsteps=steps, ksteps=ks,
                            sources=dict(killer=ksrc, victim=vsrc, impact_tick=Tk, **info), confidence=conf))
    return out

def score(rec):
    c = rec['check']
    bad = len(c['victim_bad']) + len(c['killer_bad'])
    s = rec['sources']
    fam = {'window_repeat': 0, 'death_swap': 1, 'attack_swap': 1}[rec['family']]
    conf = {'high': 0, 'medium': 1, 'low': 2}[rec['confidence']]
    return (bad, conf, fam, s.get('hold_ticks', 0) + 3 * s.get('entries_dropped', 0), round(c['victim_unattested_worst'], 2))

def render(rec, ticks, sounds, path, step=3):
    sel = [i for i in range(len(ticks)) if i % step == 0]
    frames = [ticks[i][1] for i in sel]
    y0, y1, x0, x1 = bbox(frames); y0 -= 2; x0 -= 2; y1 += 2; x1 += 2
    w, h = x1 - x0, y1 - y0; cols = max(1, min(12, 1500 // (w + 2))); rows = (len(sel) + cols - 1) // cols
    img = Image.new('RGB', (cols * (w + 2) + 2, rows * (h + 14) + 16), (230, 230, 230)); dr = ImageDraw.Draw(img)
    dr.text((2, 2), rec['id'] + ': ' + rec['description'][:200], fill=(0, 0, 0))
    for j, i in enumerate(sel):
        rc = ticks[i][0]; xx = 2 + (j % cols) * (w + 2); yy = 16 + (j // cols) * (h + 14)
        img.paste(Image.fromarray(to_rgb(ticks[i][1][y0:y1, x0:x1])), (xx, yy + 12))
        dr.text((xx, yy), f't{i} V{rc["V"][0]}:{rc["V"][1]} K{rc["K"][0]}:{rc["K"][1]}', fill=(0, 0, 0))
    img.save(path)

if __name__ == '__main__':
    import glob
    for f in glob.glob(os.path.join(OUT, '*.png')): os.remove(f)
    allrec = []; summary = []
    for tag in SCENES:
        cands = fam_window_repeat(tag) + fam_death_swap(tag) + fam_attack_swap(tag)
        done = []
        for c in cands:
            c['pair'] = tag
            rec, ticks, sounds = finalize(c)
            if rec['check']['victim_bad'] or rec['check']['killer_bad']: rec['confidence'] = 'low'
            done.append((rec, ticks, sounds))
        done.sort(key=lambda x: score(x[0]))
        pick = []; seen = set()
        for rec, ticks, sounds in done:
            if len(pick) >= 4: break
            key = (rec['family'], rec['sources'].get('killer'), rec['sources'].get('victim', rec['sources'].get('victim_death')))
            if key in seen: continue
            seen.add(key); pick.append((rec, ticks, sounds))
        for n, (rec, ticks, sounds) in enumerate(pick, 1):
            rec['id'] = f'{tag}-{n}'; rec['victim'] = FULL[tag[0]]; rec['killer'] = FULL[tag[1]]
            render(rec, ticks, sounds, os.path.join(OUT, rec['id'] + '.png'))
            allrec.append(rec)
        summary.append((tag, len(cands), [(r['family'], r['confidence']) for r, _, _ in pick]))
    for s in summary: print(s)
    json.dump(allrec, open(os.path.join(R, 'research', 'recipes_raw.json'), 'w'), indent=1, default=int)
    print('total', len(allrec))
