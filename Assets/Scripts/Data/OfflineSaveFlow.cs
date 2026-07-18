using System;

namespace ZWebGLFrameWork.Data
{
    public interface IOfflineSaveGateway
    {
        bool Exists(int slot);
        bool Create(int slot);
        bool Load(int slot);
    }

    public sealed class DefaultOfflineSaveGateway : IOfflineSaveGateway
    {
        public bool Exists(int slot) => SaveManager.Exists(slot);
        public bool Create(int slot) => GameInitializer.CreateDefaultSave(slot);
        public bool Load(int slot) => SaveManager.LoadToDataManager(slot);
    }

    public sealed class OfflineSaveFlow
    {
        private readonly IOfflineSaveGateway _gateway;

        public OfflineSaveFlow(IOfflineSaveGateway gateway) =>
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

        public bool TryPrepareSlot(int slot)
        {
            if (!_gateway.Exists(slot) && !_gateway.Create(slot))
                return false;
            return _gateway.Load(slot);
        }
    }
}
