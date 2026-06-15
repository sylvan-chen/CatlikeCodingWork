using UnityEngine;
using Random = UnityEngine.Random;

namespace CustomRP.Examples
{
    public class MeshBall : MonoBehaviour
    {
        private static int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static int METALLIC_ID = Shader.PropertyToID("_Metallic");
        private static int SMOOTHNESS_ID = Shader.PropertyToID("_Smoothness");

        [SerializeField]
        private Mesh _mesh;
        [SerializeField]
        private Material _material;

        private Matrix4x4[] _matrices = new Matrix4x4[1023];
        private Vector4[] _baseColors = new Vector4[1023];
        private float[] _metallic = new float[1023];
        private float[] _smoothness = new float[1023];
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            for (int i = 0; i < _matrices.Length; i++)
            {
                _matrices[i] = Matrix4x4.TRS(
                    Random.insideUnitSphere * 10f,
                    Quaternion.Euler(Random.value * 360f, Random.value * 360f, Random.value * 360f),
                    Vector3.one * Random.Range(0.5f, 1.5f)
                );

                _baseColors[i] = new Vector4(Random.value, Random.value, Random.value, Random.Range(0.5f, 1f));
                // 将 25% 的实例设为金属质感，光滑度在 0.05 ~ 0.95 之间
                _metallic[i] = Random.value < 0.25f ? 1f : 0f;
                _smoothness[i] = Random.Range(0.05f, 0.95f);
            }
        }

        private void Update()
        {
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
                _block.SetVectorArray(BASE_COLOR_ID, _baseColors);
                _block.SetFloatArray(METALLIC_ID, _metallic);
                _block.SetFloatArray(SMOOTHNESS_ID, _smoothness);
            }
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices, 1023, _block);
        }
    }
}