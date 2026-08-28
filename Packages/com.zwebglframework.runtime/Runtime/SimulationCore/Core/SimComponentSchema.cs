using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public static class SimComponentTypeId
    {
        public const int Transform = 1;
        public const int Health = 2;
        public const int Team = 3;
        public const int Movement = 4;
        public const int Attack = 5;
        public const int Target = 6;
        public const int Identity = 7;
    }

    public readonly struct ComponentRegistration
    {
        public Type Type { get; }
        public int Id { get; }

        public ComponentRegistration(Type type, int id)
        {
            Type = type;
            Id = id;
        }
    }

    public struct SimIdentityComponent : IHashableComponent
    {
        public int ArchetypeId;
        public int OwnerPlayerId;

        public ulong HashInto(ulong hash)
        {
            const ulong prime = 1099511628211UL;
            hash ^= unchecked((uint)ArchetypeId);
            hash *= prime;
            hash ^= unchecked((uint)OwnerPlayerId);
            hash *= prime;
            return hash;
        }
    }

    public static class SimComponentSchema
    {
        public const int Version = 1;

        public static readonly ComponentRegistration[] Registrations =
        {
            new ComponentRegistration(typeof(SimTransformComponent), SimComponentTypeId.Transform),
            new ComponentRegistration(typeof(SimHealthComponent), SimComponentTypeId.Health),
            new ComponentRegistration(typeof(SimTeamComponent), SimComponentTypeId.Team),
            new ComponentRegistration(typeof(SimMovementComponent), SimComponentTypeId.Movement),
            new ComponentRegistration(typeof(SimAttackComponent), SimComponentTypeId.Attack),
            new ComponentRegistration(typeof(SimTargetComponent), SimComponentTypeId.Target),
            new ComponentRegistration(typeof(SimIdentityComponent), SimComponentTypeId.Identity),
        };

        public static ComponentTypeRegistry CreateRegistry() => new ComponentTypeRegistry(Registrations);
    }
}
