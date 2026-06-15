# DrawCall 优化解析

## DrawCall 数据

在早期的图形 API（如古老的 OpenGL 2.0 或 DX9）中，如果 CPU 要传数据给 GPU 的 Shader，只能一个变量一个变量地传。比如传一个颜色、传一个浮点数，每次传都要调用一次底层 API（例如 `glUniform`）。这就像用小轿车送快递，跑一次只能送一件，CPU 极度劳累，带宽利用率极低。

为了解决这个问题，现代图形 API 引入了常量缓冲区的概念：

- 在 DirectX 中叫 Constant Buffer (`cbuffer`)
- 在 Vulkan / OpenGL 中叫 Uniform Buffer Object (UBO)

它的本质是：在系统内存中划分一块连续的内存结构（类似 C# 的 Struct），把所有的变量打包塞进去，然后一次性“整车”推送到 GPU 显存中。Shader 执行时，直接从这块显存里高速读取数据。

## SPR Batcher

**SRP Batcher 原理**：不是减少 DrawCall，而是简化 DrawCall。

SRP Batcher 的核心机制在于它对 cbuffer 进行了“严苛的标准化分类”和“持久化存储”。

SRP Batcher 强制要求 Shader 把变量拆分到两个特定的 cbuffer 块中：

1. **`cbuffer UnityPerMaterial` (材质缓冲区)**：只放材质参数（颜色、粗糙度等）。
2. **`cbuffer UnityPerObject` (逐物体缓冲区)**：只放物体参数（世界坐标矩阵等）。

```plaintext
┌────────────────────────────────────────────────────────────┐
│  传统方式 (SetPass Call 开销较大)                             │
│                                                            │
│  每个 DrawCall: [材质属性 + 变换矩阵 + 纹理 + ...] 全部上传     │
│  → 即使材质相同，只是物体不同，也要重新上传一切                   │
└────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  SRP Batcher 方式                                           │
│                                                             │
│  UnityPerMaterial (材质缓冲区):                              │
│  ┌──────────────────────────────────────┐                   │
│  │ _BaseColor, _MainTex, ...            │ ← 材质不变就缓存    │
│  │ 只在切换材质时才更新                    │   在 GPU 上不动     │
│  └──────────────────────────────────────┘                   │
│                                                             │
│  UnityPerDraw (逐物体缓冲区):                                 │
│  ┌──────────────────────────────────────┐                   │
│  │ unity_ObjectToWorld, unity_LODFade...│ ← 每个物体都更新    │
│  │ 每个 DrawCall 都快速更新               │   但是数据量很小     │
│  └──────────────────────────────────────┘                   │
│                                                             │
│  → 同一材质的多个物体可以快速连续绘制，CPU 开销极低                 │
└─────────────────────────────────────────────────────────────┘
```

**流程**：

1. Unity 按材质排序所有 DrawCall（同材质的放一起）
2. 第一个物体：上传 UnityPerMaterial（材质属性） + 上传 UnityPerDraw（变换矩阵） → 绘制
3. 第二个物体（同材质）：UnityPerMaterial 已在 GPU 上不动，只更新 UnityPerDraw → 绘制
4. 第三个物体（同材质）：同上，只更新 UnityPerDraw → 绘制
5. 切换材质时：才重新上传新的 UnityPerMaterial

**性能来源**：渲染时 CPU 只需要向 GPU 发送极短的“切换显存指针”的指令 (切换 UnityPerMaterial) 以及少量逐物体数据 (UnityPerDraw)，UnityPerDraw 只有几十个字节（几个矩阵），而传统方式每次要上传几百字节到几 KB 的材质数据。SRP Batcher 把"大而慢"的材质上传变成了"小而快"的逐物体更新。

### 如何支持与使用

#### Shader 层支持

SRP Batcher 的核心理念是“让引擎自动完成”，所以只要 Shader 编写声明了支持即可。

核心规则只有一条：**所有在 Properties 块中暴露的材质属性，必须严格包裹在 UnityPerMaterial 的 cbuffer 中。**

