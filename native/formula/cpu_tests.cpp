#include "miniai_cpu.h"
#include <cmath>
#include <cstdio>
#include <random>
#include <stdexcept>
#include <string>

static void verify(miniai_cpu::Dot dot,const char *name){
    std::mt19937 random(1207);std::uniform_real_distribution<float> sample(-1.0f,1.0f);
    for(int n:{1,7,8,15,16,24,31,32,33,63,64,127,384,1264,1536,2048}){
        std::vector<float>a(n+3),b(n+3);double reference=0,absolute=0;
        for(int i=0;i<n;i++){a[i+1]=sample(random);b[i+1]=sample(random);double v=(double)a[i+1]*b[i+1];reference+=v;absolute+=std::abs(v);}
        float actual=dot(a.data()+1,b.data()+1,n);
        if(!std::isfinite(actual)||std::abs((double)actual-reference)>3e-6*absolute+1e-6)throw std::runtime_error(std::string(name)+" numerical mismatch at "+std::to_string(n));
    }
    std::printf("PASS: %s, tails and unaligned inputs, 16 shapes\n",name);
}
int main(){try{
    __builtin_cpu_init();verify(miniai_cpu::scalar,"baseline");
    if(__builtin_cpu_supports("avx2")&&__builtin_cpu_supports("fma"))verify(miniai_cpu::avx2,"AVX2 + FMA");else std::puts("SKIP: AVX2 unsupported on this runner");
    if(__builtin_cpu_supports("avx512f")&&__builtin_cpu_supports("fma"))verify(miniai_cpu::avx512,"AVX-512");else std::puts("SKIP: AVX-512 unsupported on this runner");
    std::vector<float> expected(257),actual(257,-1);
    for(int i=0;i<257;i++)expected[i]=(float)(i*3+1);
    {miniai_cpu::Pool pool(4);for(int trial=0;trial<100;trial++)pool.run(257,[&](int start,int end){for(int i=start;i<end;i++)actual[i]=expected[i];});}
    if(expected!=actual)throw std::runtime_error("parallel rows omitted or changed data");
    std::printf("PASS: persistent 4-core workers, 100 jobs; selected %s\n",miniai_cpu::features());return 0;
}catch(const std::exception &error){std::fprintf(stderr,"FAIL: %s\n",error.what());return 1;}}
