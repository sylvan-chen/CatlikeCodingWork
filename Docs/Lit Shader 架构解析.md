# Lit Shader 架构解析

## 通用光照架构

```plaintext
    CPU (Lighting.cs)                   GPU (Shaders)
  ┌──────────────────┐       ┌───────────────────────────────────┐
  │ CullingResults   │       │   Light.hlsl                      │
  │                  │       │                                   │
  │ → VisibleLight[] │──────→│   CBUFFER _CustomLight            │
  │                  │       │                                   │
  │   finalColor     │       │   _DirectionalLightColors[4]      │
  │                  │       │                                   │
  │   direction      │       │   _DirectionalLightDirections[4]  │
  │                  │       │                                   │
  └──────────────────┘       └─────────────────┬─────────────────┘
                                               │
  ┌────────────────────────────────────────────▼───────────────────────────┐
  │ LitPass.hlsl (片段着色器)                                               │
  │  1. 构建 Surface (normal, viewDir, color, metallic, smoothness, alpha) │
  │  2. 构建 BRDF (diffuse, specular, roughness)                           │
  │  3. GetLighting() 遍历所有方向光                                        │
  └─────────────────────────────────────┬──────────────────────────────────┘
                                        │
                    ┌───────────────────▼─────────────────────┐
                    │ Lighting.hlsl                           │
                    │  对每个光源:                             │
                    │                                         │
                    │   IncomingLight = saturate(N·L) * color │
                    │   result = IncomingLight * DirectBRDF   │
                    └───────────────────┬─────────────────────┘
                                        │
                ┌───────────────────────▼──────────────────────┐
                │ Common.hlsl                                  │
                │  DirectBRDF = SpecularStrength * specular    │
                │            + diffuse                         │
                │                                              │
                │  SpecularStrength (Cook-Torrance D 项):      │
                │   h = normalize(L + V)  // 半角向量           │
                │   nh² = (N·h)²                               │
                │   lh² = (L·h)²                               │
                │   r²  = roughness²                           │
                │   d²  = (nh²*(r²-1)+1.00001)²                │
                │   norm = roughness*4+2                       │
                │   D = r² / (d² * max(0.1, lh²) * norm)       │
                └──────────────────────────────────────────────┘

```

## 光照计算（光照渲染方程）

**射出去的光 = 到达的光 × 反射比例**。

> 房间里有 100 瓦的灯照进来（入射光），墙壁反射率是 0.3（BRDF），那你看到墙壁反射的光就是 100 × 0.3 = 30 瓦。

渲染方程描述的是：一个表面点向观察者反射了多少光？

$$
L_{out} = \int_\Omega L_{in}(\omega_i) \cdot f_{BRDF}(\omega_i, \omega_o) \cdot (\hat{N}
  \cdot \hat{\omega_i}) , d\omega_i
$$
拆开看每一项：

|              符号              |                  含义                  |     对应代码中的      |
| :----------------------------: | :------------------------------------: | :-------------------: |
|            $L_{in}$            |           从光源来的入射辐射           |     `light.color`     |
| $\hat{N} \cdot \hat{\omega_i}$ |     几何因子: 表面朝向接收了多少光     | `saturate(dot(N, L))` |
|           $f_{BRDF}$           | BRDF: 入射光有多少比例被反射到观察方向 |    `DirectBRDF()`     |

## 阴影投射

**阴影的物理本质**：一个点是否在阴影里，取决于：**从光源看过去，这个点前面有没有别的东西挡着它**。如果有更近的物体挡住了光路，这个点就接收不到光，就是阴影。

那么，**怎么用 GPU 判断“前面有没有东西挡着”呢？**

直接对每个点做光线相交测试太贵，阴影投射的核心技巧是把问题转化为深度比较：

- **第一步（生产者 / Caster Pass）**：把相机搬到光源的位置，朝光照方向看，渲染一遍场景。但这次我们**不要颜色，只要深度**——把“从光源看，每个位置最近的几何体有多远”存进一张图，这张图叫 **shadow map (阴影贴图)**。
- **第二步（消费者 / Receiver）**：正常从相机渲染场景。对每个像素，我们已经知道它的世界坐标。把这个世界坐标**用光源的 VP 矩阵变换到光源的裁剪空间**，得到“它离光源有多远”。然后去 shadow map 上对应的像素采样，取出“从光源看，这个方向上最近的物体有多远”。
  - 如果 采样到的最近深度 < 该像素自己到光源的深度 → 说明前面有更近的物体挡着 → 在阴影里，光照衰减为 0。
  - 否则 → 没被挡 → 正常受光。

这就是全部核心。一句话：**阴影 = “从光源看的最近深度”和“该点离光源的实际深度”做比较**。

### 不同光源阴影的投影矩阵

