# 【6】方向光阴影 - 原理与图集（Shadow Atlas）

> 本节讲阴影的核心机制：shadow map 两步法、多 tile 图集布局、把世界空间的点变换到 tile UV 的矩阵推导。学完后你应该能解释清楚 `_DirectionalShadowAtlas` 是怎么从 CPU 画出来、怎么在 shader 里被采样的。

---

## 一、阴影的物理本质

> **一个点是否在阴影里 = 从光源看过去，这个点前面有没有别的东西挡着它。**

直接对每个像素做"从这个像素发一条光线到光源，问中间有没有东西挡"——这叫**光线追踪（Ray tracing）**，太贵了。一般来说，我们的实时渲染用 **Shadow map**：把"中间有没有东西挡"这件事**预计算**成一张深度图，渲染主场景时直接查表。

```
        光源
         │
         │   有物体挡着
         ▼  ╱╱╱   ← 这个点的光线被前面挡住
        ┌─────────┐
        │ A 点    │   ← 在阴影里
        └─────────┘
         │   物体（cube）
         │   ┌───┐
         │   │   │ ← 物体
         │   │   │
         ▼   └───┘
        ┌─────────┐
        │ B 点    │   ← 不在阴影里
        └─────────┘
         │
         ▼
        地面
```

---

## 二、Shadow Map 两步法

### 第一步（Caster Pass / 生产者）：画一张深度图

把相机搬到光源的位置，用光源的视角渲染**整个场景**。但这次不要颜色，**只要深度**——把"从光源看，每个方向最近的物体有多远"存到一张纹理里，这张纹理叫 **shadow map**。

```csharp
// Shadows.cs
_buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);  // 用光源的 VP
ExecuteBuffer();
_context.DrawShadows(ref shadowSettings);                         // 画"LightMode" = "ShadowCaster" 的 Pass
```

`_context.DrawShadows` 会自动把所有可见渲染器的 `LightMode = ShadowCaster` Pass 跑一遍——这个 Pass 通常只输出深度（`ColorMask 0`）。

### 第二步（Receiver Pass / 消费者）：采样对比

渲染主场景时，每个被光照到的片段 P：

```
                光源 view 空间
                  │
                  ▼
   ┌──────────────────────────┐
   │ shadow map 里某格 (u, v) │   ← 这一格记录了"从光源这个方向看，最近的物体有多远"
   └──────────────────────────┘
                   
   P 点 → 投到光源 view 空间 → 得到 (u, v, depth_P)
                                    │
                                    ├─ depth_S = shadow_map[u, v]      ← 已有
                                    ├─ depth_P = P 自己在光源 view 下的深度
                                    └─ if depth_P > depth_S → 在阴影里
```

关键判断：
- **depth_P > depth_S**：P 比最近物体更远 → P 被挡住了 → 阴影
- **depth_P ≤ depth_S**：P 至少和最近物体一样近 → 不被挡 → 受光

---

## 三、方向光阴影：正交投影

| 光源类型 | 投影方式 | 类比的相机 |
|---------|---------|-----------|
| 方向光 | **正交投影**（Orthographic） | 长焦相机，没有近大远小 |
| 聚光灯 | 透视投影 | 普通相机 |
| 点光源 | 立方贴图（6 个正交面） | 6 个正交相机 |

方向光是平行光，所以从光源"看"出去，所有光线平行，没有"近大远小"的概念——**正交投影**最合适。

这意味着方向光的 shadow map 渲染时：
- 相机在光源方向的无穷远，**没有具体位置**
- 视野是一个**长方体盒子**（不是视锥）
- 盒子朝向光照方向

```csharp
// Shadows.cs:243-253
_cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
    light.VisibleLightIndex,
    cascadeIndex,        // 当前级联
    cascadeCount,        // 总级联数
    cascadeRatios,       // 级联比例
    tileSize,            // tile 边长（像素）
    light.NearPlaneOffset,
    out Matrix4x4 viewMatrix,
    out Matrix4x4 projectionMatrix,
    out ShadowSplitData splitData
);
```

`ComputeDirectionalShadowMatricesAndCullingPrimitives` 这个函数是 Unity 帮你做的"构造正交盒子"的活——它会算出一个刚好包住"相机视锥在阴影范围内的部分"的长方体，返回 view 矩阵和 projection 矩阵。

---

## 四、Shadow Atlas：多 tile 布局

