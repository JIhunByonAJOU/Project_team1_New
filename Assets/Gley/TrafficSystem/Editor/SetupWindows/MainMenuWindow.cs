using Gley.Common.Editor;
using Gley.UrbanSystem.Editor;
using System.IO;
using UnityEditor;
using UnityEngine;

#if GLEY_TRAFFIC_SYSTEM
using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;
#else
using VehicleTypes = Gley.TrafficSystem.VehicleTypes;
#endif

namespace Gley.TrafficSystem.Editor
{
    public class MainMenuWindow : SetupWindowBase
    {
        private const string SAVING = "GleyTrafficSaving";
        private const string STEP = "GleyTrafficStep";
#if GLEY_TRAFFIC_SYSTEM
        private readonly int _scrollAdjustment = 140;
#else
        private readonly int _scrollAdjustment = 90;
#endif

        private int _step;
        private bool _saving;

        public override SetupWindowBase Initialize(WindowProperties windowProperties, SettingsWindowBase window)
        {
            _saving = EditorPrefs.GetBool(SAVING);
            _step = EditorPrefs.GetInt(STEP);

            return base.Initialize(windowProperties, window);
        }


        protected override void ScrollPart(float width, float height)
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, false, GUILayout.Width(width - SCROLL_SPACE), GUILayout.Height(height - _scrollAdjustment));

#if GLEY_TRAFFIC_SYSTEM
            InitializedScrollButtons();
#else
            NotInitializedScrollButtons();
#endif

