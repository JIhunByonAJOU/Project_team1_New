#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace DRT.Editor
{
    [InitializeOnLoad]
    public static class DRTAutoSmokeRunner
    {
        private const double SmokeDurationSeconds = 75.0;
        private const string WaitingKey = "DRT_SMOKE_WAITING";
        private const string RunningKey = "DRT_SMOKE_RUNNING";
        private const string StartTimeKey = "DRT_SMOKE_START_TIME";
        private const string DurationKey = "DRT_SMOKE_DURATION";
        private const string TravelModeKey = "DRT_SMOKE_TRAVEL_MODE";
        private const string NextStopPolicyKey = "DRT_SMOKE_NEXT_STOP_POLICY";
        private const string LabelKey = "DRT_SMOKE_LABEL";
        private const string ConfigAppliedKey = "DRT_SMOKE_CONFIG_APPLIED";

        private static string SmokeFlagPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "DRT_AutoSmoke.flag");

        static DRTAutoSmokeRunner()
        {
            if (File.Exists(SmokeFlagPath) && EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += StopInterruptedSmokeRun;
            }

            EditorApplication.delayCall += TryStartFromFlag;
            EditorApplication.update += PollForSmokeFlag;
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
        }

        private static void StopInterruptedSmokeRun()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            FinishActiveEpisode("Smoke interrupted by script reload.");
            DeleteSmokeFlag();
            SessionState.SetBool(RunningKey, false);
            Debug.Log("[DRT_SMOKE] Stopping interrupted Play Mode smoke test.");
            EditorApplication.ExitPlaymode();
        }

        private static void PollForSmokeFlag()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            TryStartFromFlag();
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

            ReadSmokeConfig();
            DeleteSmokeFlag();
            ApplyEditorConfigBeforePlay();
            SessionState.SetBool(WaitingKey, true);
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(ConfigAppliedKey, false);
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
                SessionState.SetBool(ConfigAppliedKey, false);
                EditorApplication.update += UpdateSmoke;
                ApplyRuntimeConfigIfNeeded();
                Debug.Log($"[DRT_SMOKE] Entered Play Mode. label={SessionState.GetString(LabelKey, "-")}");
            }

            if (state == PlayModeStateChange.EnteredEditMode && File.Exists(SmokeFlagPath))
            {
                DeleteSmokeFlag();
                SessionState.SetBool(WaitingKey, false);
                SessionState.SetBool(RunningKey, false);
                Debug.Log("[DRT_SMOKE] Smoke flag removed.");
            }
        }

        private static void UpdateSmoke()
        {
            ApplyRuntimeConfigIfNeeded();

            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(StartTimeKey, 0f);
            float durationSeconds = Mathf.Max(1f, SessionState.GetFloat(DurationKey, (float)SmokeDurationSeconds));
            if (elapsed < durationSeconds)
            {
                return;
            }

            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= UpdateSmoke;
            FinishActiveEpisode("Smoke timeout.");
            Debug.Log("[DRT_SMOKE] Stopping Play Mode smoke test.");
            EditorApplication.ExitPlaymode();
        }

        private static void ReadSmokeConfig()
        {
            SessionState.SetFloat(DurationKey, (float)SmokeDurationSeconds);
            SessionState.SetString(TravelModeKey, string.Empty);
            SessionState.SetString(NextStopPolicyKey, string.Empty);
            SessionState.SetString(LabelKey, "-");

            string[] lines = File.ReadAllLines(SmokeFlagPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                if (key.Equals("durationSeconds", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(value, out float durationSeconds))
                {
                    SessionState.SetFloat(DurationKey, durationSeconds);
                }
                else if (key.Equals("travelExecutionMode", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(TravelModeKey, value);
                }
                else if (key.Equals("nextStopPolicy", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(NextStopPolicyKey, value);
                }
                else if (key.Equals("label", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(LabelKey, value);
                }
            }
        }

        private static void DeleteSmokeFlag()
        {
            if (File.Exists(SmokeFlagPath))
            {
                File.Delete(SmokeFlagPath);
            }
        }

        private static void ApplyEditorConfigBeforePlay()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            var busController = UnityEngine.Object.FindObjectOfType<DRTBusController>();
            var nextStopSelector = UnityEngine.Object.FindObjectOfType<DRTNextStopSelector>();
            if (busController == null || nextStopSelector == null)
            {
                return;
            }

            ApplyConfig(busController, nextStopSelector);
        }

        private static void ApplyRuntimeConfigIfNeeded()
        {
            if (!EditorApplication.isPlaying ||
                SessionState.GetBool(ConfigAppliedKey, false) ||
                !SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            var busController = UnityEngine.Object.FindObjectOfType<DRTBusController>();
            var nextStopSelector = UnityEngine.Object.FindObjectOfType<DRTNextStopSelector>();
            if (busController == null || nextStopSelector == null)
            {
                return;
            }

            ApplyConfig(busController, nextStopSelector);
            SessionState.SetBool(ConfigAppliedKey, true);
            Debug.Log(
                $"[DRT_SMOKE] Applied runtime config. label={SessionState.GetString(LabelKey, "-")}, " +
                $"mode={busController.TravelExecutionModeName}, policy={nextStopSelector.NextStopPolicyName}");
        }

        private static void ApplyConfig(
            DRTBusController busController,
            DRTNextStopSelector nextStopSelector)
        {
            string travelModeText = SessionState.GetString(TravelModeKey, string.Empty);
            if (Enum.TryParse(travelModeText, true, out DRTTravelExecutionMode travelMode))
            {
                SetPrivateField(busController, "travelExecutionMode", travelMode);
            }

            string policyText = SessionState.GetString(NextStopPolicyKey, string.Empty);
            if (Enum.TryParse(policyText, true, out DRTNextStopPolicy nextStopPolicy))
            {
                SetPrivateField(nextStopSelector, "nextStopPolicy", nextStopPolicy);
            }

            if (nextStopSelector.NextStopPolicy == DRTNextStopPolicy.ONNXInference)
            {
                TryTransferLegacyInferenceModel(busController, nextStopSelector);
            }

            nextStopSelector.Configure(busController);
        }

        private static void TryTransferLegacyInferenceModel(
            DRTBusController busController,
            DRTNextStopSelector nextStopSelector)
        {
            FieldInfo modelField = GetPrivateField(typeof(DRTBusController), "physicalDriveInferenceModel");
            FieldInfo deviceField = GetPrivateField(typeof(DRTBusController), "physicalDriveInferenceDevice");
            if (modelField == null || deviceField == null)
            {
                return;
            }

            var model = modelField.GetValue(busController) as Unity.Barracuda.NNModel;
            if (model == null)
            {
                return;
            }

            var device = (InferenceDevice)deviceField.GetValue(busController);
            nextStopSelector.ConfigureLegacyInferenceModel(model, device);
        }

        private static void FinishActiveEpisode(string reason)
        {
            var busController = UnityEngine.Object.FindObjectOfType<DRTBusController>();
            if (busController == null)
            {
                return;
            }

            MethodInfo finishEpisodeMethod = typeof(DRTBusController).GetMethod(
                "FinishEpisode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            finishEpisodeMethod?.Invoke(busController, new object[] { reason });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = GetPrivateField(target.GetType(), fieldName);
            field?.SetValue(target, value);
        }

        private static FieldInfo GetPrivateField(Type type, string fieldName)
        {
            return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }
}
#endif
