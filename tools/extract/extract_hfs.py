import sys, os, machfs, macresources, collections
img, out = sys.argv[1], sys.argv[2]
v = machfs.Volume()
v.read(open(img, 'rb').read())
print('volume:', v.name)
def walk(folder, path):
    for name, item in folder.items():
        safe = ''.join(c if c.isalnum() or c in ' ._-' else '_' for c in name)
        p = os.path.join(path, safe)
        if isinstance(item, machfs.Folder):
            os.makedirs(p, exist_ok=True); walk(item, p)
        else:
            open(p + '.data', 'wb').write(item.data)
            open(p + '.rsrc', 'wb').write(item.rsrc)
            types = collections.Counter()
            if item.rsrc:
                for r in macresources.parse_file(item.rsrc): types[r.type.decode('mac_roman')] += 1
            print(f'{p!r} type={item.type} creator={item.creator} data={len(item.data)} rsrc={len(item.rsrc)} {dict(types)}')
os.makedirs(out, exist_ok=True)
walk(v, out)
