#!/usr/bin/env python3
"""
DiakonieWhisper Model Setup

Downloads pre-built sherpa-onnx ONNX models from GitHub releases and uploads
all 6 files to HuggingFace under NilEneb/DiakonieWhisper-models.

No PyTorch / ONNX export required — uses official pre-built binaries.

Run once. Duration: ~5-20 min depending on network speed.

Prerequisites:
    pip install huggingface_hub requests tqdm
    hf login   # Token mit Write-Rechten hinterlegen
"""

import sys
import bz2
import io
import tarfile
from pathlib import Path

# ── Dependency pre-check ────────────────────────────────────────────────────
_MISSING = []
for _mod in ("requests", "tqdm", "huggingface_hub"):
    try:
        __import__(_mod)
    except ImportError:
        _MISSING.append(_mod)
if _MISSING:
    sys.exit(
        f"Fehlende Pakete: {', '.join(_MISSING)}\n"
        f"Bitte installieren:\n  pip install {' '.join(_MISSING)}"
    )

import requests
from tqdm import tqdm

HF_REPO_ID = "NilEneb/DiakonieWhisper-models"
SCRIPT_DIR = Path(__file__).parent
OUTPUT_DIR = SCRIPT_DIR / "models_output"

WHISPER_ARCHIVE_URL = (
    "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/"
    "sherpa-onnx-whisper-medium.tar.bz2"
)

# Final filenames must match ModelDownloader.cs exactly
WHISPER_FILES = {
    "medium-encoder.onnx",
    "medium-decoder.onnx",
    "medium-tokens.txt",
}

DIRECT_DOWNLOAD_URLS = {
    "silero_vad.onnx":
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx",
    "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx":
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/"
        "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
}

PYANNOTE_ARCHIVE_URL = (
    "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/"
    "sherpa-onnx-pyannote-segmentation-3-0.tar.bz2"
)
PYANNOTE_ARCHIVE_ENTRY = "sherpa-onnx-pyannote-segmentation-3-0/model.onnx"
PYANNOTE_DEST_NAME = "sherpa-onnx-pyannote-segmentation-3-0.onnx"


# ── Helpers ────────────────────────────────────────────────────────────────

def download(url: str, dest: Path, desc: str = "") -> Path:
    dest.parent.mkdir(parents=True, exist_ok=True)
    r = requests.get(url, stream=True, timeout=600)
    r.raise_for_status()
    total = int(r.headers.get("content-length", 0))
    with open(dest, "wb") as f, tqdm(
        desc=desc or dest.name, total=total, unit="B", unit_scale=True
    ) as bar:
        for chunk in r.iter_content(chunk_size=1 << 20):
            f.write(chunk)
            bar.update(len(chunk))
    return dest


def extract_entry(archive_path: Path, entry_suffix: str, dest: Path):
    """Extract the first tar entry whose name ends with entry_suffix."""
    print(f"  extracting *{entry_suffix} → {dest.name}...")
    with open(archive_path, "rb") as fh:
        raw = fh.read()
    with bz2.open(io.BytesIO(raw)) as bz:
        with tarfile.open(fileobj=bz) as tar:
            for member in tar.getmembers():
                name = member.name.replace("\\", "/")
                if name.endswith(entry_suffix) and not member.isdir():
                    fobj = tar.extractfile(member)
                    if fobj is None:
                        continue
                    dest.write_bytes(fobj.read())
                    print(f"  -> {dest.name} ({dest.stat().st_size:,} bytes)")
                    return
    raise FileNotFoundError(f"No entry ending with '{entry_suffix}' in {archive_path.name}")


def list_archive(archive_path: Path) -> list[str]:
    """Return all non-directory entry names in a tar.bz2."""
    with open(archive_path, "rb") as fh:
        raw = fh.read()
    with bz2.open(io.BytesIO(raw)) as bz:
        with tarfile.open(fileobj=bz) as tar:
            return [m.name for m in tar.getmembers() if not m.isdir()]


# ── Step 1: Download pre-built Whisper-small ──────────────────────────────

