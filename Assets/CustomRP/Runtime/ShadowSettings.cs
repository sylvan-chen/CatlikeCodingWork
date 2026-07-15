using System;
using UnityEngine;

namespace CustomRP
{
    /// <summary>
    /// 阴影设置
    /// </summary>
    [Serializable]
    public class ShadowSettings
    {
        public enum MapSize
        {
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096,
            _8192 = 8192,
        }

        /// <summary>
        /// PCF 过滤模式
        /// </summary>
        public enum FilterMode
        {
            PCF2x2 = 0, // 无
            PCF3x3 = 1, // 3x3, 采样 4 个点
            PCF5x5 = 2, // 5x5, 采样 9 个点
            PCF7x7 = 3, // 7x7, 采样 16 个点
        }

        /// <summary>
        /// 级联混合（级联之间平滑过渡）
        /// </summary>
        public enum CascadeBlendMode
        {
            Hard = 0,
            Soft = 1,
            Dither = 2,
        }

        /// <summary> 方向阴影设置 </summary>
        [Serializable]
        public struct DirectionalShadow
        {
            [Tooltip("阴影图集尺寸")]
            public MapSize AtlasSize;
            [Tooltip("PCF 过滤模式")]
            public FilterMode Filter;
            [Range(1, 4)]
            [Tooltip("级联数量，每个方向光最多支持 4 个级联")]
            public int CascadeCount;
            [Range(0f, 1f)]
            public float CascadeRatio1;
            [Range(0f, 1f)]
            public float CascadeRatio2;
            [Range(0f, 1f)]
            public float CascadeRatio3;
            [SerializeField, Range(0.001f, 1f)]
            [Tooltip("阴影级联衰减系数")]
            public float CascadeFade;
            [Tooltip("级联混合模式（级联之间如何平滑过渡）")]
            public CascadeBlendMode CascadeBlend;

            public Vector3 CascadeRatios => new Vector3(CascadeRatio1, CascadeRatio2, CascadeRatio3);
        }

        [SerializeField, Min(0.001f)]
        [Tooltip("阴影最大投射距离")]
        private float _maxDistance = 100f;
        [SerializeField, Range(0.001f, 1f)]
        [Tooltip("阴影远距离渐隐的衰减系数")]
        private float _distanceFade = 0.1f;
        [SerializeField]
        [Tooltip("方向阴影设置")]
        private DirectionalShadow _directional = new()
        {
            AtlasSize = MapSize._1024,
            Filter = FilterMode.PCF2x2,
            CascadeCount = 4,
            CascadeRatio1 = 0.1f,
            CascadeRatio2 = 0.25f,
            CascadeRatio3 = 0.5f,
            CascadeFade = 0.1f,
            CascadeBlend = CascadeBlendMode.Hard,
        };

        public float MaxDistance => _maxDistance;
        public float DistanceFade => _distanceFade;
        public DirectionalShadow Directional => _directional;
    }
}