```hlsl
// 在你的 Shader 的 HLSLINCLUDE 或 Pass 中：

// 1. 必须使用准确的宏 CBUFFER_START 和 CBUFFER_END
// 2. 命名必须严格为 UnityPerMaterial
CBUFFER_START(UnityPerMaterial)
    // 所有的材质变量（对应 Properties 里的定义）放在这里
    float4 _BaseColor;
    float _Metallic;
    float _Smoothness;
    float4 _BaseMap_ST; // 纹理的 Tiling 和 Offset 也要放进来
CBUFFER_END

// 纹理对象本身不是常量数据，不要放进 CBuffer！放在外面：
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
```

#### 逻辑层如何使用

- **开启全局开关**：确保在你的 `CustomRenderPipelineAsset` 或 URP Asset 中，勾选了 `Use SRP Batcher`（如果是在代码里写，确保 `GraphicsSettings.useScriptableRenderPipelineBatching = true;`）。
- **场景搭建**：直接把材质球赋给普通的 MeshRenderer 即可。

### 总结

**优点：**

- **兼容性极强**：允许完全不同的 Mesh（网格）。只要使用的是同一个 Shader（且未被修改变体），哪怕有一百个不同的材质实例，也能被高效合批处理。
- **开发最省心**：不需要复杂的代码逻辑，在编辑器中预设好各个不同的 Material 丢给物体即可，Unity 底层会自动调度。

**缺点：**

- **显存与带宽开销**：每一个 Material 实例都会在显存中独占一块 UnityPerMaterial CBuffer。实例数量无节制增多时，不仅占用内存，加载场景时的数据上传带宽也会增加。
- **绝对排斥 PropertyBlock**：一旦在物体上挂载 MaterialPropertyBlock 注入个体数据，或者修改了 Shader Keywords（产生了不同的 Shader 变体），就会立刻破坏该物体的合批资格。

**适用场景：**

- **模块化与差异化明显的系统**：例如开发一套支持拼装的模块化机甲系统，机甲的头部、手臂、躯干都是不同的 Mesh，且可能有不同的磨损度、不同的涂装颜色。此时直接赋予它们不同的材质球实例，走 SRP Batcher 是最高效的。
- **场景建筑与环境杂物**：场景中各种形状各异、但共用标准 Shader（如 Lit Shader）的静态物体。

## GPU Instancing

GPU Instancing 就是让多个相同网格和材质的物体，只发一次 DrawCall。其**核心思想**是：既然 Mesh 和材质都一样，CPU 为什么不把所有不同的数据（比如 1000 个矩阵）打包成一个数组，一次性发给 GPU，然后只发一次 Draw 指令？

底层的图形 API 都支持一种特殊的绘制指令，例如：

- **DirectX**: `DrawIndexedInstanced`
- **Vulkan**: `vkCmdDrawIndexed` (带有 `instanceCount` 参数)
- **OpenGL ES**: `glDrawElementsInstanced`

当调用这个指令时，它的工作原理分为两部分：数据准备和着色器执行。

- **数据准备**：GPU Instacing 把 cbuffer 块当成一个数组，该数组装有一批物体各自独有的数据。CPU 只需要一次 DrawCall，把这些数据一次性传给 GPU 显存即可。
- **着色器执行**：当 GPU 收到 `DrawInstanced(1000)` 指令时，它的硬件调度器会启动 `顶点数 × 1000` 个 Vertex Shader 线程。 为了让每一个顶点知道自己属于哪个“实例”（比如：我是第 5 个方块的顶点，还是第 500 个方块的顶点？），硬件会为每一个 Shader 线程注入一个极其重要的系统变量：`InstanceID` (在 HLSL 中叫 `SV_InstanceID`，在 GLSL 中叫 `gl_InstanceID`)

### 如何支持与使用

#### Shader 层支持

你需要让 Vertex Shader 知道如何拆解大数组里的数据。

