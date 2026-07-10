#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "ShaderLibrary/Common.hlsl"
#include "ShaderLibrary/Surface.hlsl"
#include "ShaderLibrary/Shadows.hlsl"
#include "ShaderLibrary/Light.hlsl"
#include "ShaderLibrary/BRDF.hlsl"
#include "ShaderLibrary/Lighting.hlsl"

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
    UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 baseUV : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID // GPU Instancing 实例索引
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : VAR_POSITION;
    // 尽管法向量在顶点着色器中是单位长度的，但跨三角形的线性插值会改变其长度。
    // 在 3D 渲染中，模型是由无数个三角形构成的。
    // - 顶点着色器 (Vertex Shader)： 处理三角形的三个顶点。在这里，顶点的法向量（Normal Vector，表示表面朝向的向量）通常被标准化为单位长度（即长度等于 1.0）。
    // - 光栅化与插值 (Rasterization & Interpolation)： 当画面传递给片段着色器（Fragment Shader，处理三角形内部的像素）时，系统会根据三个顶点的数据，线性插值计算出三角形内部每一个像素的法线。
    // 问题就在于“线性插值”。 想象你在一个圆（代表长度为 1）的圆周上有两个点 A 和 B，它们代表两个单位法向量。如果你在这两个点之间画一条直线（线性插值），这条直线上的所有中间点都会落在圆的内部，
    // 这意味着插值出来的向量长度都小于 1.0。法线方向夹角越大，中间插值结果的长度缩短得就越厉害。
    // 因此，为了保证物理光照计算的准确性，不要直接使用顶点着色器传过来的法线向量，必须在片段着色器中用 normalize() 对其重新进行归一化处理。
    float3 normalWS : VAR_NORMAL;
    float2 baseUV : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID // GPU Instancing 实例索引
};

Varyings LitPassVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input)             // 从输入中提取实例索引，并将其存储在其他 Instancing 宏所依赖的全局静态变量中
    UNITY_TRANSFER_INSTANCE_ID(input, output); // 索引存在时，复制索引到 output 中

    output.positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(output.positionWS);

    output.normalWS = TransformObjectToWorld(input.normalOS);

    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    output.baseUV = input.baseUV * baseST;

    return output;
}

float4 LitPassFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input) // 从实例中提取实例索引

    float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
    float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    float4 base = baseMap * baseColor;
    #if defined(_CLIPPING)
    clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
    #endif

    // 构造表面数据结构
    Surface surface;
    surface.position = input.positionWS;
    surface.normal = normalize(input.normalWS);
    surface.viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);
    surface.color = base.rgb;
    surface.depth = -TransformWorldToView(input.positionWS).z;
    surface.alpha = base.a;
    surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
    surface.smoothness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
    surface.dither = InterleavedGradientNoise(input.positionCS.xy, 0);

    // 累加光照的颜色贡献
    #if defined(_PREMULTIPLY_ALPHA)
    BRDF brdf = GetBRDF(surface, true);
    #else
    BRDF brdf = GetBRDF(surface);
    #endif
    float3 color = GetLighting(surface, brdf);

    // 返回最终颜色
    return float4(color, surface.alpha);
}

#endif
