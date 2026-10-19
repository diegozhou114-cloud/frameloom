using System;
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.MiniSiege
{
    public sealed partial class MiniSiegeGame
    {
        private StreamWriter _evidence;
        private string _evidencePath;
        private bool _screenshotTaken;

        private void BeginEvidence()
        {
            _evidence?.Dispose();
            _evidencePath = Argument("-siegeEvidence", "");
            if (string.IsNullOrEmpty(_evidencePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_evidencePath)));
            _evidence = new StreamWriter(_evidencePath) { AutoFlush = true };
            _evidence.WriteLine("player,tick,checksum,blueHP,redHP,winner");
        }

        private void RecordEvidence(int tick, string checksum)
        {
            _evidence?.WriteLine(_player + "," + tick + "," + checksum + "," +
                _simulation.BaseHealth(0) + "," + _simulation.BaseHealth(1) + "," + _simulation.Winner);
            if (tick % 100 == 0)
                Debug.Log("[MiniSiege] VERIFIED player=" + _player + " tick=" + tick + " checksum=" + checksum);
        }

        private void FinishEvidence()
        {
            _evidence?.Flush();
            Debug.Log("[MiniSiege] MATCH COMPLETE winner=" + _simulation.Winner +
                " verifiedFrames=" + _verifiedFrames + " checksum=" + _simulation.Checksum.ToString("X16"));
            if (HasArgument("-siegeQuitAfterMatch")) Invoke(nameof(QuitDemo), 1f);
        }

        private void CaptureEvidence()
        {
            if (_screenshotTaken || _simulation.Tick < 300) return;
            string path = Argument("-siegeScreenshot", "");
            if (string.IsNullOrEmpty(path)) return;
            _screenshotTaken = true;
            ScreenCapture.CaptureScreenshot(path);
        }

        private void QuitDemo() => Application.Quit();
    }
}
