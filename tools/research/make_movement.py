import sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
from bclib import *
pmap = GIMG[G-0xcec:G-0xcec+64]
X = [W(-0xb04 + 2*i) for i in range(64)]; Y = [W(-0xa84 + 2*i) for i in range(8)]
DIRS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW']
def decode_t4(lst):
    steps = []; snd = []; i = 0; b = list(lst)
    while i < len(b):
        if b[i] > 0x7f and b[i] != 0xff: snd.append({'sfx': b[i] & 0xf, 'pitch': b[i+1] - 0x40}); i += 2
        else: steps.append({'delay': b[i], 'sounds': snd}); snd = []; i += 1
    return {'raw': list(lst) + [0xff], 'sound_steps': [{'step': k, 'sounds': s['sounds']} for k, s in enumerate(steps) if s['sounds']],
            'nonzero_delays': [k for k, s in enumerate(steps) if s['delay']]}
out = {'_doc': {
  'ticks': 'one engine tick = one HANDLEALL pass, at most every 6 Mac ticks (TickCount delta >= 6) -> 10 fps max',
  'facing': 'raw facing 0..7 = N,NE,E,SE,S,SW,W,NW (clockwise from N = away from viewer); stored in board byte bits 3-5',
  'square_coords': 'screen x = X[sq] (64 entries, sq = row*8+col, row 0 = far side), y = Y[row]; tables A5-0xB04 / A5-0xA84',
  'interp': 'LINECCFW(x0,y0,x1,y1,n,i): x = x0 + sign(dx)*(|dx|//n*i + ((|dx|%n)*i + n//2)//n) (same for y) i.e. round-half-up of dx*i/n',
  'walk_rule': 'WALK = one activity per square. Walk sequence (key + len deltas) is played exactly once per square: n = len+1 steps; step i (0..len) drawn at interp(start,end,n,i); after the last delta the logical position snaps to the destination centre and the next activity starts (same or next tick, depends on active-list order). Per square duration = len+1 ticks.',
  'turn_rule': 'TURN to target facing: shortest way, ties (180 deg) go counter-clockwise; chained single steps; each step plays one turn sequence (key + len deltas, 1+len ticks). 8-facing pieces: CW from f = seq 8+f, CCW from f = seq 16+f. 4-facing pieces (B,N,R): CW = seq 4+f//2, CCW = seq 8+f//2 (f = raw facing). Board facing is updated to the target when the TURN activity starts.',
  'sound_scripts_T4': 'A5-0x3684: 6 types x 24 byte lists (CHECKTIME format: per step [sound cmds 0x80|sfx, pitch+0x40] then delay byte; 0xFF end). Index 0..7 = WALK/HWALK/CP script by raw facing, 8+2*f+(1 if clockwise else 0) = TURN step script from facing f. All delays are 0 (one frame per tick); they only add footstep sounds.',
  'slide': 'SLIDE (knight path): the blocking piece is moved rigidly (current shape, no animation) by (DX[dir],DY[dir]) in 6 interpolation steps (7 ticks), dir = knight first-leg direction; slides back (dir+4) after the knight passed. DX=[0,19,19,19,0,-19,-19,-19], DY=[-9,-9,0,9,9,9,0,-9] (A5-0x8C4/-0x8B4).',
  'hwalk': 'HWALK: first half of the facing walk sequence (n//2 steps, n = len+1) moving by (DX[dir],DY[dir]) from the current position; used only by the victim stepping aside (dir W).',
  'cp': 'CP (capture position): walk sequence of current facing, n = len+1 steps if (p3==1 and facing in S,SW,W) else n//2 steps, from current logical position to: p3=1 (killer) (X+15, Y-1); p3=0 (victim) (X-25, Y+13); p3=2 (after combat) exact square centre. Last drawn step is i=n-1, then position snaps to target.',
  'standing_pose_map': 'A5-0xCEC: 64 bytes indexed by (facing<<3)|type, value = shape index (pose = value-17).',
 }, 'X': X, 'Y': Y, 'pieces': {}}
