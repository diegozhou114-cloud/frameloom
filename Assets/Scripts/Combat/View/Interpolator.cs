using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// Tick 间插值器 — 在渲染帧之间平滑过渡逻辑位置。
    /// 逻辑 Tick 20/s，渲染 60fps → 每 3 渲染帧插值一次。
    /// </summary>
    public class Interpolator
    {
        /// <summary>当前插值进度 [0, 1)</summary>
        public float Alpha { get; private set; }

        private float _logicDt; // 逻辑 Tick 间隔（秒）

        public Interpolator(float ticksPerSecond = 20f)
        {
            _logicDt = 1f / ticksPerSecond;
        }

        /// <summary>更新插值进度（每渲染帧调用）</summary>
        public void Update(float renderDt)
        {
            Alpha += renderDt / _logicDt;
            while (Alpha >= 1f) Alpha -= 1f;  // while handles frame spikes where Alpha may exceed 2f+
        }

        /// <summary>插值两个 Vector3 位置</summary>
        public static Vector3 LerpPosition(Vector3 prev, Vector3 current, float alpha)
        {
            return Vector3.Lerp(prev, current, alpha);
        }
    }
}