- **方向光阴影**：使用正交投影矩阵。方向光没有位置，光线是平行的（就像太阳）。所以从方向光“看”场景，等价于一个正交相机（没有近大远小透视）。
- **聚光灯阴影**：聚光灯用透视投影。
- **点光源阴影**：点光源用六面立方体贴图（cubemap）。

### 阴影管线全览

**CPU 端（`CameraRenderer.Render` 之前的所有准备）**

```plaintext
Frame Start
│
├─ CustomRenderPipeline.Render(context, cameras)              ← 入口
│       ↓ 每个 camera
│  CameraRenderer.Render(context, camera, ..., shadowSettings)
│       │
│       ├─ Cull(shadowSettings.MaxDistance)                    ← 关键：
│       │   ScriptableCullingParameters.shadowDistance = 100   把「阴影距离」告诉 Unity
│       │   context.Cull(...) 返回 CullingResults              这样 CullingResults 里的
│       │                                                      "shadow caster bounds" 会被
│       │                                                      限制在 100m 以内
│       │
│       └─ Lighting.Setup(context, cullingResults, shadowSettings)
│              │
│              ├─ Shadows.Setup(...)                           ← 只是清零 _shadowedDirectionalLightCount
│              │
│              ├─ SetupLights()                                ← 遍历 visibleLights
│              │      │
│              │      └─ 对每盏方向光：SetupDirectionalLight(index, ref light)
│              │              │
│              │              ├─ 拿颜色、方向（矩阵第 3 列取反）
│              │              │
│              │              └─ _shadows.ReserveDirectionalShadows(light, index)
│              │                     │
│              │                     ├─ if 满足登记条件 (强度>0，包围盒有效，未超上限)
│              │                     │      _shadowedDirectionalLights[N] = { visibleLightIndex }
│              │                     │      N++
│              │                     │      return (strength, tileIndex=N-1)   ← 这里的 tileIndex
│              │                     │                                          就是 Shadow Atlas 里
│              │                     │                                          分到的格子编号
│              │                     └─ 否则 return (0, 0)  → 强度为 0 = shader 端不采样
│              │
│              │      → 把 (strength, tileIndex) 写进 DirectionalLightShadowData[index]
│              │
│              │      → SetGlobalVectorArray 把 3 个数组（colors/directions/shadowData）
│              │        全塞进 _CustomLight cbuffer
│              │
│              └─ Shadows.Render()  ★这里才真正画 Shadow Map
│                     │
│                     ├─ if 有登记灯 → RenderDirectionalShadows()
│                     │      │
│                     │      ├─ GetTemporaryRT(_DirectionalShadowAtlas, 1024, 1024, 32, Shadowmap)
│                     │      │      申请一张深度纹理，格式 = Shadowmap（硬件深度对比专用）
│                     │      │
│                     │      ├─ SetRenderTarget → ClearRenderTarget(only depth)
│                     │      │
│                     │      ├─ split = (lightCount<=1) ? 1 : 2   → 单光整张图，多光 2×2 切
│                     │      │      tileSize = 1024 / split
│                     │      │
│                     │      └─ for i in _shadowedDirectionalLightCount:
│                     │             RenderDirectionalShadows(i, split, tileSize)
│                     │                    │
│                     │                    ├─ _cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
│                     │                    │        light.VisibleLightIndex,
│                     │                    │        cascadeIndex=0, cascadeCount=1,   ← 你现在的单级 cascade
│                     │                    │        splitRatio=Vector3.zero,
│                     │                    │        tileSize,
│                     │                    │        nearPlaneOffset=0,
│                     │                    │        out viewMatrix,           ← 光源视角的观察矩阵
│                     │                    │        out projectionMatrix,     ← 一个正交投影盒子
│                     │                    │        out splitData             ← 让 Unity 只画该盒子里的物体
│                     │                    │    )
│                     │                    │
│                     │                    │   → Unity 内部做的事：
│                     │                    │       1. 找到相机视锥体在阴影距离内的部分
│                     │                    │       2. 把这块空间投影到光源方向
│                     │                    │       3. 求一个正交包围盒（AABB in light-space）
│                     │                    │       4. 用这个盒子构造 ortho projection
│                     │                    │
│                     │                    ├─ SetTileViewport(i, split, tileSize)  → 限制画在图集的第 i 块
│                     │                    │
│                     │                    ├─ ConvertToAtlasMatrix(P*V, offset, split)
│                     │                    │       把「world → light clip [-1,1]」的矩阵
│                     │                    │       转成「world → 该 tile 的 UV [0..1/split] + offset」
│                     │                    │       同时处理 D3D 反向深度（m2x 取反）
│                     │                    │       结果存进 DirectionalShadowMatrices[i]
│                     │                    │
│                     │                    ├─ _buffer.SetViewProjectionMatrices(view, projection)
│                     │                    │
│                     │                    └─ _context.DrawShadows(ref shadowSettings)
│                     │                           ← Unity 用这个 VP 去渲染 CullingResults 里所有 caster
│                     │                             的 ShadowCaster Pass。ShadowCasterPass.hlsl 什么颜色
│                     │                             都不写（ColorMask 0），只写深度到 tile 里
│                     │
│                     ├─ SetGlobalMatrixArray(_DirectionalShadowMatrices, ...)   把矩阵数组给 shader
│                     │
│                     └─ if 无登记灯：申请一张 1×1 虚假 RT，避免 shader 端 sample 时报错
│
└─ 到这里为止：GPU 内存里有一张 1024×1024 的深度图 _DirectionalShadowAtlas
              cbuffer _CustomShadows 里有 4 个 tile 变换矩阵
              cbuffer _CustomLight 里有每盏灯的 (strength, tileIndex)
```