如果只有一盏光、一层级联，那 shadow map 就是一张纹理。但**多盏光 × 多级联 = 多个独立的 shadow map**。每张都建一个 RenderTexture 太浪费切换成本，所以教程把它们拼到**同一张大纹理**的不同矩形区域里——这就是 atlas。

```
atlas 1024×1024（一张大纹理）
┌──────────────────┬──────────────────┐
│                  │                  │
│   tile 0         │   tile 1         │
│   512×512        │   512×512        │
│   光A-级联0      │   光A-级联1      │
│                  │                  │
├──────────────────┼──────────────────┤
│                  │                  │
│   tile 2         │   tile 3         │
│   512×512        │   512×512        │
│   光A-级联2      │   光A-级联3      │
│                  │                  │
└──────────────────┴──────────────────┘
```

切分规则（Shadows.cs:184-202）：

```csharp
int tiles = _settings.Directional.CascadeCount * _shadowedDirectionalLightCount;
int split;
if (tiles <= 1)        split = 1;   // 1 块
else if (tiles <= 4)   split = 2;   // 2×2 = 4 块
else                   split = 4;   // 4×4 = 16 块

int tileSize = atlasSize / split;
```

> 注意：`tiles` 是"总块数"，`split` 是"边数"。1 块就是 1，4 块就是 2（因为 2×2=4），16 块就是 4。

每盏光占 `cascadeCount` 个 tile，**连续排列**：
- 光 A 占 tile 0、1、2、3
- 光 B 占 tile 4、5、6、7

### tile 索引 → tile 在 atlas 中的位置

```csharp
// Shadows.cs:264
int tileIndex = tileOffset + cascadeIndex;  // 该级联所在的 tile

// Shadows.cs:325-330
private Vector2 SetTileViewport(int index, int split, float tileSize)
{
    Vector2 offset = new Vector2(index % split, index / split);  // 行列坐标
    _buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
    return offset;
}
```

举例：split=2、tileSize=512、tileIndex=2
- `offset = (2 % 2, 2 / 2) = (0, 1)`
- `Rect(0, 512, 512, 512)` = atlas 的左下角 512×512 区域

设置 viewport 后，**所有 GPU 的绘制都只写进这个矩形**——`DrawShadows` 自动只把这一块的深度写到 atlas 对应区域。

---

## 五、ConvertToAtlasMatrix：把世界坐标变到 tile UV

shader 端做深度比较时，需要：
```
P 世界坐标  ──light VP──►  光源 clip 空间 (-1, 1)
                       ──缩放/平移──►  tile UV (0, 1) ← 但只是这一个 tile 的 UV，不是整张 atlas
                       ──z 单独处理──►  depth 比较用
```

**这个"世界 → tile UV"的变换矩阵**就是 `_DirectionalShadowMatrices[tileIndex]`。**它把世界坐标直接变成"在这个 tile 内的 UV + 深度"**——shader 拿到这个矩阵后，一行 `mul` 就到位了。

下面推导这一步怎么算：

### 5.1 标准 view-projection 矩阵 M = P × V

对一个点 P_world，标准的"光源 view-projection"矩阵会把它变换到：
- xy ∈ [-1, 1]（光源的"屏幕"范围）
- z ∈ [-1, 1]（光源的深度范围）

### 5.2 从 [-1, 1] 到 [0, 1]（单个 tile）

如果只有 1 个 tile（split=1），UV 范围就是整张 atlas 的 [0, 1]。从 [-1, 1] 线性映射到 [0, 1]：

```
uv = (clip.xy + 1) / 2 = 0.5 * clip.xy + 0.5
```

矩阵形式（对 x 列）：

```
M_new.xx = 0.5 * M.xx + 0.5 * M.wx
M_new.xy = 0.5 * M.xy + 0.5 * M.wy
M_new.xz = 0.5 * M.xz + 0.5 * M.wz
M_new.xw = 0.5 * M.xw + 0.5 * M.ww
```

对应代码：

```csharp
// Shadows.cs:318-321
matrix.m20 = 0.5f * (matrix.m20 + matrix.m30);  // z 列
matrix.m21 = 0.5f * (matrix.m21 + matrix.m31);
matrix.m22 = 0.5f * (matrix.m22 + matrix.m32);
matrix.m23 = 0.5f * (matrix.m23 + matrix.m33);
```

等等，**为什么是改 m20~m23 而不是 m00~m03**？因为矩阵是用**列主序**存的：

