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
        private bool _useGPUInstancing = true;
        [SerializeField]
        private bool _useSRPBatcher = true;

        [SerializeField]
        private ShadowSettings _shadows = null;

        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderPipeline(_useDynamicBatching, _useGPUInstancing, _useSRPBatcher, _shadows);
        }
    }
}