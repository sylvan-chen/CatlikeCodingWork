# Shader 变体

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