"""Extracts index-32 payloads and scores two readings of them against each other.

Two subcommands:

  extract <cacheDir> <outDir>
      Writes every index-32 group whose payload opens FF D8 as <outDir>/g<id>.jpg.
      Reads main_file_cache.idx32 and main_file_cache.dat2 directly and walks the
      sector chain by the layout in RSSector.Decode, so it goes through neither
      FlashEditor's decoder nor any reference document - which is what lets its
      output be used as evidence about either. Every open is 'rb'; the cache is
      read-only and stays that way.

  compare <dirA> <sufA> <dirB> <sufB>
      Scores two dumps of the same images pixel for pixel. Both sides write
      width and height as two big-endian shorts, then one 3-byte RGB triple per
      pixel.

The point of the pair is that "our reading agrees with the JVM" is only worth
saying if disagreement would have shown. Measured over the vanilla b639
capture's twenty-one images: ours against JDK 8's Toolkit is 0.02 mean absolute
error per channel and never more than 3; the CMYK reading a marker-less
four-component JPEG defaults to is 53.84 mean and up to 201. The two candidates
are nowhere near each other, so the agreement is a result rather than a
coincidence.
"""
import bz2
import gzip
import io
import os
import sys

SECTOR = 520
HEADER = 8
DATA = 512
INDEX = 32


def read_idx(path):
    """Reads an idx file into {groupId: (storedLength, firstSector)}."""
    with open(path, 'rb') as f:
        raw = f.read()
    out = {}
    for gid in range(len(raw) // 6):
        o = gid * 6
        length = int.from_bytes(raw[o:o + 3], 'big')
        start = int.from_bytes(raw[o + 3:o + 6], 'big')
        if length > 0 and start > 0:
            out[gid] = (length, start)
    return out


def read_chain(dat, gid, length, start):
    """Follows one group's sector chain, checking every sector header as it goes."""
    out = bytearray()
    sector = start
    chunk = 0
    while len(out) < length:
        dat.seek(sector * SECTOR)
        block = dat.read(SECTOR)
        if len(block) < HEADER:
            raise IOError('short sector %d for group %d' % (sector, gid))
        sid = int.from_bytes(block[0:2], 'big')
        schunk = int.from_bytes(block[2:4], 'big')
        nxt = int.from_bytes(block[4:7], 'big')
        sidx = block[7]
        if sid != gid or schunk != chunk or sidx != INDEX:
            raise IOError('sector header mismatch at %d: id=%d chunk=%d idx=%d' % (sector, sid, schunk, sidx))
        out += block[HEADER:HEADER + min(DATA, length - len(out))]
        sector = nxt
        chunk += 1
    return bytes(out)


def container_payload(stored):
    """Unwraps a stored container into (payload, compressionType, trailerLength).

    compressedSize counts the compressed body alone, and the body starts after
    the 4-byte uncompressed-size field, so it is stored[9 : 9 + compressedSize].
    Sizing it from offset 5 instead silently shifts every compressed container
    by four bytes.
    """
    ctype = stored[0]
    csize = int.from_bytes(stored[1:5], 'big')
    if ctype == 0:
        return stored[5:5 + csize], ctype, len(stored) - (5 + csize)

    usize = int.from_bytes(stored[5:9], 'big')
    body = stored[9:9 + csize]
    trailer = len(stored) - (9 + csize)
    if ctype == 2:
        payload = gzip.GzipFile(fileobj=io.BytesIO(body)).read()
    elif ctype == 1:
        # Jagex strips the standard BZh1 header, so it has to be put back.
        payload = bz2.decompress(b'BZh1' + body)
    else:
        raise IOError('unknown compression %d' % ctype)
    if len(payload) != usize:
        raise IOError('uncompressed size mismatch %d != %d' % (len(payload), usize))
    return payload, ctype, trailer


def extract(cache_dir, out_dir):
    """Writes every JPEG-shaped index-32 group to out_dir."""
    os.makedirs(out_dir, exist_ok=True)
    idx = read_idx(os.path.join(cache_dir, 'main_file_cache.idx%d' % INDEX))
    jpegs = 0
    others = 0
    with open(os.path.join(cache_dir, 'main_file_cache.dat2'), 'rb') as dat:
        for gid in sorted(idx):
            length, start = idx[gid]
            payload, ctype, trailer = container_payload(read_chain(dat, gid, length, start))
            if payload[:2] == b'\xff\xd8':
                jpegs += 1
                with open(os.path.join(out_dir, 'g%d.jpg' % gid), 'wb') as f:
                    f.write(payload)
                print('group %-5d jpeg  ctype=%d trailer=%d bytes=%d' % (gid, ctype, trailer, len(payload)))
            else:
                others += 1
                print('group %-5d sprite ctype=%d trailer=%d bytes=%d' % (gid, ctype, trailer, len(payload)))
    print('--- jpegs=%d sprite-sets=%d ---' % (jpegs, others))


def load(path):
    """Reads one dump into (width, height, RGB bytes)."""
    with open(path, 'rb') as f:
        raw = f.read()
    return (raw[0] << 8) | raw[1], (raw[2] << 8) | raw[3], raw[4:]


def compare(dir_a, suf_a, dir_b, suf_b):
    """Scores every pair of dumps that both directories hold."""
    total = identical = 0
    absolute_error = channels = 0
    worst = files = 0
    for name in sorted(os.listdir(dir_a)):
        if not name.endswith(suf_a):
            continue
        base = name[:-len(suf_a)]
        other = os.path.join(dir_b, base + suf_b)
        if not os.path.exists(other):
            print('%-8s NO COUNTERPART' % base)
            continue
        aw, ah, ab = load(os.path.join(dir_a, name))
        bw, bh, bb = load(other)
        if (aw, ah) != (bw, bh):
            print('%-8s GEOMETRY %dx%d vs %dx%d' % (base, aw, ah, bw, bh))
            continue
        n = aw * ah
        same = sum(1 for i in range(n) if ab[i * 3:i * 3 + 3] == bb[i * 3:i * 3 + 3])
        deltas = [abs(ab[i] - bb[i]) for i in range(n * 3)]
        files += 1
        total += n
        identical += same
        absolute_error += sum(deltas)
        channels += len(deltas)
        worst = max(worst, max(deltas))
        print('%-8s %4dx%-4d pixels=%-7d identical=%-7d (%6.2f%%) maxChannelDelta=%d'
              % (base, aw, ah, n, same, 100.0 * same / n, max(deltas)))
    print('--- files=%d pixels=%d identical=%d (%.4f%%) meanAbsChannelError=%.4f worstChannelDelta=%d ---'
          % (files, total, identical, 100.0 * identical / max(total, 1),
             absolute_error / max(channels, 1), worst))


if __name__ == '__main__':
    if sys.argv[1] == 'extract':
        extract(sys.argv[2], sys.argv[3])
    elif sys.argv[1] == 'compare':
        compare(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5])
    else:
        raise SystemExit(__doc__)
