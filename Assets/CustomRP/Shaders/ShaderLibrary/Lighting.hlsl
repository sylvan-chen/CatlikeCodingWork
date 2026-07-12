#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Surface.hlsl"
#include "Shadows.hlsl"
#include "Light.hlsl"
#include "BRDF.hlsl"

/**
 * 计算镜面强度
 * @param surface 表面
 * @param brdf 光反射数据
 * @param light 光数据
 * @return 镜面强度
 */
float SpecularStrength(Surface surface, BRDF brdf, Light light)
{
    // 跟表面的"粗糙度"和"半角向量"有关
    // - 光滑表面 + 视线和光线的中间方向 = 高光集中；
    // - 粗糙表面 = 高光分散。
    float3 h = SafeNormalize(light.direction + surface.viewDirection); // 半角向量
    float nh2 = Square(saturate(dot(surface.normal, h)));
    float lh2 = Square(saturate(dot(light.direction, h)));
    float r2 = Square(brdf.roughness);
    float d2 = Square(nh2 * (r2 - 1.0) + 1.00001);
    float normalization = brdf.roughness * 4.0 + 2.0;
    return r2 / (d2 * max(0.1, lh2) * normalization);
}

/**
 * 表面把光反射回去的量
 * @param surface 表面
 * @param brdf 光反射数据
 * @param light 光数据
 * @return 反射光比例
 */
float3 DirectBRDF(Surface surface, BRDF brdf, Light light)
{
    // Cook-Torrance BRDF 的简化版（只算了 D 项，没算 F 和 G)。
    // 核心概念：BRDF = 镜面反射 (高光) + 漫反射 (基础色)
    //              = specular_strength * specular_color + diffuse_color
    return SpecularStrength(surface, brdf, light) * brdf.specular + brdf.diffuse;
}

// 反照率 (albedo): 在拉丁语中意为“白色”。它是衡量一个表面漫反射光量的指标。如果反照率并非完全为白色，那么部分光能会被吸收而非反射出去。

/**
 * 计算光照到表面上的入射光量（到达表面的光）
 * @param surface 表面
 * @param light 光数据
 * @return 入射光量
 */
float3 IncomingLight(Surface surface, Light light)
{
    // saturate 限制点积为负时设为零
    return saturate(dot(surface.normal, light.direction) * light.attenuation) * light.color;
}

/**
 * 获取单盏光照到表面上的颜色
 * @param surface 表面
 * @param brdf 光反射数据
 * @param light 光数据
 * @return 颜色值
 */
float3 GetLighting(Surface surface, BRDF brdf, Light light)
{
    // 最终颜色 = 到达这个表面的光 x 表面把这些光反射出去的比例
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

/**
 * 获取光照到表面后的最终颜色
 * @param surfaceWS 表面 
 * @param brdf 光反射数据
 * @return 最终颜色值
 */
float3 GetLighting(Surface surfaceWS, BRDF brdf)
{
    // 获取全局阴影数据
    ShadowData shadowData = GetShadowData(surfaceWS);
    float3 color = 0.0;
    // 累加每盏光对颜色的贡献
    for (int i = 0; i < GetDirectionalLightCount(); i++)
    {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
    return color;
}

#endif