| 元素 | 含义 | 在 `mul(M, p)` 里的作用 |
|-----|------|------------------------|
| `m00, m01, m02, m03` | 第一列（x 输出） | 算 `out.x` |
| `m10, m11, m12, m13` | 第二列（y 输出） | 算 `out.y` |
| `m20, m21, m22, m23` | 第三列（z 输出） | 算 `out.z` |
| `m30, m31, m32, m33` | 第四列（w 输出） | 算 `out.w` |

```csharp
// Shadows.cs:289-296 (第一行：算 out.x 列 = 0.5 * 原 out.x + 0.5 * 原 out.w)
matrix.m00 = (0.5f * (matrix.m00 + matrix.m30) + offset.x * matrix.m30) * scale;
matrix.m01 = (0.5f * (matrix.m01 + matrix.m31) + offset.x * matrix.m31) * scale;
matrix.m02 = (0.5f * (matrix.m02 + matrix.m32) + offset.x * matrix.m32) * scale;
matrix.m03 = (0.5f * (matrix.m03 + matrix.m33) + offset.x * matrix.m33) * scale;
```

外层的 `* scale` 暂时跳过，5.3 节解释。

### 5.3 多 tile 时还要平移 + 缩放

如果 split=2，每个 tile 只占 atlas 的 1/4。tile (offsetX, offsetY) 占 atlas 的 [offsetX/2, offsetX/2 + 0.5] × [offsetY/2, offsetY/2 + 0.5]：

```
tile (0, 0) 占 [0, 0.5] × [0, 0.5]
tile (1, 0) 占 [0.5, 1] × [0, 0.5]
tile (0, 1) 占 [0, 0.5] × [0.5, 1]
tile (1, 1) 占 [0.5, 1] × [0.5, 1]
```

所以要把 [-1, 1] 范围**先压到 [0, 1]**，再**缩放到 tile 大小**，再**平移到 tile 偏移**：

```
uv = (clip.xy + 1) / 2      ← 0.5 * xy + 0.5
   = uv_raw * (1/split) + offset/split
```

合并到矩阵：

```csharp
matrix.m00 = (0.5f * (matrix.m00 + matrix.m30) + offset.x * matrix.m30) * scale;
//            ↑ 0.5 * xy                       ↑ offset * w                ↑ 1/split
```

`0.5 * (m00 + m30)` 是把 xy 压到 [0, 1]；`+ offset.x * m30` 是把 w 项乘以 offset 加进去（让平移起作用）；最后 `* scale = 1/split` 是整体缩到 tile 大小。

### 5.4 Reverse-Z：GPU 平台差异

```csharp
// Shadows.cs:300-306
if (SystemInfo.usesReversedZBuffer)
{
    matrix.m20 = -matrix.m20;
    matrix.m21 = -matrix.m21;
    matrix.m22 = -matrix.m22;
    matrix.m23 = -matrix.m23;
}
```

D3D11/12、Metal、Vulkan 用 **reverse-Z**（近平面深度 = 1，远平面 = 0），而 OpenGL 是正序（近 = 0，远 = 1）。reverse-Z 让远平面精度更高。

需要把所有 z 相关的元素取反，因为我们的阴影深度比对默认是正序的（远 > 近 → 在阴影里）。

### 5.5 完整流程图

```
                         ┌───────────────┐
                         │  P (世界坐标)  │
                         └───────┬───────┘
                                 │
                                 │  mul(_DirectionalShadowMatrices[i], float4(P, 1))
                                 │
                                 ▼
                ┌────────────────────────────────────┐
                │  (uv.x, uv.y, depth, 1)            │
                │   uv ∈ [0, 1] 当前 tile            │
                │   depth ∈ [0, 1] reverse-Z 或正序 │
                └────────────────────────────────────┘
                                 │
                ┌────────────────┼─────────────────┐
                ▼                ▼                 ▼
        SAMPLE_TEXTURE2D_   depth (z)         w (透视除法分母)
        SHADOW(uv) 用于比较  比较用            通常不用
```

shader 端的采样代码（Shadows.hlsl:103-108）：

```hlsl
float SampleDirectionalShadowAtlas(float3 positionSTS)
{
    return SAMPLE_TEXTURE2D_SHADOW(
        _DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}
```

`SAMPLE_TEXTURE2D_SHADOW` 这个宏会自动做：取 xy 当 UV、z 当 depth、查 depth comparison 采样器（PCF 硬件过滤）。

---

## 六、从零写一遍最小阴影系统

### 6.1 最小骨架：CPU 侧

