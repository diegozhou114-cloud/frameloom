using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public interface IEntityView
    {
        void SetPosition(float x, float y);
    }

    public interface IEntityViewFactory
    {
        IEntityView Create(EntityPresentationKey key, int archetypeId);
        void Destroy(IEntityView view);
    }
}