nf = {'B': 4, 'K': 8, 'N': 4, 'P': 8, 'Q': 8, 'R': 4}
for t, n in enumerate(NAMES):
    c = res[2][t]; sq = c['seqs']
    d = {'type_board': t + 1, 'chunk': f'ALLCANM2 chunk {t} (SETUPSEQ v={t+3})', 'facings': nf[n], 'n_seqs': len(sq),
         'standing_pose_by_facing': {DIRS[f]: pmap[f*8 + t + 1] - 17 for f in range(8) if pmap[f*8 + t + 1] != 99},
         'seqs': [{'id': i, 'key_pose': k & 0x7f, 'len': len(fr), 'ticks': len(fr) + 1} for i, (k, fr) in enumerate(sq)],
         'walk': {}, 'turn_cw': {}, 'turn_ccw': {}}
    for f in range(8):
        idx = f if nf[n] == 8 else f >> 1
        if nf[n] == 4 and ((n == 'B' and f % 2 == 0) or (n != 'B' and f % 2 == 1)): continue
        d['walk'][DIRS[f]] = {'seq': idx, 'len': len(sq[idx][1]), 'ticks_per_square': len(sq[idx][1]) + 1, 'key_pose': sq[idx][0] & 0x7f,
                              'sound_script': decode_t4(tabs['T4'][t*24 + f])}
        cw = 8 + f if nf[n] == 8 else 4 + (f >> 1); ccw = 16 + f if nf[n] == 8 else 8 + (f >> 1)
        step = 1 if nf[n] == 8 else 2
        d['turn_cw'][DIRS[f]] = {'seq': cw, 'to': DIRS[(f + step) % 8], 'ticks': len(sq[cw][1]) + 1, 'key_pose': sq[cw][0] & 0x7f, 'sound_script': decode_t4(tabs['T4'][t*24 + 8 + 2*f + 1])}
        d['turn_ccw'][DIRS[f]] = {'seq': ccw, 'to': DIRS[(f - step) % 8], 'ticks': len(sq[ccw][1]) + 1, 'key_pose': sq[ccw][0] & 0x7f, 'sound_script': decode_t4(tabs['T4'][t*24 + 8 + 2*f])}
    out['pieces'][FULL[n]] = d
out['pieces']['pawn']['notes'] = ('pawn standing poses 16..23 have only two images (back view N/NE/E/NW, front view SE/S/SW/W); turn sequences start from separate turn poses 39..46 '
    '(39/40/46 back, 41 profile E, 42/43/44 front, 45 profile W). Single-frame turn seqs (len 1, null delta) = no visible change. Walk seqs: E uses the NE (back) walk, W the NW walk; '
    'DOWALK maps a pawn E/W move (only en-passant first leg) to NE/NW.')
out['pieces']['rook']['transform'] = {
    'rule': 'TR activity: seq = 14 + p2, played in place, one frame per tick (no CHECKTIME). p2=0: tower(pose 38) -> golem N (32), board facing := 0; p2=1: tower -> golem S (34), facing := 4; '
            'p2=2: golem N -> tower (seq 16, ends ~pose 38); p2=3: golem S -> tower (seq 17). After p2>=2 board facing := 7 (tower pose 36) if object x > 160 else 3 (tower pose 37). '
            'Seqs 12/13 (len 0) are the board tower poses 36/37.',
    'seqs': {str(i): {'key_pose': sq[i][0] & 0x7f, 'len': len(sq[i][1])} for i, sq in [(i, res[2][5]['seqs']) for i in (12, 13, 14, 15, 16, 17)]},
    'usage': 'start of every rook move: TR p2=1 if moving down (to row > from row) or horizontal and the rook is a far-side (bit6=0) piece, else p2=0; '
             'then TURN to move direction; at the end: E/W moves: near-side (bit6=1) TURN N + TR 2, far-side TURN S + TR 3; N move: TR 2; S move: TR 3. '
             'As a victim: TR 1 first (tower -> golem S).'}
out['pieces']['knight']['path'] = ('first leg: ONE square along the short axis (row diff 1 -> vertical first; col diff 1 -> horizontal first), with a TURN to that direction, then TURN to the long-axis '
    'direction and two single-square WALKs. A piece standing on a square the knight enters (except the destination) SLIDEs by (DX,DY) of the first-leg direction in parallel with the walk '
    'and slides back (opposite direction) when the knight walks on.')
json.dump(out, open(os.path.join(R, 'research', 'movement.json'), 'w'), indent=1)
for n in out['pieces']:
    p = out['pieces'][n]; print(n, {k: (v['seq'], v['ticks_per_square']) for k, v in p['walk'].items()})
