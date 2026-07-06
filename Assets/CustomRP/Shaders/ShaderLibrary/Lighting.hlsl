#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Surface.hlsl"
#include "Shadows.hlsl"
#include "Light.hlsl"
#include "BRDF.hlsl"

float SpecularStrength(Surface surface, BRDF brdf, Light light)
{
    float3 h = SafeNormalize(light.direction + surface.viewDirection);
    float nh2 = Square(saturate(dot(surface.normal, h)));
    float lh2 = Square(saturate(dot(light.direction, h)));
    float r2 = Square(brdf.roughness);
    float d2 = Square(nh2 * (r2 - 1.0) + 1.00001);
    float normalization = brdf.roughness * 4.0 + 2.0;
    return r2 / (d2 * max(0.1, lh2) * normalization);
}

/**
 * 在给定表面、BRDF 和光源的情况下，返回通过直接光照获得的颜色。其结果是由镜面强度调制后的镜面颜色，再加上漫反射颜色。
 **/
float3 DirectBRDF(Surface surface, BRDF brdf, Light light)
{
    return SpecularStrength(surface, brdf, light) * brdf.specular + brdf.diffuse;
}

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
    ShadowData shadowData = GetShadowData(surfaceWS);
    float3 color = 0.0;
    for (int i = 0; i < GetDirectionalLightCount(); i++)
    {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
    return color;
}


#endif
