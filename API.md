# Local API

Mini-AI binds to **127.0.0.1**. Its portable folder contains `state/connection.json` with `url`, a random `token`, `pid` and `api_version: 1`. Keep this file private. Add `Authorization: Bearer <token>` to every request. Models stay local. The service rejects nonlocal Host headers, malformed/oversized requests and unapproved browser origins.

## Health

`GET /health` reports `ocr`, `ocr_fast` and translation availability and an `inference` object: engine/version, warm-up state, physical core/thread counts, selected CPU ISA, backend, actual compute type, supported target codes and GPU fallback information when applicable. A missing GPU runtime does not disable CPU translation.

## Translation

`POST /v1/translate`, JSON `{"text":"The control system remains stable.","target":"zh","mode":"fast"}`. `mode` is **fast** (default, beam 1) or **quality** (beam 4). Targets: **zh**, **en**, **zh-Hant**. Input: 1–10,000 characters. The response includes `text`, `target`, `truncated`, `cached`, model/backend information and elapsed worker time. Applications should report `truncated: true` rather than treating it as a complete result.

For streaming, add **`"stream": true`**. Response type: `application/x-ndjson; charset=utf-8`. Each UTF-8 line is a JSON object:

```json
{"type":"partial","text":"控制系统"}
{"type":"partial","text":"控制系统保持稳定。"}
{"type":"done","result":{"text":"控制系统保持稳定。","target":"zh","truncated":false}}
```

`partial.text` is the **cumulative snapshot**, not a delta: replace the displayed text. A `done` event marks success. A streaming failure emits `{"type":"error","error":"..."}`; disconnection without `done` is incomplete. Closing a request cancels abandoned native decoding at its next token callback. The next selection can then proceed. New clients also accept a final non-streaming response from an older pack.

Fast emits token snapshots; quality emits completed sentences/segments. Quality cancellation takes effect after the current bounded segment finishes. Complete results and identical segments are cached in bounded memory; cache entries are separated by mode and language. The API does not accept arbitrary executable/model paths or generation settings.

## OCR

`POST /v1/ocr`, JSON `{"image_base64":"<PNG/JPEG bytes encoded as Base64>"}`. Optional `"mode":"fast"` selects tiny; omitted or `"quality"` selects medium. Missing fast files fall back to medium. The response reports actual `mode` and `model`. Maximum decoded image: 24 megapixels. Returns `text`, `width`, `height` and `lines` containing `text`, `confidence`, and four normalized `box` points. Desktop photo import also handles EXIF rotation and bounds large images. OCR text can be edited, selected, copied or translated.

## Saved results and lifecycle

Inference endpoints do not save instant results. `POST /v1/results` with `{ "kind":"ocr", "text":"..." }` or `{ "kind":"translation", "source":"...", "target":"zh", "text":"..." }` saves a normal result to the sibling **MiniAI Results** folder (or `MINIAI_RESULTS_DIR`). Returns `saved` and `file`; arbitrary output paths are not accepted.

Mini PDF and desktop windows hold exclusive `state/clients/*.lock` file leases. Closing one client sends `POST /v1/client-release` with `{}`. The service retires after the last client and active request finish. Portable cleanup waits for all launcher/service/client leases and deletes only unchanged manifest-owned files; unknown/modified files survive. `--extract-only` does not clean up.

## Shutdown and browser access

`POST /v1/shutdown` with `{}` ends the service and its owned worker process. `429` means the two request slots are occupied; retry briefly or cancel old work. Headless service stops after ten idle minutes.

Browser origins are disabled by default. To opt in, place exact trusted origins, one per line, in `allowed-origins.txt` beside MiniAI.exe. Extensions still need the access token. Preflight responses allow the authenticated local API; no wildcard origin or unauthenticated inference is enabled.

The API is a localhost integration point, not an Internet-facing server. No external service is contacted during OCR/translation. Downloading a release/model package is a separate build/install step.
