# Offline models and inference

## Translation

CTranslate2 **4.8.4**, official Windows x64 wheel, with OPUS-MT **eng-zho** and **zho-eng**, original `opus-2020-07-17` archives from the University of Helsinki. Both original archives contain **CC BY 4.0**. Attribution and modifications are recorded in `licenses/OPUS-MT-ATTRIBUTION.txt`.

Original archive sizes: 288,947,487 and 288,654,138 bytes. Downloaded files are build inputs; they are excluded from distribution. INT8 conversion plus configuration, vocabularies and SentencePiece tokenizers totals **171,387,305 bytes**. All original and converted SHA-256 values are pinned in `assets.json`. Conversion is reproducible with the embedded runtime and does not require PyTorch or Transformers. English-to-Chinese uses the required `>>cmn_Hans<<` / `>>cmn_Hant<<` source language token. SentencePiece loads model bytes directly to support Unicode Windows folder names.

**Fast** uses beam size **1**, **High quality** uses **4**. Fast streams token snapshots; quality streams completed source segments because CTranslate2 callbacks require beam 1. The same weights are used, without a second translation model. Separate mode/target/text cache keys prevent mixing modes. No score/attention outputs, one translation worker, intra-op threads equal to the physical CPU core count. Long inputs are split at sentence/word/token boundaries (up to 192 source tokens per decoding segment). The output cap scales with input length, up to 512 tokens; reaching it is reported as truncated rather than silently accepted. The API accepts up to 10,000 characters per request; the workspaces split longer inputs.

Both models are loaded and warmed in a background process. They persist across calls and are released after ten idle minutes. A combined LRU cache keeps up to 512 complete translations/segments, bounded to 300,000 output characters. No translation cache is written to disk. Decoder attention/KV state belongs to its source input; different selections do not share incompatible decoder state. Cross-request acceleration comes from model reuse and exact text/segment caches.

The official wheel dispatches supported CPU instructions automatically (including AVX2/AVX-512). `CT2_FORCE_CPU_ISA` is left unset by the application. The local machine selects **AVX2**. CPU computation uses **INT8/FP32**, with the actual compute type reported in `/health`.

NVIDIA devices are inspected using the installed driver. The largest-memory NVIDIA device is tried first. CUDA initialization and a real warm-up kernel must succeed before the backend is advertised as accelerated. The default pack does not include CUDA/cuBLAS; compatible **CUDA 12** libraries installed on the machine or placed in `MiniAI/runtime/cuda` are discovered automatically. Missing libraries, insufficient drivers or an inference failure trigger CPU fallback. AMD/Intel GPU backends are not shipped. No local physical discrete-GPU performance result is claimed.

The small OPUS models prioritize latency and package size. Terminology and complex scientific phrasing may be weaker than the previous Hy-MT2 LLM. The new default is Chinese/English; older releases retain the earlier multilingual model. This is not OCR or mathematical formula recognition.

## OCR

**PP-OCRv6 medium**, detection + Chinese/English recognition, FP32 ONNX, about **138.82 MB** of model/dictionary files. **ONNX Runtime 1.20.1 CPU** and its app-local dependencies are bundled. OCR intra-op threads also follow the physical core count. OCR and translation share the local authenticated API, but their model weights and runtime backends remain separate.

A second **PP-OCRv6 tiny** detector/recognizer/dictionary adds **6,346,587 bytes** for fast screenshots/photos. The tiny-specific dictionary is pinned separately. API OCR defaults to **quality/medium**, preserving PDF searchable-layer and Word-export behavior; callers can choose `mode: fast`. Older packs without tiny fall back to medium and report the actual OCR mode. Both OCR models unload after five idle minutes.

Printed scans, photographs and screenshots are supported. Dense/rotated layouts, handwriting, mathematical notation, uncommon fonts and low-quality images can fail. See actual fixture coverage in `Verification.md`.

## Runtime and packaging

The package includes a private Python **3.13.17** embeddable runtime, CTranslate2, SentencePiece **0.2.2**, NumPy **2.5.4** and PyYAML **6.0.3**. Users do not install Python or packages. Build inputs, tests, headers, type stubs and unused cuDNN binaries are excluded from the portable payload; required notices are retained. The native import audit includes DLL, EXE and PYD files and the isolated test uses a system-only PATH.
