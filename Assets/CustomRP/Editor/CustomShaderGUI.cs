using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP.Editor
{
    /// <summary>
    /// 自定义 Shader 面板
    /// </summary>
    public class CustomShaderGUI : ShaderGUI
    {
        public enum ShadowMode
        {
            On,
            Clip,
            Dither,
            Off
        }

        private MaterialEditor _materialEditor;
        private Object[] _materials;
        private MaterialProperty[] _materialProperties;

        private bool _showPresets;

        public bool Clipping
        {
            set => ToggleProperty("_Clipping", "_CLIPPING", value);
        }

        public bool PremultiplyAlpha
        {
            set => ToggleProperty("_PremulAlpha", "_PREMULTIPLY_ALPHA", value);
        }

        public BlendMode SrcBlend
        {
            set => SetProperty("_SrcBlend", (float)value);
        }

        public BlendMode DstBlend
        {
            set => SetProperty("_DstBlend", (float)value);
        }

        public bool ZWrite
        {
            set => SetProperty("_ZWrite", value ? 1f : 0f);
        }

        public ShadowMode Shadows
        {
            set
            {
                if (SetProperty("_Shadows", (float)value))
                {
                    SetKeyword("_SHADOWS_CLIP", value == ShadowMode.Clip);
                    SetKeyword("_SHADOWS_DITHER", value == ShadowMode.Dither);
                }
            }
        }

        private bool HasProperty(string name) => FindProperty(name, _materialProperties, false) != null;

        public RenderQueue RenderQueue
        {
            set
            {
                foreach (var matObj in _materials)
                {
                    var mat = (Material)matObj;
                    mat.renderQueue = (int)value;
                }
            }
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();

            base.OnGUI(materialEditor, properties);

            _materialEditor = materialEditor;
            _materials = materialEditor.targets;
            _materialProperties = properties;

            EditorGUILayout.Space();
            _showPresets = EditorGUILayout.Foldout(_showPresets, "Presets", true);
            if (_showPresets)
            {
                OpaquePreset();
                ClipPreset();
                FadePreset();
                TransparentPreset();
            }

            if (EditorGUI.EndChangeCheck())
            {
                SetShadowCasterPass();
            }
        }

        /// <summary>
        /// 设置 keyword 启用/关闭
        /// </summary>
        private void SetKeyword(string keyword, bool enabled)
        {
            if (enabled)
            {
                foreach (var matObj in _materials)
                {
                    var mat = (Material)matObj;
                    mat.EnableKeyword(keyword);
                }
            }
            else
            {
                foreach (var matObj in _materials)
                {
                    var mat = (Material)matObj;
                    mat.DisableKeyword(keyword);
                }
            }
        }

        /// <summary>
        /// 设置属性值
        /// name("XXX", ...)
        /// </summary>
        private bool SetProperty(string name, float value)
        {
            var property = FindProperty(name, _materialProperties, false);
            if (property != null)
            {
                property.floatValue = value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 设置 Toggle 属性值
        /// [Toggle(keyword)] name("XXX", Float) = 0
        /// </summary>
        private void ToggleProperty(string name, string keyword, bool value)
        {
            if (SetProperty(name, value ? 1f : 0f))
            {
                SetKeyword(keyword, value);
            }
        }

        /// <summary>
        /// 不透明预设
        /// </summary>
        private void OpaquePreset()
        {
            if (PresetButton("Opaque"))
            {
                Clipping = false;
                PremultiplyAlpha = false;
                SrcBlend = BlendMode.One;
                DstBlend = BlendMode.Zero;
                ZWrite = true;
                RenderQueue = RenderQueue.Geometry;
            }
        }

        /// <summary>
        /// 裁剪预设
        /// </summary>
        private void ClipPreset()
        {
            if (PresetButton("Clip"))
            {
                Clipping = true;
                PremultiplyAlpha = false;
                SrcBlend = BlendMode.One;
                DstBlend = BlendMode.Zero;
                ZWrite = true;
                RenderQueue = RenderQueue.AlphaTest;
            }
        }

        /// <summary>
        /// 淡入淡出预设
        /// - shader 里的 brdf.diffuse 不乘 alpha，输出的 color 是全亮度的 (漫反射 + 镜面反射都满)。
        /// - SrcBlend = ScrAlpha 让 GPU 在混合时自动把 src.rgb 乘上 src.a。
        /// - 结果：整个像素 (漫反射 + 镜面高光) 一起被 alpha 削弱，即半透明像素的高光也跟着变暗，看起来就像是蒙了一层色雾，高光发闷。
        /// - 适合烟雾、全息投影、谈出的 UI 等。
        /// </summary>
        private void FadePreset()
        {
            if (PresetButton("Fade"))
            {
                Clipping = false;
                PremultiplyAlpha = false;
                SrcBlend = BlendMode.SrcAlpha;
                DstBlend = BlendMode.OneMinusSrcAlpha;
                ZWrite = false;
                RenderQueue = RenderQueue.Transparent;
            }
        }

        /// <summary>
        /// 透明预设
        /// - shader 里的 brdf.diffuse *= surface.alpha，漫反射已经在 shader 里乘了 alpha，而镜面反射不变。
        /// - ScrBlend = One 让 GPU 保留当前亮度 (避免重复相乘)
        /// - 结果：漫反射随透明度变暗，镜面高光保持全亮度，看起来像是玻璃，高光锐利。
        /// - 适合窗户、水面、透明塑料等。
        /// </summary>
        private void TransparentPreset()
        {
            if (HasProperty("_PremulAlpha") && PresetButton("Transparent"))
            {
                Clipping = false;
                PremultiplyAlpha = true;
                SrcBlend = BlendMode.One;
                DstBlend = BlendMode.OneMinusSrcAlpha;
                ZWrite = false;
                RenderQueue = RenderQueue.Transparent;
            }
        }

        private void SetShadowCasterPass()
        {
            MaterialProperty shadows = FindProperty("_Shadows", _materialProperties, false);
            if (shadows == null || shadows.hasMixedValue)
            {
                return;
            }
            bool enabled = shadows.floatValue < (float)ShadowMode.Off;
            foreach (var o in _materials)
            {
                var m = (Material)o;
                m.SetShaderPassEnabled("ShadowCaster", enabled);
            }
        }

        /// <summary>
        /// 绘制预设按钮
        /// </summary>
        private bool PresetButton(string name)
        {
            if (GUILayout.Button(name))
            {
                _materialEditor.RegisterPropertyChangeUndo(name);
                return true;
            }

            return false;
        }
    }
}
