using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>Reusable borrowed presentation collections populated by TickInto.</summary>
    /// <remarks>Views are invalidated with their parent SimTickOutputBuffer and are not owning snapshots.</remarks>
    public sealed class SimPresentationOutputBuffer
    {
        private readonly List<EntityPresentationState> _entities;
        private readonly List<EntityPresentationState> _created;
        private readonly List<EntityPresentationState> _updated;
        private readonly List<EntityPresentationKey> _destroyed;

        internal SimPresentationOutputBuffer(int initialCapacity)
        {
            _entities = new List<EntityPresentationState>(initialCapacity);
            _created = new List<EntityPresentationState>(initialCapacity);
            _updated = new List<EntityPresentationState>(initialCapacity);
            _destroyed = new List<EntityPresentationKey>(initialCapacity);
            Entities = _entities.AsReadOnly();
            Created = _created.AsReadOnly();
            Updated = _updated.AsReadOnly();
            Destroyed = _destroyed.AsReadOnly();
        }

        /// <summary>Gets all current entity presentation states.</summary>
        public IReadOnlyList<EntityPresentationState> Entities { get; }
        /// <summary>Gets entities created during the tick.</summary>
        public IReadOnlyList<EntityPresentationState> Created { get; }
        /// <summary>Gets entities updated during the tick.</summary>
        public IReadOnlyList<EntityPresentationState> Updated { get; }
        /// <summary>Gets entity keys destroyed during the tick.</summary>
        public IReadOnlyList<EntityPresentationKey> Destroyed { get; }

        internal List<EntityPresentationState> MutableEntities => _entities;
        internal List<EntityPresentationState> MutableCreated => _created;
        internal List<EntityPresentationState> MutableUpdated => _updated;
        internal List<EntityPresentationKey> MutableDestroyed => _destroyed;

        internal void EnsureCapacity(int capacity)
        {
            if (_entities.Capacity < capacity) _entities.Capacity = capacity;
            if (_created.Capacity < capacity) _created.Capacity = capacity;
            if (_updated.Capacity < capacity) _updated.Capacity = capacity;
            if (_destroyed.Capacity < capacity) _destroyed.Capacity = capacity;
        }

        internal void Clear()
        {
            _entities.Clear();
            _created.Clear();
            _updated.Clear();
            _destroyed.Clear();
        }

        internal void CopyFrom(
            IReadOnlyList<EntityPresentationState> entities,
            IReadOnlyList<EntityPresentationState> created,
            IReadOnlyList<EntityPresentationState> updated,
            IReadOnlyList<EntityPresentationKey> destroyed)
        {
            Copy(entities, _entities);
            Copy(created, _created);
            Copy(updated, _updated);
            Copy(destroyed, _destroyed);
        }

        private static void Copy<T>(IReadOnlyList<T> source, List<T> destination)
        {
            for (int index = 0; index < source.Count; index++)
                destination.Add(source[index]);
        }
    }
}
