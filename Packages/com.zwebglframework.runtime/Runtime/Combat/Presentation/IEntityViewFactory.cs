using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// A presentation view borrowed from its creating <see cref="IEntityViewFactory"/>.
    /// The reference is valid only until the matching factory destroy call.
    /// </summary>
    public interface IEntityView
    {
        /// <summary>Updates the borrowed view's world position.</summary>
        void SetPosition(float x, float y);
    }

    /// <summary>Creates and destroys borrowed entity presentation views.</summary>
    public interface IEntityViewFactory
    {
        /// <summary>
        /// Returns a borrowed view. The caller must stop using the reference after passing it to
        /// <see cref="Destroy"/>. A later create call may return the same view instance.
        /// </summary>
        IEntityView Create(EntityPresentationKey key, int archetypeId);

        /// <summary>
        /// Ends the matching borrow. The caller must not use <paramref name="view"/> afterward.
        /// </summary>
        void Destroy(IEntityView view);
    }
}
