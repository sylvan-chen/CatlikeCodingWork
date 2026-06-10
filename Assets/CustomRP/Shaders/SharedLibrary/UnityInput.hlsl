#ifndef CUSTOM_UNITY_INPUT_INCLUDED
#define CUSTOM_UNITY_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// 显示声明引擎内部预设的名称

// (M) 模型变换矩阵 (物体空间 -> 世界空间)
float4x4 unity_ObjectToWorld;
// (V) 视图变换矩阵 (世界空间 -> 视图空间)
float4x4 unity_MatrixV;
// (P) 投影矩阵 (世界空间 -> 视图空间)
float4x4 glstate_matrix_projection;
// (VP) 视图投影矩阵 (世界空间 -> 视图空间 -> 裁剪空间)
float4x4 unity_MatrixVP;

// (IM) 模型变换逆矩阵 (世界空间 -> 物体空间)
float4x4 unity_WorldToObject;
// (IV) 视图变换逆矩阵 (视图空间 -> 世界空间)
float4x4 unity_MatrixInvV;

float4x4 unity_prev_MatrixM;
float4x4 unity_prev_MatrixIM;

// real4 本身并非有效类型，而是根据目标平台分别作为 float4 或 half4 的别名
real4 unity_WorldTransformParams;

#endif
