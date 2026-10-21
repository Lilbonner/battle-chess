import sys, struct, macresources
from PIL import Image

def unpack(data, n):
    out = bytearray(); i = 0
    while len(out) < n and i < len(data):
        c = data[i]; i += 1
        if c < 128:
            out += data[i:i + c + 1]; i += c + 1
        elif c > 128:
            out += bytes([data[i]]) * (257 - c); i += 1
    return bytes(out[:n])

def decode(d):
    # header: size(2) + frame(8)
    p = 10
    v2 = d[p:p + 2] == b'\x00\x11'
    if not v2:
        assert d[p] == 0x11; p += 2  # version 1
    else:
        p += 4
    images = []
    while p < len(d):
        if v2:
            if p % 2: p += 1
            op = struct.unpack('>H', d[p:p + 2])[0]; p += 2
        else:
            op = d[p]; p += 1
        if op in (0x00,): continue
        if op == 0xff: break
        if op == 0x0c00: p += 24; continue
        if op == 0x01:
            sz = struct.unpack('>H', d[p:p + 2])[0]; p += sz; continue
        if op == 0xa0: p += 2; continue
        if op == 0xa1:
            p += 2; sz = struct.unpack('>H', d[p:p + 2])[0]; p += 2 + sz; continue
        if op == 0x1e: continue
        if op in (0x90, 0x98):
            rb = struct.unpack('>H', d[p:p + 2])[0] & 0x3fff; p += 2
            t, l, b, r = struct.unpack('>4h', d[p:p + 8]); p += 8
            src = struct.unpack('>4h', d[p:p + 8]); p += 8
            dst = struct.unpack('>4h', d[p:p + 8]); p += 8
            p += 2
            h, w = b - t, r - l
            rows = []
            for y in range(h):
                if op == 0x98:
                    if rb > 250: n = struct.unpack('>H', d[p:p + 2])[0]; p += 2
                    else: n = d[p]; p += 1
                    rows.append(unpack(d[p:p + n], rb)); p += n
                else:
                    rows.append(d[p:p + rb]); p += rb
            img = Image.new('1', (w, h), 1)
            px = img.load()
            for y in range(h):
                for x in range(w):
                    if rows[y][x >> 3] & (0x80 >> (x & 7)): px[x, y] = 0
            images.append((dst, img))
            continue
        print('unknown op', hex(op), 'at', p); break
    return images

if __name__ == "__main__":
  rs = list(macresources.parse_file(open(sys.argv[1], 'rb').read()))
  for r in rs:
      if r.type == b'PICT':
          try:
              ims = decode(bytes(r))
              for k, (dst, im) in enumerate(ims):
                  im.save(f'{sys.argv[2]}/pict_{r.id}_{k}.png'); print(r.id, k, dst, im.size)
          except Exception as e:
              print(r.id, 'fail', e)
