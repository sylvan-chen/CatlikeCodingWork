using UnityEngine;

namespace CustomRP.Examples
{
    [DisallowMultipleComponent]
    public class PerObjectMaterialProperties : MonoBehaviour
    {
        // 当我们大量的物体都是相同网格的，仅仅是一些简单数值不同 (比如颜色/透明度等)，这个时候如果创建多个材质就很浪费了。
        // 因此，使用 PropertyBlock 就能很好的减少材质数量: 它只是在共享的同一个材质上修改数值，无需重新提交。
        // 当然，PropertyBlock 也有它的限制:
        // 1. 不能传纹理
        // 2. 不能传 CommandBuffer
        // 3. 不能支持不同的 Shader 变体 (Shader Keywords)
        // 4. 不能支持 SRP Batcher
        //    - PB 相当于把数据塞进 UnityPerDraw 的 cbuffer 中，破环了统一的 cbuffer 结构，因此会被踢出合批队列。
        //    - 通常 PB 是为了支持 GPU Instancing: 大量同网格的物体，共用同一个材质，GPU Instancing 能打包成一次 DrawCall
        private static int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static int CUTOFF_ID = Shader.PropertyToID("_Cutoff");

        private static MaterialPropertyBlock _block;

        [SerializeField]
        private Color _baseColor = Color.white;
        [SerializeField, Range(0f, 1f)]
        private float _cutoff = 0.5f;

        private void Awake()
        {
            OnValidate();
        }

        private void OnValidate()
        {
            if (_block == null) _block = new MaterialPropertyBlock();

            _block.SetColor(BASE_COLOR_ID, _baseColor);
            _block.SetFloat(CUTOFF_ID, _cutoff);
            GetComponent<Renderer>().SetPropertyBlock(_block);
        }
    }
}