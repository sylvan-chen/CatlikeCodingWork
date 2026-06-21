using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 专门用于处理光照
    /// </summary>
    public class Lighting
    {
        /// <summary> 最大支持的方向光数量 </summary>
        private const int MAX_DIRECTIONAL_LIGHT_COUNT = 4;

        private const string BUFFER_NAME = "Lighting";
        private static int DIRECTIONAL_LIGHT_COUNT_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static int DIRECTIONAL_LIGHT_COLORS_ID = Shader.PropertyToID("_DirectionalLightColors");
        private static int DIRECTIONAL_LIGHT_DIRECTIONS_ID = Shader.PropertyToID("_DirectionalLightDirections");

        private static Vector4[] DirectionalLightColors = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
        private static Vector4[] DirectionalLightDirections = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];

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
            _shadows.Setup(context, cullingResults, shadowSettings);
            SetupLights();
            _shadows.Render();
            _buffer.EndSample(BUFFER_NAME);

            ExecuteBuffer();
        }

        private void SetupLights()
        {
            // 从剔除结果获取所有可见光
            NativeArray<VisibleLight> visibleLights = _cullingResults.visibleLights;

            // 处理方向光
            int directionalLightCount = 0;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight visibleLight = visibleLights[i];
                if (visibleLight.lightType == LightType.Directional)
                {
                    SetupDirectionalLight(directionalLightCount++, ref visibleLight);
                    if (directionalLightCount >= MAX_DIRECTIONAL_LIGHT_COUNT) break;
                }
            }

            _buffer.SetGlobalInt(DIRECTIONAL_LIGHT_COUNT_ID, directionalLightCount);
            _buffer.SetGlobalVectorArray(DIRECTIONAL_LIGHT_COLORS_ID, DirectionalLightColors);
            _buffer.SetGlobalVectorArray(DIRECTIONAL_LIGHT_DIRECTIONS_ID, DirectionalLightDirections);
        }

        private void SetupDirectionalLight(int index, ref VisibleLight visibleLight)
        {
            // 通过 finalColor 获取最终颜色
            DirectionalLightColors[index] = visibleLight.finalColor;
            // 前向向量可通过 VisibleLight.localToWorldMatrix 属性获取，它是矩阵的第三列
            DirectionalLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
            // 预留可见光的阴影
            _shadows.ReserveDirectionalShadows(visibleLight.light, index);
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}