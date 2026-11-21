#!/usr/bin/env python3
"""Block list of a game pack for the updater's delta download (src/View/PckDelta.cs reads it).

Line 1: "pckblocks 1 <size> <block size> <sha256 of the whole file>"; then one line per block of the file:
"<weak checksum, 8 hex digits> <sha256 of the block>". The weak checksum is rsync's: a = sum of the bytes mod 65521,
b = sum of (n - k) * byte[k] mod 65521 for a block of n bytes, weak = b << 16 | a. The updater rolls it over its own copy
of the pack to find every block it already has, wherever it now sits, and fetches only the rest.
"""
import hashlib
import sys

M = 65521
BLOCK = 65536


def weak(block: bytes) -> int:
    n = len(block)
    a = sum(block) % M
    b = sum((n - k) * x for k, x in enumerate(block)) % M
    return (b << 16) | a


def main(path: str) -> None:
    data = open(path, "rb").read()
    out = [f"pckblocks 1 {len(data)} {BLOCK} {hashlib.sha256(data).hexdigest()}"]
    for i in range(0, len(data), BLOCK):
        block = data[i:i + BLOCK]
        out.append(f"{weak(block):08x} {hashlib.sha256(block).hexdigest()}")
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main(sys.argv[1])
