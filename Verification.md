# Verification: Mini-AI 0.2.0 / Mini PDF 0.12.3

Windows x64, i7-8565U, four physical cores, AVX2. **147 focused checks passed locally**:

| Area | Checks | Evidence |
|---|---:|---|
| Actual OCR/translation service | 45 | Authentication, origins/Host, malformed inputs, medium/tiny bilingual OCR, bounded coordinates, skew, low contrast, columns, bilingual inference, Traditional Chinese, physical cores and cache |
| Streaming/mode behavior | 38 | Partial-before-done, complete outputs, stale cancellation, beam 1/4 switching, mode-separated caches |
| Standalone workspace | 11 | Real selected text, photos, clipboard, drag/drop, EXIF, image bounds, missing delayed-DLL detection, reverse screen selection |
| Bubble lifecycle | 8 | Pending/partial visibility, reading grace, pin/expiry, stale callbacks, exact substring |
| Image window | 12 | Dark background, rendered text including single-line blocks, numbered paragraphs, OCR reuse, quality re-recognition, translated text extraction, normal saved results and instant no-save |
| Reader/OCR/Word | 33 | Actual PDF worker/service, invisible selectable layer, exact page pixels, Undo, Unicode save/reopen/search, real drag bubbles, Traditional Chinese, cropped image translation, both OCR Word modes, cancellation |

See [verification.json](verification.json). Earlier releases' totals describe their historical runs; they are not added to this change's total.

**20 complete-EXE checks and 10 actual reader package-loading checks also passed locally**, for 177 focused assertions including those above. The tested EXE was 307,272,058 bytes (about 293 MiB), versus 580,470,401 bytes for 0.1.2.

`tools/test_portable.py` additionally checks each complete EXE in a Unicode folder with a system-only PATH: embedded SHA-256, DLL/PYD dependency closure, missing/corrupt-file repair, real fast/quality translation and OCR, regular result retention, instant no-save, last-client cleanup, active-client protection and preservation of unknown user files. The release workflow requires these checks before publication. Generated package logs remain build outputs, not application source.

Measured after prewarming: uncached short API sentences around 0.04–0.1 seconds and two-sentence academic samples around 0.15–0.4 seconds in these runs. Tiny OCR alone took around 0.35 seconds versus 6.6 seconds for medium on a three-line dark screenshot; the first full image-window OCR path took around 0.85–1.2 seconds and subsequent image translation/composition around 0.25–0.35 seconds. User-provided English and Chinese screenshots took 0.3–0.7 seconds for tiny OCR. These are fixture observations, not universal speed or accuracy guarantees; cached and uncached timing must be distinguished. The screenshots and private documents are not published.

The image window and exported PNG were visually inspected; a clipped single-line rendering bug found during inspection was fixed and given a regression check. CPU fallback was exercised after a forced failed CUDA initialization. No physical discrete GPU or AVX-512 CPU was available locally. The bundled runtime selects supported instructions; optional NVIDIA CUDA/cuBLAS is not included in the default package. The smaller OPUS models may need scientific terminology corrections. High quality uses a larger beam on the same weights and does not guarantee a better result for every sentence.

最后一个客户端退出后，单 EXE 清理经过校验的运行文件；普通 OCR／翻译结果保留在 MiniAI Results，即时划词／截图不自动保存。未知或修改过的用户文件保留。开发用 --extract-only 明确保留解包文件。发布前还要通过完整 EXE 的独立目录检查；本地用例不代表所有电脑与所有文档均无问题。