```hlsl
// 1. 开启 Instancing 变体编译
#pragma multi_compile_instancing

// 2. 定义每个实例特有的属性 (等同于你 C# 里用 PropertyBlock 传的数据)
// 注意命名规范
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

// 3. 在顶点输入结构体中声明 ID
struct Attributes {
    float3 positionOS : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID // 插入 ID
};

// 4. 如果片元着色器也需要独立的属性，在输出结构体中也要声明
struct Varyings {
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID // 如果传给 Fragment 就加这个
};

Varyings vert(Attributes input) {
    Varyings output;
    // 5. 必须在开头调用，提取当前实例的 ID 并设置全局状态
    UNITY_SETUP_INSTANCE_ID(input); 
    // 6. 将 ID 传递给片元着色器 (如果需要的话)
    UNITY_TRANSFER_INSTANCE_ID(input, output); 

    // 获取常规的世界矩阵 (引擎底层会根据 ID 自动取对矩阵)
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
  
    return output;
}

float4 frag(Varyings input) : SV_TARGET {
    UNITY_SETUP_INSTANCE_ID(input);
  
    // 7. 使用专用的宏来读取属于当前物体的专属颜色
    float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    return color;
}
```

#### 逻辑层如何使用

**方法 A: 各自做成 GameObject，通过脚本设置 MPB**

- 将同一个材质赋给场景中多个相同 Mesh 的 GameObject。
- 在材质面板上勾选 Enable GPU Instancing。
- 给每个 GameObject 挂载脚本，使用 MaterialPropertyBlock 设置独有的参数。
- **缺点**：GameObject 层级的开销依然存在，且受限于 cbuffer 分块（移动端经常被切成几十个 DrawCall）。

**方法 B: 传统底层 API (适合几百到一千个物体)**

通过单独一个管理器脚本，使用 `Graphics.DrawMeshInstanced` 直接绘制大量物体。

```csharp
// 限制：每次最多只能画 1023 个
public Mesh mesh;
public Material mat;
private Matrix4x4[] matrices = new Matrix4x4[1000];
private Vector4[] colors = new Vector4[1000];
private MaterialPropertyBlock block;

void Update() {
    block.SetVectorArray("_BaseColor", colors);
    // 直接推给 GPU，不需要 GameObject
    Graphics.DrawMeshInstanced(mesh, 0, mat, matrices, 1000, block); 
}
```

### 现代高性能 API (适合数以万计的物体，推荐 Unity 6 使用)

如果你在做极其硬核的优化，请放弃上述方法 A 和方法 B，直接使用 Unity 较新的 `Graphics.RenderMeshInstanced` 或基于 `GraphicsBuffer` 的 API（如 `DrawMeshInstancedIndirect`）。

- 这些方法支持直接传递 `NativeArray` 或显存 Buffer，全程 0 GC。
- 完美契合 Burst 编译器和 Unity 6 的新特性，突破了单次 1023 个的限制，是处理海量同构物体的终极方案。

#### Shader 层支持

在 Shader 端，不再依赖传统的 `UNITY_INSTANCING_BUFFER_START` 宏，而是直接去读显存里的泛型结构体数据（StructuredBuffer）。

#### 逻辑层如何使用

该方法主要分为两个层次的 API：基于 C# 原生内存的 `RenderMeshInstanced`，以及完全交由 GPU 接管的 `RenderMeshIndirect`。

**第一层: 完全基于非托管内存的 `Graphics.RenderMeshInstanced`**

如果你希望逻辑依然保留在 C# 端，但要突破原来 `DrawMeshInstanced` 每次最多 1023 个的限制，并消除托管数组带来的 GC 压力，这是首选方案。

**核心原理**：它摒弃了传统的 `Matrix4x4[]` 托管数组，强制要求你传入 `NativeArray<T>`。这意味着你的变换矩阵和自定义数据是直接分配在 C++ 底层的连续内存块中的。C# 侧的数据读写和 GPU 侧的抓取都变得极其高效。

