using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed partial class SimBattleSession
    {
        private SimTickOutputBuffer _boundOutput;

        /// <inheritdoc/>
        public void TickInto(SimTickOutputBuffer destination)
        {
            if (destination == null)
            {
                InvalidateBorrowedOutput();
                throw new ArgumentNullException(nameof(destination));
            }
            if (_disposed)
            {
                if (destination.Owner != null && !ReferenceEquals(destination.Owner, this))
                    throw new InvalidOperationException(
                        "The output buffer is bound to another session.");
                destination.Clear();
                destination.Publish(TickCode.Disposed, _lifecycle, _phase,
                    _completedTick, _timelineRevision, _checksum, _result);
                return;
            }
            EnsureOutputBinding(destination);
            destination.Clear();
            try
            {
                if (_lifecycle != SessionLifecycle.Running)
                {
                    if (destination.Includes(SimTickOutputFields.Presentation))
                        _presentationFrames.CopyCurrentTo(destination.Presentation);
                    destination.Publish(TickCode.NotRunning,
                        _lifecycle, _phase, _completedTick, _timelineRevision,
                        _checksum, _result);
                    return;
                }
                AdvanceTick(destination);
            }
            catch
            {
                destination.Clear();
                throw;
            }
        }

        private TickResult TickOwned()
        {
            InvalidateBorrowedOutput();
            if (_disposed) return CreateTickResult(TickCode.Disposed);
            if (_lifecycle != SessionLifecycle.Running)
                return CreateTickResult(TickCode.NotRunning);
            return AdvanceTick(null);
        }

        internal void DetachOutputBuffer(SimTickOutputBuffer destination)
        {
            if (ReferenceEquals(_boundOutput, destination)) _boundOutput = null;
        }

        private void EnsureOutputBinding(SimTickOutputBuffer destination)
        {
            if (_boundOutput != null && !ReferenceEquals(_boundOutput, destination))
                throw new InvalidOperationException(
                    "A session cannot alternate output buffers while one remains bound.");
            if (destination.Owner != null && !ReferenceEquals(destination.Owner, this))
                throw new InvalidOperationException("The output buffer is bound to another session.");
            if (_boundOutput != null) return;
            _boundOutput = destination;
            destination.Bind(this);
        }

        private void InvalidateBorrowedOutput() => _boundOutput?.Clear();

        private void ReleaseBorrowedOutput()
        {
            SimTickOutputBuffer output = _boundOutput;
            _boundOutput = null;
            output?.Release(this);
        }
    }
}
