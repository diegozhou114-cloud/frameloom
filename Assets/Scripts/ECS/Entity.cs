using System;

namespace ZWebGLFrameWork.ECS
{
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        internal readonly int Generation;
        public static readonly Entity Invalid = new Entity(-1, -1);

        internal Entity(int id, int generation) { Id = id; Generation = generation; }

        /// <summary>
        /// 仅检查 Id >= 0。注意：Entity 被 World.DestroyEntity 销毁后世代会递增，
        /// 但 Entity 本身无法感知（不持有 World 引用），因此 <see cref="IsValid"/>
        /// 不会自动检测世代失效。外部代码请使用 <c>World.HasComponent</c> 等 API 验证实体有效性。
        /// </summary>
        public bool IsValid => Id >= 0;

        /// <summary>检查世代是否匹配（供 World 内部使用）。外部代码请用 World.HasComponent 等验证。</summary>
        internal bool IsGenerationValid(int currentGeneration) => Generation == currentGeneration;
        public bool Equals(Entity other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => Id;
        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"Entity({Id}:{Generation})" : "Entity(Invalid)";
    }
}
