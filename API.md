# MiniAI local API

Start `MiniAI.exe --headless --port 0`. Read `state/connection.json` after startup:

```json
{"api_version":1,"url":"http://127.0.0.1:PORT","token":"LOCAL_SECRET","pid":1234,"root":"..."}
```

Use the returned port rather than assuming 17643. Treat the token as a local secret. Each service start creates a new token; reconnect after a restart. Multiple launches of the same package reuse its running service. Connections and requests never require a network proxy.

```python
import base64, json, urllib.request
from pathlib import Path

connection = json.loads(Path('MiniAI/state/connection.json').read_text(encoding='utf-8'))
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def call(path, body=None):
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode('utf-8')
    request = urllib.request.Request(connection['url'] + path, data=data, headers={
        'Authorization': 'Bearer ' + connection['token'],
        'Content-Type': 'application/json; charset=utf-8',
    })
    with opener.open(request, timeout=660) as reply:
        return json.loads(reply.read())

print(call('/health'))
print(call('/v1/translate', {'text': 'The system remains stable.', 'target': 'zh'}))
image = base64.b64encode(Path('scan.png').read_bytes()).decode('ascii')
print(call('/v1/ocr', {'image_base64': image})['text'])
```

Windows PowerShell can call the same service without installing Python:

```powershell
$connection = Get-Content .\MiniAI\state\connection.json -Raw -Encoding UTF8 | ConvertFrom-Json
$body = @{text='The system remains stable.';target='zh'} | ConvertTo-Json
Invoke-RestMethod ($connection.url+'/v1/translate') -Method Post `
  -Headers @{Authorization=('Bearer '+$connection.token)} `
  -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

OCR returns `text`, image `width`/`height` and `lines`. Each line has `text`, a model `confidence`, and four `box` points in normalized image coordinates, starting at top-left along the detected text direction. Coordinates are clamped to the image bounds. Coordinates refer to the supplied image, not PDF points. Confidence is a model score, not a calibrated probability of exact transcription.

Translation returns `text`, `target`, `model`, `truncated`, `cached` (result-cache hit), and `kv_cached_tokens` (common native prompt tokens reused on inference). Cache hits skip generation and report zero native reused tokens for that request. Cache entries are memory-only and bounded to 32. Supported target codes: `zh`, `en`, `ja`, `ko`, `fr`, `de`, `es`, `ru`, `pt`, `it`, `zh-Hant`. The same model is reused; no additional language download. Input is limited to 10,000 characters per request; for predictable latency and context capacity, split long text into short paragraphs (Mini PDF uses at most 900 characters per part). If `truncated` is true, shorten and retry. Do not silently use an incomplete result.

Errors are JSON with an `error` string: 400 invalid input/inference failure, 401 invalid token, 403 disallowed Host/origin, 404 unknown route, 405 invalid method and 429 busy. Up to two computing requests are admitted; OCR and translation each serialize access to their own model. A disconnected translation client cancels the underlying generation request. OCR notices disconnects between recognition batches. A health request remains available while inference slots are busy. Authenticated shutdown remains available too.

Requests use HTTP/1.1 and an explicit bounded Content-Length. Chunked request uploads are not accepted. Maximum JSON body: 32 MiB; OCR base64 string: 24 MiB; image: 24 megapixels. Header size and duplicate headers are checked.

Browser clients must supply the same authorization header. Add the exact extension origin to `allowed-origins.txt`; OPTIONS preflight is supported for allowed origins. A content script's origin is normally the website, so prefer a browser extension background worker with explicitly configured permissions and origin. No wildcard origin is enabled. Other local programs running as the same user may read the connection file; the token is not a sandbox against a compromised local account.

The API accepts images and text, never arbitrary paths or executable commands. Mini PDF renders PDF pages itself, in its isolated PDF worker, then sends images to MiniAI. PDFium is not duplicated in this service.

## 中文

启动后读取本机连接文件，使用实际端口与 Bearer 令牌。服务重启后令牌会变化；对方繁忙返回 429 时稍后重试。浏览器需准确来源白名单和令牌，插件建议由后台脚本调用。只支持本机，不提供公网监听、文件执行或云端上传。识别返回归一化坐标与行置信度；翻译长文应分段，并检查是否截断。
