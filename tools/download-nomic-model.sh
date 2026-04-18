#!/bin/bash
# Downloads the nomic-embed-text-v1.5 int8 quantized ONNX model from HuggingFace
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MODEL_DIR="$SCRIPT_DIR/../VectorSharp.Embedding.NomicEmbed/Models"
mkdir -p "$MODEL_DIR"

# Download model_int8.onnx (137MB)
if [ ! -f "$MODEL_DIR/model_int8.onnx" ]; then
    echo "Downloading model_int8.onnx (137MB)..."
    curl -L -o "$MODEL_DIR/model_int8.onnx" \
        "https://huggingface.co/nomic-ai/nomic-embed-text-v1.5/resolve/main/onnx/model_quantized.onnx"
    echo "Downloaded model_int8.onnx"
else
    echo "model_int8.onnx already exists, skipping"
fi

# Download tokenizer.json (HuggingFace tokenizer config)
if [ ! -f "$MODEL_DIR/tokenizer.json" ]; then
    echo "Downloading tokenizer.json..."
    curl -L -o "$MODEL_DIR/tokenizer.json" \
        "https://huggingface.co/nomic-ai/nomic-embed-text-v1.5/resolve/main/tokenizer.json"
    echo "Downloaded tokenizer.json"
else
    echo "tokenizer.json already exists, skipping"
fi

echo "Done. Model files are in $MODEL_DIR"
