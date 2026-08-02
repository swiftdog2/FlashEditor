# XTEA keys

`xteas.json` holds the XTEA keys for the **revision 639** map index (index 5). Map archives
are the only encrypted content in a 639 cache; without these keys they cannot be read by
anything, including the game client.

## Provenance

Downloaded from the [OpenRS2 archive](https://archive.openrs2.org/), **cache id 1194**,
build 639, dated 2011-02-23. Retrieved from:

```
https://archive.openrs2.org/caches/runescape/1194/keys.json
```

The file is that response, filtered to entries for index 5. Its shape is OpenRS2's own:

```json
[ { "archive": 5, "group": 1, "name_hash": -1153472937,
    "name": "l40_55", "mapsquare": 10295, "key": [k0, k1, k2, k3] } ]
```

Note that `archive` is the **index** and `group` is the **archive id** within it. See
`XTEAKeyTable.LoadFromArray`, which special-cases this dialect - reading `archive` as the
archive id collapses the whole file onto a single entry.

## That these keys belong to this build

All 1,587 entries were matched against the reference cache's index 5 reference table by
name hash: every one of the 1,587 `group` ids agrees with the archive id carrying that
hash, with no disagreements. The keys then decrypt real archives, which is the check that
actually matters - see `CapturedCacheBytesTests` and
`RealCacheConformanceTests.EncryptedMapArchives_DecryptWithTheKeysForThisBuild`.

## Coverage, and what is missing

Against the reference cache: **598 of its 659 encrypted map archives decrypt.** The other
61 have no key here, and none is recoverable from any published dump - brute-forcing all
25,959 keys OpenRS2 holds across its entire archive solves none of them. Their name hashes
do not appear in the build 639 dump at all, so those map squares' keys look never to have
been recovered.

This file is therefore known-incomplete by design. Treat a missing key as "cannot be read",
not as a defect.

## How it is found

`XTEAKeyTable.FindKeyFile` probes the cache directory and its parent, each combined with
the subdirectories `""`, `xteas` and `keys`, for the names `xteas.json`, `xtea.json`,
`keys.json` and `xteakeys.json`. With a cache at the repository root as `cache/`, this file
is discovered as the parent's `xteas/xteas.json`. Keep the file name as-is: renaming it to
something build-specific would stop it being found.

## Other builds

Only revision 639 is committed, because that is what this editor targets. For another
build, find its cache in `https://archive.openrs2.org/caches.json` and fetch that id's
`keys.json`. Keys are per map square and change only when a square is re-encrypted, so a
neighbouring build's keys often work - but that is a convenience, not a guarantee, and the
only proof is that an archive decrypts and decompresses to its declared length.