```csharp
public class SimpleShadows
{
    private static readonly int AtlasId = Shader.PropertyToID("_DirectionalShadowAtlas");
    private static readonly int MatrixId = Shader.PropertyToID("_DirectionalShadowMatrix");
    private static Matrix4x4 _matrix;

    private readonly CommandBuffer _buffer = new() { name = "Shadows" };

    public void Render(CullingResults culling, Light light)
    {
        // 1) 分配一张 1024×1024 的纹理
        _buffer.GetTemporaryRT(AtlasId, 1024, 1024, 32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
        _buffer.SetRenderTarget(AtlasId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        _buffer.ClearRenderTarget(true, false, Color.clear);

        // 2) 算出光源的 VP 矩阵
        culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
            0, 0, 1, Vector3.zero, 1024, 0f,
            out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData splitData);

        // 3) 把 VP 矩阵变成"世界 → tile UV"的矩阵
        Matrix4x4 m = proj * view;
        if (SystemInfo.usesReversedZBuffer)
            for (int i = 0; i < 4; i++) m[i, 2] = -m[i, 2];
        // 这里省略 [-1,1] → [0,1] 的变换...
        _matrix = m;

        // 4) 用光源的 VP 矩阵渲染 ShadowCaster Pass
        _buffer.SetViewProjectionMatrices(view, proj);
        _buffer.SetGlobalMatrix(MatrixId, _matrix);

        var settings = new ShadowDrawingSettings(culling, 0) { splitData = splitData };
        Graphics.ExecuteCommandBuffer(_buffer);
        _buffer.Clear();
        ScriptableRenderContext.current.DrawShadows(ref settings);
    }
}
```

### 6.2 最小骨架：Shader 侧

```hlsl
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
SAMPLER_CMP(sampler_linear_clamp_compare);

float4x4 _DirectionalShadowMatrix;

float GetShadow(float3 worldPos)
{
    float4 sts = mul(_DirectionalShadowMatrix, float4(worldPos, 1.0));
    return SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, sampler_linear_clamp_compare, sts.xyz);
}
```

就是这么简洁。但这只是**单光源单级联**——没有多 tile、没有级联、没有阴影质量优化。

---

## 七、回顾整条链路

```
┌────────────────────────────────────────────────────────────────────┐
│ 1. CPU：分配 atlas 纹理 (Shadows.cs:147-157)                        │
│    GetTemporaryRT(_DirectionalShadowAtlas, atlasSize, atlasSize)   │
└──────────────────────────────────┬─────────────────────────────────┘
                                   ▼
┌────────────────────────────────────────────────────────────────────┐
│ 2. CPU：对每盏方向光、每个级联                                      │
│    a) ComputeDirectionalShadowMatricesAndCullingPrimitives(...)     │
│       → 算 view / proj / splitData                                  │
│    b) SetTileViewport(...)                                          │
│       → 把 viewport 划到对应 tile                                   │
│    c) ConvertToAtlasMatrix(...)                                     │
│       → 把 VP 矩阵变成"世界 → tile UV"矩阵                         │
│    d) SetViewProjectionMatrices(view, proj) + DrawShadows(...)      │
│       → 用光源 VP 渲染 ShadowCaster Pass, 深度只写到当前 tile      │
└──────────────────────────────────┬─────────────────────────────────┘
                                   ▼
┌────────────────────────────────────────────────────────────────────┐
│ 3. CPU：把所有数据传 GPU                                            │
│    SetGlobalMatrixArray(_DirectionalShadowMatrices, ...)            │
└──────────────────────────────────┬─────────────────────────────────┘
                                   ▼
┌────────────────────────────────────────────────────────────────────┐
│ 4. Shader：每个被照亮的片段                                         │
│    a) worldPos → mul(M, worldPos) → tile 空间坐标                  │
│    b) SAMPLE_TEXTURE2D_SHADOW(atlas, sampler, sts.xyz)              │
│       → 硬件 PCF 比较深度，返回 0~1 的"被遮挡比例"                 │
│    c) 把这个值乘到 IncomingLight 上，遮挡越严重光越弱               │
└────────────────────────────────────────────────────────────────────┘
```

---

## 八、下一节预告

单一 shadow map 的最大问题是**分辨率**——一张图覆盖范围越大，每个 texel 对应的世界空间面积越大，阴影越粗糙。下一节【7】级联阴影会讲怎么把覆盖范围拆成几段，每段一张高分辨率图，从而兼顾远近。