using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 光照渲染处理
    /// </summary>
    public class Lighting
    {
        private const string BUFFER_NAME = "Lighting";

        // ============================== 方向光相关数据 ==============================

        /// <summary> 最大方向光数量 </summary>
        private const int MAX_DIRECTIONAL_LIGHT_COUNT = 4;

        /// <summary> 方向光数量 </summary>
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        /// <summary> 方向光颜色 </summary>
        private static readonly int DirectionalLightColorsId = Shader.PropertyToID("_DirectionalLightColors");
        /// <summary> 方向光方向 </summary>
        private static readonly int DirectionalLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");
        /// <summary> 方向光阴影数据 </summary>
        private static readonly int DirectionalLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData");

        private static Vector4[] DirectionalLightColors = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
        private static Vector4[] DirectionalLightDirections = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
        private static Vector4[] DirectionalLightShadowData = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];

        // ==========================================================================

        /// <summary> 渲染上下文 </summary>
        private ScriptableRenderContext _context;
        /// <summary> 剔除结果 </summary>
        private CullingResults _cullingResults;

        /// <summary> 命令缓冲区 </summary>
        private readonly CommandBuffer _buffer = new() { name = BUFFER_NAME };
        /// <summary> 阴影 </summary>
        private readonly Shadows _shadows = new();

        public void Setup(ScriptableRenderContext context, CullingResults cullingResults, ShadowSettings shadowSettings)
        {
            _context = context;
            // 简单的方式是通过 RenderSetting.sun 获取场景主光源，但我们要支持多光源，因此通过剔除后的结果拿到光源信息
            _cullingResults = cullingResults;

            _buffer.BeginSample(BUFFER_NAME);
            // 配置阴影
            _shadows.Setup(context, cullingResults, shadowSettings);
            // 配置光照
            SetupLights();
            // 绘制阴影
            _shadows.Render();
            _buffer.EndSample(BUFFER_NAME);

            ExecuteBuffer();
        }

        public void Cleanup()
        {
            _shadows.Cleanup();
        }

        private void SetupLights()
        {
            // 从剔除结果获取所有可见光
            NativeArray<VisibleLight> visibleLights = _cullingResults.visibleLights;

            int directionalLightCount = 0;

            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight visibleLight = visibleLights[i];

                // --- 方向光 ---
                if (visibleLight.lightType is LightType.Directional)
                {
                    SetupDirectionalLight(directionalLightCount++, ref visibleLight);
                    if (directionalLightCount >= MAX_DIRECTIONAL_LIGHT_COUNT) break;
                }
            }

            // --- 方向光 ---
            _buffer.SetGlobalInt(DirectionalLightCountId, directionalLightCount);
            _buffer.SetGlobalVectorArray(DirectionalLightColorsId, DirectionalLightColors);
            _buffer.SetGlobalVectorArray(DirectionalLightDirectionsId, DirectionalLightDirections);
            _buffer.SetGlobalVectorArray(DirectionalLightShadowDataId, DirectionalLightShadowData);
        }

        private void SetupDirectionalLight(int index, ref VisibleLight visibleLight)
        {
            // 通过 finalColor 获取最终颜色
            DirectionalLightColors[index] = visibleLight.finalColor;
            // 前向向量可通过 VisibleLight.localToWorldMatrix 属性获取，它是矩阵的第三列
            DirectionalLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
            // 登记这盏光的阴影数据
            DirectionalLightShadowData[index] = _shadows.ReserveDirectionalShadows(visibleLight.light, index);
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}
