using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public sealed class UnityEntityViewFactory : IEntityViewFactory
    {
        private readonly ViewConfig _viewConfig;

        public UnityEntityViewFactory(ViewConfig viewConfig)
        {
            _viewConfig = viewConfig;
        }

        public IEntityView Create(EntityPresentationKey key, int archetypeId)
        {
            GameObject prefab = _viewConfig?.Entries == null
                ? null : _viewConfig.GetPrefab(archetypeId);
            GameObject instance = prefab != null
                ? Object.Instantiate(prefab)
                // TODO(PRODUCTION-CONTENT): Replace primitive fallback with configured battle prefabs.
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = $"SimEntity_{key.EntityId}_{key.Generation}";
            return new UnityEntityView(instance);
        }

        public void Destroy(IEntityView view)
        {
            if (!(view is UnityEntityView unityView) || unityView.GameObject == null) return;
            Object.Destroy(unityView.GameObject);
        }

        private sealed class UnityEntityView : IEntityView
        {
            public GameObject GameObject { get; }

            public UnityEntityView(GameObject gameObject)
            {
                GameObject = gameObject;
            }

            public void SetPosition(float x, float y)
            {
                GameObject.transform.position = new Vector3(x, 0f, y);
            }
        }
    }
}
