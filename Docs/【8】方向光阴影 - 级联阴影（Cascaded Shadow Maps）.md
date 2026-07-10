# 【8】方向光阴影 - 级联阴影（Cascaded Shadow Maps）

> 本节讲级联阴影：单一 shadow map 的"分辨率 vs 覆盖范围"矛盾怎么解；剔除球、剔除数据怎么算；shader 端怎么选级联。学完后你应该能解释为什么远处的阴影看起来没那么"糊"，以及 `_CascadeCullingSpheres` 和 `_CascadeData` 数组每一项是什么含义。

---

## 一、为什么要级联？

方向光影响场景里所有方向的物体，所以它的 shadow map 必须覆盖一个很大的范围。假设最大阴影距离 100m，用 1024×1024 的 shadow map：

```
1 texel ≈ 100m / 1024 ≈ 10cm
```

- **人物在 2m 外**：脚下的阴影 30cm，屏幕上有几十个像素 → shadow map 里只有 **3 个 texel** 描述 → 锯齿严重
- **房子在 80m 外**：屏幕上 5 个像素 → shadow map 里用了 **100 个 texel** → 严重浪费

这就是分辨率矛盾：

```
覆盖范围大 → 每个 texel 世界空间大 → 近处细节糊
覆盖范围小 → 远处物体根本不在 shadow map 里 → 没阴影
```

**级联**就是把这个"又大又糊"的 shadow map 拆成几张：

```
┌─────────────────────────────────────┐
│  Cascade 0: 0-10m     1024×1024     │  ← 1 texel ≈ 1cm, 人物脚下超清
├─────────────────────────────────────┤
│  Cascade 1: 10-25m    1024×1024     │  ← 1 texel ≈ 2.5cm, 中距离清晰
├─────────────────────────────────────┤
│  Cascade 2: 25-50m    1024×1024     │  ← 1 texel ≈ 5cm
├─────────────────────────────────────┤
│  Cascade 3: 50-100m   1024×1024     │  ← 1 texel ≈ 10cm, 远距离够用
└─────────────────────────────────────┘
```

每张图分辨率相同（1024），但覆盖范围不同 → **近处 1cm/texel（清晰），远处 10cm/texel（够用）**。

---

## 二、级联比例（CascadeRatios）

`ShadowSettings.cs:46-57`：

```csharp
[Range(0f, 1f)] public float CascadeRatio1;  // 0.1
[Range(0f, 1f)] public float CascadeRatio2;  // 0.25
[Range(0f, 1f)] public float CascadeRatio3;  // 0.5

public Vector3 CascadeRatios => new Vector3(CascadeRatio1, CascadeRatio2, CascadeRatio3);
```

这 3 个比例把 `[0, MaxDistance]` 分成 4 段（4 个级联）：

```
0  CascadeRatio1×MaxDistance  CascadeRatio2×MaxDistance  CascadeRatio3×MaxDistance  MaxDistance
|←──  Cascade 0  ──→|<──  Cascade 1  ──→|<────  Cascade 2  ────→|<────  Cascade 3  ────→|
   0-10% (0-10m)         10-25% (10-25m)     25-50% (25-50m)        50-100% (50-100m)
```

`CascadeRatios` 是个 `Vector3`，传给 Unity 的 `ComputeDirectionalShadowMatricesAndCullingPrimitives`，由它内部去算每个级联覆盖的距离区间。

---

## 三、每个级联的正交盒子

每个级联都有自己的 view + projection 矩阵（光源的视锥体只覆盖这个级联的距离段）。Unity 帮你构造这个盒子：

```csharp
// Shadows.cs:243-253
_cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
    light.VisibleLightIndex,
    cascadeIndex,    // 当前级联（0/1/2/3）
    cascadeCount,    // 总级联数
    cascadeRatios,   // 级联比例
    tileSize,        // 这个 tile 的边长
    light.NearPlaneOffset,
    out Matrix4x4 viewMatrix,
    out Matrix4x4 projectionMatrix,
    out ShadowSplitData splitData
);
```

