#!/usr/bin/env python3
"""
DiakonieWhisper Model Setup

Exports whisper-small to sherpa-onnx ONNX format, downloads auxiliary models
(VAD, pyannote segmentation, 3D-Speaker), and uploads all 6 files to
HuggingFace under NilEneb/DiakonieWhisper-models.

Run once. Duration: ~30-90 min depending on hardware and network.

Prerequisites:
    pip install openai-whisper onnx onnxruntime huggingface_hub requests tqdm
    huggingface-cli login   # or export HF_TOKEN=hf_...
"""

import os
import sys
import subprocess
import shutil
import tarfile
import bz2
import io
import tempfile
import glob
from pathlib import Path

import requests
from tqdm import tqdm

HF_REPO_ID = "NilEneb/DiakonieWhisper-models"
SCRIPT_DIR = Path(__file__).parent
OUTPUT_DIR = SCRIPT_DIR / "models_output"
SHERPA_TOOLS_DIR = Path(tempfile.gettempdir()) / "sherpa-onnx-tools"

# Final filenames must match ModelDownloader.cs exactly
WHISPER_FILES = {
    "small-encoder.onnx",
    "small-decoder.onnx",
    "small-tokens.txt",
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

def run(cmd, **kwargs):
    print(f"  $ {' '.join(str(c) for c in cmd)}")
    subprocess.run(cmd, check=True, **kwargs)


def download(url: str, dest: Path, desc: str = ""):
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


def extract_from_tarbz2(archive_path: Path, entry_path: str, dest: Path):
    print(f"  extracting {entry_path} from {archive_path.name}...")
    with open(archive_path, "rb") as fh:
        data = fh.read()
    with bz2.open(io.BytesIO(data)) as bz2_stream:
        with tarfile.open(fileobj=bz2_stream) as tar:
            normalized = entry_path.replace("\\", "/").rstrip("/")
            for member in tar.getmembers():
                name = member.name.replace("\\", "/").rstrip("/")
                if name.lower() == normalized.lower():
                    f = tar.extractfile(member)
                    if f is None:
                        raise RuntimeError(f"Cannot read {member.name} from archive")
                    dest.write_bytes(f.read())
                    print(f"  -> extracted {dest.name} ({dest.stat().st_size:,} bytes)")
                    return
    raise FileNotFoundError(f"Entry '{entry_path}' not found in archive")


# ── Step 1: Export Whisper-small → ONNX ───────────────────────────────────

def export_whisper():
    print("\n=== Step 1: Export whisper-small to ONNX ===")

    # Sparse-clone just the whisper export scripts
    if SHERPA_TOOLS_DIR.exists():
        shutil.rmtree(SHERPA_TOOLS_DIR)

    run(["git", "clone", "--depth", "1", "--filter=blob:none", "--sparse",
         "https://github.com/k2-fsa/sherpa-onnx.git", str(SHERPA_TOOLS_DIR)])
    run(["git", "sparse-checkout", "set", "scripts/whisper"],
        cwd=SHERPA_TOOLS_DIR)

    export_script = SHERPA_TOOLS_DIR / "scripts" / "whisper" / "export-onnx.py"
    if not export_script.exists():
        raise FileNotFoundError(f"Export script not found: {export_script}")

    export_cwd = OUTPUT_DIR / "whisper_export"
    export_cwd.mkdir(parents=True, exist_ok=True)

    run([sys.executable, str(export_script), "--model", "small"], cwd=export_cwd)

    # Locate generated files — naming varies by sherpa-onnx version
    encoder = _find_file(export_cwd, "*encoder*.onnx", "*encoder.onnx")
    decoder = _find_file(export_cwd, "*decoder*.onnx", "*decoder.onnx")
    tokens  = _find_file(export_cwd, "tokens.txt", "multilingual.txt", "small-tokens.txt")

    if not encoder:
        raise FileNotFoundError("Encoder ONNX not found after export")
    if not decoder:
        raise FileNotFoundError("Decoder ONNX not found after export")
    if not tokens:
        raise FileNotFoundError("Tokens file not found after export")

    shutil.copy2(encoder, OUTPUT_DIR / "small-encoder.onnx")
    shutil.copy2(decoder, OUTPUT_DIR / "small-decoder.onnx")
    shutil.copy2(tokens,  OUTPUT_DIR / "small-tokens.txt")
    print(f"  Whisper files ready:\n"
          f"    small-encoder.onnx ({(OUTPUT_DIR / 'small-encoder.onnx').stat().st_size:,} bytes)\n"
          f"    small-decoder.onnx ({(OUTPUT_DIR / 'small-decoder.onnx').stat().st_size:,} bytes)\n"
          f"    small-tokens.txt   ({(OUTPUT_DIR / 'small-tokens.txt').stat().st_size:,} bytes)")


def _find_file(base: Path, *patterns: str) -> Path | None:
    for pattern in patterns:
        matches = list(base.rglob(pattern))
        if matches:
            return matches[0]
    return None


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

    # Pyannote via tar.bz2
    pyannote_dest = OUTPUT_DIR / PYANNOTE_DEST_NAME
    if pyannote_dest.exists():
        print(f"  {PYANNOTE_DEST_NAME} already present, skipping")
    else:
        archive_path = OUTPUT_DIR / "pyannote.tar.bz2"
        print("  Downloading pyannote segmentation archive...")
        download(PYANNOTE_ARCHIVE_URL, archive_path, desc="pyannote archive")
        extract_from_tarbz2(archive_path, PYANNOTE_ARCHIVE_ENTRY, pyannote_dest)
        archive_path.unlink()


# ── Step 3: Upload to HuggingFace ──────────────────────────────────────────

def upload_to_huggingface():
    print(f"\n=== Step 3: Upload to HuggingFace ({HF_REPO_ID}) ===")
    try:
        from huggingface_hub import HfApi, create_repo
    except ImportError:
        sys.exit("ERROR: pip install huggingface_hub")

    api = HfApi()

    # Create repo if missing (public, model type)
    try:
        create_repo(HF_REPO_ID, repo_type="model", private=False, exist_ok=True)
        print(f"  Repo {HF_REPO_ID} ready")
    except Exception as e:
        sys.exit(f"ERROR creating repo: {e}")

    expected = [
        "small-encoder.onnx",
        "small-decoder.onnx",
        "small-tokens.txt",
        "silero_vad.onnx",
        PYANNOTE_DEST_NAME,
        "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
    ]

    for fname in expected:
        local = OUTPUT_DIR / fname
        if not local.exists():
            print(f"  WARNING: {fname} not found in {OUTPUT_DIR}, skipping upload")
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

    print(f"\n  All files uploaded to https://huggingface.co/{HF_REPO_ID}")


# ── Verification ────────────────────────────────────────────────────────────

def verify():
    print("\n=== Verification ===")
    expected = [
        "small-encoder.onnx",
        "small-decoder.onnx",
        "small-tokens.txt",
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
        print("\nWhisper ONNX files already present — skipping export step")
    else:
        export_whisper()

    download_aux_models()

    if not verify():
        sys.exit("ERROR: Not all model files were generated. Check logs above.")

    upload_to_huggingface()

    print("\n=== Setup complete ===")
    print(f"Update ModelDownloader.cs to use:")
    print(f"  https://huggingface.co/{HF_REPO_ID}/resolve/main/<filename>")


if __name__ == "__main__":
    main()
