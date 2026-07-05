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

        /// <summary> 方向阴影 </summary>
        [Serializable]
        public struct DirectionalShadow
        {
            public MapSize AtlasSize;
            [Range(1, 4)]
            [Tooltip("级联数量，每个方向光最多支持 4 个级联")]
            public int CascadeCount;
            [Range(0f, 1f)]
            public float CascadeRatio1;
            [Range(0f, 1f)]
            public float CascadeRatio2;
            [Range(0f, 1f)]
            public float CascadeRatio3;

            public Vector3 CascadeRatios => new Vector3(CascadeRatio1, CascadeRatio2, CascadeRatio3);
        }

        [SerializeField, Min(0f)]
        private float _maxDistance = 100f;

        [SerializeField]
        private DirectionalShadow _directional = new()
        {
            AtlasSize = MapSize._1024,
            CascadeCount = 4,
            CascadeRatio1 = 0.1f,
            CascadeRatio2 = 0.25f,
            CascadeRatio3 = 0.5f,
        };

        public float MaxDistance => _maxDistance;
        public DirectionalShadow Directional => _directional;
    }
}
