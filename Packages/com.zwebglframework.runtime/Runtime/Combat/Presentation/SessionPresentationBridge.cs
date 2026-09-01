using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public sealed class SessionPresentationBridge
    {
        private const float PositionScale = 10000f;
        private readonly IEntityViewFactory _factory;
        private readonly Dictionary<EntityPresentationKey, IEntityView> _views =
            new Dictionary<EntityPresentationKey, IEntityView>();
        private readonly List<KeyValuePair<EntityPresentationKey, IEntityView>> _sampleViews =
            new List<KeyValuePair<EntityPresentationKey, IEntityView>>();
        private readonly PresentationInterpolator _interpolator =
            new PresentationInterpolator();
        private long _lease;
        private long _revision = long.MinValue;

        public int ViewCount => _views.Count;

        public SessionPresentationBridge(IEntityViewFactory factory)
        {
            _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        }

        public void Apply(PresentationFrame frame)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            long lease = ++_lease;
            _revision = frame.TimelineRevision;
            _interpolator.BeginFrame(frame);
            if (frame.IsFull)
                ApplyFull(frame.Entities, lease);
            else
                ApplyDelta(frame, lease);
            if (lease == _lease) Sample(1f, lease);
        }

        /// <summary>
        /// Synchronously consumes presentation data borrowed from a parent
        /// <see cref="SimTickOutputBuffer"/> while that parent remains valid.
        /// All borrowed collections are consumed before return; no buffer, collection, or list view
        /// is retained. Only interpolation value copies and bridge-owned views remain.
        /// </summary>
        /// <param name="frame">The presentation buffer borrowed from the parent output.</param>
        /// <param name="revision">The revision from the same parent output's
        /// <see cref="SimTickOutputBuffer.Revision"/>.</param>
        /// <remarks>After return, the caller may clear, reuse, or dispose the parent output;
        /// subsequent <see cref="Sample(float)"/> calls remain valid.</remarks>
        public void Apply(SimPresentationOutputBuffer frame, long revision)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            long lease = ++_lease;
            bool reset = revision != _revision;
            _revision = revision;
            _interpolator.BeginFrame(frame, revision);
            if (reset)
                ApplyFull(frame.Entities, lease);
            else
                ApplyDelta(frame.Destroyed, frame.Created, frame.Updated, lease);
            if (lease == _lease) Sample(1f, lease);
        }

        public void Sample(float alpha)
        {
            Sample(alpha, _lease);
        }

        private void Sample(float alpha, long lease)
        {
            alpha = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
            _sampleViews.Clear();
            try
            {
                foreach (KeyValuePair<EntityPresentationKey, IEntityView> pair in _views)
                    _sampleViews.Add(pair);
                for (int index = 0; index < _sampleViews.Count; index++)
                {
                    if (lease != _lease) return;
                    KeyValuePair<EntityPresentationKey, IEntityView> pair =
                        _sampleViews[index];
                    if (!_interpolator.TryGet(pair.Key, out InterpolationPair states))
                        continue;
                    pair.Value.SetPosition(Interpolate(states.Previous.PosXRaw,
                        states.Current.PosXRaw, alpha), Interpolate(
                        states.Previous.PosYRaw, states.Current.PosYRaw, alpha));
                    if (lease != _lease) return;
                }
            }
            finally
            {
                _sampleViews.Clear();
            }
        }

        public void Cleanup()
        {
            ++_lease;
            _revision = long.MinValue;
            _sampleViews.Clear();
            var views = new List<IEntityView>(_views.Values);
            _views.Clear();
            _interpolator.Clear();
            foreach (IEntityView view in views)
                _factory.Destroy(view);
        }

        private void ApplyFull(IReadOnlyList<EntityPresentationState> states, long lease)
        {
            var present = new HashSet<EntityPresentationKey>();
            for (int index = 0; index < states.Count; index++)
            {
                EntityPresentationState state = states[index];
                EntityPresentationKey key = Key(state);
                present.Add(key);
                if (!ApplyState(state, lease)) return;
            }

            var removed = new List<EntityPresentationKey>();
            foreach (EntityPresentationKey key in _views.Keys)
                if (!present.Contains(key)) removed.Add(key);
            foreach (EntityPresentationKey key in removed)
                if (!Destroy(key, lease)) return;
        }

        private void ApplyDelta(PresentationFrame frame, long lease)
        {
            ApplyDelta(frame.Destroyed, frame.Created, frame.Updated, lease);
        }

        private void ApplyDelta(IReadOnlyList<EntityPresentationKey> destroyed,
            IReadOnlyList<EntityPresentationState> created,
            IReadOnlyList<EntityPresentationState> updated, long lease)
        {
            for (int index = 0; index < destroyed.Count; index++)
                if (!Destroy(destroyed[index], lease)) return;
            for (int index = 0; index < created.Count; index++)
                if (!ApplyState(created[index], lease)) return;
            for (int index = 0; index < updated.Count; index++)
                if (!ApplyState(updated[index], lease)) return;
        }

        private bool ApplyState(EntityPresentationState state, long lease)
        {
            if (lease != _lease) return false;
            EntityPresentationKey key = Key(state);
            if (!_views.TryGetValue(key, out IEntityView view))
            {
                view = _factory.Create(key, state.ArchetypeId);
                if (view == null) return lease == _lease;
                if (lease != _lease)
                {
                    _factory.Destroy(view);
                    return false;
                }
                _views.Add(key, view);
            }
            return true;
        }

        private bool Destroy(EntityPresentationKey key, long lease)
        {
            if (lease != _lease) return false;
            if (!_views.TryGetValue(key, out IEntityView view)) return true;
            _views.Remove(key);
            _factory.Destroy(view);
            return lease == _lease;
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);

        private static float Interpolate(long previous, long current, float alpha) =>
            (float)((previous + ((current - previous) * (double)alpha)) /
                PositionScale);
    }
}
