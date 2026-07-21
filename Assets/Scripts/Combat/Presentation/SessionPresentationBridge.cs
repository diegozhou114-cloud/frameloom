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
        private readonly PresentationInterpolator _interpolator =
            new PresentationInterpolator();
        private long _lease;

        public int ViewCount => _views.Count;

        public SessionPresentationBridge(IEntityViewFactory factory)
        {
            _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        }

        public void Apply(PresentationFrame frame)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            long lease = ++_lease;
            _interpolator.BeginFrame(frame);
            if (frame.IsFull)
                ApplyFull(frame.Entities, lease);
            else
                ApplyDelta(frame, lease);
            if (lease == _lease) Sample(1f, lease);
        }

        public void Sample(float alpha)
        {
            Sample(alpha, _lease);
        }

        private void Sample(float alpha, long lease)
        {
            alpha = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
            var views = new List<KeyValuePair<EntityPresentationKey, IEntityView>>(_views);
            foreach (KeyValuePair<EntityPresentationKey, IEntityView> pair in views)
            {
                if (lease != _lease) return;
                if (!_interpolator.TryGet(pair.Key, out InterpolationPair states))
                    continue;
                pair.Value.SetPosition(Interpolate(states.Previous.PosXRaw,
                    states.Current.PosXRaw, alpha), Interpolate(
                    states.Previous.PosYRaw, states.Current.PosYRaw, alpha));
                if (lease != _lease) return;
            }
        }

        public void Cleanup()
        {
            ++_lease;
            var views = new List<IEntityView>(_views.Values);
            _views.Clear();
            _interpolator.Clear();
            foreach (IEntityView view in views)
                _factory.Destroy(view);
        }

        private void ApplyFull(IReadOnlyList<EntityPresentationState> states, long lease)
        {
            var present = new HashSet<EntityPresentationKey>();
            foreach (EntityPresentationState state in states)
            {
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
            foreach (EntityPresentationKey key in frame.Destroyed)
                if (!Destroy(key, lease)) return;
            foreach (EntityPresentationState state in frame.Created)
                if (!ApplyState(state, lease)) return;
            foreach (EntityPresentationState state in frame.Updated)
                if (!ApplyState(state, lease)) return;
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
