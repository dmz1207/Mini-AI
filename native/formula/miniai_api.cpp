// Small C ABI for the formula-only backend. No downloads, server, or unrelated
// inference engines are linked into this library.
#include "ppformulanet_ocr.h"
#include <exception>
#include <cstdint>
#include "miniai_cpu.h"
#define API extern "C" __declspec(dllexport)
API void *crispembed_ocr_model_init(const char *path,int threads){try{return ppformulanet_ocr_init(path,threads);}catch(...){return nullptr;}}
API const char *crispembed_ocr_model_recognize(void *ctx,const uint8_t *pixels,int w,int h,int channels,int *size){try{return ppformulanet_ocr_recognize_raw((ppformulanet_ocr_context*)ctx,pixels,w,h,channels,size);}catch(...){return nullptr;}}
API float crispembed_ocr_model_mean_confidence(void *ctx){return ppformulanet_ocr_mean_confidence((ppformulanet_ocr_context*)ctx);}
API const float *crispembed_ocr_model_confidences(void *ctx,int *count){return ppformulanet_ocr_confidences((ppformulanet_ocr_context*)ctx,count);}
API void crispembed_ocr_model_free(void *ctx){ppformulanet_ocr_free((ppformulanet_ocr_context*)ctx);}
API const char *miniai_formula_cpu_features(){return miniai_cpu::features();}