```csharp
using UnityEngine;
using Unity.Collections;

public class HighPerfRenderer : MonoBehaviour
{
    public Mesh targetMesh;
    public Material targetMaterial;
  
    // 使用非托管的 NativeArray，完全避免 GC
    private NativeArray<Matrix4x4> _matrices;
    private RenderParams _renderParams;

    private void Start()
    {
        int instanceCount = 50000; // 轻松突破 1023 限制
      
        // 申请持久化的原生内存块
        _matrices = new NativeArray<Matrix4x4>(instanceCount, Allocator.Persistent);
      
        // 初始化矩阵数据 (实际项目中通常用 Burst/Job System 填充)
        for (int i = 0; i < instanceCount; i++)
        {
            _matrices[i] = Matrix4x4.Translate(new Vector3(i * 1.5f, 0, 0));
        }

        // RenderParams 是现代渲染传参的核心结构体
        _renderParams = new RenderParams(targetMaterial);
      
        // 如果你需要像之前那样传额外的 Color 等参数：
        // MaterialPropertyBlock block = new MaterialPropertyBlock();
        // block.SetVectorArray("_BaseColor", ...); // 注意这里如果传海量数据也要优化
        // _renderParams.matProps = block;
    }

    private void Update()
    {
        // 极其轻量的 API 调用，直接把非托管内存指针推给底层管线
        Graphics.RenderMeshInstanced(_renderParams, targetMesh, 0, _matrices);
    }

    private void OnDestroy()
    {
        // 必须手动释放非托管内存
        if (_matrices.IsCreated) _matrices.Dispose();
    }
}
```

**第二层: GPU 驱动渲染 `Graphics.RenderMeshIndirect`**

如果你的物体数量达到十万、百万级别，或者物体的运动逻辑（如物理碰撞、群体寻路）非常消耗 CPU，那么仅仅优化渲染指令是不够的。你需要将**数据流彻底留在显存中**。

**核心原理**：使用 `GraphicsBuffer`（或 `ComputeBuffer`）在显存中开辟一块空间。用 **Compute Shader** 来更新这些物体的坐标、颜色和存活状态。更新完毕后，CPU 甚至都不知道这些物体在哪里，只需要调用一次 `RenderMeshIndirect`。

在这个模式下，管线的工作流变成了：

1. **CPU 开局**： 创建一个 `GraphicsBuffer`，描述你要画多少个物体，分配显存。
2. **Compute Shader 运算**： 每帧在 GPU 端并行计算这几万个物体的新位置，将结果写回显存中的 Buffer。
3. **CPU 下令**： 发出一条 `RenderMeshIndirect` 指令（参数包含绘制数量的 Buffer 引用）。
4. **Vertex Shader 渲染**： 直接从显存的那个 Buffer 中根据 `InstanceID` 抓取矩阵并绘制。

**为什么它最强？**

- **零 CPU 带宽消耗**： 每帧没有任何数据从主板内存拷贝到显卡显存。
- **算力解放**： 几万个物体的运动轨迹计算全由 GPU 的并行流处理器包揽。

### 总结

**优点：**

- **极其恐怖的 DrawCall 削减能力**：能把成百上千个物体的渲染指令压缩成 1 次（或少数几次） DrawCall，彻底解放 CPU。
- **极低的显存碎片占用**：数据紧密排列在连续的 Buffer 中，非常紧凑。
- **支持个体差异**：完美契合 `MaterialPropertyBlock`，可以在同一个 DrawCall 内让一千个相同的模型展现出一千种不同的颜色或状态。

**缺点：**

- **严苛的触发条件**：必须是完全相同的 Mesh，且必须使用完全相同的 Material 实例。
- **Vertex Shader 压力剧增**：每一个顶点都需要在 Shader 中根据 ID 去 Buffer 里寻址读取数据。对于 Meta Quest 这类采用 Tile-Based 架构的移动端 VR 设备而言，如果模型的顶点数较高，这种疯狂的显存寻址极易打满顶点计算单元，导致严重的设备发热和降频。
- **UBO 容量限制断层**：移动端图形 API（如 Vulkan/GLES3）通常对单次绑定的 Uniform Buffer 大小有严格限制（例如 64KB）。如果每个实例注入的数据较多，底层会被迫将其切分成多个块（Chunk），导致预期外的不完美合批。

