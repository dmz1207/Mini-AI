"""Developer-only SVG to multi-size Windows icon; no runtime dependency."""
from pathlib import Path
import io
import fitz
from PIL import Image

root=Path(__file__).resolve().parents[1]
with fitz.open(root/'assets/icon.svg') as document:
    pixels=document[0].get_pixmap(matrix=fitz.Matrix(4,4),alpha=True)
with Image.open(io.BytesIO(pixels.tobytes('png'))) as image:
    image.save(root/'assets/icon.ico',format='ICO',sizes=[(s,s) for s in (16,20,24,32,40,48,64,96,128,256)])
print('Windows icon generated:',(root/'assets/icon.ico').stat().st_size,'bytes')
