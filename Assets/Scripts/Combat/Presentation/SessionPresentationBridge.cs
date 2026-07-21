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

        public int ViewCount => _views.Count;

        public SessionPresentationBridge(IEntityViewFactory factory)
        {
            _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        }

        public void Apply(PresentationFrame frame)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            _interpolator.BeginFrame(frame);
            if (frame.IsFull)
                ApplyFull(frame.Entities);
            else
                ApplyDelta(frame);
            Sample(1f);
        }

        public void Sample(float alpha)
        {
            alpha = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
            foreach (var pair in _views)
            {
                if (!_interpolator.TryGet(pair.Key, out InterpolationPair states))
                    continue;
                pair.Value.SetPosition(Interpolate(states.Previous.PosXRaw,
                    states.Current.PosXRaw, alpha), Interpolate(
                    states.Previous.PosYRaw, states.Current.PosYRaw, alpha));
            }
        }

        public void Cleanup()
        {
            foreach (IEntityView view in _views.Values)
                _factory.Destroy(view);
            _views.Clear();
            _interpolator.Clear();
        }

        private void ApplyFull(IReadOnlyList<EntityPresentationState> states)
        {
            var present = new HashSet<EntityPresentationKey>();
            foreach (EntityPresentationState state in states)
            {
                EntityPresentationKey key = Key(state);
                present.Add(key);
                ApplyState(state);
            }

            var removed = new List<EntityPresentationKey>();
            foreach (EntityPresentationKey key in _views.Keys)
                if (!present.Contains(key)) removed.Add(key);
            foreach (EntityPresentationKey key in removed)
                Destroy(key);
        }

        private void ApplyDelta(PresentationFrame frame)
        {
            foreach (EntityPresentationKey key in frame.Destroyed)
                Destroy(key);
            foreach (EntityPresentationState state in frame.Created)
                ApplyState(state);
            foreach (EntityPresentationState state in frame.Updated)
                ApplyState(state);
        }

        private void ApplyState(EntityPresentationState state)
        {
            EntityPresentationKey key = Key(state);
            if (!_views.TryGetValue(key, out IEntityView view))
            {
                view = _factory.Create(key, state.ArchetypeId);
                if (view == null) return;
                _views.Add(key, view);
            }
        }

        private void Destroy(EntityPresentationKey key)
        {
            if (!_views.TryGetValue(key, out IEntityView view)) return;
            _factory.Destroy(view);
            _views.Remove(key);
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);

        private static float Interpolate(long previous, long current, float alpha) =>
            (float)((previous + ((current - previous) * (double)alpha)) /
                PositionScale);
    }
}
