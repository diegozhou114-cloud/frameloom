using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>攻击目标</summary>
    public struct TargetComponent : IComponent
    {
        /// <summary>目标实体</summary>
        public Entity TargetEntity;

        /// <summary>是否有目标</summary>
        public bool HasTarget => TargetEntity.IsValid;

        public static TargetComponent Create() => new TargetComponent
        {
            TargetEntity = Entity.Invalid,
        };
    }
}
