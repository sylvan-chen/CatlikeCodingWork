# 【9】方向光阴影 - 阴影质量（Shadow Quality）

> 本节讲阴影的"质量"问题：阴影斑点（acne）三种解决方案、距离衰减、级联渐隐、PCF 软化、级联混合、Pancaking、Culling Bias。这些是 shader 上看到的"细节"，但每一个都在 CPU 端有对应的"准备数据"。

---

## 一、Shadow Acne 复盘

先快速回顾：

shadow map 用**离散的 texel 描述连续的深度场**，每个 texel 只能存一个数字。Receiver 端的 `depth_P` 和 `depth_S` 都是浮点数，**会相差几个 ULP**：

```
depth_P 算出来 = 5.0000003f   ← 矩阵乘法四舍五入
depth_S 算出来 = 4.9999997f   ← rasterizer 写深度时四舍五入
                              ↑ 这里 depth_S 比 depth_P 还"小"，所以 P 被误判阴影
```

外加**表面倾斜加剧误差**：相邻 texel 的深度差异可能比浮点误差大很多。

下面的方案都是围绕"让这个比较**稳定**"展开的。

---

## 二、三种治 acne 方案对比

| 方案 | 怎么改 | 副作用 | 用不用 |
|------|--------|--------|--------|
| Constant depth bias | shadow map 所有深度 + ε | 全局 Peter Pan（阴影缩小 ε） | ❌ 不用 |
| Slope-scale depth bias | 深度 + ε + slope*k | 难调到合适的 k | ❌ 不用 |
| Normal bias | **不改 shadow map**，挪查询点 | 边缘错位 ~texelSize | ✅ 用这个 |

### 2.1 Slope-Scale Bias（不开，但保留）

```csharp
// Shadows.cs
_buffer.SetGlobalDepthBias(0f, light.SlopeScaleBias);
ExecuteBuffer();
_context.DrawShadows(ref shadowSettings);
_buffer.SetGlobalDepthBias(0f, 0f);
```

`SetGlobalDepthBias(constantBias, slopeScaleBias)` 是 GPU 的**硬件 depth bias**——给深度比较加偏移：
- `constantBias`：所有深度统一加
- `slopeScaleBias`：按多边形斜率加

`slopeScaleBias` 取 light 自带的 `shadowBias`（Unity 的 Inspector 滑块）。但**教程里默认设为 0**，因为改用 normal bias 后，slope-scale 反而会引入额外的偏移，叠加出问题。

> 之所以注释里保留这两行，是因为教程后面会讨论"是否需要再加一点 slope-scale"。当前项目里它就是 0，相当于关闭。

### 2.2 Normal Bias（核心方案）

shader 端代码：

```hlsl
// Shadows.hlsl
float3 normalBias = surfaceWS.normal * (directional.normalBias * _CascadeData[global.cascadeIndex].y);
float3 positionSTS = mul(
    _DirectionalShadowMatrices[directional.tileIndex],
    float4(surfaceWS.position + normalBias, 1.0)
).xyz;
```

**关键点**：查 shadow map 时，传入的位置是 `surfaceWS.position + 法线 × 偏移`，不是直接 `surfaceWS.position`。

### 2.3 三个数字的物理意义

| 因子 | 含义 | 谁提供 |
|------|------|--------|
| `surfaceWS.normal` | 当前片段的法线方向 | 顶点→片段插值，fragment 里 normalize |
| `_CascadeData[...].y` | texelSize × √2 | CPU `SetCascadeData` 算好 |
| `directional.normalBias` | light 自带的 Normal Bias 滑块 | CPU `ReserveDirectionalShadows` 返回 |

```csharp
// Shadows.cs:107-128
public Vector3 ReserveDirectionalShadows(Light light, int visibleLightIndex)
{
    ...
    return new Vector3(
        light.shadowStrength,            // .x → 阴影强度
        ...tile 偏移...,                 // .y → tile 索引
        light.shadowNormalBias           // .z → Normal Bias 滑块
    );
}
```

`Vector3` → `Vector4` 隐式转换（`.w` = 0），所以 `_DirectionalLightShadowData[i].z` 就是 `light.shadowNormalBias`。

shader 端读取：

