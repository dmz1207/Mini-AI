# Mini-AI 1.0.0

First formal release: a self-contained offline OCR, formula and translation tool, with Mini PDF integration.

- Compact editable workspace, photo/drop/clipboard OCR, screenshot extraction and translated-image preview.
- Shared resizable translation bubble with readable streaming text, exact substring translation, pin/close controls and stale-request cancellation.
- Hy-MT2 1.8B 1.25-bit model; all 38 supported target options; fast and quality decoding with independent caches.
- CPU-first inference using physical cores, AVX2 detection and a baseline fallback; background low-priority initialization and prompt/model reuse. Optional Vulkan backend is reproducibly built without a local SDK requirement.
- Cropped formula recognition, editable LaTeX, offline visual preview and MathML clipboard export for compatible MathType versions. Multiple formulas keep separate corrections.
- Windows-native lossless compression; every decoded payload is checked; owned runtime files are cleaned after the final client exits, while OCR/translation results are preserved.
- Fresh-source asset installer and native dependency audit; service, stream/cancel, multilingual, formula, desktop, image and isolated portable checks. Actual IEEE paper selections were tested through Mini PDF without uploading private papers.

Accuracy depends on language, source layout and image quality. Recognition/translation is not infallible. The installed MathType application and discrete GPUs were not available for local validation. See Verification.md for concrete coverage.
