using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 自定义管线资源
    /// </summary>
    [CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline", fileName = "CustomRenderPipelineAsset")]
    public class CustomRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField]
        private bool _useDynamicBatching = false;
        [SerializeField]
        /// <summary>对应 URP - 无，由 #pragma multi_compile_instancing 决定。URP 17 里被 GPU Resident Drawer 取代</summary>
        private bool _useGPUInstancing = true;
        [SerializeField]
        /// <summary>对应 URP - 无，强制开启</summary>
        private bool _useSRPBatcher = true;

        [SerializeField]
        private ShadowSettings _shadows = null;

        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderPipeline(_useDynamicBatching, _useGPUInstancing, _useSRPBatcher, _shadows);
        }
    }
}
