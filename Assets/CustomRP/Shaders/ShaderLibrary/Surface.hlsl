#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

// 着色器中的光照是为了模拟光线照射到表面时的相互作用，这意味着我们必须记录表面的属性。
// 目前我们有一个法向量和一个基础颜色。我们可以将后者拆分为两部分：RGB 颜色和透明度值。
// 我们会在多个地方使用这些数据，因此我们来定义一个便捷的 Surface 结构体来包含所有相关数据。
struct Surface
{
    float3 position;
    float3 normal;
    float3 viewDirection;
    float3 color;
    float depth;
    float alpha;
    float metallic;
    float smoothness;
};

#endif
