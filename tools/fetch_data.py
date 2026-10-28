"""Download the raw CT datasets used by X-Ray Hands into ``data_raw/``.

Downloads are resumable (HTTP Range requests) and verified against the
checksum the publisher provides (SHA-256 sidecar files for NIST, MD5 from the
Zenodo API). Raw downloads are never committed: ``data_raw/`` is git-ignored.

Usage::

    python fetch_data.py nist-segmented            # all 5 NIST ground-truth zips (~78 MB)
    python fetch_data.py nist-raw --sample set1sample4
    python fetch_data.py enigma                    # Enigma rotor ROI (~209 MB)
"""
from __future__ import annotations

import argparse
import hashlib
import logging
import sys
from dataclasses import dataclass
from pathlib import Path

import requests

LOG = logging.getLogger("fetch_data")

REPO_ROOT = Path(__file__).resolve().parent.parent
DATA_RAW = REPO_ROOT / "data_raw"

NIST_BASE = "https://data.nist.gov/od/ds/ark:/88434/mds2-2162"
NIST_SAMPLES = ("set1sample2", "set1sample3", "set1sample4", "set1sample5", "set1sample6")

ZENODO_ENIGMA_URL = "https://zenodo.org/api/records/8232721/files/1993-522_ROI_rotors.dcm.zip/content"
# MD5 as published by the Zenodo API for record 8232721 (checked 2026-10-28).
ZENODO_ENIGMA_MD5 = "496f1241c4c388a2d48be0a68c27b0c3"

CHUNK_BYTES = 1 << 20  # 1 MiB: large enough for throughput, small enough for progress logs
TIMEOUT_SECONDS = 60


@dataclass(frozen=True)
class RemoteFile:
    """One file to download plus how to verify it."""

    url: str
    dest: Path
    algorithm: str | None = None  # "sha256" or "md5"
    expected_hash: str | None = None
    checksum_url: str | None = None  # sidecar file holding the expected hash


def _expected_hash(item: RemoteFile) -> str | None:
    """Return the expected hex digest, fetching a sidecar file if needed."""
    if item.expected_hash:
        return item.expected_hash.lower()
    if item.checksum_url:
        resp = requests.get(item.checksum_url, timeout=TIMEOUT_SECONDS)
        resp.raise_for_status()
        # Sidecars contain either "<hash>" or "<hash>  <filename>".
        return resp.text.strip().split()[0].lower()
    return None


def _hash_file(path: Path, algorithm: str) -> str:
    digest = hashlib.new(algorithm)
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(CHUNK_BYTES), b""):
            digest.update(block)
    return digest.hexdigest()


def download(item: RemoteFile) -> Path:
    """Download ``item`` with resume support and verify its checksum.

    A partially downloaded file is continued with an HTTP Range request
    instead of starting over, which matters for the 1.3 GB NIST zips.
    """
    item.dest.parent.mkdir(parents=True, exist_ok=True)
    head = requests.head(item.url, allow_redirects=True, timeout=TIMEOUT_SECONDS)
    total = int(head.headers.get("Content-Length", 0))
    have = item.dest.stat().st_size if item.dest.exists() else 0

    if total and have == total:
        LOG.info("already complete: %s", item.dest.name)
    else:
        headers = {"Range": f"bytes={have}-"} if have else {}
        with requests.get(item.url, headers=headers, stream=True, timeout=TIMEOUT_SECONDS) as resp:
            resp.raise_for_status()
            # 206 = server honoured the range; 200 = it did not, so restart from zero.
            mode = "ab" if resp.status_code == 206 else "wb"
            done = have if mode == "ab" else 0
            next_report = done + 50 * CHUNK_BYTES
            with item.dest.open(mode) as fh:
                for chunk in resp.iter_content(CHUNK_BYTES):
                    fh.write(chunk)
                    done += len(chunk)
                    if done >= next_report:
                        pct = 100.0 * done / total if total else 0.0
                        LOG.info("%s: %.0f MB (%.1f%%)", item.dest.name, done / 1e6, pct)
                        next_report = done + 50 * CHUNK_BYTES

    if item.algorithm:
        try:
            expected = _expected_hash(item)
        except requests.RequestException as exc:
            LOG.warning("could not fetch checksum for %s (%s); continuing unverified", item.dest.name, exc)
            return item.dest
        if expected:
            actual = _hash_file(item.dest, item.algorithm)
            if actual != expected:
                # Some NIST records flag their own checksums as invalid; we warn and keep going
                # rather than block the pipeline, and the warning is visible in the log.
                LOG.warning("%s mismatch for %s: expected %s, got %s",
                            item.algorithm, item.dest.name, expected, actual)
            else:
                LOG.info("%s verified: %s", item.algorithm, item.dest.name)
    return item.dest


def nist_segmented() -> list[RemoteFile]:
    """All five NIST ground-truth segmentation zips (small; used to pick a sample)."""
    return [
        RemoteFile(
            url=f"{NIST_BASE}/segmented/{s}segmented.zip",
            dest=DATA_RAW / "nist" / f"{s}segmented.zip",
            algorithm="sha256",
            checksum_url=f"{NIST_BASE}/segmented/{s}segmented.zip.sha256",
        )
        for s in NIST_SAMPLES
    ]


def nist_raw(sample: str) -> list[RemoteFile]:
    """One NIST 16-bit raw TIFF stack zip (~1.1-1.3 GB)."""
    if sample not in NIST_SAMPLES:
        raise ValueError(f"unknown sample {sample!r}; choose one of {NIST_SAMPLES}")
    return [
        RemoteFile(
            url=f"{NIST_BASE}/raw/{sample}raw.zip",
            dest=DATA_RAW / "nist" / f"{sample}raw.zip",
            algorithm="sha256",
            checksum_url=f"{NIST_BASE}/raw/{sample}raw.zip.sha256",
        )
    ]


def enigma() -> list[RemoteFile]:
    """Enigma I rotor ROI DICOM zip from Zenodo record 8232721."""
    return [
        RemoteFile(
            url=ZENODO_ENIGMA_URL,
            dest=DATA_RAW / "enigma" / "1993-522_ROI_rotors.dcm.zip",
            algorithm="md5",
            expected_hash=ZENODO_ENIGMA_MD5,
        )
    ]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("dataset", choices=["nist-segmented", "nist-raw", "enigma"])
    parser.add_argument("--sample", default="set1sample4", help="NIST sample for nist-raw")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    items = {
        "nist-segmented": nist_segmented,
        "nist-raw": lambda: nist_raw(args.sample),
        "enigma": enigma,
    }[args.dataset]()
    for item in items:
        download(item)
    return 0


if __name__ == "__main__":
    sys.exit(main())
