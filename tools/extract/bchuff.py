# Распаковщик Хаффмана из Battle Chess (CODE 5, 0x878/0xa62), повторяет логику 68k-кода.
class Bits:
    def __init__(self, data, pos):
        self.d = data; self.p = pos  # позиция в байтах, слова big-endian
        self.cur = int.from_bytes(data[pos:pos+2], 'big'); self.p += 2; self.mask = 0x8000
    def bit(self):
        b = self.cur & self.mask
        self.mask >>= 1
        if self.mask == 0:
            self.mask = 0x8000
            self.cur = int.from_bytes(self.d[self.p:self.p+2], 'big'); self.p += 2
        return 1 if b else 0
    def byte(self):
        v = 0
        for _ in range(8): v = (v << 1) | self.bit()
        return v

def read_tree(bs):
    if bs.bit(): return bs.byte()
    left = read_tree(bs)
    bs.bit()
    right = read_tree(bs)
    return (left, right)

def decode(data, pos, limit=None):
    size = int.from_bytes(data[pos:pos+4], 'big')
    extra = int.from_bytes(data[pos+4:pos+8], 'big')
    bs = Bits(data, pos + 8)
    tree = read_tree(bs)
    n = size if limit is None else min(size, limit)
    out = bytearray()
    while len(out) < n:
        node = tree
        while isinstance(node, tuple): node = node[bs.bit()]
        out.append(node)
    return bytes(out), size, extra, bs.p