这个函数做的事：
1. 找出"相机视锥里"这个级联距离段对应的部分
2. 构造一个**刚好包住这部分**的正交盒子
3. 返回 view 矩阵、projection 矩阵
4. 返回 `splitData`，里面有**这个级联的剔除球**（下面解释）

---

## 四、剔除球（Culling Sphere）

每个级联有自己的覆盖范围（一个长方体），但**shader 端需要一个简单的"我落在不在这个级联里"的判断**——长方体判断太复杂。

教程用**球**来近似长方体：把长方体"塞进"一个刚好包住的球。球只有 4 个数（中心 xyz + 半径 w），shader 判断 4D 点积平方 vs 半径平方，开销极低。

```
         ┌─────────────┐
       ╱             ╱ │
     ╱   长方体     ╱   │
   ╱             ╱     │
  ┌─────────────┐      │
  │             │      │
  │             │      │  ← 球比长方体大一点，但判断简单
  │             │      │
  │             │    ╱
  │             │  ╱
  └─────────────┘╱
       ╲___________╱
        剔除球（半径等于长方体对角线的一半）
```

### 4.1 CPU 端的剔除球

`ShadowSplitData.cullingSphere` 就是 Unity 给的剔除球，是个 `Vector4(球心.x, 球心.y, 球心.z, 半径)`。

### 4.2 Shader 端的剔除球

```hlsl
// Shadows.hlsl:14-18
CBUFFER_START(_CustomShadows)
    int _CascadeCount;
    float4 _CascadeCullingSpheres[MAX_CASCADES_COUNT];  // .xyz = 球心, .w = 半径²
    ...
```

**为什么 w 是半径² 而不是半径？**因为 shader 里要判断"点到球心距离² < 半径²"，把平方提前算好，shader 端**免去一次开方**：

```hlsl
// Shadows.hlsl:59-61
float4 sphere = _CascadeCullingSpheres[i];
float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
if (distanceSqr < sphere.w)  // 已经是平方 vs 平方
```

对应 C# 代码：

```csharp
// Shadows.cs:282-293
private void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
{
    float texelSize = 2f * cullingSphere.w / tileSize;
    float filterSize = texelSize * ((float)_settings.Directional.Filter + 1f);
    cullingSphere.w -= filterSize;
    cullingSphere.w *= cullingSphere.w;  // ← 这里平方了
    CascadeCullingSpheres[index] = cullingSphere;
    ...
}
```

注意 `cullingSphere.w -= filterSize;` —— **把球稍微缩小一点**，避免边缘出现 acne。filter 越大缩得越多（PCF 章节会详细讲）。

---

## 五、级联数据（CascadeData）

`CascadeData[i]` 是个 `Vector4`，每个分量都有用：

```csharp
// Shadows.cs:292
CascadeData[index] = new Vector4(1f / cullingSphere.w, filterSize * 1.4142136f);
```

| 分量 | 含义 | 用在哪儿 |
|-----|------|---------|
| `.x` | `1 / 半径²` | shader 里乘以距离²，免去除法 |
| `.y` | `texelSize × √2` | Normal Bias（下一节讲） |

### 5.1 `.x = 1 / 半径²`：免去除法

shader 里要做 `distanceSqr / radiusSqr` 算距离百分比（用于 cascade fade），写成 `distanceSqr * (1/radiusSqr)` 比 `distanceSqr / radiusSqr` 快：

```hlsl
// Shadows.hlsl:63-65
float fade = FadedShadowStrength(
    distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
);

// FadedShadowStrength 实现（Shadows.hlsl:46-49）
// saturate((1.0 - distance * scale) * fade)
// = saturate((1.0 - distanceSqr / radiusSqr) * fade)
// = saturate((1.0 - distanceSqr * (1/radiusSqr)) * fade)
```

### 5.2 `.y = texelSize × √2`：Normal Bias 基础量

