# 【6】方向光（Directional Lights）

> 本节教你把"一盏方向光怎么把光照到物体上"这件事，从 C# 到 Shader 完整打通。学完后你应该能独立写出一套支持多盏方向光、含 BRDF 的最小光照系统。

---

## 一、方向光的物理本质

方向光是**在无穷远处、平行光**——就像太阳。光线的方向一致、没有位置、不会衰减。

```
      太阳（无穷远）
        │  │  │
        │  │  │
        ▼  ▼  ▼         所有光线平行，没有起点
        │  │  │
       ─┴──┴──┴─         物体表面被均匀照射
```

这一点决定了它在数学上的特殊性：
- **没有位置** → 不会有"距离衰减"
- **方向统一** → 不需要算"从光源到点"的连线，只需要一个固定方向
- **在数学上等价于一个正交相机**（后面阴影那节会用到）

---

## 二、整体架构：CPU → GPU 的数据流

我们要把"场景里有多少盏方向光、每盏的方向和颜色是什么"传到 shader 里。流程是这样的：

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ CameraRenderer.Render (CameraRenderer.cs:39)                                │
│   │                                                                         │
│   ├─ 1. Cull(...) ─────────────────────────┐                                │
│   │     └─ ScriptableCullingParameters     │                                │
│   │     └─ _context.Cull(...)              │  ← 剔除掉相机看不到的物体      │
│   │     ↓ 返回 CullingResults              │    同时也告诉了 Unity 哪些     │
│   │                                        │    光会影响这个相机            │
│   │                                        ▼                                │
│   ├─ 2. _lighting.Setup(...) ──────────────┐                                │
│   │     ├─ _shadows.Setup()                │  ← 配置阴影参数                │
│   │     ├─ SetupLights() ──────────────┐   │                                │
│   │     │   ├─ 遍历 visibleLights      │   │                                │
│   │     │   ├─ 找出方向光              │   │                                │
│   │     │   ├─ SetupDirectionalLight   │   │                                │
│   │     │   └─ _shadows.Render()       │   │  ← 画 shadow map               │
│   │     └─ SetGlobalVectorArray()      │   │  ← 把数据传到 GPU 全局 cbuffer │
│   │                                    ▼   ▼                                │
│   ├─ 3. SetupCamera()                                                       │
│   └─ 4. DrawVisibleGeometry() ──────────────┐                               │
│         └─ _context.DrawRenderers(...)      │                               │
│         ↓ 命中 "CustomLit" Pass             │                               │
│         ┌───────────────────────────────────▼────────────────────────────┐  │
│         │ LitPass.hlsl::LitPassFragment                                  │  │
│         │   ├─ 构造 Surface (颜色/法线/视角方向)                         │  │
│         │   ├─ GetLighting(surface, brdf) ──────► Lighting.hlsl          │  │
│         │   │   ├─ 遍历所有方向光                                        │  │
│         │   │   │   └─ Light.hlsl::GetDirectionalLight(...)              │  │
│         │   │   │       ├─ 从 _DirectionalLightColors/Directions 读      │  │
│         │   │   │       └─ light.attenuation = GetDirectionalShadow...   │  │
│         │   │   └─ 累加每盏光的颜色贡献                                  │  │
│         │   └─ 返回最终颜色                                              │  │
│         └────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**两个关键点**：
1. **剔除（Cull）阶段是光照数据的源头**：`CullingResults.visibleLights` 告诉你哪些光需要处理
2. **CPU 把数据塞进全局 Vector4[] 数组，shader 用 cbuffer 读**：这是个数组+固定最大长度的模式，类似"早期 GPU 没有动态数组"的妥协做法

---

## 三、CPU 侧详解：Lighting.cs

整个文件做的事情就 3 件：找到方向光 → 收集数据 → 喂给 GPU。

### 3.1 全局数组：CPU 和 GPU 的"握手协议"

