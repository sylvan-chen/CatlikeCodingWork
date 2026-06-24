#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 4 // 最大方向光数量

#include "ShaderLibrary/Shadows.hlsl"

// 用 _CustomLight 缓冲区存放 CPU 发送过来的光照数据
CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END

struct Light
{
    float3 color;
    float3 direction;
    float attenuation; // 衰减值
};

// 获取方向阴影数据
DirectionalShadowData GetDirectionalShadowData(int lightIndex)
{
    DirectionalShadowData data;
    data.strength = _DirectionalLightShadowData[lightIndex].x;
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y;
    return data;
}

// 获取方向光数量
int GetDirectionalLightCount()
{
    return _DirectionalLightCount;
}

// 返回接收的方向光数据
Light GetDirectionalLight(int index, Surface surfaceWS)
{
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    // 这里的方向是入射方向，而不是出射方向
    light.direction = _DirectionalLightDirections[index].xyz;
    DirectionalShadowData shadowData = GetDirectionalShadowData(index);
    light.attenuation = GetDirectionalShadowAttenuation(shadowData, surfaceWS);
    return light;
}

#endif
