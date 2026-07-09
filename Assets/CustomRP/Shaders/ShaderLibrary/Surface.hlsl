#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

/**
 * 着色器中的光照是为了模拟光线照射到表面时的相互作用。
 * 我们会在多个地方用到表面相关的属性，因此定义一个 Surface 结构体来封装所有相关数据。
 */
struct Surface
{
    float3 position;
    float3 normal;
    float3 viewDirection;
    float3 color;
    float depth;
    float alpha;
    float metallic;   // 金属度（光的反射率）
    float smoothness; // 平滑度
    float dither;
};

#endif
