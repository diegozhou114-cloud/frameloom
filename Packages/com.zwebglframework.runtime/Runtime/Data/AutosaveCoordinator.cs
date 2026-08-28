using System;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public sealed class AutosaveCoordinator
    {
        private readonly Func<PlayerSaveData> _capture;
        private readonly Func<bool> _save;
        private string _savedFingerprint;

        public AutosaveCoordinator(Func<PlayerSaveData> capture, Func<bool> save)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public void InitializeBaseline() =>
            _savedFingerprint = Fingerprint(_capture());

        public bool TrySaveIfChanged()
        {
            string current = Fingerprint(_capture());
            if (current == _savedFingerprint) return false;
            if (!_save()) return false;
            _savedFingerprint = current;
            return true;
        }

        public bool FlushPending() => TrySaveIfChanged();

        private static string Fingerprint(PlayerSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string saveTime = data.SaveTime;
            try
            {
                data.SaveTime = string.Empty;
                return JsonUtility.ToJson(data, false);
            }
            finally
            {
                data.SaveTime = saveTime;
            }
        }
    }
}
