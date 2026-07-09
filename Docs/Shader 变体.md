# Shader 关键字和变体

Unity 不会把一个 shader 编译成一个完整的程序。它会根据 keyword 的开/关组合，编译出多个不同版本的 shader，每个版本叫做一个 变体（variant）。

## 变体生成的来源


| 方式                             | 举例                                               | 本质                                                |
|--------------------------------| -------------------------------------------------- | --------------------------------------------------- |
| `#pragma shader_feature`       | `shader_feature _CLIPPING`                         | 基于 keyword 的条件编译，按需编译                   |
| `#pragma multi_compile`        | `multi_compile_instancing`                         | 基于 keyword 的条件编译，全量编译                   |
| `#pragma multi_compile_local`  | `multi_compile_local _A _B`                        | 同`multi_compile`，但 keyword 仅限当前 shader 使用  |
| `#pragma shader_feature_local` | `shader_feature_local _X`                          | 同`shader_feature`，但 keyword 仅限当前 shader 使用 |
| Pass 光照模式                      | `"LightMode" = "ShadowCaster"`                     | 每个 Pass 自身就是一个独立变体集合                  |
| 硬件层级                           | `#pragma hardware_tier_variants`                   | 针对不同 GPU 等级自动生成变体                       |
| 内置 `multi_compile` 快捷方式        | `multi_compile_instancing`、`multi_compile_fog` 等 | 预定义的一组 keyword 集合                           |

上面带 `_local` 后缀的版本，就是让 keyword 的作用域仅存在于当前 shader 内部。由于全局 keyword 的上限实际上只有 256 个 (Unity 内置的就占用了很多)，因此对于仅本 Shader 使用的功能，应当优先使用 _local 版本。

## 变体编译策略: 按需 vs 全量

- `shader_feature` 根据 keyword 启用/禁用，**只会编译材质实际用到的那个变体**。
- `multi_compoile` 则是无条件编译所有组合，无论材质有没有用到。

**取舍**：全量编译可以支持运行时切换变体，比如 GPU Instancing 在运行时根据 DrawCall 自定决定是否走 instancing 路径，必须保证变体存在。但过多的变体数量也会导致着色器编译时间加上，打包体积膨胀 (这就是为什么大型项目中变体爆炸是个严重问题：一个 URP/Lit shader 可能产生 几千个变体)。

## Toggle 和 shader_feature 的联系

比如开关 Alpha 裁剪，一般会在 Properties 块里面写 `[Toggle(_CLIPPING)] _Clipping("Alpha Clipping", Float) = 0`，然后在 HLSL 块中写 `#pragma shader_feature _CLIPPING`。这两者必须配合使用，缺一不可。那么它们的职责分别是什么呢？

- `[Toggle(_CLIPPING)]` 是 Properties 块里的属性修饰符，它做两件事：件事：在材质面板上画一个复选框，并且在勾选/取消时给材质设置/取消 `_CLIPPING` 这个 keyword。
- `#pragma shader_feature _CLIPPING` 是 Pass 里的编译指令，它告诉编译器：编译出两个变体，一个定义了 _CLIPPING，一个没有。

一个是"运行时在材质上开关 keyword"，一个是"编译时为这个 keyword  生成对应的变体代码"。一个管"开关状态"，一个管"开关控制的代码是否存在"。

### 各自具体做了什么

- `[Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0`
  - `_Clipping` 是一个普通的 Float 属性，值 0 或 1。
  - `[Toggle(...)]` 是 Unity 的材质属性开关。带参数 `_CLIPPING` 时，它的特殊行为是：当材质面板勾选时，Unity 会调用 `material.EnableKeyword("_CLIPPING")`；取消时调用 `DisableKeyword`。 同时 `_Clipping` 的 float 值也跟着存 1/0。
  - 也就是说，Toggle 的"附加能力"是**把一个面板复选框绑定到一个 shader keyword 的开关动作上**。没有这个修饰符，你勾选属性只会改 float 值，不会动 keyword。

- `#pragma shader_feature _CLIPPING`
  - 这是 HLSL 的 pragma，声明"这个 keyword 存在两种状态，请分别编译"。
  - 它产生的效果是编译出两个变体：
    - 变体 A：`_CLIPPING` 未定义 → `#if defined(_CLIPPING)` 分支里的 `clip(...)` 不编译进去。
    - 变体 B：`_CLIPPING` 已定义 → `clip(...)` 被编译进去。
  - 运行时，Unity 根据材质当前的 keyword 状态，挑选对应的变体去执行。

