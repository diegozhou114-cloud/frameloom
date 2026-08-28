using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真实体标识符 — 轻量值类型，由 SimWorld 分配。
    /// 完全等价于现有 ECS 的 Entity，但属于 SimulationCore 命名空间。
    /// </summary>
    public readonly struct SimEntity : IEquatable<SimEntity>
    {
        public readonly int Id;
        public readonly int Generation;
        internal readonly int WorldEpoch;
        public static readonly SimEntity Invalid = new SimEntity(-1, -1, -1);

        internal SimEntity(int id, int generation)
            : this(id, generation, 0)
        {
        }

        internal SimEntity(int id, int generation, int worldEpoch)
        {
            Id = id;
            Generation = generation;
            WorldEpoch = worldEpoch;
        }

        public bool IsValid => Id >= 0;

        public bool Equals(SimEntity other) => Id == other.Id && Generation == other.Generation && WorldEpoch == other.WorldEpoch;
        public override bool Equals(object obj) => obj is SimEntity e && Equals(e);
        /// <summary>
        /// 仅返回 Id — 回收后的实体可能 Hash 碰撞，但不会同时存在于 Hash 集合中，可接受。
        /// </summary>
        public override int GetHashCode() => unchecked((Id * 397) ^ Generation ^ WorldEpoch);
        public static bool operator ==(SimEntity a, SimEntity b) => a.Equals(b);
        public static bool operator !=(SimEntity a, SimEntity b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"SimEntity({Id}:{Generation}@{WorldEpoch})" : "SimEntity(Invalid)";
    }
}
