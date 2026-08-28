using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ZWebGLFrameWork.UI
{
    public sealed class SmokeReadinessReporter : MonoBehaviour
    {
        [Serializable] private sealed class Marker
        {
            public string @event;
            public string state;
            public float buttonX;
            public float buttonY;
            public int screenWidth;
            public int screenHeight;
        }

        private string _exitFile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWhenRequested()
        {
            if (!TryGetSmokeRequest(out string exitFile)) return;
            var instance = new GameObject("[SmokeReadinessReporter]")
                .AddComponent<SmokeReadinessReporter>();
            instance._exitFile = exitFile;
            DontDestroyOnLoad(instance.gameObject);
        }

        private static bool TryGetSmokeRequest(out string exitFile)
        {
            exitFile = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            return Application.absoluteURL.IndexOf("zweb-smoke=1", StringComparison.Ordinal) >= 0;
#else
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] != "-zwebSmokeExitFile") continue;
                exitFile = Path.GetFullPath(arguments[index + 1]);
                return true;
            }
            return false;
#endif
        }

        private IEnumerator Start()
        {
            Button button = null;
            while (button == null)
            {
                button = FindObjectsOfType<Button>(true)
                    .Where(candidate => candidate.isActiveAndEnabled && candidate.interactable &&
                        candidate.gameObject.activeInHierarchy)
                    .OrderBy(candidate => HierarchyPath(candidate.transform), StringComparer.Ordinal)
                    .FirstOrDefault();
                if (button == null) yield return null;
            }
            yield return null;
            var rect = (RectTransform)button.transform;
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[2]) * 0.5f;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, center);
            button.onClick.AddListener(ReportButtonClicked);
            Emit(new Marker { @event = "app-ready", state = SceneManager.GetActiveScene().name,
                buttonX = screen.x, buttonY = screen.y, screenWidth = Screen.width,
                screenHeight = Screen.height });
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(_exitFile) || !File.Exists(_exitFile)) return;
            File.Delete(_exitFile);
            Emit(new Marker { @event = "app-exit", state = SceneManager.GetActiveScene().name });
            Application.Quit(0);
            _exitFile = null;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        private static void ReportButtonClicked() =>
            Emit(new Marker { @event = "button-clicked", state = SceneManager.GetActiveScene().name });

        private static void Emit(Marker marker) =>
            Debug.Log("[ZWEB_SMOKE] " + JsonUtility.ToJson(marker));
    }
}
