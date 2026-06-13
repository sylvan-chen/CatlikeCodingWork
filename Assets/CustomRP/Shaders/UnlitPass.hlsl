#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "SharedLibrary/Common.hlsl"

// 定义在 cbuffer 块中代表进入内存缓冲区，以支持 SRP Batcher
// 宏定义相当于 cbuffer UnityPerMaterial，通过宏定义可以保证不支持的平台 (如 OpenGL ES 2.0) 不会生成对应代码
// - 在 Properties 中定义的，针对每个材质都会发生变化的属性，放在 UnityPerMaterial 缓冲区
// - 对于每个物体都不同的引擎内置变量 (如 unity_ObjectToWorld)，放在 UnityPerDraw 缓冲区 
CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    // float4 _MainTex_ST; // 如果有纹理，它的 Tiling/Offset 也放这里
CBUFFER_END


float4 UnlitPassVertex(float3 positionOS : POSITION) : SV_POSITION
{
    float3 positionWS = TransformObjectToWorld(positionOS.xyz);
    return TransformWorldToHClip(positionWS);
}

float4 UnlitPassFragment() : SV_TARGET
{
    return _BaseColor;
}


#endif
