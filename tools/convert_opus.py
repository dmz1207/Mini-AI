"""Build a pinned INT8 CTranslate2 model without PyTorch or Transformers."""
import argparse,shutil
from pathlib import Path
from ctranslate2.converters import OpusMTConverter

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    OpusMTConverter(str(args.source)).convert(str(args.output),quantization='int8',force=True)
    for name in ('source.spm','target.spm'):shutil.copy2(args.source/name,args.output/name)