```hlsl
// Light.hlsl:24-30
DirectionalShadowData GetDirectionalShadowData(int lightIndex, ShadowData shadowData)
{
    DirectionalShadowData data;
    data.strength = _DirectionalLightShadowData[lightIndex].x * shadowData.strength;
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;
    data.normalBias = _DirectionalLightShadowData[lightIndex].z;
    return data;
}
```

`DirectionalShadowData` 加了 `normalBias` 字段，就是为了把 light 自带的滑块值一路传到 attenuation 函数里。

---

## 三、texelSize × √2 推导

`_CascadeData.y` 中存储了纹素大小，这是后面法线要偏移的长度。

### 3.1 texelSize 是什么

```
texelSize = 一格 shadow map 像素在世界空间里有多大
          = 2 × 剔除球半径 / tile 边长像素数
```

为什么是 `2 × 半径`？因为剔除球的**直径**才是 tile 覆盖的世界宽度：

```
        ┌─────────────┐
       ╱  ┌─────────┐  ╲
      ╱   │ tile    │   ╲      tile 边长 W = 2 × R / pixel
     ╱    │ 像素    │    ╲
    ╱     │ 数 N    │     ╲
   │      │         │      │
   │      │         │      │     1 texel = 2R / N
   ╲      │         │      ╱
    ╲     │         │     ╱
     ╲    └─────────┘    ╱
      ╲                 ╱
       ╲_______________╱
```

### 3.2 为什么乘 √2

shadow map 的 texel 是**正方形**。沿法线推多少才能保证"逃出当前 texel"？

```
    ┌─────────────┐
    │             │     N = 法线方向，沿 X 轴
    │      →      │     沿 N 推 1 个 texel = 走 1 个边长
    │             │     ✓ 已经逃出当前 texel
    │             │
    └─────────────┘
```

```
    ┌─────────────┐
    │             │     N = 法线方向，恰好沿对角线
    │      ↘      │     沿 N 推 1 个 texel = 走 √2 个边长
    │             │     推 1 个边长 = 没逃出当前 texel ❌
    │             │     推 √2 个边长 = 才逃出当前 texel ✓
    └─────────────┘
```

**最坏情况**：法线沿对角线方向。所以为了保证所有方向都能逃出，**至少要推 √2 × 边长**。

这就是 `texelSize × √2 ≈ texelSize × 1.4142136f` 的物理意义。

### 3.3 texelSize 自动跟随 cascade 缩放

不同 cascade 的剔除球半径差别很大：

```
近处 cascade:  半径 10m,  tile 1024 像素 → texelSize ≈ 0.02m
远处 cascade:  半径 200m, tile 1024 像素 → texelSize ≈ 0.4m
```

近处 acne 风险小 → 偏移小（0.02m × √2 = 0.028m，几乎无感）
远处 acne 风险大 → 偏移大（0.4m × √2 = 0.57m，但相对远处尺度仍然小）

**这是级联阴影最优雅的一点：texelSize 自动平衡了每个 cascade 的"合理偏移量"**。

---

## 四、Max Distance Fade（远处渐隐）

```csharp
// Shadows.cs
new Vector4(
    1f / _settings.MaxDistance,
    1f / _settings.DistanceFade,
    1f / (1f - cascadeFade * cascadeFade)
)
```

`_ShadowDistanceFade` 的 4 个分量：`.x` 给全局距离 fade 用，`.y` 和 `.z` 给级联 fade 用，`.w` 没用（其实是 4D 向量必有 4 个分量）。

### 4.1 全局距离衰减

```hlsl
// Shadows.hlsl
data.strength = FadedShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);
//                          = saturate((1 - depth/MaxDistance) * MaxDistance/DistanceFade)
```

- 当 `depth = 0`（在相机处）：强度 = `1 × (MaxDistance/DistanceFade)`
- 当 `depth = MaxDistance`（阴影最远）：强度 = `0`

`MaxDistance/DistanceFade` 这个倍数大于 1，意味着**刚到 MaxDistance 时强度立刻归零**——会出现一段明显的"阴影突然消失"边界。

教程的目的就是让阴影在 `[MaxDistance - DistanceFade, MaxDistance]` 这段距离里**逐渐消失**，而不是突然剪掉：

