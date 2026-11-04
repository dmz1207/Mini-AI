# Offline models and runtime

## Hy-MT2 translation

The offline model is the pinned 461,860,800-byte `AngelSlim/Hy-MT2-1.8B-1.25Bit-GGUF`, revision `0989912c0cc2d3edeeecd76171d1c7d94ee17255`. `prepare_model.py` adapts historical GGUF type identifiers to native revision `c828a2e883defef95fd6121ee1586efcce89ad40`; tensor weight bytes remain unchanged. The exact upstream and prepared hashes are in `assets.json` and `model-provenance.json`.

This STQ model needs the compatible fork. The C# process owns a local, pinned llama.cpp server; it does not load a stock LLamaSharp binary or CTranslate2. Fast decoding is greedy (temperature 0, top-k 1). Quality uses temperature 0.1, top-p 0.6 and top-k 40. Both use beam size 1, stream tokens and retain independent result-cache keys. Quality mode is a different decoding choice, not a guarantee that every sentence improves.

`LanguageCatalog.cs` contains the official 38 target options, including Chinese variants and regional languages. The same catalog drives service validation and every target menu. A target appearing in the menu is not a claim of equal quality across all languages.

Automatic device selection is the default. CPU threads and batch threads use the physical-core count; `MINIAI_TRANSLATION_THREADS` can override it. AVX2 is selected only on compatible CPUs, with a separate baseline runtime for others. No AVX-512 flag is forced on unsupported hardware. On the development i7-8565U, four threads decoded the measured academic passage faster than eight; this does not establish the optimum on every CPU.

Model preparation runs at below-normal process priority before the first request. A one-token, discard-only warmup primes the real prompt, tokenizer, kernels and KV prefix; a foreground request cancels that extra warmup and resumes normal priority. Idle models are released after five minutes and are loaded again on demand. Long input is split at paragraph/word boundaries with bounded context and full-result aggregation.

### Automatic Vulkan acceleration

The complete package includes `runtime/translation/vulkan`. `MINIAI_DEVICE=cpu` forces CPU; otherwise automatic mode prefers detected discrete devices, excludes known integrated/software devices, and retries on CPU after a backend failure. For explicit control, `MINIAI_DEVICE=gpu` enables a detected Vulkan device, `MINIAI_DEVICE=Vulkan0` selects one explicitly, and `MINIAI_DEVICE=auto` prefers a detected discrete device while leaving known integrated graphics on CPU. CPU is the safe fallback if the backend cannot start. `MINIAI_GPU_LAYERS` optionally bounds offload layers; otherwise the runtime chooses a fitting split. CPU and GPU can each own model layers; this is not guaranteed to outperform CPU-only decoding.

The pinned loader patch expands only offloaded STQ tensors to FP16 **in memory**, checks every expanded weight against the original dequantized value, and uploads the result. CPU layers keep STQ kernels. Disk weights and download size do not change; GPU memory use increases, potentially by several GB. The original model and CPU runtime remain usable independently. Vulkan drivers are provided by the GPU vendor; the optional loader is app-local. No Vulkan SDK, CUDA toolkit or Python is installed on the user's machine.

A real UHD 620 Vulkan run completed locally, but was slower than the CPU; further GPU speed testing was stopped at the user's request. Discrete NVIDIA/AMD/Intel hardware has not been validated locally.

## Text and formula OCR

PP-OCRv6 medium is the quality/PDF default. PP-OCRv6 tiny handles fast screenshots/photos. Both matching dictionaries are bundled. ONNX Runtime and its required native VC++ DLLs are app-local, avoiding missing-DLL failures on machines without developer tools.

Source builds fetch the exact OCR files from the pinned `MiniAI-ocr-models.zip` release dependency first, avoiding upstream ModelScope timeouts. Both the archive and each extracted model/dictionary are SHA-256 checked against `assets.json`. Upstream URLs remain a fallback. This changes download reliability only; model weights and recognition behavior are unchanged.

RapidLaTeX OCR encoder, decoder, resizer and tokenizer are pinned to `inYourOwnBrowser/rapid-latex-ocr-onnx` revision `0d0a23977b50433b07cdf95513ed7b7e45d3a761`. Dedicated formula crops bypass ordinary text detection. Mixed screenshots first identify text/math regions; heuristic classification can miss or misclassify content. A bounded decoder reports failure instead of silently accepting a cut-off expression. Models are reused until idle and cancellation is checked between decode steps.

WpfMath 0.12.0 provides a compact offline formula preview using Windows WPF. A separate bounded C# parser exports presentation MathML with the original TeX annotation. Unsupported structures retain editable TeX. This is neither Mathpix's model nor a claim of Mathpix-level recognition accuracy.

## Lossless package

The application uses Windows .NET Framework, WinForms/WPF, image codecs, cryptography and compression instead of bundling a browser, Python interpreter or full .NET runtime. Portable model blocks use Windows Cabinet LZMS plus reversible byte-lane packing when smaller than ZIP Deflate. The extraction manifest verifies the original SHA-256 after decoding. The public ZIP stays a standard ZIP. All runtime/model downloads are pinned and checked.
