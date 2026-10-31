# Third-party notices

The MiniAI service and integration code are MIT licensed. Bundled model weights and native components retain their own licenses; they are not relicensed by this project.

| Component | Source | License / notice |
|---|---|---|
| PP-OCRv6 medium detector, recognizer and dictionary | [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR), [RapidOCR ONNX exports](https://github.com/RapidAI/RapidOCR) | Apache 2.0; `licenses/PaddleOCR-Apache-2.0.txt` and `licenses/RapidOCR-Apache-2.0.txt` |
| Hy-MT2-1.8B 1.25-bit stride-16 GGUF | [AngelSlim official weights](https://huggingface.co/AngelSlim/Hy-MT2-1.8B-1.25Bit-GGUF) | The exact upstream `LICENSE.txt` is retained as `licenses/Hy-MT2-License.txt`; Apache 2.0 |
| ONNX Runtime 1.20.1 CPU | [Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime/tree/v1.20.1) | MIT and upstream third-party notices, included under `licenses/` |
| llama.cpp with STQ1_0 and x86 implementation | [Pinned source](https://github.com/avariiiiiiiiii/llama.cpp/tree/c828a2e883defef95fd6121ee1586efcce89ad40) | MIT; each runtime folder includes LICENSE-llama.cpp.txt |
| App-local Visual C++ runtime DLLs, when included | Microsoft Visual C++ Redistributable | Microsoft terms, not MIT; see [Visual Studio runtime terms](https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/) |

The GGUF compatibility adapter changes only the exact pinned model's tensor-type and file-type identifiers. It does not change learned tensor payloads. `model-provenance.json` records both hashes and every remapping. The incompatible newer `-v2.gguf` file is deliberately not substituted merely because its name sounds newer. Native arithmetic tests alone did not catch that mismatch; release checks now also require coherent short translations and an actual end-of-sequence stop.

Keep this notice, model provenance, and all license files with redistributed offline packages. The upstream Hy-MT2 license currently has a family-name header mentioning 30B-A3B; it is preserved verbatim from the selected 1.8B GGUF repository rather than rewritten.