```
阴影强度
  1 ┤████████████████████
    │                    ╲
    │                     ╲     ← 这一段平滑过渡（DistanceFade 长度）
    │                      ╲
  0 ┤                       ╲___  MaxDistance
    └────────────────────────────────── 距离
   0                           MaxDist
```

实际公式 `saturate((1 - depth/MaxDist) × MaxDist/DistanceFade)`：

- `depth = MaxDist - DistanceFade`：`1 - (MaxDist - DistanceFade)/MaxDist = DistanceFade/MaxDist`，乘以 `MaxDist/DistanceFade` = 1 → strength = 1（满阴影）
- `depth = MaxDist`：`1 - 1 = 0`，strength = 0
- 中间值：线性插值

### 4.2 为什么 `surfaceWS.depth = -TransformWorldToView(...).z`

```hlsl
// LitPass.hlsl:97
surface.depth = -TransformWorldToView(input.positionWS).z;
```

Unity 的 view 空间看向 -Z，所以 `viewZ` 是负数。取负得到**相机到表面的距离**（正数），方便做"距离衰减"。

---

## 五、Cascade Fade（最后一个级联内部渐隐）

```hlsl
// Shadows.hlsl
if (distanceSqr < sphere.w)  // 落在某个级联球内
{
    float fade = FadedShadowStrength(
        distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
    );
    if (i == _CascadeCount - 1)   // 最后一个级联
        data.strength *= fade;
    else
        data.cascadeBlend = fade;
    break;
}
```

**最后一个级联内部**再做一次 fade：
- 越靠近剔除球边缘 → fade 越接近 0
- 越靠近球心 → fade 越接近 1

```csharp
// Shadows.cs
private void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
{
    float texelSize = 2f * cullingSphere.w / tileSize;
    float filterSize = texelSize * ((float)_settings.Directional.Filter + 1f);
    cullingSphere.w -= filterSize;     // 球缩小一圈，预留 PCF 边界
    cullingSphere.w *= cullingSphere.w;
    CascadeCullingSpheres[index] = cullingSphere;
    CascadeData[index] = new Vector4(1f / cullingSphere.w, filterSize * 1.4142136f);
}
```

`1 / cullingSphere.w`（已平方）就是 `_CascadeData[i].x`。`FadedShadowStrength(distanceSqr, 1/r², fade)` = `saturate((1 - d²/r²) × fade)`——球心为 1，球面为 0。

```
强度
1 ┤████████████━━━
  │            ╲
  │             ╲
0 ┤              ●━━━  球面
  └──────────────────  距离（球心→边缘）
```

**意义**：最后一个级联的覆盖范围大，但边缘的剔除球"假装"比实际小一圈——这样边缘处自然 fade 出去，**避免阴影在 MaxDistance 处突然剪掉**的硬边。

---

## 六、PCF 过滤：软化阴影边缘

### 6.1 什么是 PCF

**Percentage Closer Filtering**——不是把阴影采样器的输出当 0/1，而是**采样多次**取平均，得到 0~1 的渐变值。

```
无 PCF：                   有 PCF 3×3：
                           
阴影 ███████░░░░ 受光     阴影 ▓▓▓▒▒░░░░░ 受光
     ███████░░░░                ▒▒▒░░░░░░
     ███████░░░░                ▒░░░░░░░░
                                ↑ 边缘过渡更柔和
```

### 6.2 代码实现

```hlsl
// Shadows.hlsl
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#if defined(_DIRECTIONAL_PCF3)
#define DIRECTIONAL_FILTER_SAMPLES 4
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_PCF5)
#define DIRECTIONAL_FILTER_SAMPLES 9
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_PCF7)
#define DIRECTIONAL_FILTER_SAMPLES 16
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x3
#endif
```

`#pragma multi_compile _ _DIRECTIONAL_PCF3 _DIRECTIONAL_PCF5 _DIRECTIONAL_PCF7`（在 `Lit.shader:51`）让 Unity 编译 4 个变体（none/3x3/5x5/7x7）。

C# 端切 keyword：

```csharp
// Shadows.cs
SetKeywords(DirectionalFilterKeywords, (int)_settings.Directional.Filter - 1);
```

