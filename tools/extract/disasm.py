import sys, os, glob, capstone
d = sys.argv[1]
md = capstone.Cs(capstone.CS_ARCH_M68K, capstone.CS_MODE_BIG_ENDIAN | capstone.CS_MODE_M68K_000)
traps = {0xA000:'_Open',0xA001:'_Close',0xA002:'_Read',0xA003:'_Write',0xA044:'_SetFPos',0xA011:'_GetEOF',0xA11E:'_NewPtr',0xA122:'_NewHandle',0xA9A0:'_GetResource',0xA8EC:'_CopyBits',0xA9C8:'_SysBeep',0xA0A4:'_?',0xA80A:'_SndPlay?',0xA805:'_SndDoCommand',0xA807:'_SndNewChannel',0xA808:'_SndPlay',0xA06E:'_?',0xA9A9:'_?',0xA01F:'_DisposPtr',0xA023:'_DisposHandle',0xA02E:'_BlockMove',0xA060:'_FSDispatch'}
for f in sorted(glob.glob(os.path.join(d, 'CODE_*.bin')), key=lambda p:int(p.split('_')[-1][:-4])):
    seg = int(f.split('_')[-1][:-4])
    if seg == 0: continue
    data = open(f,'rb').read()
    code = data[4:]
    out = []
    off = 0
    while off < len(code):
        w = int.from_bytes(code[off:off+2],'big')
        if (w & 0xF000) == 0xA000:
            out.append(f'{off:06x}: TRAP {w:04x} {traps.get(w & 0xF9FF, traps.get(w,""))}'); off += 2; continue
        ins = next(md.disasm(code[off:off+10], off), None)
        if ins is None:
            out.append(f'{off:06x}: dc.w ${w:04x}'); off += 2; continue
        out.append(f'{off:06x}: {ins.mnemonic} {ins.op_str}'); off += ins.size
    open(os.path.join(d, f'seg{seg:02d}.asm'),'w').write('\n'.join(out))
    print(seg, len(out), 'lines')
