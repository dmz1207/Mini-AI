# Mini-AI 1.1.1

Fix a formula-worker pipe lifetime race that could terminate the service and leave Mini PDF reporting a closed connection.

- Complete asynchronous pipe operations through callback-backed tasks, with faults observed after cancellation. Keep cancellation, timeouts and worker restart.
- Add a production-transport regression: the previous build reproduces the same fatal SafeHandle exception; the corrected build passes connect/read/write cancellation and recovery, 1,000 fragmented exchanges, bounded handle usage and worker-exit recovery.
- Model weights, CPU kernels and decoding settings are unchanged.

---

# Mini-AI 1.1.0

Replace formula recognition with Texo-distill Q8_0 while retaining PP-OCRv6 text recognition and Hy-MT2 translation.

- Use a pinned 22.7 MB quantized formula model and a formula-only native library instead of the previous formula encoder/decoder/resizer files.
- Cache fixed decoder weights and use persistent workers with a physical-core default. Formula decoder kernels select AVX-512 or AVX2+FMA when supported; unsupported instructions are never forced. AVX-512 is compiled but was not executable on the local i7-8565U.
- Align input preprocessing with Texo and retain confident equation rows, fractions, accents and brackets. Preserve double norm bars in editable MathML.
- Isolate native work in an owned helper so cancellation can interrupt inference and subsequent requests recover. Repeated exact requests still reuse the existing in-memory service cache.
- Include source, hashes, licenses and focused old/new CPU measurements. No FP16 comparison was performed. Publisher-crop completion is reported separately from transcription accuracy; difficult mathematics still needs review.

See Verification.md for actual tested scope. The installed MathType application was not available for a paste test; exported MathML structure and clipboard formats are checked locally.

---

# Mini-AI 1.0.2

Performance and integration maintenance update. Existing OCR, formula and Hy-MT2 weights are unchanged.

- Warm ordinary OCR at low priority; load formula graphs only for an actual formula candidate or an explicit formula request.
- Reuse four recent image replies in memory, keyed by exact image bytes, OCR quality and recognition mode. Concurrent identical requests share one inference. No screenshot results are written to disk.
- Use the physical-core ONNX budget, bounded parallel pixel conversion and sleeping idle model pools. Preserve the established graph optimizations and floating-point preprocessing.
- Include the pinned Vulkan runtime in the complete EXE. Automatic mode prefers a detected discrete GPU, keeps known weak/integrated/software devices on CPU, and falls back to CPU on backend failure. CPU/explicit-device overrides remain available. No local discrete GPU benchmark is claimed.
- Export scriptstyle/smaller-script formulas as editable MathML. Mini PDF 0.12.5 adds capture-to-text/LaTeX and selectable image/native-equation Word export.

Formula symbol recognition still requires review. This update does not retrain, requantize or replace the existing models.

---

# Mini-AI 1.0.1

Formula layout and export maintenance update. The bundled OCR and Hy-MT2 model
weights, translation modes, and supported languages are unchanged.

- Use glyph-relative geometry for long multiline formulas while retaining the
  whole-image path for compact expressions. Protect fraction groups and bracket
  spans; retain small ink components rather than filtering them away.
- Confirm isolated equation numbers with the bundled text recognizer before
  excluding them from formula decoding. Use the formula model's native
  512-position limit and faster bulk pixel access.
- Preserve structured TeX when ordinary OCR disagrees. Fix Unicode superscript
  conversion and export calligraphic/font declarations and over/under annotations
  to editable MathML.
- Add generated multiline regression coverage and keep private paper samples out
  of the repository. See Verification.md for tested scope and known failures.

Dense mathematics and unusual glyphs still need manual review. Geometry improves
layout handling; it does not retrain the existing model or make it Mathpix-level.

## Original 1.0.0 features

First formal release: a self-contained offline OCR, formula and translation tool, with Mini PDF integration.

- Compact editable workspace, photo/drop/clipboard OCR, screenshot extraction and translated-image preview.
- Shared resizable translation bubble with readable streaming text, exact substring translation, pin/close controls and stale-request cancellation.
- Drag from the bubble header and resize from every edge/corner. Screen selection uses a bright green crosshair with a dark outline.
- Hy-MT2 1.8B 1.25-bit model; all 38 supported target options; fast and quality decoding with independent caches.
- CPU-first inference using physical cores, AVX2 detection and a baseline fallback; background low-priority initialization and prompt/model reuse. Optional Vulkan backend is reproducibly built without a local SDK requirement.
- Cropped formula recognition, editable LaTeX, offline visual preview and MathML clipboard export for compatible MathType versions. Multiple formulas keep separate corrections.
- Formula captures open against the original for review; failed recognition keeps an editable TeX draft. Models are unchanged; dense multiline formulas and unusual glyphs remain limited.
- Windows-native lossless compression; every decoded payload is checked; owned runtime files are cleaned after the final client exits, while OCR/translation results are preserved.
- Fresh-source asset installer and native dependency audit; service, stream/cancel, multilingual, formula, desktop, image and isolated portable checks. Actual IEEE paper selections were tested through Mini PDF without uploading private papers.

Accuracy depends on language, source layout and image quality. Recognition/translation is not infallible. The installed MathType application and discrete GPUs were not available for local validation. See Verification.md for concrete coverage.