```csharp
// Lighting.cs
private const int MAX_DIRECTIONAL_LIGHT_COUNT = 4;

private static readonly int DirectionalLightCountId      = Shader.PropertyToID("_DirectionalLightCount");
private static readonly int DirectionalLightColorsId     = Shader.PropertyToID("_DirectionalLightColors");
private static readonly int DirectionalLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");
private static readonly int DirectionalLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData");

private static Vector4[] DirectionalLightColors     = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
private static Vector4[] DirectionalLightDirections = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
private static Vector4[] DirectionalLightShadowData = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
```

`Shader.PropertyToID` 把字符串名字哈希成一个 int ID——这是 GPU API 的传统，`glGetUniformLocation` 这种调用太慢，所以缓存成 ID。

`Vector4[]` 是"早期不支持结构体数组 + 内存对齐友好"的取舍。`Vector3` 也能用，但 `Vector4` 能塞额外的东西（后面的 shadow data 用了 .z 和 .y 装其他信息）。

**最大数量 4 是个硬约束**——超过 4 盏方向光直接被截断。这是为了 cbuffer 大小可控、shader 循环能完全展开。

### 3.2 SetupLights：找出所有方向光

```csharp
// Lighting.cs
private void SetupLights()
{
    // 1) 拿剔除后的所有可见光（注意：不是"所有光"，是"相机能看见的光"）
    NativeArray<VisibleLight> visibleLights = _cullingResults.visibleLights;

    int directionalLightCount = 0;

    for (int i = 0; i < visibleLights.Length; i++)
    {
        VisibleLight visibleLight = visibleLights[i];

        // 2) 处理方向光
        if (visibleLight.lightType is LightType.Directional)
        {
            SetupDirectionalLight(directionalLightCount++, ref visibleLight);
            if (directionalLightCount >= MAX_DIRECTIONAL_LIGHT_COUNT) break;
        }
    }

    // 3) 把所有数据一次性传给 GPU（不是逐盏光传）
    _buffer.SetGlobalInt(DirectionalLightCountId, directionalLightCount);
    _buffer.SetGlobalVectorArray(DirectionalLightColorsId, DirectionalLightColors);
    _buffer.SetGlobalVectorArray(DirectionalLightDirectionsId, DirectionalLightDirections);
    _buffer.SetGlobalVectorArray(DirectionalLightShadowDataId, DirectionalLightShadowData);
}
```

三个要点：
1. **`visibleLights` 是剔除结果**——剔除会剔除看不见的物体，但**不会自动剔除看不见的光**（光的影响范围由它的强度+衰减决定，跟相机视角无关）。Unity 4 盏方向光全在场景里就全传过去。
2. **`directionalLightCount++` 是先存再递增**——存进数组时用 0、1、2、3 这种紧凑索引，跟 `_DirectionalLightColors[4]` 对应。
3. **`SetGlobalVectorArray` 是"全量覆盖"**——传的是整个 4 长度的数组，不是只传用到的前 N 个。GPU 端通过 `_DirectionalLightCount` 知道哪些有效。

### 3.3 SetupDirectionalLight：填一盏光的数据

```csharp
// Lighting.cs:93-101
private void SetupDirectionalLight(int index, ref VisibleLight visibleLight)
{
    // finalColor 是 Unity 帮你算好的"最终颜色"，已经把 light.color × intensity 算完了
    DirectionalLightColors[index] = visibleLight.finalColor;

    // localToWorldMatrix 的第三列 = Z 轴 = 物体的"前向"
    // 对方向光来说，前向 = 光线前进的方向
    // 我们要的"指向光源的方向"（从表面射向光源），所以取负
    DirectionalLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);

    // 登记这盏光的阴影，返回 Vector3(强度, tile 偏移, 法线偏移)
    DirectionalLightShadowData[index] = _shadows.ReserveDirectionalShadows(visibleLight.light, index);
}
```

