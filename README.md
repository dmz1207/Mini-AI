# Mini-AI

[English](README.md) | [简体中文](README.zh-CN.md)

**Offline OCR, selection/screen translation and a shared local API.**

MiniAI is a small native Windows service for [Mini PDF](https://github.com/dmz1207/Mini-PDF), desktop tools and browser extensions. Documents stay on your computer. It runs on the CPU, needs no administrator access, Python, Office, cloud account or subscription after the offline package is installed.

The service executable is about **119 KiB**, including its application icon. The separate offline package contains **PP-OCRv6 medium** text detection/recognition, ONNX Runtime and **Hy-MT2-1.8B 1.25-bit** translation with a pinned STQ inference engine. The translation weights alone are **461,860,800 bytes**; the complete single EXE is about 554 MiB. Models stay separate from the PDF reader.

## One EXE: double-click to run

Version **0.1.2** adds a flat, rounded icon with **Mini** lettering to both EXEs, the desktop window, taskbar and first-run preparation window. The SVG source and 16–256 px Windows icon are included under `assets/`; the installed application needs no separate icon file.

Download **[MiniAI-Portable.exe](https://github.com/dmz1207/Mini-AI/releases/latest/download/MiniAI-Portable.exe)** and put it in a writable folder. It contains both models, the service and all required runtime DLLs. First launch checks SHA-256 and extracts a `MiniAI` folder beside the EXE; subsequent launches verify and reuse those files. Damaged or missing package files are repaired. Your unrelated files are preserved. No installer, administrator access, separate model download or manual DLL installation is needed. Keep the extracted folder while using the service; it occupies about 610 MiB in addition to the download. Updating an older copy requires closing MiniAI first.

**Open photo** accepts JPEG, PNG, BMP, TIFF and GIF. You can also drop a photo into the window, paste a clipboard image with **Ctrl+V**, or click **Screenshot OCR**. OCR fills the editable upper text box: correct it, select text, copy it, save UTF-8 text, or translate it. **Translate after OCR** is optional. Large photos are scaled before recognition and JPEG orientation is respected. TIFF/GIF use the first frame. **Alt+S / Screen translate** retains the compact OCR-and-translation bubble.

Version **0.1.1** fixes missing `libssl-3-x64.dll`, `libcrypto-3-x64.dll` and `msvcp140_1.dll`: local translation is built without OpenSSL, OCR includes its app-local Microsoft runtime, and release packaging rejects unresolved native imports. The single EXE is tested from an empty folder with a Windows-system-only PATH, including actual OCR and translation.

## Use with Mini PDF

1. Put **MiniAI-Portable.exe** beside `Mini PDF.exe`. Mini PDF 0.12.1 or later detects it and prepares the package on first use. Alternatively, extract **MiniAI-windows-x64.zip** from [Releases](https://github.com/dmz1207/Mini-AI/releases) to obtain:

   ```text
   Mini PDF.exe
   MiniAI/
       MiniAI.exe
       models/
       runtime/
       licenses/
   ```

2. Open a PDF. Once the package is detected, selection/capture translation is enabled; without it these commands are disabled with an installation hint. Click **OCR** or **Translate** in the toolbar. Mini PDF starts MiniAI automatically when needed. If the package is elsewhere, select its folder once through **Tools → Load MiniAI package**.
3. Drag to select text and release: a small rounded translation bubble appears beside the selection. It defaults to Chinese/English auto direction and offers other supported targets. Move away to dismiss, hold **Ctrl** to keep it visible, or click **Ctrl** to pin/unpin. Press **Alt+S** to dim the desktop and drag a screen region: only that region is OCRed and translated. Recognized text is selectable and correctable; select a sentence to translate it, or press Ctrl+Enter after correction.
4. For the larger review window, select text and press **Ctrl+Alt+T**, or right-click → **Translate selected text offline**. With no selection, translation starts with the current page's extracted text.
5. For a scanned PDF, press **Ctrl+Shift+O**. Choose current page, all pages or a custom range, run OCR, review the result and click **Add searchable text layer**. Save the PDF to retain the layer. The original scanned appearance is preserved; insertion supports Undo.

6. **File → PDF to Word → OCR scanned pages first (MiniAI)** recognizes scan-only pages before either Word export mode. Confident ordinary text becomes editable; complex/uncertain content stays artwork. Scanned layouts are approximate and require review. The source PDF is never rewritten by export.

The reader remains usable while recognition or translation runs. Tasks can be canceled. Models load on demand and unload after five idle minutes; a background service started by the reader exits after ten idle minutes. Double-clicking MiniAI.exe opens an independent text translation workspace with paste, image import, screen capture and copy controls. It also works without Mini PDF. Multiple clients using the same package reuse one service.

## Other applications and browser extensions

MiniAI listens only on `127.0.0.1`. Start `MiniAI.exe --headless --port 0` for a free local port, or double-click the executable for its window. Read `state/connection.json` or click **Copy connection**. Send the supplied bearer token with every API call. The file is generated locally and must not be committed or shared.

- `GET /health`: API version and installed OCR/translation capabilities.
- `POST /v1/ocr`: `{ "image_base64": "..." }`; returns text and individual line confidences with normalized quadrilateral coordinates. Input: up to 24 megapixels and 24 MiB of base64 text.
- `POST /v1/translate`: `{ "text": "The system remains stable.", "target": "zh" }`; targets include `zh`, `en`, `ja`, `ko`, `fr`, `de`, `es`, `ru`, `pt`, `it` and `zh-Hant`. Returns translated text, an explicit `truncated` flag, result-cache use and reused KV-token diagnostics. Segment long inputs; Mini PDF handles this automatically.
- `POST /v1/shutdown`: authenticated shutdown, body `{}`.

See [API and client example](API.md). Browser access is disabled by default. Add an extension's exact origin (for example, `chrome-extension://your-extension-id`) to `allowed-origins.txt`, one per line. Browser clients still require the token; allowlisting an origin does not authenticate it. Arbitrary websites are not enabled. No browser extension is bundled.

## Quality and limits

- OCR targets printed Chinese and English, basic skew and ordinary one/two-column pages. It exposes confidence per line. Handwriting, vertical text, complex tables, poor scans and mathematical notation can need manual correction. It does not reconstruct mathematical semantics.
- Hy-MT2 is a specialized translation model. Aggressive quantization trades accuracy for size; scientific terminology, equations and citations still need checking. A translator is not a source of factual evidence.
- A persistent inference process reuses prompt KV cache; the last 32 complete translations are cached in memory, never written to document files. New selections cancel superseded generation. AVX2 kernels and bounded CPU threads are used; first use still needs model loading and dense-page OCR is not instant.
- Windows 10/11 **x64** is the current package target. AVX2 is preferred, with a tested generic CPU build as fallback. A GPU is not required. No macOS, Linux or ARM build is claimed for this release.
- The main PDF reader does not load the inference libraries. Recognition and translation run in the separate service, with bounded requests and serialized per-model execution. Up to two API requests are admitted concurrently; extra requests receive 429.

See [verification](Verification.md), [model provenance](Models.md) and [third-party notices](THIRD-PARTY.md). Local checks cover named fixtures, not every possible document or a guarantee of zero bugs.

## Build

Run `powershell -ExecutionPolicy Bypass -File build.ps1` on Windows with the system .NET Framework 4.x compiler. The service has no NuGet dependency. Python is optional developer tooling for fetching/validating model assets and testing; it is not shipped or needed by users.

`python tools/fetch_models.py --root .` downloads pinned OCR/translation assets, verifies hashes, and adapts the exact historical GGUF descriptor to the pinned STQ runtime without changing tensor payloads. Native CPU runtimes are built and tested by [GitHub Actions](.github/workflows/native-runtime.yml). The release workflow uses those tested artifacts and includes all required notices. Sources are MIT; model/runtime licenses apply separately.
