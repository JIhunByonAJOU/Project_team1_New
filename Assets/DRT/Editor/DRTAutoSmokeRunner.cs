#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Unity.Barracuda;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        private const string PolicyKey = "DRT_SMOKE_POLICY";
        private const string OnnxModelPathKey = "DRT_SMOKE_ONNX_MODEL_PATH";
        private const string ScenePathKey = "DRT_SMOKE_SCENE_PATH";
        private const string LabelKey = "DRT_SMOKE_LABEL";
        private const string ConfigAppliedKey = "DRT_SMOKE_CONFIG_APPLIED";
        private const string TargetCompletedRunsKey = "DRT_SMOKE_TARGET_COMPLETED_RUNS";
        private const string StartWallTimeTicksKey = "DRT_SMOKE_START_WALL_TIME_TICKS";
        private const string LastExportCountKey = "DRT_SMOKE_LAST_EXPORT_COUNT";
        private const string ExitEditorOnStopKey = "DRT_SMOKE_EXIT_EDITOR_ON_STOP";
        private const string CommandLineConsumedKey = "DRT_SMOKE_COMMAND_LINE_CONSUMED";

        private static string SmokeFlagPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "DRT_AutoSmoke.flag");

        static DRTAutoSmokeRunner()
        {
            if (File.Exists(SmokeFlagPath) && EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += StopInterruptedSmokeRun;
            }

            EditorApplication.delayCall += TryStartFromFlag;
            EditorApplication.delayCall += TryStartFromCommandLine;
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

            ReadSmokeConfig(SmokeFlagPath);
            DeleteSmokeFlag();
            StartConfiguredSmokeRun();
        }

        private static void TryStartFromCommandLine()
        {
            if (SessionState.GetBool(CommandLineConsumedKey, false))
            {
                return;
            }

            string configPath = GetCommandLineValue("-drtSmokeConfig");
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return;
            }

            SessionState.SetBool(CommandLineConsumedKey, true);

            if (!File.Exists(configPath))
            {
                Debug.LogError($"[DRT_SMOKE] Command-line smoke config was not found. path={configPath}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                }
                return;
            }

            ReadSmokeConfig(configPath);
            StartConfiguredSmokeRun();
        }

        private static void StartConfiguredSmokeRun()
        {
            OpenConfiguredSceneIfNeeded();
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
                SessionState.SetString(StartWallTimeTicksKey, DateTime.Now.Ticks.ToString());
                SessionState.SetInt(LastExportCountKey, -1);
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

            if (state == PlayModeStateChange.EnteredEditMode &&
                SessionState.GetBool(ExitEditorOnStopKey, false) &&
                !SessionState.GetBool(WaitingKey, false) &&
                !SessionState.GetBool(RunningKey, false))
            {
                Debug.Log("[DRT_SMOKE] Exiting Unity editor after smoke run.");
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
        }

        private static void UpdateSmoke()
        {
            ApplyRuntimeConfigIfNeeded();

            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            if (TargetCompletedRunsReached())
            {
                StopSmokeRun("Target completed run count reached.", false);
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(StartTimeKey, 0f);
            float durationSeconds = Mathf.Max(1f, SessionState.GetFloat(DurationKey, (float)SmokeDurationSeconds));
            if (elapsed < durationSeconds)
            {
                return;
            }

            StopSmokeRun("Smoke timeout.", true);
        }

        private static void ReadSmokeConfig(string configPath)
        {
            SessionState.SetFloat(DurationKey, (float)SmokeDurationSeconds);
            SessionState.SetString(TravelModeKey, string.Empty);
            SessionState.SetString(PolicyKey, string.Empty);
            SessionState.SetString(OnnxModelPathKey, string.Empty);
            SessionState.SetString(ScenePathKey, string.Empty);
            SessionState.SetString(LabelKey, "-");
            SessionState.SetInt(TargetCompletedRunsKey, 0);
            SessionState.SetBool(ExitEditorOnStopKey, false);

            string[] lines = File.ReadAllLines(configPath);
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
                    SessionState.SetString(PolicyKey, value);
                }
                else if (key.Equals("onnxModelPath", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(OnnxModelPathKey, value);
                }
                else if (key.Equals("scenePath", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(ScenePathKey, value);
                }
                else if (key.Equals("label", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(LabelKey, value);
                }
                else if (key.Equals("targetCompletedRuns", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(value, out int targetCompletedRuns))
                {
                    SessionState.SetInt(TargetCompletedRunsKey, Mathf.Max(0, targetCompletedRuns));
                }
                else if (key.Equals("exitEditorOnStop", StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetBool(
                        ExitEditorOnStopKey,
                        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        private static string GetCommandLineValue(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return string.Empty;
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

            string policyText = SessionState.GetString(PolicyKey, string.Empty);
            if (Enum.TryParse(policyText, true, out DRTNextStopPolicy policy))
            {
                SetPrivateField(nextStopSelector, "nextStopPolicy", policy);
            }

            ApplyConfiguredOnnxModel(nextStopSelector);

            nextStopSelector.Configure(busController);
        }

        private static void OpenConfiguredSceneIfNeeded()
        {
            string scenePath = SessionState.GetString(ScenePathKey, string.Empty);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            string normalizedScenePath = NormalizeProjectAssetPath(scenePath);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (string.IsNullOrWhiteSpace(normalizedScenePath) ||
                !File.Exists(Path.Combine(projectRoot, normalizedScenePath)))
            {
                Debug.LogError($"[DRT_SMOKE] Scene path was not found. path={scenePath}");
                return;
            }

            EditorSceneManager.OpenScene(normalizedScenePath, OpenSceneMode.Single);
        }

        private static void ApplyConfiguredOnnxModel(DRTNextStopSelector nextStopSelector)
        {
            string modelPath = SessionState.GetString(OnnxModelPathKey, string.Empty);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return;
            }

            string assetPath = NormalizeProjectAssetPath(modelPath);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                Debug.LogError($"[DRT_SMOKE] ONNX model path is outside this project. path={modelPath}");
                return;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<NNModel>(assetPath);
            if (model == null)
            {
                Debug.LogError($"[DRT_SMOKE] Failed to load ONNX NNModel at {assetPath}");
                return;
            }

            SetPrivateField(nextStopSelector, "onnxInferenceModel", model);
        }

        private static string NormalizeProjectAssetPath(string modelPath)
        {
            string normalized = modelPath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (Path.IsPathRooted(modelPath))
            {
                string absolutePath = Path.GetFullPath(modelPath).Replace('\\', '/');
                if (!absolutePath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return absolutePath.Substring(projectRoot.Length + 1);
            }

            string candidateAbsolutePath = Path.GetFullPath(Path.Combine(projectRoot, normalized)).Replace('\\', '/');
            if (!candidateAbsolutePath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return candidateAbsolutePath.Substring(projectRoot.Length + 1);
        }

        private static void StopSmokeRun(string reason, bool finishActiveEpisode)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= UpdateSmoke;
            if (finishActiveEpisode)
            {
                FinishActiveEpisode(reason);
            }

            Debug.Log($"[DRT_SMOKE] Stopping Play Mode smoke test. reason={reason}");
            EditorApplication.ExitPlaymode();
        }

        private static bool TargetCompletedRunsReached()
        {
            int targetCompletedRuns = SessionState.GetInt(TargetCompletedRunsKey, 0);
            if (targetCompletedRuns <= 0)
            {
                return false;
            }

            int completedRuns = CountCompletedRunExportsSinceStart();
            int lastExportCount = SessionState.GetInt(LastExportCountKey, -1);
            if (completedRuns != lastExportCount)
            {
                SessionState.SetInt(LastExportCountKey, completedRuns);
                Debug.Log(
                    $"[DRT_SMOKE] Completed export progress. " +
                    $"label={SessionState.GetString(LabelKey, "-")}, count={completedRuns}/{targetCompletedRuns}");
            }

            return completedRuns >= targetCompletedRuns;
        }

        private static int CountCompletedRunExportsSinceStart()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string exportDirectory = Path.Combine(projectRoot, "DRT_Episode_Exports");
            if (!Directory.Exists(exportDirectory))
            {
                return 0;
            }

            string policyToken = GetPolicyExportToken(SessionState.GetString(PolicyKey, string.Empty));
            string travelToken = GetTravelModeExportToken(SessionState.GetString(TravelModeKey, string.Empty));
            string pattern = $"drt_{travelToken}_{policyToken}_*_summary.csv";
            DateTime startTime = GetRunStartWallTime().AddSeconds(-1);
            int count = 0;

            foreach (string summaryPath in Directory.GetFiles(exportDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTime(summaryPath) < startTime)
                    {
                        continue;
                    }

                    string text = File.ReadAllText(summaryPath);
                    if (text.Contains("completed_all_requests,1"))
                    {
                        count++;
                    }
                }
                catch (IOException)
                {
                    // The exporter may still be writing this CSV; count it on the next poll.
                }
            }

            return count;
        }

        private static DateTime GetRunStartWallTime()
        {
            string ticksText = SessionState.GetString(StartWallTimeTicksKey, string.Empty);
            if (long.TryParse(ticksText, out long ticks))
            {
                return new DateTime(ticks);
            }

            return DateTime.Now;
        }

        private static string GetPolicyExportToken(string policyText)
        {
            if (Enum.TryParse(policyText, true, out DRTNextStopPolicy policy))
            {
                switch (policy)
                {
                    case DRTNextStopPolicy.ONNXInference:
                        return "onnx_inference";
                    case DRTNextStopPolicy.VanillaSequential:
                        return "vanilla_sequential";
                }
            }

            return "ml_agents_training";
        }

        private static string GetTravelModeExportToken(string travelModeText)
        {
            if (Enum.TryParse(travelModeText, true, out DRTTravelExecutionMode travelMode) &&
                travelMode == DRTTravelExecutionMode.PhysicalDrive)
            {
                return "physical_drive";
            }

            return "matrix_teleport";
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
