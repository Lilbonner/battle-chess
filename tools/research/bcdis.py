# Re-disassemble a CODE segment with switch tables (jsr $3a(a5)) and MacsBug names decoded.
import sys, os, re, json, capstone
R = os.environ.get('BC_WORK', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'work'))
APP = os.path.join(R, 'bcextract', 'app')
seg = int(sys.argv[1])
code = open(os.path.join(APP, f'CODE_{seg}.bin'), 'rb').read()[4:]
jt = json.load(open(os.path.join(APP, 'jt.json')))
names = {int(k.split(':')[1]): v for k, v in json.load(open(os.path.join(APP, 'names.json'))).items() if int(k.split(':')[0]) == seg}
md = capstone.Cs(capstone.CS_ARCH_M68K, capstone.CS_MODE_BIG_ENDIAN | capstone.CS_MODE_M68K_000)
W = lambda o: int.from_bytes(code[o:o+2], 'big')
SW = lambda o: int.from_bytes(code[o:o+2], 'big', signed=True)
out = []; off = 0; labels = set()
while off < len(code):
    if off in names: out.append(f'\n; ===== {names[off]} =====')
    w = W(off)
    if (w & 0xF000) == 0xA000:
        out.append(f'{off:06x}: TRAP {w:04x}'); off += 2; continue
    ins = next(md.disasm(code[off:off+10], off), None)
    if ins is None:
        out.append(f'{off:06x}: dc.w ${w:04x}'); off += 2; continue
    l = f'{off:06x}: {ins.mnemonic} {ins.op_str}'
    m = re.search(r'jsr \$([0-9a-f]+)\(a5\)', l)
    if m:
        e = jt.get(hex(int(m.group(1), 16)))
        if e: l += f'   ; -> {e[2]} (seg{e[0]}:{e[1]:x})'
    out.append(l); off += ins.size
    if ins.mnemonic == 'jsr' and ins.op_str == '$3a(a5)':
        lo, hi = SW(off), SW(off+2); d = off + 4
        out.append(f'; SWITCH lo={lo} hi={hi} default -> {d+SW(d):06x}')
        for k in range(hi - lo + 1):
            a = d + 2 + 2*k
            out.append(f';   case {lo+k}: -> {a+SW(a):06x}')
        off = d + 2 + 2*(hi - lo + 1)
    if ins.mnemonic in ('rts', 'jmp') and off < len(code):
        # MacsBug name: 8 printable chars, optional extra word
        nb = code[off:off+8]
        if all(0x20 <= (b & 0x7f) <= 0x7e for b in nb) and (off + 8) not in names:
            # skip until next link or known name
            nxt = min([n for n in names if n > off] + [len(code)])
            out.append(f'{off:06x}: ; name {bytes(b & 0x7f for b in nb)!r} (skipped to {nxt:06x})')
            # only skip if within 16 bytes
            if nxt - off <= 16: off = nxt
open(os.path.join(R, 'research', f'seg{seg:02d}.sw.asm'), 'w').write('\n'.join(out))
print('ok', len(out))
