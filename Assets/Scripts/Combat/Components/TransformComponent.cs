using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>位置和朝向</summary>
    public struct TransformComponent : IComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector2Int GridPosition; // 当前所在网格

        public static TransformComponent Create(Vector3 pos, Vector2Int gridPos)
        {
            return new TransformComponent
            {
                Position = pos,
                Rotation = Quaternion.identity,
                Scale = Vector3.one,
                GridPosition = gridPos,
            };
        }
    }
}
