#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 4 // 最大方向光数量

// 用 _CustomLight 缓冲区存放 CPU 发送过来的光照数据
CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END

struct Light
{
    float3 color;
    float3 direction;
};

// 获取方向光数量
int GetDirectionalLightCount()
{
    return _DirectionalLightCount;
}

// 返回接收的方向光数据
Light GetDirectionalLight(int index)
{
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    // 这里的方向是入射方向，而不是出射方向
    light.direction = _DirectionalLightDirections[index].xyz;
    return light;
}

#endif
