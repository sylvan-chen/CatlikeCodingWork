#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Surface.hlsl"

// 反照率 (albedo): 在拉丁语中意为“白色”。它是衡量一个表面漫反射光量的指标。如果反照率并非完全为白色，那么部分光能会被吸收而非反射出去。

// 计算给定表面光源的入射光量
float3 IncomingLight(Surface surface, Light light)
{
    // saturate 限制点积为负是设为零
    return saturate(dot(surface.normal, light.direction) * light.attenuation) * light.color;
}

// 计算光照后颜色
float3 GetLighting(Surface surface, BRDF brdf, Light light)
{
    // 入射光乘以出射光 (BRDF) 结果
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

// 计算光照后颜色
float3 GetLighting(Surface surfaceWS, BRDF brdf)
{
    float3 color = 0.0;
    for (int i = 0; i < GetDirectionalLightCount(); i++)
    {
        color += GetLighting(surfaceWS, brdf, GetDirectionalLight(i, surfaceWS));
    }
    return color;
}


#endif
