# Разбор блоков ALLCANM: SEF (список u32 + поток) и SEQ (6-байтовые записи + сценарии + поток).
ZERO4 = bytes(4)


class Dec:
    """Потоковый декодер Хаффмана с отслеживанием позиции в битах."""
    def __init__(self, d, pos):
        self.d = d; self.pos = pos
        self.size = int.from_bytes(d[pos:pos+4], 'big'); self.extra = int.from_bytes(d[pos+4:pos+8], 'big')
        self.bitpos = (pos + 8) * 8
        self.tree = self._tree(0)
        self.tree_end = self.bitpos

    def bit(self):
        b = (self.d[self.bitpos >> 3] >> (7 - (self.bitpos & 7))) & 1; self.bitpos += 1; return b

    def byte(self):
        v = 0
        for _ in range(8): v = (v << 1) | self.bit()
        return v

    def _tree(self, depth):
        if depth > 30: raise ValueError('tree too deep')
        if self.bit(): return self.byte()
        l = self._tree(depth + 1); self.bit(); r = self._tree(depth + 1); return (l, r)

    def read(self, n):
        out = bytearray()
        for _ in range(n):
            node = self.tree
            while isinstance(node, tuple): node = node[self.bit()]
            out.append(node)
        return bytes(out)


def parse_sef(d, p):
    """Список 4-байтовых записей (w,h,hx,hy); конец — 0 или место, где u32 потока равен сумме размеров."""
    recs = []; q = p; total = 0
    while len(recs) < 400:
        v = d[q:q+4]
        if len(v) < 4: return None
        if total > 0:
            if v == ZERO4 and int.from_bytes(d[q+4:q+8], 'big') == total: sp = q + 4; break
            if int.from_bytes(v, 'big') == total: sp = q; break
        if v[0] == 0 or v[1] == 0 or v[0] > 200 or v[1] > 200: return None
        recs.append(tuple(v)); total += ((v[0] + 3) >> 2) * v[1]; q += 4
    else:
        return None
    dec = Dec(d, sp)
    out = dec.read(total)
    return {'kind': 'sef', 'start': p, 'stream': sp, 'recs': recs, 'data': out, 'end_bit': dec.bitpos}


def parse_seq(d, p):
    recs = []; q = p
    while True:
        r = d[q:q+6]
        if len(r) < 6: return None
        if r[:4] == ZERO4: break
        recs.append((r[0], r[1], r[2], r[3], int.from_bytes(r[4:6], 'big'))); q += 6
        if len(recs) > 400: return None
    if not recs: return None
    q += 6
    count = d[q]; q += 1
    seqs = []
    for _ in range(count):
        ln = d[q]; flag = d[q+1]; seqs.append((flag, list(d[q+2:q+2+ln]))); q += 2 + ln
    sp = q
    dec = Dec(d, sp)
    frames = []
    for r in recs:
        w, h, hx, hy, bits = r
        if w == 0 or h == 0: frames.append(b''); continue
        b0 = dec.bitpos
        data = dec.read(((w + 3) >> 2) * h)
        frames.append((data, dec.bitpos - b0))
    return {'kind': 'seq', 'start': p, 'stream': sp, 'recs': recs, 'seqs': seqs, 'frames': frames,
            'tree_bits': dec.tree_end - (sp + 8) * 8, 'end_bit': dec.bitpos, 'size': dec.size, 'extra': dec.extra}
