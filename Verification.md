# Verification — Mini-AI 1.0.1

The 1.0.1 revision adds glyph-relative row geometry, fraction/bracket protection,
confirmed margin-number separation, the model's native 512-position decoding
limit, bulk pixel conversion, and additional MathML notation. Model files and
translation weights are unchanged.

New local checks on 2026-11-04:

- 40 formula, MathML, clipboard and compact-dialog assertions, including six-row
  coverage of every dark pixel and keeping a fraction intact.
- 14 formula crops from 11 private papers across IEEE, Elsevier, Springer,
  Wiley, Taylor & Francis, MDPI and Oxford. Under a 20-second per-crop bound,
  the previous build returned TeX for 4 crops, and the revision for 9. The four
  previously returned strings were unchanged. **This is completion coverage,
  not a recognition-accuracy score.** Several returned equations still contain
  substitutions. A six-line boundary-condition case improved; dense matrices,
  hybrid cases, and the calligraphic L/V stress case remain problematic.
- Public/generated controls retain the exact upstream fraction transcription,
  a correct three-row control, and the original single-line paths. The six-row
  control now preserves six rows, but still misreads some individual glyphs.
- Lossless packaging was audited against the existing 719,959,648-byte EXE:
  98.4% of compressed payload bytes are models. The only identical files were
  small runtime notices. Sampled byte/bit/XOR transforms gave no consistent
  improvement; doubling 8 MiB frames saved only 32 bytes per 16 MiB sample.
  The established codec and all original model hashes are retained.

The following broader validation belongs to the 1.0.0 baseline and is retained
for provenance; it was not all repeated for this revision.

Local Windows validation on an i7-8565U (4 physical cores / 8 logical threads), using the real bundled models and native runtime. `verification.json` records counts and limitations.

- **202 focused checks:** service authentication/host/origin validation, OCR, streaming and cancellation, every advertised target, formula structure/MathML/clipboard, desktop selection, bubble lifecycle and translated images. Fast and quality modes both execute real Hy inference.
- **Mini PDF integration:** 847 native reader checks and 33 independent PDF-engine checks passed. Another 21 checks cover the new file/screenshot input panel, real OCR-to-Hy translation, source switching/cancellation, Unicode and GBK input, image isolation from PDF edits, compact layout, and the high-contrast screen cursor. Three real selections from a local IEEE double-column paper passed 22 additional assertions: full source, line-end word reconstruction, real translation, pinned opacity, repeated source toggling and resized close-button visibility. The private paper and extracted passages are not published.
- Formula export checks cover fractions, roots, Greek symbols, accents, matrices, cases, scripts and unsupported/malformed input limits. Ordinary prose remains text. Dedicated formula API checks verify two exponent groups and returned geometry, plus an exactly transcribed upstream fraction fixture; **they are not a claim that every base symbol was recognized correctly**. The clean test expression still produced substitutions, so manual verification/editing remains necessary. The existing model and preprocessing are retained; the formula dialog keeps the original for review and provides an editable draft after failed recognition.
- CPU tuning compared 4 and 8 inference threads on the development laptop. Four were faster on the measured passage, so physical cores are the default. Background initialization, a real prompt warmup, persistent inference, token streaming, cancellation and result/prompt caches are enabled. Timing varies with hardware, text and warm state.
- The native source workflow builds baseline/AVX2/Vulkan with embedded Web UI, SSL and unused subprocess routing disabled, and runs quantization arithmetic checks. Optional GPU loading checks exact STQ-to-FP16 weight expansion. A local UHD 620 run completed but was slower; further GPU performance tests were stopped at the user's request. CPU is the release default.

The final EXE is separately tested in an empty Unicode/spaces folder with a system-only PATH. `portable-checks.json` attached to the release records extraction/hash checks, missing-DLL and damaged-model repair, actual OCR/translation, shared-client leases, normal cleanup, result retention and preservation of user-added files. Extraction uses only Windows Cabinet compression and verifies original payload hashes. These checks do not require Python on the application's PATH; Python is used only by the test driver.

MathType itself and discrete GPUs were unavailable locally. All target options completing a short smoke translation does not establish native-language accuracy. Handwriting, dense or low-quality scans, unusual mathematical notation, unsupported TeX and complex layouts remain difficult. Recognition confidence is not a correctness guarantee.

Developer entry points: `tools/test_all.ps1 -Desktop`, `tools/test_portable.py`, reader `build.ps1 -Test`, and optional `test_translation_paper.ps1` with a user-supplied private path. Do not run clipboard/GUI suites concurrently in the same interactive desktop session.
