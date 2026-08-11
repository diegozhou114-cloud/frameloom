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
            BeginFrame(frame.Entities, frame.Created, frame.Updated, frame.Destroyed,
                frame.TimelineRevision, reset);
        }

        internal void BeginFrame(SimPresentationOutputBuffer frame, long revision)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            BeginFrame(frame.Entities, frame.Created, frame.Updated, frame.Destroyed,
                revision, revision != _revision);
        }

        private void BeginFrame(IReadOnlyList<EntityPresentationState> entities,
            IReadOnlyList<EntityPresentationState> created,
            IReadOnlyList<EntityPresentationState> updated,
            IReadOnlyList<EntityPresentationKey> destroyed, long revision, bool reset)
        {
            if (reset)
            {
                _pairs.Clear();
                _revision = revision;
                for (int index = 0; index < entities.Count; index++)
                {
                    EntityPresentationState state = entities[index];
                    _pairs.Add(Key(state), new InterpolationPair(state, state));
                }
                return;
            }

            for (int index = 0; index < destroyed.Count; index++)
                _pairs.Remove(destroyed[index]);
            for (int index = 0; index < created.Count; index++)
            {
                EntityPresentationState state = created[index];
                _pairs[Key(state)] = new InterpolationPair(state, state);
            }
            for (int index = 0; index < updated.Count; index++)
            {
                EntityPresentationState state = updated[index];
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