这里有个常被忽视的关键点：**`Light.direction` 是"光线射出去的方向"**，而 shader 里要算 `N · L`，**L 必须是"从表面射向光源的方向"**（入射方向）。所以这里取负号。

```
光源 ──────────► 物体     光线方向（向前）= (1,0,0)
     ◄──────────          入射方向（向后）= (-1,0,0)
```

shader 里的代码也呼应了这一点：

```hlsl
// Light.hlsl
Light GetDirectionalLight(int index, Surface surfaceWS, ShadowData shadowData)
{
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    light.direction = _DirectionalLightDirections[index].xyz;
    ...
}
```

`light.direction` 已经是入射方向了，shader 不用再取负。

---

## 四、GPU 侧详解：Light.hlsl 和 Lighting.hlsl

### 4.1 Light struct：最少必要信息

```hlsl
// Light.hlsl:16-21
struct Light
{
    float3 color;
    float3 direction;
    float attenuation; // 衰减值
};
```

为什么要 `attenuation`？后续阴影要乘进去——阴影里的光要衰减。**当前这盏光被遮住多少**，就是 `attenuation`。

### 4.2 遍历所有方向光：Lighting.hlsl

```hlsl
// Lighting.hlsl:45-55
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
```

`GetShadowData` 一次算出**这个片段落在哪个级联里、距离衰减多少**。所有光共用一份 `ShadowData`——因为级联是按"世界空间距离"切的，跟具体哪盏光无关。

每盏光返回一个 `Light`，再调另一个重载算这盏光对颜色的贡献，最后累加。

### 4.3 每盏光对颜色的贡献：Lighting.hlsl::GetLighting(Surface, BRDF, Light)

```hlsl
// Lighting.hlsl:38-42
float3 GetLighting(Surface surface, BRDF brdf, Light light)
{
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}
```

这就是简化后的渲染方程：

```
最终颜色 = 到达这个表面的光 × 表面把这些光反射出去的比例
        = IncomingLight × DirectBRDF
```

### 4.4 IncomingLight：到达表面的光

```hlsl
// Lighting.hlsl:31-35
float3 IncomingLight(Surface surface, Light light)
{
    return saturate(dot(surface.normal, light.direction) * light.attenuation) * light.color;
}
```

每一项的物理意义：

| 表达式 | 含义 |
|--------|------|
| `dot(N, L)` | 表面法线和光源入射方向的夹角余弦——表面正对光源时为 1，背对时为 0 或负数 |
| `saturate(...)` | 限制到 [0,1]，负数视为 0（背光的表面不接收该光） |
| `× light.attenuation` | 乘上这盏光在这里被遮挡多少（没阴影就是 1） |
| `× light.color` | 乘上光的颜色（白色光就是 (1,1,1)） |

```
       N (法线)
        │
        │  N · L = cos θ
        │  
        ╱ L (入射方向)
```

### 4.5 DirectBRDF：表面把这些光反射多少回去

```hlsl
// BRDF.hlsl:80-89 (SpecularStrength, Cook-Torrance D 项)
float SpecularStrength(Surface surface, BRDF brdf, Light light)
{
    float3 h = SafeNormalize(light.direction + surface.viewDirection);  // 半角向量
    float nh2 = Square(saturate(dot(surface.normal, h)));
    float lh2 = Square(saturate(dot(light.direction, h)));
    float r2  = Square(brdf.roughness);
    float d2  = Square(nh2 * (r2 - 1.0) + 1.00001);
    float normalization = brdf.roughness * 4.0 + 2.0;
    return r2 / (d2 * max(0.1, lh2) * normalization);
}

// BRDF.hlsl:91-95
float3 DirectBRDF(Surface surface, BRDF brdf, Light light)
{
    return SpecularStrength(surface, brdf, light) * brdf.specular + brdf.diffuse;
}
```

