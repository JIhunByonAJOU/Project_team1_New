#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DRT.Editor
{
    [InitializeOnLoad]
    public static class DRTAutoSmokeRunner
    {
        private const string SmokeFlagPath = "Temp/DRT_AutoSmoke.flag";
        private const double SmokeDurationSeconds = 75.0;
        private const string WaitingKey = "DRT_SMOKE_WAITING";
        private const string RunningKey = "DRT_SMOKE_RUNNING";
        private const string StartTimeKey = "DRT_SMOKE_START_TIME";

        static DRTAutoSmokeRunner()
        {
            EditorApplication.delayCall += TryStartFromFlag;
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
        }

        private static void TryStartFromFlag()
        {
            if (!File.Exists(SmokeFlagPath))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SessionState.SetBool(WaitingKey, true);
            SessionState.SetBool(RunningKey, false);
            Debug.Log("[DRT_SMOKE] Starting Play Mode smoke test.");
            EditorApplication.EnterPlaymode();
        }

        private static void PlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(WaitingKey, false))
            {
                SessionState.SetBool(WaitingKey, false);
                SessionState.SetBool(RunningKey, true);
                SessionState.SetFloat(StartTimeKey, (float)EditorApplication.timeSinceStartup);
                EditorApplication.update += UpdateSmoke;
                Debug.Log("[DRT_SMOKE] Entered Play Mode.");
            }

            if (state == PlayModeStateChange.EnteredEditMode && File.Exists(SmokeFlagPath))
            {
                File.Delete(SmokeFlagPath);
                SessionState.SetBool(WaitingKey, false);
                SessionState.SetBool(RunningKey, false);
                Debug.Log("[DRT_SMOKE] Smoke flag removed.");
            }
        }

        private static void UpdateSmoke()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(StartTimeKey, 0f);
            if (elapsed < SmokeDurationSeconds)
            {
                return;
            }

            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= UpdateSmoke;
            Debug.Log("[DRT_SMOKE] Stopping Play Mode smoke test.");
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
