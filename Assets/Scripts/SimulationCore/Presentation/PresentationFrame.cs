namespace ZWebGLFrameWork.SimulationCore
{
    public readonly struct EntityPresentationKey
    {
        public int EntityId { get; }
        public int Generation { get; }

        public EntityPresentationKey(int entityId, int generation)
        {
            EntityId = entityId;
            Generation = generation;
        }
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

        private static System.Collections.Generic.IReadOnlyList<T> Freeze<T>(T[] values)
        {
            return System.Array.AsReadOnly((T[])(values ?? System.Array.Empty<T>()).Clone());
        }
    }
}
