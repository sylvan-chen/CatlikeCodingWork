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