`texelSize = 2 × 球半径 / tile 边长`——即"一个 shadow map 像素在世界空间里多大"。

`× √2` 是最坏情况的安全系数：法线方向沿 texel 对角线时也能逃出当前 texel（详见【8】节）。

---

## 六、shader 端怎么选级联：GetShadowData

```hlsl
// Shadows.hlsl:51-94
ShadowData GetShadowData(Surface surfaceWS)
{
    ShadowData data;
    data.cascadeBlend = 1.0;
    // 1) 先算全局的"阴影强度"，按相机距离 MaxDistance 的比例衰减
    data.strength = FadedShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);

    int i;
    // 2) 遍历所有级联，找第一个包住这个点的级联
    for (i = 0; i < _CascadeCount; i++)
    {
        float4 sphere = _CascadeCullingSpheres[i];
        float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
        if (distanceSqr < sphere.w)
        {
            // 找到了
            float fade = FadedShadowStrength(
                distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
            );
            if (i == _CascadeCount - 1)
                data.strength *= fade;       // 最后一个级联：再乘一个内部 fade
            else
                data.cascadeBlend = fade;    // 其他级联：用 fade 当 blend 系数
            break;
        }
    }

    // 3) 如果所有级联都包不住这个点（极端情况），强度清零
    if (i == _CascadeCount)
        data.strength = 0.0;

    data.cascadeIndex = i;
    return data;
}
```

### 6.1 算法解析

**步骤 1：全局距离衰减**

`FadedShadowStrength(surfaceWS.depth, 1/MaxDistance, 1/DistanceFade)`：
- `surfaceWS.depth` 是这个点到相机的距离（取负的 viewZ，所以是正数）
- 当 `depth = MaxDistance` 时，`depth × (1/MaxDistance) = 1` → `1 - 1 = 0` → 强度为 0
- 当 `depth = 0`（在相机处）时，强度为 `1 × fade`

**步骤 2：找第一个包住的级联**

按顺序遍历级联，**第一个球包住这个点的就是它**。级联是嵌套设计的（Cascade 0 覆盖近处，Cascade 3 覆盖最远），所以一个点只可能属于 0 个或 1 个级联。

**步骤 3：超出范围的兜底**

如果一个点**所有级联都包不住**（比如在 MaxDistance 之外但在 DistanceFade 内），把强度直接清零——`strength = 0`，light.attenuation = 0，光照完全衰减，等于"在远处完全没阴影"。

### 6.2 ShadowData 结构

```hlsl
// Shadows.hlsl:36-41
struct ShadowData
{
    int cascadeIndex;    // 这个片段落在哪个级联
    float cascadeBlend;  // 当前级联与下一个级联的混合系数（默认 1 = 不混合）
    float strength;      // 最终的阴影强度
};
```

`cascadeBlend` 用于【8】节 cascade blending（让两个相邻级联之间的过渡更平滑）。

### 6.3 与光无关：所有方向光共用一份 ShadowData

`GetShadowData` 不接 light 参数——因为级联是**世界空间距离**划分的，跟具体哪盏光无关。所有光都查同一份 `_CascadeCullingSpheres`。

---

## 七、CPU 端只对 index==0 写级联数据

```csharp
// Shadows.cs:258-262
if (index == 0)
{
    SetCascadeData(cascadeIndex, splitData.cullingSphere, tileSize);
}
```

**为什么？**因为级联布局跟具体的光无关——`ComputeDirectionalShadowMatricesAndCullingPrimitives` 只用 `cascadeRatios` 决定级联区间，跟 `visibleLightIndex` 无关。所以无论几盏光，级联布局都一样。**只对第一盏光算一次**。

---

## 八、整体链路：CPU → GPU

