# Pinned offline assets

OCR uses **PP-OCRv6 medium**, replacing the initial PP-OCRv5 mobile development configuration. The final recognition and detector assets are separate ONNX models from RapidOCR v3.10.0; the dictionary comes from PaddleOCR. Their raw sizes are 76,629,984 and 62,119,454 bytes. Together that is 138,749,438 bytes (about 132.32 MiB) before the dictionary and runtime. Roughly 80 MB describes the recognizer alone, not the complete detection/recognition pipeline.

Translation uses the official **Hy-MT2-1.8B-1.25Bit.gguf** stride-16 model at revision `0989912c0cc2d3edeeecd76171d1c7d94ee17255`, 461,860,800 bytes. It is paired with llama.cpp/STQ revision `c828a2e883defef95fd6121ee1586efcce89ad40`.

- Original model SHA-256: `cc497fe8f033b52b3b8b00a7669e9661435432f9d4cd43f7ed24400c01507a93`.
- Prepared model SHA-256: `e482a38ceaaf8420573483c96ddc8449922b5f5de6a8023b70316e65d41e6de7`.
- Metadata changes: 224 STQ tensor descriptors from 42 to 43; general file type from 41 to 42. Quantized tensor payloads remain byte-for-byte unchanged.

Do not use an arbitrary GGUF with the same model family name. The `-v2` file has a different historical encoding; a type-number rewrite made it load but produced repeated garbage with this runtime. That combination was rejected. The adapter accepts only the pinned original hash and leaves unknown models untouched. The final source records this failure rather than claiming that successful loading or quantization arithmetic proves translation quality.

The full offline download is larger than the reader: it includes the OCR models, translation weights, CPU inference executables and required licenses. Mini PDF itself keeps its approximately 3 MiB executable. No model downloads occur during normal OCR/translation calls; first install or extract the independent package.

Use [assets.json](assets.json) for URLs, exact hashes and sizes. `tools/fetch_models.py` is standard-library Python developer tooling and tries direct connections first, falling back to a configured proxy only when needed. Every download is checked before use. End users can simply extract the release ZIP and do not need Python.

## 中文

最终使用 PP-OCRv6 medium，完整检测与识别约 138.7 MB；翻译权重约 461.9 MB，另加运行库。模型放在独立包中，不增加阅读器主 EXE。固定模型文件、原生内核和校验值，避免同名但格式不兼容的模型静默替换。历史 GGUF 只改类型编号描述，不改模型参数；第一次开发中发现的 `-v2` 输出异常已排除，不能用“加载成功”冒充翻译质量通过。
