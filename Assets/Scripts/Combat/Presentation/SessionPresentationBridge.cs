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

        public int ViewCount => _views.Count;

        public SessionPresentationBridge(IEntityViewFactory factory)
        {
            _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        }

        public void Apply(PresentationFrame frame)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            if (frame.IsFull)
                ApplyFull(frame.Entities);
            else
                ApplyDelta(frame);
        }

        public void Cleanup()
        {
            foreach (IEntityView view in _views.Values)
                _factory.Destroy(view);
            _views.Clear();
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
            view.SetPosition(state.PosXRaw / PositionScale,
                state.PosYRaw / PositionScale);
        }

        private void Destroy(EntityPresentationKey key)
        {
            if (!_views.TryGetValue(key, out IEntityView view)) return;
            _factory.Destroy(view);
            _views.Remove(key);
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);
    }
}
