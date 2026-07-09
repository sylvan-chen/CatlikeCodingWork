#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 4 // 最大方向光数量

#include "Shadows.hlsl"

CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END

/**
 * 光数据
 */
struct Light
{
    float3 color;      // 光颜色
    float3 direction;  // 光入射方向
    float attenuation; // 光的衰减值
};

/**
 * 获取方向光数量
 * @return 方向光数量
 */
int GetDirectionalLightCount()
{
    return _DirectionalLightCount;
}

/**
 * 获取方向阴影数据
 * @param lightIndex 
 * @param shadowData 
 * @return 
 */
DirectionalShadowData GetDirectionalShadowData(int lightIndex, ShadowData shadowData)
{
    DirectionalShadowData data;
    data.strength = _DirectionalLightShadowData[lightIndex].x * shadowData.strength;
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;
    data.normalBias = _DirectionalLightColors[lightIndex].z;
    return data;
}

/**
 * 获取指定方向光照射到表面后的 Light 数据
 * @param index 方向光索引（第几盏方向光）
 * @param surfaceWS 表面
 * @param shadowData 阴影数据
 * @return 
 */
Light GetDirectionalLight(int index, Surface surfaceWS, ShadowData shadowData)
{
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    // Lighting.cs 传过来的方向已经是入射方向，而不是出射方向
    light.direction = _DirectionalLightDirections[index].xyz;
    // 通过阴影数据获取这盏光的方向阴影数据
    DirectionalShadowData directionalShadowData = GetDirectionalShadowData(index, shadowData);
    // 根据方向阴影数据计算光的衰减值
    light.attenuation = GetDirectionalShadowAttenuation(directionalShadowData, shadowData, surfaceWS);
    return light;
}

#endif
