"""Patch the pinned loader to expand STQ weights only when placed on a GPU.

Disk weights remain untouched. Ternary STQ values are exactly representable in
FP16; every converted value is checked before its buffer is uploaded. No new
quantization or model download is involved. CPU layers keep their STQ kernels.
"""
from pathlib import Path
import argparse,hashlib

def patch(source):
    path=Path(source)/'src/llama-model-loader.cpp';text=path.read_text(encoding='utf-8')
    expected='''    ggml_backend_buffer_type_t buft = buft_for_tensor(&t_meta);
    if (buft == nullptr) {
        return nullptr;
    }
'''
    replacement='''    ggml_backend_buffer_type_t buft = nullptr;
    const char * stq_expansion = std::getenv("MINIAI_STQ_FP16");
    if (t_meta.type == GGML_TYPE_STQ1_0 && stq_expansion != nullptr) {
        if (use_mmap) {
            throw std::runtime_error("STQ GPU expansion requires --no-mmap");
        }
        ggml_tensor expanded = t_meta;
        expanded.type = GGML_TYPE_F16;
        expanded.nb[0] = sizeof(ggml_fp16_t);
        for (int dim = 1; dim < GGML_MAX_DIMS; ++dim) {
            expanded.nb[dim] = expanded.nb[dim - 1] * expanded.ne[dim - 1];
        }
        buft = buft_for_tensor(&expanded);
        if (buft != nullptr && (!ggml_backend_buft_is_host(buft) || std::strcmp(stq_expansion, "all") == 0)) {
            t_meta = expanded;
        } else {
            buft = buft_for_tensor(&t_meta);
        }
    } else {
        buft = buft_for_tensor(&t_meta);
    }
    if (buft == nullptr) {
        return nullptr;
    }
'''
    if text.count(expected)!=1:raise ValueError('Pinned tensor allocation changed')
    text=text.replace(expected,replacement)
    expected='''        size_t n_size = ggml_nbytes(cur);

        if (use_mmap) {'''
    replacement='''        size_t n_size = ggml_nbytes(cur);

        if (weight->tensor->type == GGML_TYPE_STQ1_0 && cur->type == GGML_TYPE_F16) {
            const size_t packed_size = ggml_nbytes(weight->tensor);
            std::vector<uint8_t> packed(packed_size);
            const auto & file = files.at(weight->idx);
            file->seek(weight->offs, SEEK_SET);
            file->read_raw(packed.data(), packed.size());
            if (!ggml_validate_row_data(GGML_TYPE_STQ1_0, packed.data(), packed.size())) {
                throw std::runtime_error("Invalid STQ tensor data");
            }
            const auto * traits = ggml_get_type_traits(GGML_TYPE_STQ1_0);
            const size_t columns = cur->ne[0];
            const size_t rows = ggml_nelements(cur) / columns;
            const size_t packed_row = ggml_row_size(GGML_TYPE_STQ1_0, columns);
            std::vector<float> row(columns);
            std::vector<ggml_fp16_t> expanded(ggml_nelements(cur));
            for (size_t r = 0; r < rows; ++r) {
                traits->to_float(packed.data() + r * packed_row, row.data(), columns);
                ggml_fp32_to_fp16_row(row.data(), expanded.data() + r * columns, columns);
                for (size_t c = 0; c < columns; ++c) {
                    if (ggml_fp16_to_fp32(expanded[r * columns + c]) != row[c]) {
                        throw std::runtime_error("STQ expansion would change a weight value");
                    }
                }
            }
            ggml_backend_tensor_set(cur, expanded.data(), 0, n_size);
            LLAMA_LOG_DEBUG("STQ exact FP16 expansion: %s\\n", ggml_get_name(cur));
        } else if (use_mmap) {'''
    if text.count(expected)!=1:raise ValueError('Pinned tensor upload changed')
    text=text.replace(expected,replacement).replace('        size_done += n_size;','        size_done += ggml_nbytes(weight->tensor);')
    text=text.replace('#include "ggml-alloc.h"','#include "ggml-alloc.h"\n#include "../ggml/src/ggml-impl.h"\n#include <cstdlib>')
    path.write_text(text,encoding='utf-8')
    print('Exact STQ-to-FP16 loader patch:',hashlib.sha256(text.encode()).hexdigest())

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);patch(p.parse_args().source)
