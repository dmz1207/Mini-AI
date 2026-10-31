# Verification and limits

Local development checks run on Windows 10 x64, using the actual portable service, native CPU runtimes and PDF reader. No private paper or scan is published.

- **40 local service checks passed** with PP-OCRv6 medium and Hy-MT2: authentication, Host/origin restrictions, opt-in CORS, invalid inputs, same-package single instance, Chinese/English printed OCR, four-degree skew, lower-resolution/contrast images, two-column reading order, and real translation in both directions. Short translations must preserve the expected meaning, stop normally and exclude prompt instructions. Edge crops retain bounded coordinates. German, French and Japanese targets, shared result caching and nonzero native KV prompt reuse are also exercised. CI may omit the optional independent saved-reader-layer check, reducing its count by one.
- **33 reader integration checks passed**: the real background PDF worker, service survival after worker exit, OCR text and coordinates, selectable/searchable invisible text, exact pixel equality, no duplicate layer, Undo, Unicode save/reopen, search, real text-drag bubbles, stale request cancellation, image-region translation and both OCR-to-Word modes on mixed native/scan pages. Export leaves source bytes/history unchanged and protects existing output on cancellation.
- **5 standalone desktop checks passed**: actual local translation, selected-text bubbles, default/available targets and real reverse-drag screen selector events.
- Two generated OCR Word outputs were opened read-only in installed Word, rendered to PDF/PNG and visually checked. The packaged LibreOffice renderer was attempted but unavailable; no large office package was installed.
- A second PDF engine independently extracts both Chinese and English from the saved OCR layer. Rendered test windows are inspected for readable, unclipped controls.
- Seven private real-document pages cover an IEEE paper, journal papers, Chinese table-of-contents/front matter and an English book front page. The recorded word-set coverage is only an ordinary-English-word proxy against an existing text layer, not character accuracy, mathematical correctness or a multilingual benchmark.
- Generic and AVX2 native kernels both pass arithmetic checks and actual coherent Chinese/English inference in [this pinned runtime build](https://github.com/dmz1207/Mini-AI/actions/runs/41215600743).

Translation examples actually produced:

| Input | Output |
|---|---|
| The control system remains stable. | 控制系统保持稳定。 |
| 该系统保持稳定。 | The system remains stable. |

The academic paragraph tests retain the basic control/consensus and simulation meaning. Terminology can still vary (for example, 共识 versus 一致性), and the tests do not establish sentence-level translation accuracy or equivalence to a larger model.

Medium OCR prioritizes recognition quality. On this CPU, dense paper pages take substantially longer than short snippets; background processing prevents reader UI blocking. Models unload after five idle minutes. The service supports four-line width-sorted batches with a sequential diagnostic option (`MINIAI_OCR_BATCH=1`); padding and floating-point differences can change marginal recognition results. Exact model hashes are retained; no unvalidated weight quantization is substituted to shrink the package.

An initial newer `-v2.gguf` compatibility attempt loaded but produced repeated garbage. It was rejected; the pinned stride-16 model and stricter checks replaced it. Successful loading, a nonempty response or the presence of target-language characters alone are insufficient evidence of a useful translator.

Clean Windows 11 installations, macOS/Linux/ARM, arbitrary browser extensions, handwriting, complex formula OCR and every possible real-world PDF have not been exhaustively tested. Authentication restricts network access but is not a sandbox against another program already running under the same Windows account. The model service is separate from PDF rendering; original PDFs remain untouched until the user explicitly adds a layer and saves.

## Reproduce

Build the service, install pinned assets and verified native runtimes, then run:

```text
MiniAI.exe --headless --port 0
python tools/test_service.py --output work/tests --translation
```

Pillow and optional PyMuPDF are development-only fixture/independent-check dependencies. Release automation builds again, runs real inference and verifies every portable ZIP entry against its original hash. Temporary inputs, local connection secrets and test output are excluded from the package. See [Mini PDF's test record](https://github.com/dmz1207/Mini-PDF/blob/main/源码/测试记录.md) for reader regression scope.
