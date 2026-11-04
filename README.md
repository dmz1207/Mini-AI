# Mini-AI

Offline text OCR, formula recognition and translation for Windows. [简体中文](README.zh-CN.md)

Download **MiniAI-Portable.exe** from [Releases](https://github.com/dmz1207/Mini-AI/releases/latest). Double-click it. Models are included; no Python, browser engine, Microsoft Office or account is needed. Windows 10/11 x64 with .NET Framework 4.8 is required.

The compact window has editable source and translation panes. Paste text or a picture, drop a photo, or open the menu to import an image. All 38 supported target options are exposed, including Chinese variants. Auto direction switches between Chinese and English. Fast and Quality use the same Hy-MT2 model with separate decoding settings and caches.

- **Alt+S:** select a screen area and translate its text directly on the picture.
- **Alt+Shift+S / Formula:** select a formula, correct its LaTeX, inspect the offline preview, then copy MathML for MathType or copy LaTeX/PNG. Multiple detected formulas remain individually selectable. MathType versions vary; verify the pasted equation.
- **Text selection:** a small resizable translation bubble appears beside the selection. Select a substring in its Source view to translate only that part. Ctrl or the pin button keeps it open. Esc and × close it.
- **Mini PDF:** load the portable EXE through its offline AI menu. The reader starts a background service when needed and reuses it. The same authenticated loopback API can serve other local clients; see [API.md](API.md).

Opening the application starts low-priority translation and ordinary-OCR preparation in the background. Formula graphs load only when needed. Four exact-image OCR replies are cached in memory; identical simultaneous crops share one inference. Foreground translation uses the physical-core CPU budget, streams text, reuses the model and prompt cache, and cancels abandoned selections. The complete package automatically prefers a detected discrete Vulkan GPU, retains CPU for known integrated/software devices, and falls back after backend failure. CPU overrides and limits are described in [Models.md](Models.md).

The single EXE uses Windows' compression API and reversible model-byte packing. It verifies the original SHA-256 of every extracted file before execution. This changes storage only, never model weights. On normal exit, its owned extraction folder is removed after the last client releases it. **MiniAI Results** keeps explicit OCR/translation outputs beside the EXE; instant API/bubble translation saves nothing. User-added files are preserved.

Recognition and translation can be wrong. Formula OCR is intended for cropped printed expressions, not arbitrary page-to-Word conversion. Unsupported MathML syntax keeps its editable LaTeX instead of silently inventing a structure. See [Verification.md](Verification.md) for measured coverage and limits.

## Build

Run `./build.ps1` in PowerShell 7 or a UTF-8-aware shell. The .NET Framework compiler and WPF assemblies come from Windows. WpfMath is embedded from the pinned `assets/lib` dependency. `python tools/fetch_models.py --root .` obtains hash-pinned offline assets; Python is a development dependency only. `./tools/test_all.ps1 -Desktop` runs real-model tests. `python tools/pack.py --output dist/MiniAI-windows-x64.zip` and `./tools/build_portable.ps1 -Package dist/MiniAI-windows-x64.zip -Output dist/MiniAI-Portable.exe` create the packages. GitHub Actions also builds the native runtimes from pinned source and checks clean-folder extraction, repair and cleanup.

Source is MIT licensed. Model/runtime notices and their licenses are listed in [THIRD-PARTY.md](THIRD-PARTY.md).
