namespace ZWebGLFrameWork.SimulationCore
{
    public readonly struct EntityPresentationKey : System.IEquatable<EntityPresentationKey>
    {
        public int EntityId { get; }
        public int Generation { get; }

        public EntityPresentationKey(int entityId, int generation)
        {
            EntityId = entityId;
            Generation = generation;
        }

        public bool Equals(EntityPresentationKey other) =>
            EntityId == other.EntityId && Generation == other.Generation;

        public override bool Equals(object obj) =>
            obj is EntityPresentationKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((EntityId * 397) ^ Generation);
    }

    public readonly struct EntityPresentationState
    {
        public int EntityId { get; }
        public int Generation { get; }
        public int ArchetypeId { get; }
        public int OwnerPlayerId { get; }
        public int TeamId { get; }
        public int GridX { get; }
        public int GridY { get; }
        public long PosXRaw { get; }
        public long PosYRaw { get; }
        public int OrientationMilliDegrees { get; }
        public int Health { get; }

        public EntityPresentationState(int entityId, int generation, int archetypeId,
            int ownerPlayerId, int teamId, int gridX, int gridY,
            long posXRaw, long posYRaw, int orientationMilliDegrees, int health)
        {
            EntityId = entityId;
            Generation = generation;
            ArchetypeId = archetypeId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            GridX = gridX;
            GridY = gridY;
            PosXRaw = posXRaw;
            PosYRaw = posYRaw;
            OrientationMilliDegrees = orientationMilliDegrees;
            Health = health;
        }
    }

    public sealed class PresentationFrame
    {
        public bool IsFull { get; }
        public int CompletedTick { get; }
        public int NextTick { get; }
        public long TimelineRevision { get; }
        public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Entities { get; }
        public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Created { get; }
        public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Updated { get; }
        public System.Collections.Generic.IReadOnlyList<EntityPresentationKey> Destroyed { get; }

        public PresentationFrame(bool isFull, int completedTick, int nextTick, long timelineRevision,
            EntityPresentationState[] entities, EntityPresentationState[] created,
            EntityPresentationState[] updated, EntityPresentationKey[] destroyed)
        {
            IsFull = isFull;
            CompletedTick = completedTick;
            NextTick = nextTick;
            TimelineRevision = timelineRevision;
            Entities = Freeze(entities);
            Created = Freeze(created);
            Updated = Freeze(updated);
            Destroyed = Freeze(destroyed);
        }

        private PresentationFrame(bool isFull, int completedTick, int nextTick,
            long timelineRevision,
            System.Collections.Generic.IReadOnlyList<EntityPresentationState> entities,
            System.Collections.Generic.IReadOnlyList<EntityPresentationState> created,
            System.Collections.Generic.IReadOnlyList<EntityPresentationState> updated,
            System.Collections.Generic.IReadOnlyList<EntityPresentationKey> destroyed)
        {
            IsFull = isFull;
            CompletedTick = completedTick;
            NextTick = nextTick;
            TimelineRevision = timelineRevision;
            Entities = entities;
            Created = created;
            Updated = updated;
            Destroyed = destroyed;
        }

        internal static PresentationFrame TakeOwnership(bool isFull, int completedTick,
            int nextTick, long timelineRevision, EntityPresentationState[] entities,
            EntityPresentationState[] created, EntityPresentationState[] updated,
            EntityPresentationKey[] destroyed)
        {
            return new PresentationFrame(isFull, completedTick, nextTick, timelineRevision,
                Own(entities), Own(created), Own(updated), Own(destroyed));
        }

        private static System.Collections.Generic.IReadOnlyList<T> Freeze<T>(T[] values)
        {
            return System.Array.AsReadOnly((T[])(values ?? System.Array.Empty<T>()).Clone());
        }

        private static System.Collections.Generic.IReadOnlyList<T> Own<T>(T[] values)
        {
            if (values == null || values.Length == 0) return EmptyList<T>.Value;
            return System.Array.AsReadOnly(values);
        }

        private static class EmptyList<T>
        {
            internal static readonly System.Collections.Generic.IReadOnlyList<T> Value =
                System.Array.AsReadOnly(System.Array.Empty<T>());
        }
    }
}
