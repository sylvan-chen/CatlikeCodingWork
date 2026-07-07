#ifndef CUSTOM_SHADOWS_INCLUDED
#define CUSTOM_SHADOWS_INCLUDED

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADES_COUNT 4

#include "Common.hlsl"

// 阴影贴图 (shadow map) 的采样用 TEXTURE2D_SHADOW，采样器用 SAMPLER_CMP
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_CustomShadows)
    int _CascadeCount;
    float4 _CascadeCullingSpheres[MAX_CASCADES_COUNT];
    float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADES_COUNT];
    float4 _ShadowDistanceFade;
CBUFFER_END

struct ShadowData
{
    int cascadeIndex;
    float strength;
};

/**
 * 衰减后的阴影强度
 */
float FadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0 - distance * scale) * fade);
}

ShadowData GetShadowData(Surface surfaceWS)
{
    ShadowData data;
    data.strength = FadedShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);
    int i;
    for (i = 0; i < _CascadeCount; i++)
    {
        float4 sphere = _CascadeCullingSpheres[i];
        float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
        if (distanceSqr < sphere.w)
        {
            if (i == _CascadeCount - 1)
            {
                data.strength *= FadedShadowStrength(distanceSqr, 1.0 / sphere.w, _ShadowDistanceFade.z);
            }
            break;
        }
    }

    // 如果超出最后一个级联，那么完全不采样阴影，强度设为 0
    if (i == _CascadeCount)
    {
        data.strength = 0.0;
    }

    data.cascadeIndex = i;
    return data;
}

struct DirectionalShadowData
{
    float strength;
    int tileIndex;
};

float SampleDirectionalShadowAtlas(float3 positionSTS)
{
    return SAMPLE_TEXTURE2D_SHADOW(
        _DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}

// 计算阴影数据应用在世界空间 Surface 上的阴影衰减值
float GetDirectionalShadowAttenuation(DirectionalShadowData data, Surface surfaceWS)
{
    if (data.strength <= 0.0)
    {
        return 1.0;
    }

    float3 positionSTS = mul(
        _DirectionalShadowMatrices[data.tileIndex],
        float4(surfaceWS.position, 1.0)
    ).xyz;
    float shadow = SampleDirectionalShadowAtlas(positionSTS);
    return lerp(1.0, shadow, data.strength);
}

#endif