`FilterMode.PCF2x2` 对应 0（无 PCF），PCF3x3 对应 1，PCF5x5 对应 2，PCF7x7 对应 3。

### 6.3 实际滤波

```hlsl
// Shadows.hlsl
float FilterDirectionalShadow(float3 positionSTS)
{
    #if defined(DIRECTIONAL_FILTER_SETUP)
    float weights[DIRECTIONAL_FILTER_SAMPLES];
    float2 positions[DIRECTIONAL_FILTER_SAMPLES];
    float4 size = _ShadowAtlasSize.yyxx;
    DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
    float shadow = 0;
    for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++)
    {
        shadow += weights[i] * SampleDirectionalShadowAtlas(
            float3(positions[i].xy, positionSTS.z)
        );
    }
    return shadow;
    #else
    return SampleDirectionalShadowAtlas(positionSTS);
    #endif
}
```

`SampleShadow_ComputeSamples_Tent_NxN` 是 Unity 内置函数，返回 N 个采样位置和权重（tent = 帐篷形权重分布，中心权重大，边缘小）。

`_ShadowAtlasSize`：

```csharp
// Shadows.cs
_buffer.SetGlobalVector(ShadowAtlasSizeId, new Vector4(atlasSize, 1f / atlasSize));
```

`.x` = atlas 像素边长，`.y` = 倒数。这俩值给 PCF 当"一个像素有多大"用，采样位置才能算对。

### 6.4 PCF 的成本权衡

| 变体 | 采样数 | 视觉 | 性能 |
|------|--------|------|------|
| 无 PCF (PCF2x2) | 1 | 锯齿边缘 | 最快 |
| PCF 3×3 | 4 | 略软 | ×4 |
| PCF 5×5 | 9 | 柔和 | ×9 |
| PCF 7×7 | 16 | 很柔和 | ×16 |

PCF 用**空间换质量**——想要边缘更柔和，必须做更多次硬件采样。

---

## 七、Cascade Blending（级联之间平滑过渡）

### 7.1 问题

Cascade 之间会有可见的接缝——前一个 cascade 的阴影和后一个的阴影可能在接缝处"跳"：

```
Cascade 0  ████████│  Cascade 1  ░░░░░░│  Cascade 2
  阴影浓 ──┘         │   阴影淡 ──┘       │
                     ↑ 这里会看到明显的边界
```

### 7.2 解决方案：同时采样两个相邻 cascade

`GetShadowData` 的 `cascadeBlend` 是这个意思：

```hlsl
// Shadows.hlsl
data.cascadeBlend = 1.0;

// Shadows.hlsl (非最后一个级联时)
data.cascadeBlend = fade;  // 越靠近级联边缘 → blend 越接近 0
```

shader 端 attenuation 函数：

```hlsl
// Shadows.hlsl:148-160
float shadow = FilterDirectionalShadow(positionSTS);
if (global.cascadeBlend < 1.0)
{
    // 再采样下一个级联
    normalBias = surfaceWS.normal *
        (directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
    positionSTS = mul(
        _DirectionalShadowMatrices[directional.tileIndex + 1],
        float4(surfaceWS.position + normalBias, 1.0)
    ).xyz;
    shadow = lerp(
        FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend
    );
}
```

**关键**：`lerp(nextCascadeResult, currentResult, cascadeBlend)`：
- 当 `cascadeBlend = 1`（完全在当前级联内）→ 取当前级联结果
- 当 `cascadeBlend = 0`（到达级联边缘）→ 取下一个级联结果
- 中间 → 线性插值

### 7.3 Soft vs Dither

`Lit.shader:52`：

```hlsl
#pragma multi_compile _ _CASCADE_BLEND_SOFT _CASCADE_BLEND_DITHER
```

**Soft**：上面的 lerp 方式，结果是平滑过渡。

**Dither**：用一个抖动噪声决定是当前还是下一个 cascade，结果像早期 PS 游戏那种"花纹过渡"。比 lerp 便宜，且能掩盖接缝。

```hlsl
// Shadows.hlsl:83-88
#if defined(_CASCADE_BLEND_DITHER)
else if (data.cascadeBlend < surfaceWS.dither)
{
    i += 1;  // 跳到下一个级联
}
#endif
// Shadows.hlsl:89-91
#if !defined(_CASCADE_BLEND_SOFT)
data.cascadeBlend = 1.0;  // 不是 Soft 就关闭 blend
#endif
```

