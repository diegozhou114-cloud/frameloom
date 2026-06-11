using System;

namespace ZWebGLFrameWork.Combat.ECS
{
    /// <summary>
    /// 实体 — 轻量级 ID 包装，零开销。
    /// 本质是一个 int，可值传递、可比较、可做字典键。
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        public readonly int Generation; // 防止重用歧义

        public static readonly Entity Invalid = new Entity(-1, -1);

        internal Entity(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool IsValid => Id >= 0;

        public bool Equals(Entity other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => Id;
        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"Entity({Id}:{Generation})" : "Entity(Invalid)";
    }
}