这部分是 Cook-Torrance BRDF 的简化版（只算了 D 项，没算 F 和 G）。**具体数学不深究**，核心概念是：

```
BRDF = 镜面反射（高光） + 漫反射（基础色）
     = specular_strength × specular_color + diffuse_color
```

`specular_strength` 跟表面的"粗糙度"和"半角向量"有关——光滑表面 + 视线和光线的中间方向 = 高光集中；粗糙表面 = 高光分散。

---

## 五、从 culling 到 shader：完整数据流

把整条链路串起来看一遍：

```
┌────────────────────────────────────────────────────────────────────────┐
│ 场景中有 2 盏方向光，主光（白色，强）+ 补光（淡蓝，弱）                │
└────────────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────────────┐
│ CameraRenderer.Cull()                                                  │
│   → CullingResults.visibleLights (NativeArray<VisibleLight>)           │
│   → 其中包含这两盏方向光（也可能包含剔除掉的点光源/聚光灯）            │
└────────────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────────────┐
│ Lighting.SetupLights()                                                 │
│   遍历 visibleLights:                                                  │
│     第 1 盏方向光：index=0                                             │
│       DirectionalLightColors[0]     = finalColor (已乘 intensity)      │
│       DirectionalLightDirections[0] = -Z 轴列 (取负是入射方向)         │
│       DirectionalLightShadowData[0] = ReserveDirectionalShadows()      │
│     第 2 盏方向光：index=1                                             │
│       ... 同上 ...                                                     │
│                                                                        │
│   SetGlobalVectorArray(_DirectionalLightColors, ...)                   │
│   SetGlobalVectorArray(_DirectionalLightDirections, ...)               │
│   SetGlobalVectorArray(_DirectionalLightShadowData, ...)               │
└────────────────────────────────────────────────────────────────────────┘
                              ↓ (GPU 端)
┌────────────────────────────────────────────────────────────────────────┐
│ cbuffer _CustomLight {                                                 │
│     int _DirectionalLightCount;                                        │
│     float4 _DirectionalLightColors[4];                                 │
│     float4 _DirectionalLightDirections[4];                             │
│     float4 _DirectionalLightShadowData[4];                             │
│ }                                                                      │
└────────────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────────────┐
│ LitPassFragment()                                                      │
│   GetLighting(surface, brdf):                                          │
│     ShadowData shadowData = GetShadowData(surface);  // 跟光无关       │
│     for i in 0.._DirectionalLightCount:                                │
│       Light light = GetDirectionalLight(i, surface, shadowData);       │
│         light.color = _DirectionalLightColors[i].rgb                   │
│         light.direction = _DirectionalLightDirections[i].xyz           │
│         light.attenuation = GetDirectionalShadow...(...) // 阴影       │
│       color += IncomingLight(surface, light)                           │
│                    * DirectBRDF(surface, brdf, light)                  │
│   return float4(color, surface.alpha)                                  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 六、为什么是这个结构？工程师角度的几个设计选择

### 6.1 为什么用 `cbuffer` 而不是函数参数？

GPU 的 cbuffer 是**每帧只更新一次的全局常量**，存在显存里。所有像素着色器访问它都很便宜。如果用函数参数传，每次调用都要从 CPU 重新加载，开销巨大。

### 6.2 为什么 cbuffer 名字都用 `_CustomXxx`？

CBUFFER_START / CBUFFER_END 是 SRP Batcher 的标记。**名字必须唯一**，否则 SRP Batcher 不会把它们合并到同一个 cbuffer 槽里。约定俗成用 `_CustomXxx` 命名。

### 6.3 为什么把"颜色×强度"放在 CPU？

`visibleLight.finalColor` 是 Unity 算好的——`light.color × light.intensity`。如果在 shader 里算，每盏光都得多做一次乘法；CPU 算好一次，shader 直接读，省事。

### 6.4 为什么衰减（attenuation）单独一个字段而不是直接乘到 color 里？

因为衰减跟**阴影**挂钩。阴影会让一盏光在某些像素上变暗。如果在 CPU 把"阴影衰减"乘到 color 里，就丢失了"这盏光在这里本身是多亮"的信息——后面要做"光的强度 UI 显示"或者"按距离渐隐"就麻烦了。把衰减分离出来更灵活。

### 6.5 为什么 BRDF 在 fragment 而不是 vertex？

光照计算跟法线有关，**法线在三角形内部是逐像素插值的**（且插值后长度会缩短，必须重新 normalize）。vertex 算的光照会有明显的多边形切面感。

---

## 七、自己写一遍的最小骨架

如果你要在一个新项目里复现这套机制，最少需要这几块：

**CPU 侧 (`Lighting.cs`)**：
```csharp
public class Lighting
{
    private const int MAX_LIGHTS = 4;
    private static readonly int ColorsId = Shader.PropertyToID("_LightColors");
    private static readonly int DirectionsId = Shader.PropertyToID("_LightDirections");
    private static readonly int CountId = Shader.PropertyToID("_LightCount");

