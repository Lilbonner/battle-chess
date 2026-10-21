import sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
from scene_labels import SCENES
R = os.environ.get('BC_WORK', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'work'))
raw = json.load(open(os.path.join(R, 'research', 'recipes_raw.json')))
recs = []
for r in raw:
    s = r['sources']
    recs.append(dict(id=r['id'], pair=r['pair'], victim=r['victim'], killer=r['killer'], family=r['family'], confidence=r['confidence'],
        description=r['description'],
        victim_playlist=r['victim_playlist'], killer_playlist=r['killer_playlist'],
        victim_script=r['victim_script'], killer_script=r['killer_script'],
        victim_script_hex=' '.join('%02x' % b for b in r['victim_script']), killer_script_hex=' '.join('%02x' % b for b in r['killer_script']),
        victim_script_stored=r['victim_script_stored'], killer_script_stored=r['killer_script_stored'],
        ticks=r['ticks'], sources=s, continuity_check=r['check'], sheet=f"sheets/recipes/{r['id']}.png"))
have = {r['pair'] for r in recs}
why = {
 'BK': 'only king-killer track that is transplantable is the revolver (BK itself); no bishop death block joins the BK pre-impact continuously at the right tick',
 'PK': 'the pawn death in PK is embedded in the long guard sequence V26 (knocked flying); king has no other portable attack for a small victim',
 'QB': 'every queen death is a gag/transformation (cat, wrung, crushed, bomb...) except QK whose stab-death needs the king embrace prelude (vpre_specific)',
 'QK': 'same: queen deaths are tied to their killers; QK itself is the only ordinary death',
 'QN': 'queen deaths are tied to their killers', 'QP': 'queen deaths are tied to their killers', 'QQ': 'queen deaths are tied to their killers', 'QR': 'queen deaths are tied to their killers',
 'RK': 'the only portable king attack is the revolver; golem deaths are crumble-to-rubble which does not fit a gunshot',
 'KB': '', }
nogen = {p: why.get(p, 'no candidate passed the continuity/timing rules') for p in SCENES if p not in have}
doc = {
 'format': 'victim_playlist / killer_playlist: sequence ids of the victim-side / killer-side combat chunk of that piece type (same numbering as the original T0/T1 lists, A5-0x38C4/-0x3834). '
           'victim_script / killer_script: runtime CHECKTIME byte stream (what EXPANDDATA produces): per step optional sound commands [0x80|sfx, pitch+0x40], then one delay byte '
           '(0 = advance one frame this tick, d>0 = after this step wait d ticks), 0xFF = end (afterwards one frame per tick). *_script_stored = the compressed form used in DATA (null when a nonzero delay is used, which the original storage cannot express).',
 'sync': 'both sides start when both COMBAT activities are active and advance one frame per tick; impact alignment is by tick count only. Delays are an engine feature never used by the originals; recipes use them only as short holds.',
 'families': {'window_repeat': 'a time window in which both sides have sequence boundaries and loop-continuous ends is played twice (lock-step) - same art, longer fight',
              'death_swap': 'original killer track and victim pre-impact; from the original impact tick the victim plays a death block from another scene of the same victim type whose death fits the killer weapon',
              'attack_swap': 'killer track of the same killer from a scene with a victim of equal/smaller size (portable: no contact with victim-specific props); victim track from a scene with an ordinary death, cut/bridged/looped so its death starts exactly at the new killer impact tick'},
 'confidence': 'high = only original art in original order, lock-step; medium = all transitions continuous and impact aligned, sizes match; low = continuous but long hold, many dropped entries, size mismatch or magic effect missing (see description)',
 'verify': 'every recipe was simulated with the HANDLECOMBAT/CHECKTIME re-implementation and rendered to sheets/recipes/<id>.png (every 3rd tick)'}
json.dump({'_doc': doc, 'recipes': recs, 'not_generated': nogen}, open(os.path.join(R, 'research', 'recipes.json'), 'w'), indent=1)
print(len(recs), nogen)
