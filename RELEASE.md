Mini-AI 0.2.0: fast offline OCR and Chinese-English translation.

CTranslate2 4.8.4 with two OPUS-MT INT8 models replaces the GGUF/LLM backend. Fast (beam 1) and High quality (beam 4), physical-core CPU threading, automatic AVX2/AVX-512 dispatch, startup prewarming, streaming and bounded translation caches reduce latency.

New selections cancel old work. Pending bubbles stay open; completed results remain readable. Hover, copy a selection, expand Source to translate a smaller part, press Ctrl to pin or Esc to close. Photo import, screenshots and clipboard OCR remain available. Fast image OCR adds PP-OCRv6 tiny; quality/PDF OCR retains medium. Image translation overlays the text regions and supports original switching, selectable OCR text, copying and PNG save.

NVIDIA CUDA acceleration is attempted automatically when a compatible driver and CUDA 12/cuBLAS runtime are present. Failure falls back to CPU. These optional CUDA libraries are not bundled; AMD/Intel GPU acceleration is not provided by this backend. Real discrete-GPU speed was not tested locally.

Targets: Simplified Chinese, English and Traditional Chinese. The smaller model can need corrections for technical terminology; earlier multilingual LLM releases remain separately available.

Download MiniAI-Portable.exe and double-click. No separate Python installation. First run verifies/extracts beside the EXE; the last client exit cleans verified runtime/model files. Normal OCR/translation results remain in MiniAI Results; instant translations leave no saved results. Next launch extracts again. Mini PDF 0.12.3 adds matching streaming and selected-text integration. See README.md, Models.md and Verification.md for measurements and limits.
