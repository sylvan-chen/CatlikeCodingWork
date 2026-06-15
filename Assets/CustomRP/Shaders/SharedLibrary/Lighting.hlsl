#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Surface.hlsl"

// 反照率 (albedo): 在拉丁语中意为“白色”。它是衡量一个表面漫反射光量的指标。如果反照率并非完全为白色，那么部分光能会被吸收而非反射出去。

float3 GetLighting(Surface surface)
{
    // 将表面颜色作为反照率 (albedo) 来计算
    return surface.normal.y * surface.color;
}

#endif
