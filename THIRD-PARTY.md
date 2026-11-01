# Third-party components

The application code is MIT licensed. This does not relicense models or native runtimes.

| Component | Source / license |
|---|---|
| OPUS-MT eng-zho and zho-eng (2020-07-17) | University of Helsinki; both original archives include CC BY 4.0; attribution and INT8 conversion recorded in `licenses/OPUS-MT-ATTRIBUTION.txt` |
| CTranslate2 4.8.4 | OpenNMT / SYSTRAN, MIT; official Windows wheel, including its numerical backends |
| Intel oneMKL / Intel OpenMP / oneDNN | Numerical backends bundled by the official CTranslate2 wheel; Intel and Apache/LLVM notices retained in licenses/ |
| SentencePiece 0.2.2 | Google, Apache 2.0 and included third-party notices |
| CPython 3.13.17 | Python Software Foundation; complete notice in `runtime/translation-ct2/LICENSE.txt` |
| NumPy 2.5.4 / OpenBLAS | NumPy BSD and bundled numerical-library notices, retained in its runtime package |
| PyYAML 6.0.3 | MIT; runtime package notices retained |
| PP-OCRv6 medium + tiny / RapidOCR model distribution | PaddleOCR / RapidAI, Apache 2.0 |
| ONNX Runtime 1.20.1 | MIT and `ONNXRuntime-ThirdPartyNotices.txt` |
| Microsoft Visual C++ runtime | App-local redistributable components required by the native libraries |

Model/runtime URLs, versions, original file sizes, SHA-256 digests and converted-output digests are pinned in `assets.json`. License texts are retained in `licenses/` and the runtime distribution. The source archive omits model weights and generated runtime binaries. Original OPUS build archives, tests, headers and unused cuDNN binaries are not shipped.

Optional CUDA libraries installed by the user remain governed by NVIDIA's terms. The default package runs on CPU without them. Older Hy-MT2/llama.cpp licenses belong to earlier releases and are not used by this backend.
