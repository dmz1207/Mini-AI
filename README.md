# Mini-AI

[English](README.md) · [简体中文](README.zh-CN.md)

Small, offline OCR and Chinese–English translation for Windows 10/11 x64. Use it independently, from [Mini PDF](https://github.com/dmz1207/Mini-PDF), or through the authenticated local API.

Download **MiniAI-Portable.exe** from [Releases](https://github.com/dmz1207/Mini-AI/releases/latest) and double-click it. Models and a private inference runtime are included; no separate Python, Office, cloud account, or GPU is required. First launch verifies and extracts a `MiniAI` folder beside the EXE. The last active window or reader closes the service and removes verified package-owned runtime/model files. Normal OCR/translation results remain in the sibling **MiniAI Results** folder; instant selection/screenshot translations are not saved. The next launch extracts again. Unknown or modified user files are preserved. The explicit developer `--extract-only` command intentionally keeps files.

## New in 0.2.0

- **CTranslate2 4.8.4 + OPUS-MT INT8** replaces the 1.8B GGUF/LLM backend. Two translation models total about 171 MB before compression. Targets: English, Simplified Chinese and Traditional Chinese. Earlier multilingual LLM releases remain available separately.
- **Streaming translation** starts showing text while decoding. Choose **Fast** (beam 1, token streaming) or **High quality** (beam 4, sentence streaming). Both share the same translation models and keep separate bounded caches. Persistent models, background prewarming and canceled stale selections reduce latency. High quality searches more alternatives; it does not guarantee a better translation.
- CPU inference uses the **physical core count**, including processor groups. The official runtime selects supported AVX2/AVX-512 instructions automatically. It never forces instructions unavailable on the processor.
- Compatible **NVIDIA CUDA** acceleration is attempted automatically; initialization or inference failure falls back to CPU. The optional CUDA runtime/cuBLAS libraries are not bundled in the default pack. AMD/Intel GPU acceleration is not provided by this backend. The workspace shows the backend actually in use.
- Bubbles remain open while translating and allow 8–20 seconds to read completed results. Hover to keep them open, press Ctrl to pin, click Ctrl again to unpin, or press Esc to close. Copy the selected result or the whole translation. Expand **Source** to translate a smaller substring.

## Everyday use

Type/paste text, select the part to translate, or use Ctrl+Enter. Open or drag in a photo, paste an image, or capture a region. OCR text remains editable/selectable. **Alt+S** captures and translates a screen region when available. **Image translate** renders translations over their text regions, with Original/translated switching, selectable text, copy and PNG save. **Extract text** opens a selectable OCR sidebar. Reuse recognized text when translating the same image. **Fast** uses PP-OCRv6 tiny for screenshots/photos; **High quality** uses the requested medium model. Switching from fast to quality reruns OCR. Searchable PDF layers and OCR-to-Word default to medium.

Place the complete EXE beside Mini PDF.exe, or select it once through the reader's package dialog. **Mini PDF 0.12.3** adds streaming bubbles, selected-input translation and background prewarming for this pack. Existing extracted-folder loading remains supported.

The service binds only to 127.0.0.1 and requires a random access token. Browser origins are denied unless explicitly configured. Documents, images and translations stay on the local computer. See [API.md](API.md).

The complete tested EXE is about **293 MiB**, including both OCR tiers, both translation directions and the private runtime (previously about 554 MiB).

## Speed and quality

On the development i7-8565U (4 physical cores, AVX2), after about 2.2 seconds of prewarming, tested short sentences took around 0.04–0.05 seconds through the streaming API; two-sentence academic passages took around 0.13–0.15 seconds. A three-line dark screenshot took about 0.35 seconds for tiny OCR versus 6.6 seconds for medium on the same CPU. User-provided English/Chinese screenshots took 0.3–0.7 seconds for tiny OCR. Model extraction/loading, image composition and translation add time; no universal two-second promise. Cached requests are measured separately. These are fixture timings, not general guarantees or a translation-quality benchmark.

The smaller model trades some linguistic quality for speed. Technical terminology and complex sentences can need correction; mathematical recognition and OCR accuracy are not guaranteed. See [Models.md](Models.md) and [Verification.md](Verification.md) for actual coverage, versions and limitations. No real discrete GPU was available for local testing.

## Build

`build.ps1` builds the small C# service using Windows' .NET Framework compiler. `tools/fetch_models.py` obtains SHA-256-pinned runtime/model assets in this project folder, converts original OPUS-MT weights to INT8 without PyTorch/Transformers, and checks the converted outputs. Add the app-local Microsoft VC++ runtime DLLs described in the release workflow. `tools/test_all.ps1 -Desktop`, `tools/pack.py`, `tools/build_portable.ps1` and `tools/test_portable.py` exercise the service and package. The source archive contains no models, credentials, extraction caches or private documents.

Application code: MIT. Model/runtime licenses and attribution: [THIRD-PARTY.md](THIRD-PARTY.md), `licenses/`, and the packaged runtime notices.
