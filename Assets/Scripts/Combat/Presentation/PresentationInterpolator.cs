using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public readonly struct InterpolationPair
    {
        public EntityPresentationState Previous { get; }
        public EntityPresentationState Current { get; }

        public InterpolationPair(EntityPresentationState previous,
            EntityPresentationState current)
        {
            Previous = previous;
            Current = current;
        }
    }

    public sealed class PresentationInterpolator
    {
        private readonly Dictionary<EntityPresentationKey, InterpolationPair> _pairs =
            new Dictionary<EntityPresentationKey, InterpolationPair>();
        private long _revision = long.MinValue;

        public void BeginFrame(PresentationFrame frame)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            bool reset = frame.IsFull || frame.TimelineRevision != _revision;
            if (reset)
            {
                _pairs.Clear();
                _revision = frame.TimelineRevision;
                foreach (EntityPresentationState state in frame.Entities)
                    _pairs.Add(Key(state), new InterpolationPair(state, state));
                return;
            }

            foreach (EntityPresentationKey key in frame.Destroyed)
                _pairs.Remove(key);
            foreach (EntityPresentationState state in frame.Created)
                _pairs[Key(state)] = new InterpolationPair(state, state);
            foreach (EntityPresentationState state in frame.Updated)
            {
                EntityPresentationKey key = Key(state);
                if (_pairs.TryGetValue(key, out InterpolationPair previous))
                    _pairs[key] = new InterpolationPair(previous.Current, state);
                else
                    _pairs[key] = new InterpolationPair(state, state);
            }
        }

        public bool TryGet(EntityPresentationKey key, out InterpolationPair pair) =>
            _pairs.TryGetValue(key, out pair);

        public void Clear()
        {
            _pairs.Clear();
            _revision = long.MinValue;
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);
    }
}
