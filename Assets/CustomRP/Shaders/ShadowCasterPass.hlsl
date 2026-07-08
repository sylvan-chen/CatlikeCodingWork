#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED

#include "ShaderLibrary/Common.hlsl"

// 声明纹理和采样器状态
TEXTURE2D(_BaseMap);
// 采样器状态会根据纹理的环绕模式和过滤模式，控制纹理的采样方式
SAMPLER(sampler_BaseMap);

// 定义在 cbuffer 块中代表进入内存缓冲区，以支持 SRP Batcher
// 宏定义相当于 cbuffer UnityPerMaterial，通过宏定义可以保证不支持的平台 (如 OpenGL ES 2.0) 不会生成对应代码
// - 在 Properties 中定义的，针对每个材质都会发生变化的属性，放在 UnityPerMaterial 缓冲区
// - 对于每个物体都不同的引擎内置变量 (如 unity_ObjectToWorld)，放在 UnityPerDraw 缓冲区 
// CBUFFER_START(UnityPerMaterial)
//     // float4 _MainTex_ST; // 如果有纹理，它的 Tiling/Offset 也放这里, ST 分别代表 Scale 和 Translation
//     float4 _BaseColor;
// CBUFFER_END

// 使用 UNITY_INSTANCING_BUFFER_START 可以代替 CBUFFER_START
// - 当开启 Instancing 支持时，就会把 cbuffer 定义成实例数组
// - 当关闭 Instancing 支持时，就会回退到正常的 cbuffer UnityPerMaterial 定义 (匹配 SRP Batcher 的 cbuffer 分区) 
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)


struct Attributes
{
    float3 positionOS : POSITION;
    float2 baseUV : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID // GPU Instancing 实例索引
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 baseUV : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID // GPU Instancing 实例索引
};


Varyings ShadowPassVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input) // 从输入中提取实例索引，并将其存储在其他 Instancing 宏所依赖的全局静态变量中
    UNITY_TRANSFER_INSTANCE_ID(input, output); // 索引存在时，复制索引到 output 中

    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);

    #if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif

    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    output.baseUV = input.baseUV * baseST;

    return output;
}


void ShadowPassFragment(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input)

    float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
    float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    float4 base = baseMap * baseColor;
    #if defined(_SHADOWS_CLIP)
    clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
    #elif defined(_SHADOWS_DITHER)
    float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
    clip(base.a - dither);
    #endif
}


#endif