**GPU 端（正常渲染 Lit Pass 时，每个 fragment 消费上面的数据）**

```plaintext
LitPassFragment
    ├─ 组装 Surface { positionWS, normalWS, ... }
    ├─ 主循环: for i in _DirectionalLightCount
    │      │
    │      └─ GetDirectionalLight(i, surfaceWS)
    │             │
    │             ├─ color / direction 从 cbuffer 直接读
    │             │
    │             └─ GetDirectionalShadowAttenuation(shadowData, surfaceWS)
    │                    │
    │                    ├─ if strength <= 0 → return 1（这盏灯不投阴影，直接不遮蔽）
    │                    │
    │                    ├─ positionSTS = mul(_DirectionalShadowMatrices[tileIndex], float4(posWS,1)).xyz
    │                    │      现在 positionSTS.xy 是 tile 内的 UV，positionSTS.z 是「从光源看这个点的深度」
    │                    │
    │                    ├─ shadow = SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, sampler_linear_clamp_compare, positionSTS)
    │                    │      硬件做的事：
    │                    │        1. 用 xy 找到对应 texel（这里 sampler 是 linear，所以其实是 2×2 texel 加权）
    │                    │        2. 从每个 texel 读出「光源看过去最近的深度」
    │                    │        3. 每个都和 positionSTS.z 做比较：near_depth < z ? 0 : 1
    │                    │        4. 4 个 0/1 双线性混合 → 得到 0~1 之间的 attenuation
    │                    │
    │                    └─ return lerp(1, shadow, strength)   → 按 shadowStrength 混合
    │
    ├─ 得到 Light.attenuation ∈ [0,1]
    │
    └─ IncomingLight = saturate(dot(N, L) * attenuation) * lightColor
          → attenuation = 0 完全在阴影里
          → attenuation = 1 完全被光照到
```

### 画 ShadowMap 时 3 个关键的坐标空间转换

| 空间                        | 是什么                                                       | 谁负责变换                       |
| --------------------------- | ------------------------------------------------------------ | -------------------------------- |
| World -> Light-Clip         | 从世界坐标到「光源视角的裁剪空间」                           | Unity 帮你算的 projection * view |
| Light-Clip -> Tile-UV       | 从 [-1, 1] 缩放到当前 tile 在阴影图集里占的 UI 范围 [offset, offset + 1/ split] | ConvertToAtlasMatrix             |
| Tile-UV + light-z -> sample | 用 xy 找 texel，用 z 做深度比较                              | SAMPLE_TEXTURE2D_SHADOW          |

上面的前两步最终算成一个 4x4 的变换矩阵，也就是代码中的 `_DirectionShadowMatrices[i]`。

### 级联阴影贴图 (Cascaded Shadow Maps)

同一个 tile 里，1 个 texel 对应的世界空间大小是固定的，但你眼睛对「近处细节」和「远处细节」的需求是完全不一样的。

方向光会影响最大阴影范围内的所有物体，因此该光的阴影贴图最终覆盖的范围是比较大的。比如最大范围是 100m，那么一个正交盒子要装下相机 100m 内的整个视锥，那么对于一个 1024 尺寸的阴影贴图来说，1 textel 要对应 100m / 1024 ~= 10cm 的空间。

- 假如一个人站在你面前 2m，他脚下的阴影可能有 30cm，在屏幕上要占用几十到上百个像素，而 shadow map 里总共就 3 个 texel 描述，最终阴影效果就是 3 个方块拼出来的锯齿；
- 假如一栋房子在你 80m 外，屏幕上可能只有 5 个像素，但 shadow map 里用了 100 个 texel 来描述，纯浪费。

这就是引入级联阴影贴图的原因：「同一盏光渲染多次，每次覆盖不同的距离段，占用多个 tile」。也就是说，把每个 tile 的尺寸缩小，同时减小正交盒覆盖范围。
