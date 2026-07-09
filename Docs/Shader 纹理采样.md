# Shader 纹理采样

### 问题

1. 写 Shader Pass 的 hlsl 代码时，为什么纹理的定义要写成 `TEXTURE2D(_BaseMap)` + `SAMPLER(sampler_BaseMap)`？
2. `sampler_XXX` 到底是随便命名还是有讲究？

### 先讲清楚一件基本事实：texture 和 sampler 是两个东西

在现代 GPU（D3D11+、Vulkan、Metal、GL ES 3.0+）里：

```plaintext
┌──────────────────┐          ┌────────────────────────────┐
│    Texture       │          │      Sampler               │
│  (纹理对象)      │          │   (采样器对象)             │
├──────────────────┤          ├────────────────────────────┤
│  实际的像素数据  │          │  怎么读这份数据的策略      │
│  格式 (RGBA8)    │          │  Filter: Point/Bilinear/   │
│  分辨率 (1024²)  │          │          Trilinear/Aniso   │
│  Mip 层          │          │  Wrap:   Clamp/Repeat/     │
│                  │          │          Mirror            │
│                  │          │  Compare: (阴影用) less/gt │
└──────────────────┘          └────────────────────────────┘
        ↓                                ↓
        └────────────┬───────────────────┘
                     ↓
                Sample(uv)
                → 返回一个 float4
```

为什么要分开？ 因为一个纹理可以被多种方式读取。比如同一张贴图，你可能想在这里做双线性采样，另一处做点采样。分开之后，同一张 `Texture2D` 可以配任意 `SamplerState`。

而老平台（GL ES 2.0 / WebGL 1.0）不支持这种分离，只能用「打包好」的 `sampler2D` —— 它同时代表纹理和采样方式。

Unity 要跨平台，于是用一堆宏把「声明纹理」和「声明采样器」在源码里写成两句，具体在每个平台上展开成什么，交给平台头文件决定。

### 这几个宏到底在做什么

这些宏定义在 `Packages/com.unity.render-pipelines.core/ShaderLibrary/API/D3D11.hlsl`（以及 `Metal.hlsl`、`Vulkan.hlsl`、`GLES2.hlsl` 各自的版本）里。在现代平台（D3D11 为例），展开如下：

```hlsl
// 你写的
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv)

// 预处理器展开后（D3D11）
Texture2D _BaseMap;
SamplerState sampler_BaseMap;
_BaseMap.Sample(sampler_BaseMap, uv)
```

在 GLES2 上：

```hlsl
// 同样的源码，展开后：
sampler2D _BaseMap;    // 纹理和采样器合一
// SAMPLER 宏被定义成空，什么都不做
tex2D(_BaseMap, uv)
```

阴影用的宏也是类似的思路，只是走「深度对比」的硬件路径：

```hlsl
// 你写的
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
SAMPLER_CMP(sampler_linear_clamp_compare);
SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, sampler_linear_clamp_compare, positionSTS);

// D3D11 上展开成：
Texture2D _DirectionalShadowAtlas;              // 内部是 R32_FLOAT / D32
SamplerComparisonState sampler_linear_clamp_compare;   // 注意：不是普通 SamplerState
_DirectionalShadowAtlas.SampleCmpLevelZero(
    sampler_linear_clamp_compare,
    positionSTS.xy,           // 用 xy 找 texel
    positionSTS.z             // 用 z 做深度比较（reference value）
)
```

`SamplerComparisonState` 是 GPU 的一种特殊采样器，它在采样的同时做深度比较，返回 0 或 1（如果开双线性过滤，就是 0~1 之间的加权值——这就是硬件 2×2 PCF 的来源）。这不是软件比较，是硬件层面一条指令干完的事。所以速度快，且比手动 tex2D + if 更省。

### `sampler_XXX` 是约定名字——而且有两种约定

`sampler_` 这个前缀不是随便起的，Unity 的 shader 编译器会扫描名字，然后决定这个 sampler 应该配什么参数。有两种命名约定，你的代码里恰好两种都用到了。

#### 约定 A：`sampler_<纹理名>` —— 复用纹理的导入设置

规则：如果 sampler 的名字是 `sampler_` + 一个已经声明过的纹理名，那 Unity 会把这个 sampler 的 filter mode / wrap mode 从对应纹理的导入设置（Inspector 面板里那些选项）中读出来。

对照你 `LitPass.hlsl` 里的：

```hlsl
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);   // ←── 名字必须是 sampler_ + 纹理名
```

Unity 看到这个名字 → 「你想用 `_BaseMap` 这张贴图 Inspector 里配的采样方式」→ 编译时把这个 sampler 绑定到该纹理的采样状态。

如果你在 Unity 里把这张贴图设成：

- Filter Mode: Bilinear
- Wrap Mode: Repeat
- Aniso Level: 4

那 `sampler_BaseMap` 就自动带上这些属性。你不需要在 shader 里再写一遍。这是 URP 里绝大多数材质纹理的用法。

#### 约定 B：`sampler_<filter>_<wrap>[_compare]` —— 内联采样器状态 (Inline Sampler State)

规则：如果 sampler 的名字符合特定 pattern，Unity 会直接根据名字生成一个采样器，不需要任何贴图匹配。

命名格式：

`sampler_<filter>_<wrap>[_compare]`

- `<filter>` 可选值：point / linear / trilinear
- `<wrap>`   可选值：clamp / repeat / mirror / mirror_once
- 可选后缀 `_compare`：这是深度对比采样器（SamplerComparisonState），专用于 shadow map

一堆合法名字：

- sampler_point_clamp
- sampler_linear_clamp
- sampler_linear_repeat
- sampler_trilinear_repeat
- sampler_point_mirror
- sampler_linear_clamp_compare      ← 你用的
- sampler_linear_repeat_aniso4      ← 甚至可以指定各向异性

对照你 `Shadows.hlsl` 里的：

```hlsl
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare   // ←── 内联命名
SAMPLER_CMP(SHADOW_SAMPLER);
```

Unity 看到这个名字，直接生成一个：

- Filter:   Linear (bilinear)
- Wrap:     Clamp
- Compare:  是（生成 SamplerComparisonState 而不是 SamplerState）

为什么阴影贴图必须走这条路？ 因为 `_DirectionalShadowAtlas` 是运行时 `GetTemporaryRT` 出来的 RenderTexture，没有 Inspector 可以让你配 Filter / Wrap。所以约定 A 走不通。你必须用约定 B，在 shader 里直接把采样状态写死。