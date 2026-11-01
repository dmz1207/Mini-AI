# Third-party notices

Mini-AI source is MIT licensed. Included components retain their own licenses:

| Component | Purpose | Notice |
| --- | --- | --- |
| Tencent Hy-MT2 / AngelSlim 1.25-bit model | Offline multilingual translation | `licenses/Hy-MT2-Apache-2.0.txt`, pinned provenance in `assets.json` |
| Pinned STQ llama.cpp fork | Native inference, CPU and optional Vulkan | `runtime/translation/*/LICENSE-llama.cpp.txt`; MIT; source revision and reproducible loader patch included |
| PP-OCRv6 / PaddleOCR | Printed text recognition | `licenses/PaddleOCR-Apache-2.0.txt` |
| RapidOCR / dictionaries | OCR assets and conversion | `licenses/RapidOCR-Apache-2.0.txt` |
| RapidLaTeXOCR | Formula recognition | `licenses/FormulaOCR-MIT.txt` |
| ONNX Runtime | OCR execution | `licenses/ONNXRuntime-LICENSE`, `licenses/ONNXRuntime-ThirdPartyNotices.txt` |
| WpfMath 0.12.0 | Embedded WPF formula preview | `licenses/WpfMath-MIT.txt`, `assets/lib/WpfMath.json` |
| CMUdict word list | Conservative PDF word dehyphenation | `licenses/CMUdict-LICENSE.txt` |
| Optional Vulkan loader | GPU driver interface | `licenses/VulkanLoader-Apache-2.0.txt`, `licenses/VulkanRT-License.txt` when bundled |
| Microsoft VC++ redistributable DLLs | App-local ONNX dependencies | Microsoft Visual Studio runtime redistribution terms; original binaries are unmodified |

Windows .NET Framework, WPF, WinForms, graphics/image codecs and Cabinet compression are operating-system facilities, not embedded redistributable runtimes. OPUS-MT, CTranslate2, SentencePiece, MKL, Python and an embedded browser are not shipped in this release.