```
┌────────────────────────────────────────────────────────────────────────────┐
│ ShadowSettings (CascadeRatios = (0.1, 0.25, 0.5), CascadeCount = 4)        │
└────────────────────────────────────┬───────────────────────────────────────┘
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ RenderDirectionalShadow(i, split, tileSize) (对每盏光、每个级联调用)         │
│   ├─ ComputeDirectionalShadowMatricesAndCullingPrimitives(...)              │
│   │   → view, proj, splitData (含 cullingSphere)                            │
│   ├─ if (i == 0) SetCascadeData(cascadeIndex, cullingSphere, tileSize)      │
│   │   → CascadeCullingSpheres[index] = sphere (w²已存)                     │
│   │   → CascadeData[index] = (1/w², texelSize × √2)                       │
│   ├─ tileIndex = lightIndex × cascadeCount + cascadeIndex                   │
│   ├─ DirectionalShadowMatrices[tileIndex] = ConvertToAtlasMatrix(...)        │
│   ├─ SetViewProjectionMatrices(view, proj) + SetGlobalDepthBias + DrawShadows│
└────────────────────────────────────┬───────────────────────────────────────┘
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ GPU 端                                                                       │
│   cbuffer _CustomShadows {                                                  │
│       int _CascadeCount;                                                    │
│       float4 _CascadeCullingSpheres[4];   // 球心 + 半径²                   │
│       float4 _CascadeData[4];             // 1/半径² + texelSize×√2       │
│       float4x4 _DirectionalShadowMatrices[16]; // 4 光 × 4 级联             │
│       float4 _ShadowDistanceFade;                                          │
│   }                                                                          │
└────────────────────────────────────┬───────────────────────────────────────┘
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ LitPassFragment → GetLighting → GetShadowData(surfaceWS)                   │
│   → 遍历 4 个级联球, 找第一个包住的 → cascadeIndex                          │
│   → ShadowData.cascadeIndex = i                                             │
└────────────────────────────────────┬───────────────────────────────────────┘
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ GetDirectionalShadowAttenuation(directional, global, surface)               │
│   → mul(_DirectionalShadowMatrices[directional.tileIndex], worldPos)        │
│   → 拿到 (uv, depth) in tile 空间                                           │
│   → SAMPLE_TEXTURE2D_SHADOW 比较深度 → 0~1                                  │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 九、自己写一遍的最小级联系统

要支持级联，最少需要这 3 件事：

**1. CPU 端**（伪代码）：
```csharp
for cascadeIndex in 0..cascadeCount:
    culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
        lightIdx, cascadeIndex, cascadeCount, cascadeRatios,
        tileSize, 0f, out view, out proj, out splitData);
    
    if lightIdx == 0:
        sphere = splitData.cullingSphere;
        sphere.w *= sphere.w;
        cascadeSpheres[cascadeIndex] = sphere;  // w = r²
        cascadeData[cascadeIndex] = new Vector4(1/sphere.w, 2*sphere.w/tileSize*1.4142f);
    
    SetViewport(cascadeIndex, split, tileSize);
    // ... 渲染到 tile
```

**2. Shader 端**：
```hlsl
int _CascadeCount;
float4 _CascadeCullingSpheres[4];

int GetCascadeIndex(float3 worldPos)
{
    for (int i = 0; i < _CascadeCount; i++)
    {
        float d2 = dot(worldPos - _CascadeCullingSpheres[i].xyz,
                       worldPos - _CascadeCullingSpheres[i].xyz);
        if (d2 < _CascadeCullingSpheres[i].w) return i;
    }
    return _CascadeCount;  // 都不包
}
```

**3. 着色器拿到 cascadeIndex 后**：用对应的矩阵采样对应 tile 的 shadow map。

---

## 十、回顾与展望

到本节为止，你已经会了：
- shadow map 两步法
- 多 tile atlas 布局
- 矩阵变换：世界 → tile UV
- 级联：每段单独一张高分辨率图
- 剔除球：快速判断"我在哪个级联里"
- 级联数据：texelSize 和距离倒数

下一节【8】会讲怎么让阴影**好看**——消灭 acne（深度 bias + 法线 bias）、让级联之间平滑过渡（cascade blending）、加 PCF 软化阴影边缘。