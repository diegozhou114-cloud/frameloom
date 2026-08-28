using UnityEngine;

namespace ZWebGLFrameWork.Combat
{
    internal sealed class PooledUnityEntityView : MonoBehaviour, IEntityView
    {
        internal UnityEntityViewFactory Owner { get; private set; }
        private UnityEntityViewPoolSlot _pool;
        private bool _checkedOut;

        internal void Checkout(UnityEntityViewFactory owner,
            UnityEntityViewPoolSlot pool)
        {
            Owner = owner;
            _pool = pool;
            _checkedOut = true;
        }

        internal bool TryReturn(UnityEntityViewFactory owner,
            out UnityEntityViewPoolSlot pool)
        {
            pool = null;
            if (!ReferenceEquals(Owner, owner) || !_checkedOut) return false;
            _checkedOut = false;
            pool = _pool;
            return true;
        }

        internal void Invalidate(UnityEntityViewFactory owner)
        {
            if (!ReferenceEquals(Owner, owner)) return;
            _checkedOut = false;
            Owner = null;
            _pool = null;
        }

        public void SetPosition(float x, float y)
        {
            transform.position = new Vector3(x, 0f, y);
        }
    }
}
