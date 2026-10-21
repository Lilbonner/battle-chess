import sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
from recipes import *   # SCENES, orig, starts, impact_tick, CONT, thr, attested ...
out = {'_doc': {
  'chunks': 'victim side = the "attacker" table of the original code (A5-0x38C4 playlist T0, seq chunk at SETUPSEQ v, SEF keyframes at v+1), killer side = the other table. '
            'v (victim) B9 K-5 N13 P15 Q-1 R19; v (killer) B-7 K-3 N25 P27 Q1 R31; chunk directory entry k = v-3 at A5-0x27A2.',
  'key': "'sef N' = full keyframe N of the side's combat SEF chunk (sef 0 = the side's combat standing stance); 'pose N' = standing pose N",
  'ticks': 'a sequence occupies 1+len ticks (keyframe tick + one tick per delta) when its script bytes are 0',
  'roles': 'intro (first entry), pre_impact (exchange/attack before the lethal moment), death (victim from impact on), remains (victim last entry, stays on screen until the final ST fades it), '
           'post_impact (killer after impact), outro (killer last entry)',
  'reuse': "generic = appears in scenes whose interaction is ordinary weapon play, or is used against several opponents; specific = belongs to a gag/prop/magic effect of one opponent pairing",
  'extent_px': 'how far (pixels) the sequence frames reach beyond the bounding box of its own keyframe; large values = props/effects/flying parts',
  'continues_into': 'other sequences of the same chunk whose keyframe matches this sequence end frame (silhouette mismatch <= chunk threshold) - safe successors',
}, 'chunks': {}}
def bb(a):
    ys, xs = np.nonzero(a != 3)
    return (ys.min(), ys.max(), xs.min(), xs.max()) if len(ys) else None
for side, V in (('victim', V_VICTIM), ('killer', V_KILLER)):
    for t in range(6):
        seq, keys, dl = load_side(V[t]); sq = seq['seqs']; n = NAMES[t]
        uses = {}
        for o in range(6):
            tag = (n + NAMES[o]) if side == 'victim' else (NAMES[o] + n)
            if tag not in SCENES: continue
            oo = orig(tag); pl = oo['vpl'] if side == 'victim' else oo['kpl']
            st, tot = starts(side, t, pl); T = impact_tick(tag); rc = (SCENES[tag].get('death_idx') or [react(tag)])[0]
            for i, s in enumerate(pl):
                if i == 0: role = 'intro'
                elif side == 'victim':
                    role = 'pre_impact' if i < rc else ('remains' if i == len(pl) - 1 else 'death')
                else:
                    role = 'outro' if i == len(pl) - 1 else ('pre_impact' if st[i] < T else 'post_impact')
                lab = SCENES[tag]['V' if side == 'victim' else 'K'].get(s, '')
                uses.setdefault(s, []).append(dict(scene=tag, pos=i, start_tick=st[i], role=role, label=lab,
                                                   scene_interaction=ATTACK[tag], victim_death=SCENES[tag]['death']))
        M = CONT[(side, n)]['M0']; th = thr(side, t)
        items = []
        for i, (k, fr) in enumerate(sq):
            frames = play(sq, keys, dl, i)
            kb = bb(frames[0]); ext = 0
            for f in frames[1:]:
                b = bb(f)
                if b and kb: ext = max(ext, kb[0] - b[0], b[1] - kb[1], kb[2] - b[2], b[3] - kb[3])
            u = uses.get(i, [])
            opp = sorted({x['scene'][1] if side == 'victim' else x['scene'][0] for x in u})
            roles = sorted({x['role'] for x in u})
            labels = sorted({x['label'] for x in u if x['label']})
            ordinary = all(x['scene_interaction'] in ('blade', 'blunt', 'gun', 'magic') and x['victim_death'] in ('generic', 'semi') for x in u) if u else False
            if not u: reuse = 'unused'
            elif len(opp) >= 2 or (ordinary and set(roles) <= {'intro', 'pre_impact', 'outro'}): reuse = 'generic'
            elif ordinary: reuse = 'generic-death' if side == 'victim' else 'generic-attack'
            else: reuse = 'specific'
            succ = [int(j) for j in np.argsort(M[i]) if M[i, j] <= th][:8]
            items.append(dict(id=i, key=('pose %d' % (k & 0x7f)) if k & 0x80 else ('sef %d' % k), len=len(fr), ticks=len(fr) + 1,
                              used_by=u, opponents=opp, roles=roles, label='; '.join(labels) if labels else '', reuse=reuse,
                              extent_px=int(ext), starts_from_stance=(k in (0, 1)), continues_into=succ))
        out['chunks'][f'{side}_{FULL[n]}'] = dict(side=side, piece=FULL[n], v=V[t], n_seqs=len(sq), n_keyframes=len(keys), continuity_threshold=round(th, 3), sequences=items)
        print(side, n, len(sq), {r: sum(1 for x in items if x['reuse'] == r) for r in ('generic', 'generic-death', 'generic-attack', 'specific', 'unused')})
json.dump(out, open(os.path.join(R, 'research', 'sequences.json'), 'w'), indent=1, default=int)
