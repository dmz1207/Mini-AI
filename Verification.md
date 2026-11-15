# Verification — Mini-AI 1.1.0

Local hardware: Intel Core i7-8565U, four physical/eight logical cores, AVX2+FMA. The formula model is Texo-distill Q8_0; no FP16 model was downloaded or compared. Translation and text-OCR weights are unchanged.

## Formula and CPU checks

43 focused formula/MathML/clipboard/UI checks, 12 endpoint structure checks and three native cancellation/recovery checks passed. Cancellation stopped the owned native process in about 0.17 s; a subsequent real inference succeeded. Baseline and AVX2 kernels passed 16 shapes, including unaligned inputs and tails; the persistent four-worker pool passed 100 jobs. AVX-512 is compiled and CPU/OS gated, but this development CPU cannot execute it. The hosted CPU workflow reports an explicit skip when a runner also lacks it.

The custom native DLL is reproducible across two output paths. It caches immutable decoder weights, uses persistent workers and selects the supported vector kernel. These changes retain the Q8 weights and greedy decoding. A bounded trailing-spacing loop guard removes only a long run of spacing tokens; incomplete/capped structures are still rejected.

## Real paper control

The user-provided four-line derivation now retains all four rows, both calligraphic L21/L22 terms, accents, fractions and norm bars. Whole screenshot inference fell from 15.378 s to 6.587 s; the 300-DPI PDF crop fell from 13.924 s to 6.131 s. The isolated first row fell from 3.427 s to 1.442 s. These are local samples, not a hardware-independent latency promise. All three outputs converted to editable MathML. The old model's two whole-formula controls reached the external 90-second timeout; its faster single-row control lost symbols.

## Publisher stress crops

The frozen comparison contains 43 crops from 30 papers spanning IEEE, Elsevier, Springer, Wiley, Taylor & Francis and MDPI. These are 144-DPI stress crops and some include adjacent prose or clipped context. The old model completed 36/43; the initial Texo integration completed 18/43; the optimized Texo integration completed 34/43. Median time over all 43 cases was respectively 3.553 s, 7.452 s and 1.864 s. Completion means a bounded, structurally complete output, not a correct transcription. Manual review found both improvements and remaining substitutions, including accents/subscripts and one Wiley expression. No overall accuracy percentage or superiority to Mathpix is claimed. Private papers/crops remain local.

The previous 1.0.2 service, translation, desktop and GPU-policy results are historical baseline evidence; their counts are not silently relabeled as new checks. Final isolated EXE repair, cleanup, dependency and unchanged-model checks are recorded separately after packaging. Installed MathType and physical AVX-512 hardware were unavailable locally.
