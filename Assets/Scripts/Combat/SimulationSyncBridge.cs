using System;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    [Obsolete("Use SessionPresentationBridge.")]
    public sealed class SimulationSyncBridge
    {
        private readonly SessionPresentationBridge _delegate;

        public int ViewCount => _delegate.ViewCount;

        public SimulationSyncBridge(SessionPresentationBridge presentation)
        {
            _delegate = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

        public void Apply(PresentationFrame frame) => _delegate.Apply(frame);
        public void Sample(float alpha) => _delegate.Sample(alpha);
        public void Cleanup() => _delegate.Cleanup();
    }
}