**适用场景：**

- **海量同模实体**：例如机甲战斗时全屏幕倾泻的同型号物理子弹、大规模的爆炸碎片。
- **自然环境生成**：大面积的草丛、同型号的树木。

## 静态批处理

静态批处理的核心逻辑非常粗暴：既然这些物体永远不动（Static），那我干脆在游戏打包或者场景加载时，把它们的网格（Mesh）在内存里“缝合”成一个超级巨大的网格。

**实现原理：**

1. 提取所有勾选了 `Batching Static` 且使用相同材质的物体。
2. 将它们各自的本地顶点坐标转换到世界空间。
3. 组合成一个巨大的 Vertex/Index Buffer 并常驻内存。
4. 渲染时，如果它们在摄像机视锥体内，就直接调用一次 DrawCall 画出这个大网格的一部分或全部。

**优点：**

- **运行时的 CPU 零开销**：由于网格已经提前合并，运行时 CPU 根本不需要做任何矩阵计算和顶点转换，是所有合批技术中 CPU 效率最高的。

**致命缺陷（VR/移动端需高度警惕）：**

- **内存刺客**：这是静态批处理最大的代价。如果你在场景里放了 100 个由同一个模型复制出来的油桶，开启静态合批后，内存中会真实地复制 100 份顶点数据。
- 在 Meta Quest 这种对内存容量和显存带宽极其敏感的 Android 设备上，滥用静态批处理不仅会让包体急剧膨胀，还极易导致设备 OOM（Out of Memory）崩溃。

## 动态批处理

动态批处理是为了解决“运动物体”无法合批而诞生的。它的本质是 CPU 在每一帧里做苦力。

**实现原理：**

1. 每一帧，CPU 遍历所有符合条件的小型运动物体。
2. CPU 将这些物体的顶点从本地空间（Local Space）通过矩阵乘法转换到世界空间（World Space）。
3. 将转换后的顶点数据强行塞进一个动态的缓冲区（Dynamic Vertex Buffer）。
4. 将这个缓冲区发送给 GPU，用一次 DrawCall 渲染。

优点：

不需要像静态批处理那样牺牲大量内存。

支持运动物体。

致命缺陷（为什么它在现代管线中几乎被淘汰）：

条件极其苛刻：由于 CPU 每一帧算顶点的开销太大，Unity 规定只有非常简单的模型才能参与。例如，顶点的属性总数不能超过 900（通常意味着模型只能有二三百个面），不能有镜像缩放，材质属性必须完全一致。

算力倒挂：在 Unity 6 和现代渲染管线（URP/HDRP/Custom RP）下，图形 API 提交一次 DrawCall 的开销已经被压得很低了。很多时候，CPU 花费大量时间去计算和合并顶点的开销，甚至远远大于它老老实实多发几个 DrawCall 的开销。

## 常用优化方式的核心区别

```plaintext
  ┌─────────────────────────────┬─────────────────────────────────────┬─────────────────────────────────────┐
  │                             │            DrawCall 数量            │            优化了什么                │
  ├─────────────────────────────┼─────────────────────────────────────┼─────────────────────────────────────┤
  │ GPU Instancing              │ 减少（1 个 DrawCall 画 N 个）        │ CPU 端的 DrawCall 提交开销           │
  ├─────────────────────────────┼─────────────────────────────────────┼─────────────────────────────────────┤
  │ SRP Batcher                 │ 不减少（每个物体仍是独立 DrawCall）   │ 每个 DrawCall 的 GPU 状态设置开销     │
  └─────────────────────────────┴─────────────────────────────────────┴─────────────────────────────────────┘
```