`surfaceWS.dither` 在 LitPass.hlsl 里设置：

```hlsl
// LitPass.hlsl:101
surface.dither = InterleavedGradientNoise(input.positionCS.xy, 0);
```

这是一个 0~1 的屏幕空间噪声，每个像素略有不同。`cascadeBlend < dither` 时跳到下一个 cascade——不同像素跳/不跳不同，整体看起来是"花纹式过渡"。

---

## 八、Pancaking（近平面贴合）

### 8.1 问题

在 caster pass 里，光源是**正交投影**。一个几何体的"前面"靠近近平面时，会被光方向强烈拉伸（透视除法在近平面附近让 z 变化剧烈），导致奇怪的阴影伪影。

### 8.2 解决方案

```hlsl
// ShadowCasterPass.hlsl:54-58
#if UNITY_REVERSED_Z
output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif
```

**把所有深度夹到 near plane**。这意味着 caster pass 渲染出来的物体"贴着近平面"——`UNITY_NEAR_CLIP_VALUE` 是个非常小的值（接近 0），比正交投影自然算出来的 z 大或小（取决于 reverse-Z）。

效果：
- **正序 Z（OpenGL）**：取较大值 → clamp 到 near
- **反序 Z（D3D/Metal/Vulkan）**：取较小值 → clamp 到 near

clamp 到 near 意味着物体在 shadow map 里"压在最浅处"，**接收端不会把这个物体后面的东西误判成"更深"**。

### 8.3 NearPlaneOffset

```csharp
// Shadows.cs:249
light.NearPlaneOffset,
```

传给 `ComputeDirectionalShadowMatricesAndCullingPrimitives` 的最后一个参数——光源的 near plane 偏移。这个值就是 light 的 `shadowNearPlane`，可在 Inspector 调。

它的作用是让光源的 near plane **不要贴得太近**，避免精度问题。一般设为 0~1。

---

## 九、Culling Bias（级联过渡区的剔除）

### 9.1 问题

阴影 caster 的剔除（Caster 阶段剔除不在光源视锥里的物体）和级联边界不重合时，**级联接缝处可能突然多出或丢失阴影**——比如一个物体在 Cascade 0 视锥边缘，半截被裁掉，阴影"突然消失"。

### 9.2 解决方案：剔除时把级联边界扩大一点

```csharp
// Shadows.cs:238
float cullingFactor = Mathf.Max(0f, 0.8f - _settings.Directional.CascadeFade);

// Shadows.cs:255
splitData.shadowCascadeBlendCullingFactor = cullingFactor;
```

`shadowCascadeBlendCullingFactor` 告诉 Unity：**剔除级联内的 caster 时，把级联视锥向外扩一点**，避免接缝处的物体被意外裁掉。

`cullingFactor = 0.8 - CascadeFade`：fade 越大（级联过渡越长），剔除扩展也越大。

---

## 十、把所有"质量"相关的字段串起来

```csharp
// ShadowSettings.cs:38-58
public struct DirectionalShadow
{
    public MapSize AtlasSize;            // atlas 分辨率
    public FilterMode Filter;            // PCF 模式
    public int CascadeCount;             // 级联数（1~4）
    public float CascadeRatio1/2/3;      // 级联比例
    public float CascadeFade;            // 级联渐隐
    public CascadeBlendMode CascadeBlend;// soft / dither / hard
}

[SerializeField, Range(0.001f, 1f)] private float _maxDistance;     // 阴影最远距离
[SerializeField, Range(0.001f, 1f)] private float _distanceFade;    // 阴影距离衰减
```

这些字段的"下游消费者"：

