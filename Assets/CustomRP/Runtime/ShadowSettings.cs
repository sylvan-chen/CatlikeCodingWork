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
        }

        [SerializeField, Min(0f)]
        private float _maxDistance = 100f;

        [SerializeField]
        private DirectionalShadow _directional = new() { AtlasSize = MapSize._1024 };

        public float MaxDistance => _maxDistance;
        public DirectionalShadow Directional => _directional;
    }
}