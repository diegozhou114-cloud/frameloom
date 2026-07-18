using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class RebindProbePanel : UIBase
    {
        public int BuildCount { get; private set; }
        public int BindCount { get; private set; }
        public int CallbackCount { get; private set; }
        public string BoundValue { get; private set; }

        protected override void OnOpen(object data)
        {
            BuildOnce(() =>
            {
                BuildCount++;
                new UnityEngine.GameObject("StaticChild").transform.SetParent(transform, false);
            });
            Bind(data);
            EventBus.Subscribe("RebindProbeEvent", OnProbeEvent);
        }

        protected override void OnRebind(object data) => Bind(data);

        protected override void OnClose()
        {
            EventBus.Unsubscribe("RebindProbeEvent", OnProbeEvent);
        }

        private void Bind(object data)
        {
            BindCount++;
            BoundValue = data as string;
        }

        private void OnProbeEvent() => CallbackCount++;
    }
}
