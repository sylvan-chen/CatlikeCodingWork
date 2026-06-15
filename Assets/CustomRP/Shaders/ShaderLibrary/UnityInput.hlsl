#ifndef CUSTOM_UNITY_INPUT_INCLUDED
#define CUSTOM_UNITY_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// 显示声明引擎内部预设的名称，Unity 会把对应名称的值填进去

// SRP Batcher 支持 (逐物体缓冲区，每物体不同)
CBUFFER_START(UnityPerDraw)
    // (M) 模型变换矩阵 (物体空间 -> 世界空间)
    float4x4 unity_ObjectToWorld;
    // (V) 视图变换矩阵 (世界空间 -> 视图空间)
    float4x4 unity_MatrixV;
    // LOD 过渡
    float4 unity_LODFade;
    // 非均匀缩放标记
    real4 unity_WorldTransformParams; // real4 本身并非有效类型，而是根据目标平台分别作为 float4 或 half4 的别名
CBUFFER_END

// (IM) 模型变换逆矩阵 (世界空间 -> 物体空间)
float4x4 unity_WorldToObject;
// (IV) 视图变换逆矩阵 (视图空间 -> 世界空间)
float4x4 unity_MatrixInvV;
// (VP) 视图投影矩阵 (世界空间 -> 视图空间 -> 裁剪空间)
float4x4 unity_MatrixVP;
// (P) 投影矩阵 (视图空间 -> 裁剪空间)
float4x4 glstate_matrix_projection;

float4x4 unity_prev_MatrixM;
float4x4 unity_prev_MatrixIM;

// 相机的世界坐标
float3 _WorldSpaceCameraPos;

#endif