            EditorGUILayout.EndScrollView();
            base.ScrollPart(width, height);
        }

        private void NotInitializedScrollButtons()
        {
            GUILayout.Label("Installation Instructions");
            GUILayout.Label("Step 1: Import Packages -> This will import the latest version of burst compiler", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            if (GUILayout.Button("Import Required Packages"))
            {
                _window.SetActiveWindow(typeof(ImportPackagesWindow), true);
            }
            EditorGUILayout.Space();

            GUILayout.Label("Step 2: Enable Traffic System -> This will enable the Traffic System scripts:", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            if (GUILayout.Button("Enable Traffic System"))
            {
                CreateAgentTypesFile();
                Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_TRAFFIC_SYSTEM, false);
            }
        }


        /// <summary>
        /// Moves a file and its .meta file to a new location.
        /// </summary>
        private void MoveFileToDestination(string source, string destination)
        {
            FileUtil.MoveFileOrDirectory(source, destination);
            FileUtil.MoveFileOrDirectory(source + ".meta", destination + ".meta");
            AssetDatabase.Refresh();
        }


        /// <summary>
        /// Creates a new AgentTypes file.
        /// </summary>
        private void CreateAgentTypesFile()
        {
            AsmdefUtils.GenerateAsmdefFile($"Assets/{TrafficSystemConstants.agentTypesPath}", TrafficSystemConstants.trafficNamespaceUser);

            if (!File.Exists($"{Application.dataPath}{TrafficSystemConstants.agentTypesPath}/VehicleTypes.cs"))
            {
                FileCreator.CreateAgentTypesFile<VehicleTypes>(null, TrafficSystemConstants.GLEY_TRAFFIC_SYSTEM, TrafficSystemConstants.trafficNamespaceUser, TrafficSystemConstants.agentTypesPath);
            }
        }

        private void InitializedScrollButtons()
        {
            EditorGUILayout.Space();

            if (GUILayout.Button("Import Required Packages"))
            {
                _window.SetActiveWindow(typeof(ImportPackagesWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Scene Setup"))
            {
                _window.SetActiveWindow(typeof(SceneSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Road Setup"))
            {
                _window.SetActiveWindow(typeof(RoadSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Intersection Setup"))
            {
                _window.SetActiveWindow(typeof(IntersectionSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Waypoint Setup"))
            {
                _window.SetActiveWindow(typeof(WaypointSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Speed Routes Setup"))
            {
                _window.SetActiveWindow(typeof(SpeedRoutesSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Vehicle Routes Setup"))
            {
                _window.SetActiveWindow(typeof(VehicleRoutesSetupWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Waypoint Priority Setup"))
            {
                _window.SetActiveWindow(typeof(WaypointPriorityWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Path Finding"))
            {
                _window.SetActiveWindow(typeof(PathFindingWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("External Tools"))
            {
                _window.SetActiveWindow(typeof(ExternalToolsWindow), true);
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Switch driving direction (Beta)"))
            {
                SwitchWaypointDirection.SwitchAll();
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Debug"))
            {
                _window.SetActiveWindow(typeof(DebugWindow), true);
            }
            EditorGUILayout.Space();
        }


        private void InitializedBottomButtons()
        {
            if (GUILayout.Button("Apply Settings"))
            {
                if (FileCreator.LoadOrCreateLayers<LayerSetup>(TrafficSystemConstants.layerPath).edited == false)
                {
                    Debug.LogWarning("Layers are not configured. Go to Tools -> Gley -> Traffic System->Scene Setup -> Layer Setup");
                }

                if (FindFirstObjectByType<VehicleComponent>() != null)
                {
                    Debug.LogError("Failed: Please remove VehicleComponent from the following objects:");
                    var objects = FindObjectsByType<VehicleComponent>(FindObjectsSortMode.None);
                    foreach (var obj in objects)
                    {
                        Debug.Log(obj.name, obj);
                    }
                    return;
                }

                _step = 0;
                SaveSettings();
            }
            EditorGUILayout.Space();

            if (GUILayout.Button("Disable Traffic System"))
            {
                Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_TRAFFIC_SYSTEM, true);
                Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_CIDY_TRAFFIC, true);
                Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_EASYROADS_TRAFFIC, true);
                Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_ROADCONSTRUCTOR_TRAFFIC, true);
            }

        }


        protected override void BottomPart()
        {
#if GLEY_TRAFFIC_SYSTEM
            InitializedBottomButtons();
#endif

            if (GUILayout.Button("Documentation"))
            {
                Application.OpenURL("https://gley.gitbook.io/mobile-traffic-system-v3/quick-start");
            }

            base.BottomPart();
        }


        private void SaveSettings()
        {
            Debug.Log($"Saving {_step + 1}/4");
            switch (_step)
            {
                case 0:
                    CreateAgentTypesFile();
                    _saving = true;
                    EditorPrefs.SetBool(SAVING, _saving);
                    _step++;
                    EditorPrefs.SetInt(STEP, _step);
                    break;
                case 1:
                    Common.Editor.PreprocessorDirective.AddToCurrent(TrafficSystemConstants.GLEY_TRAFFIC_SYSTEM, false);
                    _saving = true;
                    EditorPrefs.SetBool(SAVING, _saving);
                    _step++;
                    EditorPrefs.SetInt(STEP, _step);
                    break;
                case 2:
                    ApplyTrafficSettings();
                    _saving = true;
                    EditorPrefs.SetBool(SAVING, _saving);
                    _step++;
                    EditorPrefs.SetInt(STEP, _step);
                    break;
                default:
                    Debug.Log("Save Done");
                    break;
            }
        }


        public override void InspectorUpdate()
        {
            if (_saving)
            {
                if (EditorApplication.isCompiling == false)
                {
                    _saving = false;
                    EditorPrefs.SetBool(SAVING, false);
                    SaveSettings();
                }
            }
        }


        private void ApplyTrafficSettings()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var gridData = new GridEditorData();
            var gridCreator = new GridCreator(gridData);
            LayerMask groundLayers = 0;
            var trafficLayers = FileCreator.LoadOrCreateLayers<LayerSetup>(TrafficSystemConstants.layerPath);
            if (trafficLayers != null)
            {
                groundLayers |= trafficLayers.roadLayers;
            }
            var pedestrianEditorBridge = PedestrianEditorBridgeRegistry.Bridge;

#if GLEY_PEDESTRIAN_SYSTEM
            if (pedestrianEditorBridge != null)
            {
                pedestrianEditorBridge.AppendGroundLayers(ref groundLayers);
            }
#endif

            gridCreator.GenerateGrid(gridData.GetGridCellSize(), groundLayers);

            var trafficWaypointsConverter = new TrafficWaypointsConverter();
            trafficWaypointsConverter.ConvertWaypoints();

            IWaypointsConverter pedestrianWaypointsConverter = null;
#if GLEY_PEDESTRIAN_SYSTEM
            if (pedestrianEditorBridge != null)
            {
                pedestrianWaypointsConverter = pedestrianEditorBridge.GetWaypointsConverter();
                pedestrianEditorBridge.ApplyWaypoints(pedestrianWaypointsConverter);
            }
#endif

            var intersectionConverter = new IntersectionConverter(trafficWaypointsConverter, pedestrianWaypointsConverter);
            intersectionConverter.ConvertAllIntersections();
            stopwatch.Stop();
            Debug.Log($"Apply settings done in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
    }
}