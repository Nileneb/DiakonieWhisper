#!/usr/bin/env python3
"""
DiakonieWhisper LLM Model Download

Downloads a GGUF model for offline use with LLMUnity (llama.cpp).
Default: Qwen2.5-1.5B-Instruct Q4_K_M — optimized for CPU, ~1 GB, good German.

Prerequisites:
    pip install huggingface_hub requests tqdm

Usage:
    python tools/download_llm_model.py
    python tools/download_llm_model.py --model phi3   # alternative models
    python tools/download_llm_model.py --list         # list available models
"""

import argparse
import sys
from pathlib import Path

MODELS = {
    "nomic-embed": {
        "repo": "nomic-ai/nomic-embed-text-v1.5-GGUF",
        "file": "nomic-embed-text-v1.5.Q4_K_M.gguf",
        "size_mb": 83,
        "description": "nomic-embed-text v1.5 Q4_K_M — dediziertes Embedding-Modell, ~83MB, für RAG",
    },
    "qwen2.5-1.5b": {
        "repo": "Qwen/Qwen2.5-1.5B-Instruct-GGUF",
        "file": "qwen2.5-1.5b-instruct-q4_k_m.gguf",
        "size_mb": 986,
        "description": "Qwen2.5 1.5B Q4_K_M — gut auf Deutsch, ~1GB, empfohlen für CPU",
    },
    "qwen2.5-3b": {
        "repo": "Qwen/Qwen2.5-3B-Instruct-GGUF",
        "file": "qwen2.5-3b-instruct-q4_k_m.gguf",
        "size_mb": 1868,
        "description": "Qwen2.5 3B Q4_K_M — bessere Qualität, ~1.9GB, für GPU empfohlen",
    },
    "phi3": {
        "repo": "microsoft/Phi-3-mini-4k-instruct-gguf",
        "file": "Phi-3-mini-4k-instruct-q4.gguf",
        "size_mb": 2200,
        "description": "Phi-3 mini 4K Q4 — stark auf Englisch, ~2.2GB",
    },
    "llama3.2-1b": {
        "repo": "bartowski/Llama-3.2-1B-Instruct-GGUF",
        "file": "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
        "size_mb": 780,
        "description": "Llama 3.2 1B Q4_K_M — kleinste Option, ~0.8GB",
    },
}

DEFAULT_MODEL = "qwen2.5-1.5b"


def list_models():
    print("Verfügbare Modelle:\n")
    for key, m in MODELS.items():
        marker = " (Standard)" if key == DEFAULT_MODEL else ""
        print(f"  {key}{marker}")
        print(f"    {m['description']}")
        print(f"    Repo: {m['repo']} / {m['file']}")
        print()


def download(model_key: str, output_dir: Path):
    if model_key not in MODELS:
        sys.exit(f"Unbekanntes Modell '{model_key}'. Optionen: {', '.join(MODELS)}")

    m = MODELS[model_key]
    output_dir.mkdir(parents=True, exist_ok=True)
    dest = output_dir / m["file"]

    if dest.exists():
        size_mb = dest.stat().st_size / 1024 / 1024
        print(f"Bereits vorhanden: {dest} ({size_mb:.0f} MB)")
        print(f"\nInspector-Pfad: {dest}")
        return dest

    print(f"Download: {m['repo']} / {m['file']}")
    print(f"Geschätzte Größe: ~{m['size_mb']} MB")
    print(f"Ziel: {dest}\n")

    try:
        from huggingface_hub import hf_hub_download
    except ImportError:
        sys.exit("Fehler: huggingface_hub nicht installiert.\nBitte: pip install huggingface_hub")

    try:
        local_path = hf_hub_download(
            repo_id=m["repo"],
            filename=m["file"],
            local_dir=str(output_dir),
            local_dir_use_symlinks=False,
        )
        print(f"\nOK: {local_path}")
        final = output_dir / m["file"]
        if Path(local_path) != final and Path(local_path).exists():
            import shutil
            shutil.move(local_path, final)
        print(f"\nInspector-Pfad (im LLM-Component setzen):\n  {final}")
        return final
    except Exception as e:
        sys.exit(f"Download fehlgeschlagen: {e}")


def main():
    parser = argparse.ArgumentParser(description="DiakonieWhisper GGUF Downloader")
    parser.add_argument(
        "--model", "-m",
        default=DEFAULT_MODEL,
        help=f"Modell-Key (Standard: {DEFAULT_MODEL})",
    )
    parser.add_argument(
        "--list", "-l",
        action="store_true",
        help="Alle verfügbaren Modelle auflisten",
    )
    parser.add_argument(
        "--output", "-o",
        default=None,
        help="Ausgabeverzeichnis (Standard: tools/models_output/llm/)",
    )
    args = parser.parse_args()

    if args.list:
        list_models()
        return

    output_dir = Path(args.output) if args.output else Path(__file__).parent / "models_output" / "llm"
    download(args.model, output_dir)


if __name__ == "__main__":
    main()
