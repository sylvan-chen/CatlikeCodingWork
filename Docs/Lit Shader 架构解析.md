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

