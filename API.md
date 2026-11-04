# Mini-AI local API v1

Start `MiniAI.exe --headless --port 0` or start the portable EXE with the same arguments. Read `state/connection.json` for the actual loopback URL and bearer token. Keep that token private. Requests use `Authorization: Bearer <token>` and JSON UTF-8. Binding is loopback-only. Browser origins are denied unless explicitly allowed with the supported origin configuration; local browser extensions still need the token.

## Endpoints

- `GET /health`: OCR/formula/translation availability, the 38 target language codes, engine, warm state, physical-core/thread count, ISA and selected device. Health does not wait for model warmup.
- `POST /v1/translate`: `{ "text":"The system remains stable.", "target":"zh", "mode":"fast", "stream":true }`. Input is limited to 10,000 characters. Targets are the `/health` catalog, not just Chinese and English. `mode` is `fast` or `quality`; `engine`, if supplied, must be `hy`.
- `POST /v1/ocr`: `{ "image_base64":"...", "mode":"quality" }`. Returns text, normalized corner boxes, confidence and the model actually used. Fast mode selects the tiny model when present.
- Formula-aware OCR adds `"formula":true`; dedicated cropped formula recognition uses `"formula_only":true`. Results include `formula` capability, `formula_count`, and per-line `kind` (`text` or `formula`), `tex`, `formula_confidence` and geometry. Capability is not a positive formula detection. Formula-only requests require the formula models.
- `POST /v1/results`: explicitly persists `{ "kind":"translation", "source":"...", "target":"zh", "text":"..." }` and returns `file`. Translation/OCR endpoints do not save instant results automatically.
- `POST /v1/client-release`: notify that a managed client lease ended. The service retires only after the last lease and active request finish.
- `POST /v1/shutdown`: orderly local shutdown.

With `stream:true`, responses are newline-delimited JSON, starting with a status record before prefill. Text records carry the full translation accumulated so far; the final record contains the completed result and metadata. See `TranslationStream.Read` and `tools/test_streaming.py` for the exact protocol. Read until the final record; disconnecting cancels abandoned work. Do not append full accumulated records repeatedly or treat a partial record as complete.

Hy fast mode uses greedy decoding, while quality uses low-temperature sampling. Both are token-streamed, retain the loaded process, reuse prompt KV state and have bounded independent caches. Results include `cached`, `truncated`, `chunks`, `cached_prompt_tokens`, `threads`, `physical_cores`, `cpu_isa` and `device`. Check `truncated` and surface errors rather than silently discarding the end of a selection. Long text is split into bounded chunks without losing paragraph order.

The service accepts two concurrent requests; model execution itself is serialized as needed to avoid memory/thread oversubscription. A UI should cancel the previous selection, ignore callbacks from old generations, keep the current bubble while work is pending, and allow explicit pin/close. Mini PDF and Mini-AI share that implementation.


## 1.0.2 performance details

`/health` includes `formula_loaded`, `ocr_threads`, `ocr_inferences` and `ocr_cache_hits`. These are counts/status only, not stored image or document contents. `/v1/ocr` keeps at most four exact-image replies in memory, with separate quality/formula-mode keys. Repeated requests return the same complete response without native inference. Idle/disposal removes cached data; no new OCR result files are created. Ordinary OCR prewarms independently; formula graphs remain lazy.
