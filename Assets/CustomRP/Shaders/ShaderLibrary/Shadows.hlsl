#ifndef CUSTOM_SHADOWS_INCLUDED
#define CUSTOM_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#if defined(_DIRECTIONAL_PCF3)
#define DIRECTIONAL_FILTER_SAMPLES 4
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_PCF5)
#define DIRECTIONAL_FILTER_SAMPLES 9
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_PCF7)
#define DIRECTIONAL_FILTER_SAMPLES 16
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADES_COUNT 4

#include "Common.hlsl"

TEXTURE2D_SHADOW(_DirectionalShadowAtlas); // 阴影贴图 (Shadow map) 专用的纹理类型
#define SHADOW_SAMPLER sampler_linear_clamp_compare // 阴影贴图专用采样器
// 用这个采样器进行采样时，返回的是深度比较的结果 (0 或 1)，也就是这个像素上的该物体和这个像素上最近的物体深度比较，返回「是否被遮挡」
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_CustomShadows)
    int _CascadeCount;
    float4 _CascadeCullingSpheres[MAX_CASCADES_COUNT];
    float4 _CascadeData[MAX_CASCADES_COUNT];
    float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADES_COUNT];
    float4 _ShadowAtlasSize;
    float4 _ShadowDistanceFade;
CBUFFER_END

/**
 * 全局阴影数据
 */
struct ShadowData
{
    int cascadeIndex;
    float cascadeBlend;
    float strength;
};

/**
 * 获取根据距离衰减的阴影强度
 * @param distance 距离
 * @param scale 缩放
 * @param fade 衰减系数
 * @return 最终阴影强度
 */
float FadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0 - distance * scale) * fade);
}

/**
 * 获取表面像素对应的阴影数据
 * @param surfaceWS 表面
 * @return 阴影数据
 */
ShadowData GetShadowData(Surface surfaceWS)
{
    ShadowData data;
    data.cascadeBlend = 1.0;
    data.strength = FadedShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);
    int i;
    for (i = 0; i < _CascadeCount; i++)
    {
        float4 sphere = _CascadeCullingSpheres[i];
        float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
        if (distanceSqr < sphere.w)
        {
            float fade = FadedShadowStrength(
                distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
            );
            if (i == _CascadeCount - 1)
            {
                data.strength *= fade;
            }
            else
            {
                data.cascadeBlend = fade;
            }
            break;
        }
    }

    // 如果超出最后一个级联，那么完全不采样阴影，强度设为 0
    if (i == _CascadeCount)
    {
        data.strength = 0.0;
    }
    #if defined(_CASCADE_BLEND_DITHER)
    else if (data.cascadeBlend < surfaceWS.dither)
    {
        i += 1;
    }
    #endif
    #if !defined(_CASCADE_BLEND_SOFT)
    data.cascadeBlend = 1.0;
    #endif
    data.cascadeIndex = i;
    return data;
}

/**
 * 方向光的阴影数据
 */
struct DirectionalShadowData
{
    float strength;
    int tileIndex;
    float normalBias;
};

/**
 * 采样方向光的阴影贴图
 * @param positionSTS 采样位置 (Tile UV 坐标)
 * @return 返回采样结果 (1-完全受光 或 0-完全在阴影中)
 */
float SampleDirectionalShadowAtlas(float3 positionSTS)
{
    // 深度比较采样，positionSTS 和这个像素上最近的物体深度比较，返回「是否被遮挡」(1-完全受光 或 0-完全在阴影中)
    return SAMPLE_TEXTURE2D_SHADOW(
        _DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}

/**
 * 采样方向光的阴影贴图 (经过 PCF)
 * @param positionSTS 采样位置 (Tile UV 坐标)
 * @return 返回 PCF 后的结果 (0-1 之间)
 */
float FilterDirectionalShadow(float3 positionSTS)
{
    // 经过 PCF 过滤后，把硬阴影 (0/1) 变成软阴影 (0~1)。
    // 直接采样，拿到的要么就是遮挡，要么就是不遮挡；经过 PCF，拿到的是「遮挡了多少」，即光线衰减程度 (1 ~ 0 光线越来越弱)。
    #if defined(DIRECTIONAL_FILTER_SETUP)
    float weights[DIRECTIONAL_FILTER_SAMPLES];
    float2 positions[DIRECTIONAL_FILTER_SAMPLES];
    float4 size = _ShadowAtlasSize.yyxx;
    DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
    float shadow = 0;
    for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++)
    {
        shadow += weights[i] * SampleDirectionalShadowAtlas(
            float3(positions[i].xy, positionSTS.z)
        );
    }
    return shadow;
    #else
    return SampleDirectionalShadowAtlas(positionSTS);
    #endif
}

/**
 * 根据阴影数据，计算光线的衰减值
 * @param directional 方向光阴影数据
 * @param global 全局阴影数据
 * @param surfaceWS 目标表面
 * @return 衰减值
 */
float GetDirectionalShadowAttenuation(DirectionalShadowData directional, ShadowData global, Surface surfaceWS)
{
    #if !defined(_RECEIVE_SHADOWS)
    return 1.0;
    #endif

    if (directional.strength <= 0.0)
    {
        return 1.0;
    }

    float3 normalBias = surfaceWS.normal * (directional.normalBias * _CascadeData[global.cascadeIndex].y);
    float3 positionSTS = mul(
        _DirectionalShadowMatrices[directional.tileIndex],
        float4(surfaceWS.position + normalBias, 1.0)
    ).xyz;
    float shadow = FilterDirectionalShadow(positionSTS);
    if (global.cascadeBlend < 1.0)
    {
        normalBias = surfaceWS.normal *
            (directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
        positionSTS = mul(
            _DirectionalShadowMatrices[directional.tileIndex + 1],
            float4(surfaceWS.position + normalBias, 1.0)
        ).xyz;
        shadow = lerp(
            FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend
        );
    }

    // 最终的衰减值 = [1, 采样得到的衰减值] 之间，strength 越大衰减得越多
    return lerp(1.0, shadow, directional.strength);
}

#endif
