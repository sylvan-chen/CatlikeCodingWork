#ifndef CUSTOM_BRDF_INCLUDED
#define CUSTOM_BRDF_INCLUDED

#define MIN_REFLECTIVITY 0.04 // 非金属的反射率各不相同，平均约 0.04，我们定义为最小反射率

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Surface.hlsl"

// BRDF（双向反射分布函数，Bidirectional Reflectance Distribution Function）是光学和计算机图形学中的核心概念。
// 它用于描述光线照射到物体表面时，如何从入射方向反射到各个出射方向（漫反射和镜面反射的组合结果）。
struct BRDF
{
    float3 diffuse; // 漫反射
    float3 specular; // 镜面反射 (高光)
    float roughness; // 粗糙度
};

// 获取漫反射率 (1 - 反射率)，反射率就等于金属表面属性
// 根据定义的最小反射率，把范围从 0~1 调至 0~0.96
float OneMinusReflectivity(float metallic)
{
    float range = 1.0 - MIN_REFLECTIVITY;
    return range - metallic * range;
}

BRDF GetBRDF(Surface surface, bool applyAlphaToDiffuse = false)
{
    BRDF brdf;
    // 漫反射部分为表面颜色 * 漫反射率
    brdf.diffuse = surface.color * OneMinusReflectivity(surface.metallic);
    if (applyAlphaToDiffuse)
    {
        // 预乘透明度混合，保持漫反射根据 alpha 减弱，镜面反射不变，呈现玻璃质感 (在完全透明的玻璃情况下，光线要么直接穿透，要么被反射。镜面反射不会消失)
        brdf.diffuse *= surface.alpha;
    }
    // 以一种方式反射的光就无法再以另一种方式反射。这被称为能量守恒，即出射光的量不能超过入射光的量。
    // 这表明镜面反射颜色应等于表面颜色减去漫反射颜色，即 brdf.specular = surface.color - brdf.diffuse;
    // 然而，这忽略了一个事实：金属会影响镜面反射的颜色，而非金属则不会。非金属表面的镜面颜色应为白色，
    // 我们可以通过利用金属属性在最小反射率与表面颜色之间进行插值来实现这一点。
    brdf.specular = lerp(MIN_REFLECTIVITY, surface.color, surface.metallic);
    // 粗糙度
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surface.smoothness);
    brdf.roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    return brdf;
}

#endif