```
┌─────────────────────────────────────────────────────────────────────┐
│ ShadowSettings.Directional                                            │
│   AtlasSize ─────────► atlas 纹理大小 (atlasSize × atlasSize)        │
│   Filter ────────────► PCF keyword + texelSize 倍数                  │
│   CascadeCount ──────► _CascadeCount + array sizes                   │
│   CascadeRatios ─────► ComputeDirectionalShadowMatrices... 调用参数  │
│   CascadeFade ───────► cascade 内部 fade + culling factor             │
│   CascadeBlend ──────► _CASCADE_BLEND_SOFT / _CASCADE_BLEND_DITHER  │
│                                                                        │
│ ShadowSettings.MaxDistance / DistanceFade                              │
│   _ShadowDistanceFade.x = 1/MaxDistance                                │
│   _ShadowDistanceFade.y = 1/DistanceFade                               │
│   _ShadowDistanceFade.z = 1/(1 - cascadeFade²)                         │
└─────────────────────────────────────────────────────────────────────┘
```

外加每个 Light 自带：
- `shadowStrength`：阴影强度
- `shadowNormalBias`：Normal Bias 基础量
- `shadowBias`：slope-scale bias
- `shadowNearPlane`：近平面偏移

---

## 十一、自己写一遍"高质量阴影"的最小骨架

完整的（含 PCF + cascade blend）实现起来代码量大，**但骨架非常清晰**：

```csharp
// 1) CPU: 对每盏光每级联
for light in lights:
    for cascade in 0..cascadeCount:
        ComputeDirectionalShadowMatricesAndCullingPrimitives(
            ..., splitData.cullingSphere, ...);
        
        SetCascadeData(cascade, splitData.cullingSphere, tileSize);
        // sphere.w = (r - filterSize)²
        // data.x = 1/r², data.y = filterSize × √2
        
        SetViewport(cascade, split, tileSize);
        SetGlobalDepthBias(0, light.shadowBias);
        DrawShadows(shadowSettings);

// 2) CPU: 设置全局参数
SetGlobalInt(_CascadeCount, settings.Directional.CascadeCount);
SetGlobalVectorArray(_CascadeCullingSpheres, ...);
SetGlobalVectorArray(_CascadeData, ...);
SetGlobalMatrixArray(_DirectionalShadowMatrices, ...);
SetGlobalVector(_ShadowDistanceFade, ...);
SetGlobalVector(_ShadowAtlasSize, ...);

// 3) 设置 keyword
EnableKeyword("_DIRECTIONAL_PCF3");  // 或 5 / 7
EnableKeyword("_CASCADE_BLEND_SOFT");  // 或 _CASCADE_BLEND_DITHER
```

```hlsl
// 4) Shader: GetShadowData
for cascade in 0.._CascadeCount:
    if InCullingSphere(surfaceWS.position, _CascadeCullingSpheres[cascade]):
        cascadeIndex = cascade;
        break;

strength *= FadedShadowStrength(depth, 1/MaxDist, 1/DistanceFade);  // 全局
if lastCascade: strength *= FadedShadowStrength(...);                 // 内部

// 5) Shader: GetDirectionalShadowAttenuation
bias = normal × (lightBias × texelSize × √2);
sts = mul(_DirectionalShadowMatrices[tileIdx], float4(pos + bias, 1));
shadow = PCF(sts);  // 多次采样取平均
return lerp(1, shadow, lightStrength);
```

每个步骤都是独立的逻辑单元。**学完本节你应该能解释为什么阴影边缘会柔和、为什么距离远了阴影会消失、为什么级联之间看不出接缝**。

---

## 十二、八节学习路线回顾

```
【1】数学基础                → 坐标系、向量、矩阵
       ↓
【2】渲染管线流程            → CPU/GPU 各阶段干什么
       ↓
【3】MVP 变换                → 物体从模型空间到屏幕空间
       ↓
【4】光栅化和抗锯齿          → 三角形怎么变成像素
       ↓
【5】DrawCall 优化解析       → 初步了解优化方法
       ↓
【6】方向光                  ← 光照怎么算（Cull→CBUFFER→BRDF）
       ↓
【7】阴影原理与图集          ← Shadow Map 两步法 + 多 tile atlas
       ↓
【8】级联阴影                ← 多级分辨率 + 剔除球
       ↓
【9】阴影质量                ← acne 修复、PCF、blend、fade
```

到这一节为止，你对方向光 + 阴影的**完整链路**已经掌握。下一步教程会进入**其他光源类型**（点光源、聚光灯）或**透明物体阴影**——那些是基于这套机制的扩展。**核心思想不变**：cull→CPU 填数据→GPU 采样→光照衰减。