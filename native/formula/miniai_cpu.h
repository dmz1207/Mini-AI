// MiniAI CPU helpers. Immutable Q8 weights are expanded once per context;
// the model file, greedy decoder, and numerical format remain unchanged.
#pragma once
#include <immintrin.h>
#include <condition_variable>
#include <functional>
#include <mutex>
#include <thread>
#include <vector>
#include <cstdlib>
#include <cstring>

namespace miniai_cpu {
using Dot = float (*)(const float *, const float *, int);
static float scalar(const float *a,const float *b,int n){float s=0;for(int i=0;i<n;i++)s+=a[i]*b[i];return s;}

__attribute__((target("avx2,fma")))
static float avx2(const float *a,const float *b,int n){
    __m256 s0=_mm256_setzero_ps(),s1=s0;int i=0;
    for(;i+15<n;i+=16){s0=_mm256_fmadd_ps(_mm256_loadu_ps(a+i),_mm256_loadu_ps(b+i),s0);s1=_mm256_fmadd_ps(_mm256_loadu_ps(a+i+8),_mm256_loadu_ps(b+i+8),s1);}
    s0=_mm256_add_ps(s0,s1);for(;i+7<n;i+=8)s0=_mm256_fmadd_ps(_mm256_loadu_ps(a+i),_mm256_loadu_ps(b+i),s0);
    __m128 r=_mm_add_ps(_mm256_castps256_ps128(s0),_mm256_extractf128_ps(s0,1));r=_mm_add_ps(r,_mm_movehl_ps(r,r));r=_mm_add_ss(r,_mm_shuffle_ps(r,r,1));float sum=_mm_cvtss_f32(r);for(;i<n;i++)sum+=a[i]*b[i];return sum;
}

__attribute__((target("avx512f,fma")))
static float avx512(const float *a,const float *b,int n){
    __m512 s0=_mm512_setzero_ps(),s1=s0;int i=0;
    for(;i+31<n;i+=32){s0=_mm512_fmadd_ps(_mm512_loadu_ps(a+i),_mm512_loadu_ps(b+i),s0);s1=_mm512_fmadd_ps(_mm512_loadu_ps(a+i+16),_mm512_loadu_ps(b+i+16),s1);}
    s0=_mm512_add_ps(s0,s1);for(;i+15<n;i+=16)s0=_mm512_fmadd_ps(_mm512_loadu_ps(a+i),_mm512_loadu_ps(b+i),s0);
    float sum=_mm512_reduce_add_ps(s0);for(;i<n;i++)sum+=a[i]*b[i];return sum;
}

static Dot choose_dot(){
    __builtin_cpu_init();const char *forced=std::getenv("MINIAI_FORMULA_CPU");
    if(forced&&std::strcmp(forced,"scalar")==0)return scalar;
    // GCC's CPU probe includes OS support for the extended register state.
    bool automatic=!forced||!*forced||std::strcmp(forced,"auto")==0;
    if((automatic||std::strcmp(forced,"avx512")==0)&&__builtin_cpu_supports("avx512f")&&__builtin_cpu_supports("fma"))return avx512;
    if(__builtin_cpu_supports("avx2")&&__builtin_cpu_supports("fma"))return avx2;
    return scalar;
}
static Dot dot=choose_dot();
static const char *features(){return dot==avx512?"AVX-512":dot==avx2?"AVX2 + FMA":"SSE2 baseline";}

// Persistent workers: avoid creating several operating-system threads for
// every token. Each row owns its output, so thread count does not change the
// reduction order inside a dot product. This pool is not used recursively.
class Pool {
    std::mutex mutex;std::condition_variable ready,done;std::vector<std::thread> workers;
    std::function<void(int,int)> job;unsigned generation=0;int remaining=0,count=0;bool stopping=false;
public:
    explicit Pool(int threads){for(int id=1;id<threads;id++)workers.emplace_back([this,id,threads]{unsigned seen=0;for(;;){std::unique_lock<std::mutex> lock(mutex);ready.wait(lock,[&]{return stopping||generation!=seen;});if(stopping)return;seen=generation;auto fn=job;int n=count;lock.unlock();fn(n*id/threads,n*(id+1)/threads);lock.lock();if(--remaining==0)done.notify_one();}});}
    ~Pool(){{std::lock_guard<std::mutex> lock(mutex);stopping=true;}ready.notify_all();for(auto &worker:workers)worker.join();}
    void run(int n,const std::function<void(int,int)> &fn){
        if(workers.empty()){fn(0,n);return;}int threads=(int)workers.size()+1;
        {std::lock_guard<std::mutex> lock(mutex);job=fn;count=n;remaining=(int)workers.size();generation++;}ready.notify_all();
        fn(0,n/threads);std::unique_lock<std::mutex> lock(mutex);done.wait(lock,[&]{return remaining==0;});job={};
    }
};
}