def download_whisper():
    print("\n=== Step 1: Download pre-built sherpa-onnx-whisper-small ===")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    archive = OUTPUT_DIR / "sherpa-onnx-whisper-medium.tar.bz2"
    if not archive.exists():
        print(f"  Downloading from GitHub releases...")
        download(WHISPER_ARCHIVE_URL, archive, desc="whisper-medium archive")
    else:
        print(f"  Archive already present: {archive.name}")

    print("  Archive contents:")
    entries = list_archive(archive)
    for e in entries:
        print(f"    {e}")

    # Extract encoder — looks for *encoder*.onnx or *encoder.onnx
    encoder_suffix = next(
        (e for e in entries if "encoder" in e.lower() and e.endswith(".onnx")), None
    )
    decoder_suffix = next(
        (e for e in entries if "decoder" in e.lower() and e.endswith(".onnx")), None
    )
    tokens_suffix = next(
        (e for e in entries if e.endswith("tokens.txt")), None
    )

    if not encoder_suffix:
        raise FileNotFoundError("No encoder ONNX found in archive")
    if not decoder_suffix:
        raise FileNotFoundError("No decoder ONNX found in archive")
    if not tokens_suffix:
        raise FileNotFoundError("No tokens.txt found in archive")

    extract_entry(archive, encoder_suffix, OUTPUT_DIR / "medium-encoder.onnx")
    extract_entry(archive, decoder_suffix, OUTPUT_DIR / "medium-decoder.onnx")
    extract_entry(archive, tokens_suffix,  OUTPUT_DIR / "medium-tokens.txt")

    archive.unlink()
    print("  Whisper models ready.")


# ── Step 2: Download auxiliary models ──────────────────────────────────────

def download_aux_models():
    print("\n=== Step 2: Download auxiliary models ===")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    for name, url in DIRECT_DOWNLOAD_URLS.items():
        dest = OUTPUT_DIR / name
        if dest.exists():
            print(f"  {name} already present, skipping")
            continue
        print(f"  Downloading {name}...")
        download(url, dest, desc=name)

    pyannote_dest = OUTPUT_DIR / PYANNOTE_DEST_NAME
    if pyannote_dest.exists():
        print(f"  {PYANNOTE_DEST_NAME} already present, skipping")
    else:
        archive_path = OUTPUT_DIR / "pyannote.tar.bz2"
        print("  Downloading pyannote segmentation archive...")
        download(PYANNOTE_ARCHIVE_URL, archive_path, desc="pyannote archive")
        extract_entry(archive_path, "model.onnx", pyannote_dest)
        archive_path.unlink()


# ── Step 3: Upload to HuggingFace ──────────────────────────────────────────

def upload_to_huggingface():
    print(f"\n=== Step 3: Upload to HuggingFace ({HF_REPO_ID}) ===")
    from huggingface_hub import HfApi, create_repo

    api = HfApi()
    try:
        create_repo(HF_REPO_ID, repo_type="model", private=False, exist_ok=True)
        print(f"  Repo {HF_REPO_ID} ready")
    except Exception as e:
        sys.exit(f"ERROR creating repo: {e}")

    expected = [
        "medium-encoder.onnx",
        "medium-decoder.onnx",
        "medium-tokens.txt",
        "silero_vad.onnx",
        PYANNOTE_DEST_NAME,
        "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
    ]

    for fname in expected:
        local = OUTPUT_DIR / fname
        if not local.exists():
            print(f"  WARNING: {fname} not found, skipping")
            continue
        size_mb = local.stat().st_size / 1024 / 1024
        print(f"  Uploading {fname} ({size_mb:.1f} MB)...")
        api.upload_file(
            path_or_fileobj=str(local),
            path_in_repo=fname,
            repo_id=HF_REPO_ID,
            repo_type="model",
        )
        print(f"  ✓ {fname}")

    print(f"\n  All files at https://huggingface.co/{HF_REPO_ID}")


# ── Verification ────────────────────────────────────────────────────────────

def verify() -> bool:
    print("\n=== Verification ===")
    expected = [
        "medium-encoder.onnx",
        "medium-decoder.onnx",
        "medium-tokens.txt",
        "silero_vad.onnx",
        PYANNOTE_DEST_NAME,
        "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
    ]
    ok = True
    for fname in expected:
        p = OUTPUT_DIR / fname
        if p.exists():
            print(f"  ✓ {fname} ({p.stat().st_size:,} bytes)")
        else:
            print(f"  ✗ MISSING: {fname}")
            ok = False
    return ok


# ── Main ───────────────────────────────────────────────────────────────────

def main():
    print("DiakonieWhisper Model Setup")
    print(f"Output directory: {OUTPUT_DIR}")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    whisper_ready = all((OUTPUT_DIR / f).exists() for f in WHISPER_FILES)
    if whisper_ready:
        print("\nWhisper ONNX files already present — skipping download")
    else:
        download_whisper()

    download_aux_models()

    if not verify():
        sys.exit("ERROR: Not all model files present. Check logs above.")

    upload_to_huggingface()

    print("\n=== Setup complete ===")


if __name__ == "__main__":
    main()
