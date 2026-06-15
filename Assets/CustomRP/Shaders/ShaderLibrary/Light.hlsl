#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

// 用 _CustomLight 缓冲区存放 CPU 发送过来的光照数据
CBUFFER_START(_CustomLight)
    float3 _DirectionalLightColor;
    float3 _DirectionalLightDirection;
CBUFFER_END

struct Light
{
    float3 color;
    float3 direction;
};

// 返回接收的方向光数据
Light GetDirectionalLight()
{
    Light light;
    light.color = _DirectionalLightColor;
    // 这里的方向是入射方向，而不是出射方向
    light.direction = _DirectionalLightDirection;
    return light;
}

#endif
