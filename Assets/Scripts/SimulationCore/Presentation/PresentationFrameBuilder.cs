using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>Owns reusable working collections for one simulation session.</summary>
    internal sealed class PresentationFrameBuilder : IDisposable
    {
        private readonly List<EntityPresentationState> _states = new List<EntityPresentationState>();
        private readonly List<EntityPresentationState> _current =
            new List<EntityPresentationState>();
        private readonly Dictionary<EntityPresentationKey, EntityPresentationState> _previous =
            new Dictionary<EntityPresentationKey, EntityPresentationState>();
        private readonly HashSet<EntityPresentationKey> _remaining =
            new HashSet<EntityPresentationKey>();
        private readonly List<EntityPresentationState> _created =
            new List<EntityPresentationState>();
        private readonly List<EntityPresentationState> _updated =
            new List<EntityPresentationState>();
        private readonly List<EntityPresentationKey> _destroyed =
            new List<EntityPresentationKey>();
        private readonly Action<SimEntity, SimIdentityComponent> _collectState;
        private readonly Comparison<EntityPresentationState> _compareStates;
        private readonly Comparison<EntityPresentationKey> _compareKeys;
        private SimWorld _currentWorld;
        private bool _disposed;

        internal PresentationFrameBuilder()
        {
            _collectState = CollectState;
            _compareStates = CompareStates;
            _compareKeys = CompareKeys;
        }

        internal PresentationFrame BuildFull(
            SimWorld world, int completedTick, int nextTick, long revision)
        {
            EnsureActive();
            try
            {
                CollectStates(world);
                ReplaceCurrent();
                return BuildOwnedCurrent(completedTick, nextTick, revision);
            }
            finally
            {
                ClearAdvanceWorkingSets();
            }
        }

        internal void Advance(SimWorld world, SimPresentationOutputBuffer destination)
        {
            EnsureActive();
            ClearAdvanceWorkingSets();
            CollectStates(world);
            for (int index = 0; index < _current.Count; index++)
            {
                EntityPresentationState state = _current[index];
                EntityPresentationKey key = Key(state);
                _previous.Add(key, state);
                _remaining.Add(key);
            }
            for (int index = 0; index < _states.Count; index++)
            {
                EntityPresentationState state = _states[index];
                EntityPresentationKey key = Key(state);
                if (!_previous.TryGetValue(key, out EntityPresentationState old))
                    _created.Add(state);
                else if (!SameState(old, state))
                    _updated.Add(state);
                _remaining.Remove(key);
            }
            foreach (EntityPresentationKey key in _remaining) _destroyed.Add(key);
            _created.Sort(_compareStates);
            _updated.Sort(_compareStates);
            _destroyed.Sort(_compareKeys);
            destination?.CopyFrom(_states, _created, _updated, _destroyed);
            ReplaceCurrent();
            _states.Clear();
            _previous.Clear();
            _remaining.Clear();
        }

        internal IReadOnlyList<EntityPresentationKey> Destroyed => _destroyed;

        internal PresentationFrame BuildOwnedCurrent(
            int completedTick, int nextTick, long revision) =>
            PresentationFrame.TakeOwnership(true, completedTick, nextTick, revision,
                _current.ToArray(), Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(), Array.Empty<EntityPresentationKey>());

        internal PresentationFrame BuildOwnedDelta(
            int completedTick, int nextTick, long revision) =>
            PresentationFrame.TakeOwnership(false, completedTick, nextTick, revision,
                Array.Empty<EntityPresentationState>(), _created.ToArray(),
                _updated.ToArray(), _destroyed.ToArray());

        internal void CopyCurrentTo(SimPresentationOutputBuffer destination) =>
            destination.CopyFrom(_current, _created, _updated, _destroyed);

        internal void ClearAdvanceWorkingSets()
        {
            _states.Clear();
            ResetDelta();
            _currentWorld = null;
        }

        internal void Reset()
        {
            ClearAdvanceWorkingSets();
            _current.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }

        private void CollectState(SimEntity entity, SimIdentityComponent identity)
        {
            _currentWorld.TryGetComponent(entity, out SimTransformComponent transform);
            _currentWorld.TryGetComponent(entity, out SimHealthComponent health);
            _currentWorld.TryGetComponent(entity, out SimTeamComponent team);
            _states.Add(new EntityPresentationState(entity.Id, entity.Generation,
                identity.ArchetypeId, identity.OwnerPlayerId, team.TeamId,
                transform.GridX, transform.GridY, transform.PosX.RawValue,
                transform.PosY.RawValue, 0, health.CurrentHP));
        }

        private void CollectStates(SimWorld world)
        {
            _states.Clear();
            if (_states.Capacity < world.EntityCount) _states.Capacity = world.EntityCount;
            _currentWorld = world;
            try
            {
                world.ForEachComponent(_collectState);
            }
            finally
            {
                _currentWorld = null;
            }
            _states.Sort(_compareStates);
        }

        private void ReplaceCurrent()
        {
            _current.Clear();
            if (_current.Capacity < _states.Count) _current.Capacity = _states.Count;
            for (int index = 0; index < _states.Count; index++)
                _current.Add(_states[index]);
        }

        private void ResetDelta()
        {
            _previous.Clear();
            _remaining.Clear();
            _created.Clear();
            _updated.Clear();
            _destroyed.Clear();
        }

        private void EnsureActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PresentationFrameBuilder));
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);

        private static bool SameState(EntityPresentationState left, EntityPresentationState right) =>
            left.ArchetypeId == right.ArchetypeId &&
            left.OwnerPlayerId == right.OwnerPlayerId && left.TeamId == right.TeamId &&
            left.GridX == right.GridX && left.GridY == right.GridY &&
            left.PosXRaw == right.PosXRaw && left.PosYRaw == right.PosYRaw &&
            left.OrientationMilliDegrees == right.OrientationMilliDegrees &&
            left.Health == right.Health;

        private static int CompareStates(EntityPresentationState left, EntityPresentationState right)
        {
            int byId = left.EntityId.CompareTo(right.EntityId);
            return byId != 0 ? byId : left.Generation.CompareTo(right.Generation);
        }

        private static int CompareKeys(EntityPresentationKey left, EntityPresentationKey right)
        {
            int byId = left.EntityId.CompareTo(right.EntityId);
            return byId != 0 ? byId : left.Generation.CompareTo(right.Generation);
        }
    }
}