    private static Vector4[] Colors = new Vector4[MAX_LIGHTS];
    private static Vector4[] Directions = new Vector4[MAX_LIGHTS];

    private readonly CommandBuffer _buffer = new() { name = "Lighting" };
    private CullingResults _cullingResults;

    public void Setup(ScriptableRenderContext context, CullingResults culling)
    {
        _cullingResults = culling;
        var visibleLights = culling.visibleLights;
        int dirCount = 0;
        for (int i = 0; i < visibleLights.Length; i++)
        {
            if (visibleLights[i].lightType == LightType.Directional)
            {
                Colors[dirCount] = visibleLights[i].finalColor;
                // Z 列取负 = 入射方向
                Directions[dirCount] = -visibleLights[i].localToWorldMatrix.GetColumn(2);
                dirCount++;
                if (dirCount >= MAX_LIGHTS) break;
            }
        }
        _buffer.SetGlobalInt(CountId, dirCount);
        _buffer.SetGlobalVectorArray(ColorsId, Colors);
        _buffer.SetGlobalVectorArray(DirectionsId, Directions);
        context.ExecuteCommandBuffer(_buffer);
        _buffer.Clear();
    }
}
```

**Shader 侧 (`Light.hlsl` + `Lighting.hlsl`)**：
```hlsl
// Light.hlsl
#define MAX_LIGHTS 4
CBUFFER_START(_CustomLight)
    int _LightCount;
    float4 _LightColors[MAX_LIGHTS];
    float4 _LightDirections[MAX_LIGHTS];
CBUFFER_END

struct Light { float3 color; float3 direction; };

int GetDirectionalLightCount() { return _LightCount; }
Light GetDirectionalLight(int i, Surface s)
{
    Light light;
    light.color = _LightColors[i].rgb;
    light.direction = _LightDirections[i].xyz;
    return light;
}

// Lighting.hlsl
float3 IncomingLight(Surface s, Light l)
{
    return saturate(dot(s.normal, l.direction)) * l.color;
}

float3 DirectBRDF(Surface s, BRDF b, Light l)
{
    // 简化版：漫反射 + 一些镜面
    return b.specular + b.diffuse;
}

float3 GetLighting(Surface s, BRDF b)
{
    float3 color = 0;
    for (int i = 0; i < GetDirectionalLightCount(); i++)
        color += IncomingLight(s, GetDirectionalLight(i, s)) * DirectBRDF(s, b, GetDirectionalLight(i, s));
    return color;
}
```

这就是一个最小可工作的方向光系统。**学完本节你应该能解释每一行在干什么**——这是"学会"的最低标准。

---

## 八、下一步要解决的问题

上面的最小系统**没有阴影**——所有物体都被均匀照亮，没有遮挡关系。下一节【6】开始，我们就在这个骨架上加阴影功能。