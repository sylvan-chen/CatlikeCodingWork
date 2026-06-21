using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 自定义渲染管线
    /// </summary>
    public class CustomRenderPipeline : RenderPipeline
    {
        private readonly CameraRenderer _renderer = new CameraRenderer();

        private bool _useDynamicBatching;
        private bool _useGPUInstancing;
        private ShadowSettings _shadowSettings;

        public CustomRenderPipeline(
            bool useDynamicBatching,
            bool useGPUInstancing,
            bool useSRPBatcher,
            ShadowSettings shadowSettings)
        {
            _useDynamicBatching = useDynamicBatching;
            _useGPUInstancing = useGPUInstancing;

            // ===== 支持 SRP Batcher =====
            // SRP Batcher 原理：不是减少 DrawCall，而是简化 DrawCall，把数据分成两类，分别处理：
            //
            // ┌─────────────────────────────────────────────────────────┐
            // │  传统方式 (SetPass Call 开销较大)                          │
            // │                                                         │
            // │  每个 DrawCall: [材质属性 + 变换矩阵 + 纹理 + ...] 全部上传   │
            // │  → 即使材质相同，只是物体不同，也要重新上传一切                 │
            // └─────────────────────────────────────────────────────────┘
            //
            // ┌─────────────────────────────────────────────────────────┐
            // │  SRP Batcher 方式                                        │
            // │                                                         │
            // │  UnityPerMaterial (材质缓冲区):                           │
            // │  ┌──────────────────────────────────────┐               │
            // │  │ _BaseColor, _MainTex, ...            │ ← 材质不变就缓存 │
            // │  │ 只在切换材质时才更新                    │   在 GPU 上不动 │
            // │  └──────────────────────────────────────┘               │
            // │                                                         │
            // │  UnityPerDraw (逐物体缓冲区):                              │
            // │  ┌──────────────────────────────────────┐               │
            // │  │ unity_ObjectToWorld, unity_LODFade...│ ← 每个物体都更新 │
            // │  │ 每个 DrawCall 都快速更新               │   但是数据量很小 │
            // │  └──────────────────────────────────────┘               │
            // │                                                         │
            // │  → 同一材质的多个物体可以快速连续绘制，CPU 开销极低             │
            // └─────────────────────────────────────────────────────────┘
            //
            // 流程：
            // 1. Unity 按材质排序所有 DrawCall（同材质的放一起）
            // 2. 第一个物体：上传 UnityPerMaterial（材质属性） + 上传 UnityPerDraw（变换矩阵） → 绘制
            // 3. 第二个物体（同材质）：UnityPerMaterial 已在 GPU 上不动，只更新 UnityPerDraw → 绘制
            // 4. 第三个物体（同材质）：同上，只更新 UnityPerDraw → 绘制
            // 5. 切换材质时：才重新上传新的 UnityPerMaterial
            //
            // 性能来源：UnityPerDraw 只有几十个字节（几个矩阵），而传统方式每次要上传几百字节到几 KB 的材质数据。SRP Batcher 把"大而慢"的材质上传变成了"小而快"的逐物体更新。
            GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
            // 让光源使用线性空间
            GraphicsSettings.lightsUseLinearIntensity = true;

            // 阴影设置
            _shadowSettings = shadowSettings;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

        /// <summary>
        /// Unity 会每帧调用 RP 实例的 Render 方法。
        /// </summary>
        /// <param name="context">一个上下文结构体，提供与原生引擎的连接</param>
        /// <param name="cameras">一个摄像机数组，因为场景中可能存在多个激活的摄像机，RP 需要按摄像机数组的传入顺序渲染所有摄像机</param>
        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                _renderer.Render(context, cameras[i], _useDynamicBatching, _useGPUInstancing, _shadowSettings);
            }
        }
    }
}