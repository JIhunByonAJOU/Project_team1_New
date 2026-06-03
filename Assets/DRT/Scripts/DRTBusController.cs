using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;

namespace DRT
{
    [AddComponentMenu("DRT/DRT Bus Controller")]
    public class DRTBusController : MonoBehaviour
    {
        [HideInInspector, SerializeField] private Transform busStopsRoot;
        [HideInInspector, SerializeField] private DRTPassengerManager passengerManager;
        [HideInInspector, SerializeField] private DRTDemandGenerator demandGenerator;
        [HideInInspector, SerializeField] private DRTNextStopSelector nextStopSelector;
        [HideInInspector, SerializeField] private Transform controlledPlayerVehicle;

        [Header("Vehicle")]
        [SerializeField, InspectorName("Vehicle")] private int vehicleIndex = 0;
        [SerializeField, InspectorName("Start Stop")] private int startStopId = 1;
        [SerializeField, InspectorName("Vehicle Type")] private VehicleTypes controlledVehicleType = VehicleTypes.Car;
        [SerializeField, InspectorName("Dwell (s)")] private float dwellSeconds = 5f;
        [SerializeField, InspectorName("Arrival Dist")] private float arrivalDistanceMeters = 5f;
        [HideInInspector, SerializeField] private float stopWaypointSnapDistanceMeters = 5f;
        [HideInInspector, SerializeField] private float arrivalWaitTimeoutSeconds = 12f;
        [SerializeField, InspectorName("Speed x")] private float controlledVehicleSpeedMultiplier = 1.5f;
        [Tooltip("Only used when Physical Drive uses the Gley vehicle driver. 1.0 keeps Gley speed unchanged; 1.12 means roughly +12%.")]
        [HideInInspector, SerializeField] private float gleyControlledVehicleSpeedMultiplier = 1.12f;
        [HideInInspector, SerializeField] private float playerWaypointReachDistanceMeters = 6f;
        [SerializeField, InspectorName("Follow Bus")] private bool autoFollowControlledVehicle = true;
        [SerializeField, InspectorName("Gley Driver")] private bool useGleyVehicleControlInPhysicalDrive = true;

        [Header("Travel Execution")]
        [Tooltip("Controls how the selected next stop is reached. Matrix Teleport uses the travel-time matrix; Physical Drive uses the configured vehicle driver.")]
        [InspectorName("Mode")]
        [SerializeField] private DRTTravelExecutionMode travelExecutionMode = DRTTravelExecutionMode.MatrixTeleport;
        [SerializeField, InspectorName("Matrix Resource")] private string travelTimeMatrixResourceName = "drt_stop_travel_time_matrix";
        [Tooltip("Only used to estimate matrix-mode distance in exports. Matrix travel time is read directly from the CSV.")]
        [HideInInspector, SerializeField] private float matrixNominalSpeedMetersPerSecond = 15f;
        [Tooltip("Suppresses routine Unity logs while Matrix Teleport is active. Applies to training, ONNX inference, and vanilla policies.")]
        [HideInInspector, SerializeField] private bool suppressUnityLogsDuringMatrixTraining = true;

        [Header("Episode CSV Export")]
        [FormerlySerializedAs("exportInferenceCsvOnEpisodeEnd")]
        [SerializeField, InspectorName("Export CSV")] private bool exportEpisodeCsvOnEpisodeEnd = true;
        [HideInInspector, SerializeField] private float vehicleTraceSampleIntervalSeconds = 1f;

        [HideInInspector, SerializeField] private bool logMatrixTravel = true;
        [HideInInspector, SerializeField] private bool logEpisodeSummary = true;
        [HideInInspector, SerializeField] private bool logReward = true;
        [HideInInspector, SerializeField] private bool logDecision = true;
        [HideInInspector, SerializeField] private bool logPolicyAction = true;
        [HideInInspector, SerializeField] private bool logSpawnedRequests;
        [HideInInspector, SerializeField] private bool logStopProcessing = true;
        [HideInInspector, SerializeField] private bool logMovementDiagnostics;
        [HideInInspector, SerializeField] private float movementDiagnosticsIntervalSeconds = 2f;

        [Header("Path Visualization")]
        [SerializeField, InspectorName("Show Path")] private bool showAssignedPath = true;
        [HideInInspector, SerializeField] private bool showAssignedPathInGame = true;
        [HideInInspector, SerializeField] private bool showAssignedPathGizmos = true;
        [HideInInspector, SerializeField] private Color assignedPathColor = new Color(0f, 0.85f, 1f, 0.9f);
        [HideInInspector, SerializeField] private Color assignedPathWaypointColor = new Color(1f, 0.85f, 0f, 0.95f);
        [HideInInspector, SerializeField] private float assignedPathLineWidth = 0.7f;
        [HideInInspector, SerializeField] private float assignedPathWaypointRadius = 1.2f;
        [HideInInspector, SerializeField] private float assignedPathVerticalOffset = 0.35f;

        [Header("Background Traffic")]
        [SerializeField, InspectorName("Enable")] private bool backgroundTrafficEnabledOnStart = true;
        [SerializeField, InspectorName("Density")] private int enabledTrafficDensity = 30;

        [Header("Episode")]
        [SerializeField, InspectorName("Length (s)")] private float episodeLengthSeconds = 3000f;
        [SerializeField, InspectorName("Sim Speed")] private float simulationSecondsPerRealSecond = 1f;
        [SerializeField, InspectorName("Stop When Done")] private bool stopWhenAllRequestsCompleted = true;

        [HideInInspector, SerializeField] private bool failEpisodeOnVehicleFault = true;
        [HideInInspector, SerializeField] private float failurePenalty = -1f;
        [HideInInspector, SerializeField] private float noMovementTimeoutRealSeconds = 20f;
        [Tooltip("A longer timeout used only when Gley reports a normal stop caused by traffic lights, give-way, or obstacles. Set 0 to never fail on traffic waits.")]
        [HideInInspector, SerializeField] private float trafficBlockTimeoutRealSeconds = 180f;
        [HideInInspector, SerializeField] private float minimumVehicleMovementMeters = 1f;
        [HideInInspector, SerializeField] private float maxRoadWaypointDistanceMeters = 30f;
        [HideInInspector, SerializeField] private float fallYThreshold = -10f;

        private readonly List<DRTStop> stops = new List<DRTStop>();
        private readonly DRTStopTravelTimeMatrix travelTimeMatrix = new DRTStopTravelTimeMatrix();
        private IDRTVehicleDriver vehicleDriver;
        private DRTPlayerVehicleDriver playerVehicleDriver;
        private DRTGleyVehicleDriver gleyVehicleDriver;
        private float episodeTimeSeconds;
        private bool initialized;
        private bool driving;
        private bool episodeFinished;
        private bool waitingForArrivalProximity;
        private bool cameraFollowApplied;
        private bool backgroundTrafficEnabled;
        private bool backgroundTrafficStateInitialized;
        private int currentStopId;
        private int targetStopId;
        private Coroutine dwellRoutine;
        private Coroutine decisionRoutine;
        private readonly List<Vector3> assignedPathPoints = new List<Vector3>();
        private LineRenderer assignedPathLineRenderer;
        private float assignedPathDistanceMeters;
        private readonly List<DRTRouteLegRecord> routeLegRecords = new List<DRTRouteLegRecord>();
        private readonly List<DRTVehicleTraceRecord> vehicleTraceRecords = new List<DRTVehicleTraceRecord>();
        private DRTRouteLegRecord activeRouteLeg;
        private bool episodeCsvExported;
        private float nextMovementDiagnosticTime;
        private Vector3 lastVehicleMovementPosition;
        private float lastVehicleMovementRealtime;
        private bool hasVehicleMovementSample;
        private float trafficBlockStartRealtime;
        private bool hasTrafficBlockSample;
        private string trafficBlockReason;
        private float episodeTravelDistanceMeters;
        private Vector3 lastTravelDistanceSamplePosition;
        private bool hasTravelDistanceSample;
        private bool travelTimeMatrixLoadAttempted;
        private int episodeIndex;
        private DateTime episodeExportRunTimestamp;
        private bool hasEpisodeExportRunTimestamp;
        private bool allStationMatrixCsvExported;

        public float EpisodeTimeSeconds => episodeTimeSeconds;
        public bool IsInitialized => initialized;
        public bool IsDriving => driving;
        public bool IsEpisodeFinished => episodeFinished;
        public bool IsWaitingForArrivalProximity => waitingForArrivalProximity;
        public int CurrentStopId => currentStopId;
        public int TargetStopId => targetStopId;
        public int VehicleIndex => vehicleIndex;
        public string ControlledVehicleName => vehicleDriver != null ? vehicleDriver.VehicleName : controlledPlayerVehicle != null ? controlledPlayerVehicle.name : "-";
        public string ControlledDriverName => vehicleDriver != null ? vehicleDriver.GetType().Name : UsesGleyVehicleControl ? nameof(DRTGleyVehicleDriver) : nameof(DRTPlayerVehicleDriver);
        public Transform ControlledVehicleTransform => GetControlledVehicleTransform();
        public Vector3 ControlledVehicleBodyPosition => GetControlledVehicleBodyPosition();
        public int AssignedPathPointCount => assignedPathPoints.Count;
        public float AssignedPathDistanceMeters => assignedPathDistanceMeters;
        public bool IsVehicleTemporarilyBlocked => vehicleDriver != null && vehicleDriver.IsTemporarilyBlocked;
        public string TemporaryBlockReason => vehicleDriver != null ? vehicleDriver.TemporaryBlockReason : string.Empty;
        public float ArrivalDistanceMeters => arrivalDistanceMeters;
        public float VehicleSpeedMS => GetVehicleSpeedMS();
        public float TargetDistanceMeters => GetTargetDistanceMeters();
        public float EpisodeLengthSeconds => episodeLengthSeconds;
        public float SimulationSecondsPerRealSecond => simulationSecondsPerRealSecond;
        public float EpisodeTravelDistanceMeters => episodeTravelDistanceMeters;
        public string TargetStopObjectName => TryGetStop(targetStopId, out DRTStop stop) ? stop.name : "-";
        public bool BackgroundTrafficEnabled => backgroundTrafficEnabled;
        public int ActiveBackgroundVehicleCount => CountActiveBackgroundTrafficVehicles();
        public DRTTravelExecutionMode TravelExecutionMode => travelExecutionMode;
        public string TravelExecutionModeName => travelExecutionMode.ToString();
        public DRTNextStopPolicy NextStopPolicy => nextStopSelector != null ? nextStopSelector.NextStopPolicy : DRTNextStopPolicy.MLAgentsTraining;
        public string NextStopPolicyName => nextStopSelector != null ? nextStopSelector.NextStopPolicyName : "-";
        public bool UsesMatrixTeleport => travelExecutionMode == DRTTravelExecutionMode.MatrixTeleport;
        public bool UsesAllStationRunner => nextStopSelector != null && nextStopSelector.UsesAllStationRunner;
        public bool UsesGleyVehicleControl => travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive && useGleyVehicleControlInPhysicalDrive;
        public bool SuppressUnityLogsDuringMatrixTeleport => UsesMatrixTeleport &&
                                                             suppressUnityLogsDuringMatrixTraining;
        public bool SuppressUnityLogsDuringMatrixTraining => SuppressUnityLogsDuringMatrixTeleport;
        public IReadOnlyList<DRTStop> Stops => stops;

        public void Configure(
            Transform newBusStopsRoot,
            DRTPassengerManager newPassengerManager,
            DRTDemandGenerator newDemandGenerator,
            DRTNextStopSelector newNextStopSelector,
            int newVehicleIndex = 0,
            int newStartStopId = 1)
        {
            busStopsRoot = newBusStopsRoot;
            passengerManager = newPassengerManager;
            demandGenerator = newDemandGenerator;
            nextStopSelector = newNextStopSelector;
            vehicleIndex = Mathf.Max(0, newVehicleIndex);
            startStopId = Mathf.Max(1, newStartStopId);
            LoadStops();
            WirePassengerManager();
            WireDemandGenerator();
            WireNextStopSelector();
            ResolveControlledVehicle(false);
        }

        [ContextMenu("Run All Station Travel Time Calibration")]
        public void RunAllStationTravelTimeCalibration()
        {
            Debug.LogWarning("[BUSCONTROLLER] Select Next Stop Policy = All Station Runner, Travel Execution Mode = Physical Drive, then run Play Mode.");
        }

        private void Awake()
        {
            EnsureEpisodeExportRunTimestamp();
            ResolveReferences();
            LoadStops(false);
            WirePassengerManager();
            WireDemandGenerator();
            WireNextStopSelector();
            ResolveControlledVehicle(false);
        }

        private void Start()
        {
            if (demandGenerator != null && !demandGenerator.HasGenerated)
            {
                demandGenerator.GenerateDemand();
            }
        }

        public void ResetEpisodeFromAgent()
        {
            ResolveReferences();
            LoadStops(false);
            WireNextStopSelector();
            StopEpisodeCoroutines();

            episodeIndex++;
            episodeTimeSeconds = 0f;
            initialized = false;
            driving = false;
            episodeFinished = false;
            waitingForArrivalProximity = false;
            currentStopId = startStopId;
            targetStopId = 0;
            ClearAssignedPathVisualization();
            ResetEpisodeExportState();
            nextMovementDiagnosticTime = 0f;
            hasVehicleMovementSample = false;
            hasTrafficBlockSample = false;
            trafficBlockReason = string.Empty;
            episodeTravelDistanceMeters = 0f;
            hasTravelDistanceSample = false;
            travelTimeMatrixLoadAttempted = false;
            lastVehicleMovementRealtime = Time.realtimeSinceStartup;
            lastVehicleMovementPosition = Vector3.zero;

            WirePassengerManager();
            WireDemandGenerator();

            demandGenerator?.ResetDemand(SuppressUnityLogsDuringMatrixTeleport);

            if (API.IsInitialized())
            {
                EnsureBackgroundTrafficStateInitialized();
                ApplyBackgroundTrafficState();
                ResetControlledVehicleForEpisode();
            }

            int requestCount = passengerManager != null ? passengerManager.Requests.Count : 0;
            LogInfo($"[BUSCONTROLLER] Episode reset. index={episodeIndex}, requests={requestCount}, startStop={startStopId}");
        }

        private void Update()
        {
            if (episodeFinished)
            {
                return;
            }

            if (!initialized)
            {
                TryInitializeDriving();
                return;
            }

            if (!UsesMatrixTeleport)
            {
                episodeTimeSeconds += Time.deltaTime * simulationSecondsPerRealSecond;
            }

            AdvanceDemandToCurrentTime();
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            TrackPhysicalTravelDistanceIfNeeded();
            RecordVehicleTraceIfNeeded();
            LogMovementDiagnosticsIfNeeded();
            if (!UsesMatrixTeleport)
            {
                MonitorVehicleFailureIfNeeded();
            }

            if (episodeFinished)
            {
                return;
            }

            if (!UsesAllStationRunner && episodeTimeSeconds >= episodeLengthSeconds)
            {
                FinishEpisode("Episode time ended.");
            }
        }

        private void OnDisable()
        {
            vehicleDriver?.ReleaseControl();
            ClearAssignedPathVisualization();
        }

        [ContextMenu("Reload Stops")]
        public void LoadStops()
        {
            LoadStops(true);
        }

        private void LoadStops(bool logIfMissing)
        {
            stops.Clear();

            if (busStopsRoot == null)
            {
                if (logIfMissing)
                {
                    Debug.LogError("[DRT] BusStops root is missing.");
                }
                return;
            }

            for (int i = 0; i < busStopsRoot.childCount; i++)
            {
                Transform child = busStopsRoot.GetChild(i);
                var stop = child.GetComponent<DRTStop>();

                if (TryParseStopIdFromName(child.name, out int parsedStopId))
                {
                    if (stop == null)
                    {
                        stop = child.gameObject.AddComponent<DRTStop>();
                    }

                    stop.SetStopId(parsedStopId);
                }
                else if (stop == null)
                {
                    continue;
                }
                else if (stop.StopId < 1)
                {
                    stop.SetStopId(i + 1);
                }

                stops.Add(stop);
            }

            stops.Sort((a, b) => a.StopId.CompareTo(b.StopId));

            var duplicateIds = stops
                .GroupBy(stop => stop.StopId)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key} ({string.Join(", ", group.Select(stop => stop.name))})")
                .ToList();

            if (duplicateIds.Count > 0)
            {
                Debug.LogWarning($"[DRT] Duplicate stop IDs detected: {string.Join(", ", duplicateIds)}");
            }

            if (logIfMissing)
            {
                Debug.Log($"[BUSCONTROLLER] Loaded stops count={stops.Count}, root={busStopsRoot.name}");
                LogStopMap();
            }
        }

        private void ResolveReferences()
        {
            if (passengerManager == null)
            {
                passengerManager = FindObjectOfType<DRTPassengerManager>();
            }

            if (demandGenerator == null)
            {
                demandGenerator = FindObjectOfType<DRTDemandGenerator>();
            }

            if (nextStopSelector == null)
            {
                nextStopSelector = FindObjectOfType<DRTNextStopSelector>();
            }
        }

        private void WireNextStopSelector()
        {
            if (nextStopSelector != null)
            {
                nextStopSelector.Configure(this);
                nextStopSelector.ConfigureDiagnostics(logReward, logDecision, logPolicyAction);
            }
        }

        private void WireDemandGenerator()
        {
            if (demandGenerator == null)
            {
                return;
            }

            int configuredStopCount = stops.Count;
            if (configuredStopCount <= 0 && busStopsRoot != null)
            {
                configuredStopCount = busStopsRoot.childCount;
            }

            demandGenerator.Configure(
                passengerManager,
                Mathf.Max(2, configuredStopCount),
                episodeLengthSeconds);
            demandGenerator.ConfigureDiagnostics(logSpawnedRequests);
        }

        private void WirePassengerManager()
        {
            passengerManager?.ConfigureDiagnostics(logStopProcessing);
        }

        private bool ResolveControlledVehicle(bool logIfMissing)
        {
            if (UsesGleyVehicleControl)
            {
                return ResolveControlledGleyVehicle(logIfMissing);
            }

            return ResolveControlledPlayerVehicle(logIfMissing);
        }

        private bool ResolveControlledGleyVehicle(bool logIfMissing)
        {
            if (gleyVehicleDriver == null)
            {
                gleyVehicleDriver = GetComponent<DRTGleyVehicleDriver>();
                if (gleyVehicleDriver == null)
                {
                    gleyVehicleDriver = gameObject.AddComponent<DRTGleyVehicleDriver>();
                }
            }

            gleyVehicleDriver.Configure(vehicleIndex, controlledVehicleType, gleyControlledVehicleSpeedMultiplier);
            if (gleyVehicleDriver.VehicleTransform == null)
            {
                if (logIfMissing)
                {
                    Debug.LogWarning($"[BUSCONTROLLER] Gley controlled vehicle not found. vehicleIndex={vehicleIndex}");
                }

                return false;
            }

            vehicleDriver = gleyVehicleDriver;
            controlledPlayerVehicle = gleyVehicleDriver.VehicleTransform;
            return true;
        }

        private bool ResolveControlledPlayerVehicle(bool logIfMissing)
        {
            if (controlledPlayerVehicle == null)
            {
                var trafficComponent = FindObjectOfType<TrafficComponent>();
                if (trafficComponent != null && trafficComponent.player != null)
                {
                    controlledPlayerVehicle = trafficComponent.player;
                }
            }

            if (controlledPlayerVehicle == null)
            {
                GameObject playerObject = GameObject.Find("Player");
                if (playerObject != null)
                {
                    controlledPlayerVehicle = playerObject.transform;
                }
            }

            if (controlledPlayerVehicle == null)
            {
                if (logIfMissing)
                {
                    Debug.LogWarning("[BUSCONTROLLER] Player vehicle not found. Assign TrafficComponent.player or name the player object 'Player'.");
                }

                return false;
            }

            playerVehicleDriver = controlledPlayerVehicle.GetComponent<DRTPlayerVehicleDriver>();
            if (playerVehicleDriver == null)
            {
                playerVehicleDriver = controlledPlayerVehicle.gameObject.AddComponent<DRTPlayerVehicleDriver>();
            }

            playerVehicleDriver.Configure(
                controlledVehicleType,
                controlledVehicleSpeedMultiplier,
                playerWaypointReachDistanceMeters,
                arrivalDistanceMeters);

            vehicleDriver = playerVehicleDriver;
            return true;
        }

        private Transform GetControlledVehicleTransform()
        {
            if (vehicleDriver != null && vehicleDriver.VehicleTransform != null)
            {
                return vehicleDriver.VehicleTransform;
            }

            return controlledPlayerVehicle;
        }

        private void TryInitializeDriving()
        {
            if (passengerManager == null || nextStopSelector == null || stops.Count == 0)
            {
                return;
            }

            if (!API.IsInitialized())
            {
                return;
            }

            if (!ResolveControlledVehicle(true))
            {
                return;
            }

            initialized = true;
            currentStopId = startStopId;
            targetStopId = 0;
            EnsureBackgroundTrafficStateInitialized();
            ApplyBackgroundTrafficState();
            ResetControlledVehicleForEpisode();
            ResetTravelDistanceSample(GetControlledVehicleBodyPosition());
            RecordVehicleTrace("episode_start");
            if (UsesMatrixTeleport && !EnsureTravelTimeMatrix())
            {
                initialized = false;
                return;
            }

            ApplyCameraFollow(GetControlledVehicleTransform());
            LogInfo(
                $"[BUSCONTROLLER] Initialized mode={TravelExecutionModeName}, " +
                $"policy={NextStopPolicyName}, " +
                $"driver={ControlledDriverName}, vehicle={ControlledVehicleName}, " +
                $"gleyControl={UsesGleyVehicleControl}, gleySpeedMultiplier={gleyControlledVehicleSpeedMultiplier:0.00}, " +
                $"firstTargetHint={startStopId}");
            SendToNextStop();
        }

        public void EnableBackgroundTraffic()
        {
            SetBackgroundTrafficEnabled(true);
        }

        public void DisableBackgroundTraffic()
        {
            SetBackgroundTrafficEnabled(false);
        }

        public void ToggleBackgroundTraffic()
        {
            SetBackgroundTrafficEnabled(!backgroundTrafficEnabled);
        }

        public void SetBackgroundTrafficEnabled(bool enabled)
        {
            backgroundTrafficEnabled = enabled;
            backgroundTrafficStateInitialized = true;
            ApplyBackgroundTrafficState();
        }

        private void BeginDwellAtTarget()
        {
            driving = false;
            vehicleDriver?.StopAndHold(true);

            if (dwellRoutine != null)
            {
                StopCoroutine(dwellRoutine);
            }

            dwellRoutine = StartCoroutine(WaitForProximityThenDwell(targetStopId));
        }

        private void StopEpisodeCoroutines()
        {
            if (dwellRoutine != null)
            {
                StopCoroutine(dwellRoutine);
                dwellRoutine = null;
            }

            if (decisionRoutine != null)
            {
                StopCoroutine(decisionRoutine);
                decisionRoutine = null;
            }

            nextStopSelector?.CancelDecision();
            vehicleDriver?.StopAndHold(false);
        }

        private void ResetControlledVehicleForEpisode()
        {
            if (!ResolveControlledVehicle(true))
            {
                return;
            }

            if (TryGetStartWaypoint(out TrafficWaypoint startWaypoint))
            {
                Transform controlledTransform = GetControlledVehicleTransform();
                Quaternion fallbackRotation = controlledTransform != null ? controlledTransform.rotation : transform.rotation;
                Quaternion rotation = GetWaypointForwardRotation(startWaypoint, fallbackRotation);
                vehicleDriver.TeleportTo(startWaypoint.Position, rotation, startWaypoint.ListIndex);
                ApplyCameraFollow(GetControlledVehicleTransform());
                ResetLegSafetyState(GetControlledVehicleBodyPosition());
                return;
            }

            vehicleDriver.StopAndHold(true);
            ApplyCameraFollow(GetControlledVehicleTransform());
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
        }

        public bool PrepareAllStationCalibration(bool disableBackgroundTraffic, out string error)
        {
            error = null;

            ResolveReferences();
            LoadStops(false);
            StopEpisodeCoroutines();
            initialized = false;
            episodeFinished = true;
            driving = false;
            waitingForArrivalProximity = false;
            targetStopId = 0;
            currentStopId = startStopId;
            ClearAssignedPathVisualization();

            if (stops.Count < 2)
            {
                error = "At least two DRT stops are required.";
                return false;
            }

            if (!API.IsInitialized())
            {
                error = "Gley Traffic API is not initialized.";
                return false;
            }

            if (!ResolveControlledGleyVehicle(true))
            {
                error = $"Gley controlled vehicle not found. vehicleIndex={vehicleIndex}";
                return false;
            }

            EnsureBackgroundTrafficStateInitialized();
            if (disableBackgroundTraffic)
            {
                SetBackgroundTrafficEnabled(false);
            }
            else
            {
                ApplyBackgroundTrafficState();
            }

            vehicleDriver.StopAndHold(true);
            ApplyCameraFollow(GetControlledVehicleTransform());
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            ResetTravelDistanceSample(GetControlledVehicleBodyPosition());
            return true;
        }

        public bool TeleportCalibrationVehicleToStop(int stopId, out string error)
        {
            error = null;

            if (!API.IsInitialized())
            {
                error = "Gley Traffic API is not initialized.";
                return false;
            }

            if (!TryGetStop(stopId, out DRTStop stop))
            {
                error = $"Stop {stopId} was not found.";
                return false;
            }

            if (!ResolveControlledGleyVehicle(true))
            {
                error = $"Gley controlled vehicle not found. vehicleIndex={vehicleIndex}";
                return false;
            }

            Vector3 servicePoint = GetStopServicePoint(stop);
            Transform controlledTransform = GetControlledVehicleTransform();
            Quaternion rotation = controlledTransform != null ? controlledTransform.rotation : Quaternion.identity;
            TrafficWaypoint closestWaypoint = API.GetClosestWaypoint(servicePoint);
            if (closestWaypoint != null)
            {
                rotation = GetWaypointForwardRotation(closestWaypoint, rotation);
            }

            currentStopId = stopId;
            targetStopId = 0;
            driving = false;
            waitingForArrivalProximity = false;
            vehicleDriver.TeleportTo(servicePoint, rotation, closestWaypoint != null ? closestWaypoint.ListIndex : -1);
            vehicleDriver.StopAndHold(true);
            ApplyCameraFollow(GetControlledVehicleTransform());
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            ResetTravelDistanceSample(GetControlledVehicleBodyPosition());
            return true;
        }

        public bool AssignCalibrationRoute(
            int fromStopId,
            int toStopId,
            out int pathWaypointCount,
            out float plannedPathDistanceMeters,
            out string error)
        {
            pathWaypointCount = 0;
            plannedPathDistanceMeters = 0f;
            error = null;

            if (fromStopId == toStopId)
            {
                error = "Origin and destination stops are the same.";
                return false;
            }

            if (!API.IsInitialized())
            {
                error = "Gley Traffic API is not initialized.";
                return false;
            }

            if (!TryGetStop(fromStopId, out _) || !TryGetStop(toStopId, out DRTStop destinationStop))
            {
                error = $"Stop lookup failed. from={fromStopId}, to={toStopId}";
                return false;
            }

            if (!ResolveControlledGleyVehicle(true))
            {
                error = $"Gley controlled vehicle not found. vehicleIndex={vehicleIndex}";
                return false;
            }

            currentStopId = fromStopId;
            targetStopId = toStopId;
            waitingForArrivalProximity = false;

            Vector3 servicePoint = GetStopServicePoint(destinationStop);
            Vector3 routeStartPoint = GetGleyRouteStartPoint(out _);
            List<int> path = API.GetPath(routeStartPoint, servicePoint, controlledVehicleType);
            if (path == null || path.Count == 0)
            {
                error = $"Gley path not found. from={fromStopId}, to={toStopId}";
                return false;
            }

            if (!vehicleDriver.SetPath(path, servicePoint))
            {
                error = $"Controlled vehicle path assignment failed. from={fromStopId}, to={toStopId}";
                return false;
            }

            driving = true;
            UpdateAssignedPathVisualization(routeStartPoint, path, servicePoint);
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            ResetTravelDistanceSample(GetControlledVehicleBodyPosition());
            pathWaypointCount = path.Count;
            plannedPathDistanceMeters = assignedPathDistanceMeters;
            return true;
        }

        public float GetCalibrationDistanceToStopMeters(int stopId)
        {
            return GetDistanceToStopMeters(stopId);
        }

        public void StopCalibrationVehicle(bool zeroVelocity)
        {
            driving = false;
            waitingForArrivalProximity = false;
            targetStopId = 0;
            vehicleDriver?.StopAndHold(zeroVelocity);
        }

        private bool TryGetStartWaypoint(out TrafficWaypoint waypoint)
        {
            waypoint = null;

            if (!API.IsInitialized())
            {
                return false;
            }

            DRTStop startStop = null;
            if (!TryGetStop(startStopId, out startStop) && stops.Count > 0)
            {
                startStop = stops[0];
            }

            if (startStop == null)
            {
                return false;
            }

            Vector3 startPosition = GetStopServicePoint(startStop);
            waypoint = API.GetClosestWaypoint(startPosition);
            if (waypoint == null)
            {
                Debug.LogWarning($"[BUSCONTROLLER] Could not find a traffic waypoint near start stop {startStop.StopId}.");
            }

            return waypoint != null;
        }

        private Quaternion GetWaypointForwardRotation(TrafficWaypoint waypoint, Quaternion fallback)
        {
            if (waypoint == null || waypoint.Neighbors == null || waypoint.Neighbors.Length == 0)
            {
                return fallback;
            }

            var nextWaypoint = API.GetWaypointFromIndex(waypoint.Neighbors[0]);
            if (nextWaypoint == null)
            {
                return fallback;
            }

            Vector3 forward = nextWaypoint.Position - waypoint.Position;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : fallback;
        }

        private IEnumerator WaitForProximityThenDwell(int reachedStopId)
        {
            waitingForArrivalProximity = true;
            float waitStartTime = Time.time;

            while (!episodeFinished)
            {
                float distance = GetDistanceToStopMeters(reachedStopId);
                if (distance <= arrivalDistanceMeters)
                {
                    break;
                }

                if (Time.time - waitStartTime >= arrivalWaitTimeoutSeconds)
                {
                    Debug.LogWarning(
                        $"[BUSCONTROLLER] Controlled vehicle arrived for Stop {reachedStopId}, " +
                        $"but vehicle-stop distance is {FormatMeters(distance)} > {arrivalDistanceMeters:0.00}m. " +
                        "Boarding/dropoff skipped. Move the BusStop closer to the road waypoint or increase the arrival threshold.");
                    waitingForArrivalProximity = false;
                    CompleteActiveRouteLeg(reachedStopId, null);
                    RecordVehicleTrace("arrival_timeout");
                    currentStopId = reachedStopId;
                    yield return new WaitForSeconds(dwellSeconds);
                    SendToNextStop();
                    yield break;
                }

                yield return null;
            }

            waitingForArrivalProximity = false;

            if (episodeFinished)
            {
                yield break;
            }

            if (ProcessStopArrivalAndMaybeFinish(reachedStopId))
            {
                yield break;
            }

            yield return new WaitForSeconds(dwellSeconds);
            SendToNextStop();
        }

        private void SendToNextStop()
        {
            if (decisionRoutine != null)
            {
                StopCoroutine(decisionRoutine);
            }

            decisionRoutine = StartCoroutine(SelectAndSendToNextStop());
        }

        private IEnumerator SelectAndSendToNextStop()
        {
            if (episodeFinished)
            {
                yield break;
            }

            if (UsesMatrixTeleport)
            {
                ResolveControlledVehicle(false);
            }
            else if (!ResolveControlledVehicle(true))
            {
                driving = false;
                yield break;
            }

            AdvanceDemandToCurrentTime();
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);

            int nextStopId = -1;
            if (nextStopSelector.UsesMlAgentsDecisionPolicy)
            {
                bool decisionStarted = nextStopSelector.BeginDecision(currentStopId, stops, passengerManager, episodeTimeSeconds);
                if (decisionStarted)
                {
                    float waitStartTime = Time.time;
                    while (!episodeFinished && !nextStopSelector.TryConsumeDecision(out nextStopId))
                    {
                        if (Time.time - waitStartTime >= nextStopSelector.MaxDecisionWaitSeconds)
                        {
                            nextStopSelector.CancelDecision();
                            break;
                        }

                        yield return null;
                    }
                }
            }
            else
            {
                nextStopId = nextStopSelector.UsesAllStationRunner
                    ? nextStopSelector.SelectAllStationRunnerStopId(
                        currentStopId,
                        stops,
                        episodeTimeSeconds)
                    : nextStopSelector.SelectVanillaSequentialStopId(
                        currentStopId,
                        stops,
                        passengerManager,
                        episodeTimeSeconds);
            }

            if (nextStopId < 1 && nextStopSelector.UsesMlAgentsDecisionPolicy)
            {
                nextStopId = nextStopSelector.SelectNextStopId(
                    currentStopId,
                    stops,
                    passengerManager,
                    episodeTimeSeconds);
            }

            if (stops.Count > 1 && nextStopId == currentStopId)
            {
                int replacementStopId = GetNextNonCurrentStopId(currentStopId);
                LogInfo(
                    $"Next stop matched current stop. current={currentStopId}, " +
                    $"requested={nextStopId}, replacement={replacementStopId}");
                nextStopId = replacementStopId;
            }

            if (!TryGetStop(nextStopId, out DRTStop nextStop))
            {
                FinishEpisode($"No valid next stop found. Requested Stop={nextStopId}");
                yield break;
            }

            if (UsesMatrixTeleport)
            {
                yield return ExecuteMatrixTeleportLeg(nextStop);
                yield break;
            }

            Vector3 servicePoint = GetStopServicePoint(nextStop);
            LogRouteDiagnostics(nextStop, servicePoint);
            Vector3 routeStartPoint = GetGleyRouteStartPoint(out int routeStartWaypointIndex);
            var path = API.GetPath(routeStartPoint, servicePoint, controlledVehicleType);
            if (path == null || path.Count == 0)
            {
                driving = false;
                ClearAssignedPathVisualization();
                Debug.LogWarning(
                    $"[BUSCONTROLLER] PathAssignmentSkipped driver={ControlledDriverName}, vehicle={ControlledVehicleName}, candidateStop={nextStop.StopId}, " +
                    $"candidateObject={nextStop.name}, routeStartWaypoint={routeStartWaypointIndex}. " +
                    "Check that this BusStop is close to a Gley traffic waypoint and allowed for this vehicle type.");
                FinishFailedEpisode($"Path assignment failed. Requested Stop={nextStop.StopId}");
                yield break;
            }

            if (vehicleDriver == null || !vehicleDriver.SetPath(path, servicePoint))
            {
                driving = false;
                ClearAssignedPathVisualization();
                FinishFailedEpisode($"Controlled vehicle path assignment failed. Requested Stop={nextStop.StopId}");
                yield break;
            }

            targetStopId = nextStop.StopId;
            driving = true;
            UpdateAssignedPathVisualization(routeStartPoint, path, servicePoint);
            BeginRouteLeg(nextStop.StopId, path.Count, assignedPathDistanceMeters);
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            LogPathAssignment(nextStop, servicePoint, path, routeStartWaypointIndex);
        }

        private int GetNextNonCurrentStopId(int stopId)
        {
            if (stops == null || stops.Count == 0)
            {
                return -1;
            }

            int firstValidStopId = -1;
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] == null)
                {
                    continue;
                }

                if (firstValidStopId < 1)
                {
                    firstValidStopId = stops[i].StopId;
                }

                if (stops[i].StopId == stopId)
                {
                    for (int offset = 1; offset < stops.Count; offset++)
                    {
                        DRTStop candidate = stops[(i + offset) % stops.Count];
                        if (candidate != null && candidate.StopId != stopId)
                        {
                            return candidate.StopId;
                        }
                    }
                }
            }

            return firstValidStopId != stopId ? firstValidStopId : -1;
        }

        private Vector3 GetGleyRouteStartPoint(out int waypointIndex)
        {
            if (currentStopId > 0 && TryGetStop(currentStopId, out DRTStop currentStop))
            {
                Vector3 currentStopServicePoint = GetStopServicePoint(currentStop);
                var currentStopWaypoint = API.GetClosestWaypoint(currentStopServicePoint);
                if (currentStopWaypoint != null)
                {
                    waypointIndex = currentStopWaypoint.ListIndex;
                    return currentStopWaypoint.Position;
                }
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            Transform controlledTransform = GetControlledVehicleTransform();
            Vector3 forward = controlledTransform != null
                ? controlledTransform.forward
                : transform.forward;

            var directedWaypoint = API.GetClosestWaypointInDirection(bodyPosition, forward);
            if (directedWaypoint != null)
            {
                waypointIndex = directedWaypoint.ListIndex;
                return directedWaypoint.Position;
            }

            var closestWaypoint = API.GetClosestWaypoint(bodyPosition);
            if (closestWaypoint != null)
            {
                waypointIndex = closestWaypoint.ListIndex;
                return closestWaypoint.Position;
            }

            waypointIndex = -1;
            return bodyPosition;
        }

        private IEnumerator ExecuteMatrixTeleportLeg(DRTStop nextStop)
        {
            int originStopId = currentStopId > 0 ? currentStopId : startStopId;

            if (!EnsureTravelTimeMatrix() ||
                !travelTimeMatrix.TryGetTravelTimeSeconds(originStopId, nextStop.StopId, out float travelSeconds))
            {
                FinishFailedEpisode($"Travel time matrix lookup failed. from={originStopId}, to={nextStop.StopId}");
                yield break;
            }

            targetStopId = nextStop.StopId;
            driving = false;
            waitingForArrivalProximity = false;
            BeginRouteLeg(nextStop.StopId, 0, travelSeconds * matrixNominalSpeedMetersPerSecond);

            episodeTimeSeconds += travelSeconds;
            episodeTravelDistanceMeters += travelSeconds * matrixNominalSpeedMetersPerSecond;
            AdvanceDemandToCurrentTime();
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            TeleportControlledVehicleToStop(nextStop);
            RecordVehicleTrace("matrix_arrival");

            if (logMatrixTravel && !SuppressUnityLogsDuringMatrixTeleport)
            {
                Debug.Log(
                    $"[BUSCONTROLLER] MatrixTeleport from={originStopId}, to={nextStop.StopId}, " +
                    $"travel={travelSeconds:0.00}s, episodeTime={episodeTimeSeconds:0.00}s");
            }

            if (ProcessStopArrivalAndMaybeFinish(nextStop.StopId))
            {
                yield break;
            }

            if (AdvanceMatrixDwellAndMaybeFinish())
            {
                yield break;
            }

            yield return null;
            SendToNextStop();
        }

        private bool AdvanceMatrixDwellAndMaybeFinish()
        {
            float dwellEpisodeSeconds = GetDwellEpisodeSeconds();
            if (dwellEpisodeSeconds <= 0f)
            {
                return false;
            }

            episodeTimeSeconds += dwellEpisodeSeconds;
            AdvanceDemandToCurrentTime();
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);

            if (episodeTimeSeconds >= episodeLengthSeconds)
            {
                FinishEpisode("Episode time ended.");
                return true;
            }

            return false;
        }

        private float GetDwellEpisodeSeconds()
        {
            return Mathf.Max(0f, dwellSeconds) * Mathf.Max(0.01f, simulationSecondsPerRealSecond);
        }

        private bool ProcessStopArrivalAndMaybeFinish(int reachedStopId)
        {
            currentStopId = reachedStopId;

            if (passengerManager == null || nextStopSelector == null)
            {
                FinishEpisode("Passenger manager or next stop selector missing at stop arrival.");
                return true;
            }

            AdvanceDemandToCurrentTime();
            var stopResult = passengerManager.ProcessStopArrival(
                currentStopId,
                episodeTimeSeconds,
                SuppressUnityLogsDuringMatrixTeleport || !logStopProcessing);
            CompleteActiveRouteLeg(currentStopId, stopResult);
            nextStopSelector.RecordStopArrival(stopResult, episodeTimeSeconds);

            if (nextStopSelector.IsAllStationRunComplete)
            {
                FinishEpisode("All station runner completed.");
                return true;
            }

            if (!UsesAllStationRunner && stopWhenAllRequestsCompleted && !HasUnfinishedOrPendingRequests())
            {
                FinishEpisode("All passenger requests completed.");
                return true;
            }

            if (!UsesAllStationRunner && episodeTimeSeconds >= episodeLengthSeconds)
            {
                FinishEpisode("Episode time ended.");
                return true;
            }

            return false;
        }

        private void AdvanceDemandToCurrentTime()
        {
            demandGenerator?.SpawnDueRequests(episodeTimeSeconds, SuppressUnityLogsDuringMatrixTeleport);
        }

        private bool HasUnfinishedOrPendingRequests()
        {
            bool hasUnfinishedRequests = passengerManager != null &&
                                         passengerManager.HasUnfinishedRequests(episodeTimeSeconds);
            bool hasPendingScenarioDemand = demandGenerator != null && demandGenerator.HasPendingDemand;
            return hasUnfinishedRequests || hasPendingScenarioDemand;
        }

        private void BeginRouteLeg(int nextStopId, int pathWaypointCount, float plannedPathDistanceMeters)
        {
            if (!ShouldCollectEpisodeExportData())
            {
                return;
            }

            if (activeRouteLeg != null)
            {
                CompleteActiveRouteLeg(-1, null);
            }

            activeRouteLeg = new DRTRouteLegRecord
            {
                LegIndex = routeLegRecords.Count + 1,
                Mode = GetTravelExecutionExportToken(),
                FromStopId = currentStopId > 0 ? currentStopId : startStopId,
                ToStopId = nextStopId,
                ArrivedStopId = -1,
                DepartureTimeSeconds = episodeTimeSeconds,
                DepartureCumulativeDistanceMeters = episodeTravelDistanceMeters,
                PlannedPathDistanceMeters = Mathf.Max(0f, plannedPathDistanceMeters),
                PathWaypointCount = Mathf.Max(0, pathWaypointCount)
            };
        }

        private void CompleteActiveRouteLeg(int reachedStopId, DRTStopProcessResult? stopResult)
        {
            if (activeRouteLeg == null)
            {
                return;
            }

            activeRouteLeg.ArrivedStopId = reachedStopId;
            activeRouteLeg.ArrivalTimeSeconds = episodeTimeSeconds;
            activeRouteLeg.ArrivalCumulativeDistanceMeters = episodeTravelDistanceMeters;
            activeRouteLeg.TravelTimeSeconds = Mathf.Max(0f, activeRouteLeg.ArrivalTimeSeconds - activeRouteLeg.DepartureTimeSeconds);
            activeRouteLeg.LegDistanceMeters = Mathf.Max(0f, activeRouteLeg.ArrivalCumulativeDistanceMeters - activeRouteLeg.DepartureCumulativeDistanceMeters);
            activeRouteLeg.Completed = stopResult.HasValue && reachedStopId == activeRouteLeg.ToStopId;

            if (stopResult.HasValue)
            {
                DRTStopProcessResult result = stopResult.Value;
                activeRouteLeg.BoardedCount = result.BoardedCount;
                activeRouteLeg.DroppedOffCount = result.DroppedOffCount;
                activeRouteLeg.WaitingCount = result.WaitingCount;
                activeRouteLeg.OnBoardCount = result.OnBoardCount;
                activeRouteLeg.CompletedPassengerCount = result.CompletedCount;
            }

            DRTRouteLegRecord completedLeg = activeRouteLeg;
            routeLegRecords.Add(completedLeg);
            string traceEvent = completedLeg.Completed ? "stop_arrival" : "leg_incomplete";
            activeRouteLeg = null;
            RecordVehicleTrace(traceEvent);
            ExportPartialAllStationMatrixCsvIfNeeded(completedLeg);
        }

        private void ResetEpisodeExportState()
        {
            routeLegRecords.Clear();
            vehicleTraceRecords.Clear();
            activeRouteLeg = null;
            episodeCsvExported = false;
            allStationMatrixCsvExported = false;
        }

        private void TeleportControlledVehicleToStop(DRTStop stop)
        {
            if (stop == null || !ResolveControlledVehicle(false))
            {
                return;
            }

            Vector3 servicePoint = GetStopServicePoint(stop);
            Transform controlledTransform = GetControlledVehicleTransform();
            Quaternion rotation = controlledTransform != null ? controlledTransform.rotation : Quaternion.identity;
            TrafficWaypoint closestWaypoint = API.IsInitialized() ? API.GetClosestWaypoint(servicePoint) : null;
            if (closestWaypoint != null)
            {
                rotation = GetWaypointForwardRotation(closestWaypoint, rotation);
            }

            vehicleDriver.TeleportTo(servicePoint, rotation, closestWaypoint != null ? closestWaypoint.ListIndex : -1);
            ApplyCameraFollow(GetControlledVehicleTransform());
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            ResetTravelDistanceSample(GetControlledVehicleBodyPosition());
        }

        private bool TryGetStop(int stopId, out DRTStop stop)
        {
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] != null && stops[i].StopId == stopId)
                {
                    stop = stops[i];
                    return true;
                }
            }

            stop = null;
            return false;
        }

        private void ApplyCameraFollow(Transform target)
        {
            if (!autoFollowControlledVehicle || cameraFollowApplied || target == null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var cameraFollow = mainCamera.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    cameraFollow.target = target;
                }
            }

            API.SetCamera(target);
            cameraFollowApplied = true;
        }

        private void EnsureBackgroundTrafficStateInitialized()
        {
            if (backgroundTrafficStateInitialized)
            {
                return;
            }

            backgroundTrafficEnabled = backgroundTrafficEnabledOnStart;
            backgroundTrafficStateInitialized = true;
        }

        private void ApplyBackgroundTrafficState()
        {
            if (!API.IsInitialized())
            {
                return;
            }

            if (backgroundTrafficEnabled)
            {
                API.SetTrafficDensity(enabledTrafficDensity);
                LogInfo($"[BUSCONTROLLER] Background traffic enabled. density={enabledTrafficDensity}");
                return;
            }

            API.SetTrafficDensity(0);
            int removedCount = RemoveBackgroundTrafficVehicles();
            LogInfo($"[BUSCONTROLLER] Background traffic disabled. removed={removedCount}");
        }

        private int RemoveBackgroundTrafficVehicles()
        {
            var vehicles = API.GetAllVehicles();
            if (vehicles == null)
            {
                return 0;
            }

            int removedCount = 0;
            for (int i = 0; i < vehicles.Length; i++)
            {
                if (vehicles[i] != null && vehicles[i].gameObject.activeSelf)
                {
                    if (UsesGleyVehicleControl && i == vehicleIndex)
                    {
                        continue;
                    }

                    API.RemoveVehicle(i);
                    removedCount++;
                }
            }

            return removedCount;
        }

        private int CountActiveBackgroundTrafficVehicles()
        {
            if (!API.IsInitialized())
            {
                return 0;
            }

            var vehicles = API.GetAllVehicles();
            if (vehicles == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < vehicles.Length; i++)
            {
                if (vehicles[i] != null && vehicles[i].gameObject.activeSelf)
                {
                    if (UsesGleyVehicleControl && i == vehicleIndex)
                    {
                        continue;
                    }

                    count++;
                }
            }

            return count;
        }

        public bool TryGetTravelTimeSeconds(int fromStopId, int toStopId, out float seconds)
        {
            seconds = 0f;
            return EnsureTravelTimeMatrix() &&
                   travelTimeMatrix.TryGetTravelTimeSeconds(fromStopId, toStopId, out seconds);
        }

        public bool TryGetAverageStopTravelTimeMinutes(IReadOnlyList<DRTStop> selectedStops, out float averageMinutes)
        {
            averageMinutes = 0f;
            return EnsureTravelTimeMatrix() &&
                   travelTimeMatrix.TryGetAverageTravelTimeMinutes(selectedStops, out averageMinutes);
        }

        private bool EnsureTravelTimeMatrix()
        {
            if (travelTimeMatrix.IsLoaded)
            {
                return true;
            }

            if (stops.Count == 0)
            {
                LoadStops(false);
            }

            if (stops.Count == 0)
            {
                return false;
            }

            if (travelTimeMatrixLoadAttempted)
            {
                return false;
            }

            travelTimeMatrixLoadAttempted = true;

            TextAsset csvAsset = Resources.Load<TextAsset>(travelTimeMatrixResourceName);
            string loadError = null;
            if (csvAsset != null && travelTimeMatrix.LoadFromCsv(csvAsset.text, stops, out loadError))
            {
                LogInfo(
                    $"[BUSCONTROLLER] Loaded travel time matrix resource={travelTimeMatrixResourceName}, " +
                    $"stops={travelTimeMatrix.StopCount}");
                return true;
            }

            if (csvAsset == null)
            {
                Debug.LogError($"[BUSCONTROLLER] Travel time matrix resource not found. resource={travelTimeMatrixResourceName}");
                return false;
            }

            Debug.LogError($"[BUSCONTROLLER] Travel time matrix CSV invalid. resource={travelTimeMatrixResourceName}, error={loadError}");
            return false;
        }

        public void InvalidateTravelTimeMatrix()
        {
            travelTimeMatrix.Clear();
            travelTimeMatrixLoadAttempted = false;
        }

        private void LogRouteDiagnostics(DRTStop stop, Vector3 servicePoint)
        {
            Transform controlledTransform = GetControlledVehicleTransform();
            if (controlledTransform == null || stop == null)
            {
                return;
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            var closestStopWaypoint = API.GetClosestWaypoint(stop.Position);
            var closestPlayerWaypoint = API.GetClosestWaypoint(bodyPosition);
            var directedPlayerWaypoint = API.GetClosestWaypointInDirection(bodyPosition, controlledTransform.forward);
            float stopToWaypointDistance = closestStopWaypoint != null
                ? GetPlanarDistance(stop.Position, closestStopWaypoint.Position)
                : float.PositiveInfinity;
            float bodyToStopDistance = GetPlanarDistance(bodyPosition, stop.Position);
            float bodyToServiceDistance = GetPlanarDistance(bodyPosition, servicePoint);
            int currentWaypoint = closestPlayerWaypoint != null ? closestPlayerWaypoint.ListIndex : -1;
            int directedWaypoint = directedPlayerWaypoint != null ? directedPlayerWaypoint.ListIndex : -1;

            Debug.Log(
                $"[BUSCONTROLLER] RouteRequest driver={ControlledDriverName}, vehicle={ControlledVehicleName}, currentWaypoint={currentWaypoint}, " +
                $"directedWaypoint={directedWaypoint}, " +
                $"targetStop={stop.StopId}, targetObject={stop.name}, bodyToService={FormatMeters(bodyToServiceDistance)}, " +
                $"bodyToStopMarker={FormatMeters(bodyToStopDistance)}, " +
                $"servicePoint={FormatVector(servicePoint)}, stopMarker={FormatVector(stop.Position)}, " +
                $"stopToClosestWaypoint={FormatMeters(stopToWaypointDistance)}");
        }

        private void UpdateAssignedPathVisualization(Vector3 routeStartPoint, List<int> path, Vector3 servicePoint)
        {
            assignedPathPoints.Clear();
            assignedPathDistanceMeters = 0f;

            if (path == null || path.Count == 0)
            {
                ApplyAssignedPathLineRenderer();
                return;
            }

            assignedPathPoints.Add(OffsetAssignedPathPoint(routeStartPoint));

            for (int i = 0; i < path.Count; i++)
            {
                var waypoint = API.GetWaypointFromIndex(path[i]);
                if (waypoint != null)
                {
                    assignedPathPoints.Add(OffsetAssignedPathPoint(waypoint.Position));
                }
            }

            assignedPathPoints.Add(OffsetAssignedPathPoint(servicePoint));
            assignedPathDistanceMeters = CalculateAssignedPathDistanceMeters();
            ApplyAssignedPathLineRenderer();
        }

        private void ClearAssignedPathVisualization()
        {
            assignedPathPoints.Clear();
            assignedPathDistanceMeters = 0f;
            ApplyAssignedPathLineRenderer();
        }

        private Vector3 OffsetAssignedPathPoint(Vector3 point)
        {
            point.y += assignedPathVerticalOffset;
            return point;
        }

        private float CalculateAssignedPathDistanceMeters()
        {
            if (assignedPathPoints.Count < 2)
            {
                return 0f;
            }

            float distance = 0f;
            for (int i = 1; i < assignedPathPoints.Count; i++)
            {
                distance += GetPlanarDistance(assignedPathPoints[i - 1], assignedPathPoints[i]);
            }

            return distance;
        }

        private void EnsureAssignedPathLineRenderer()
        {
            if (assignedPathLineRenderer != null)
            {
                return;
            }

            GameObject lineObject = new GameObject("DRT Assigned Path");
            lineObject.transform.SetParent(transform, false);

            assignedPathLineRenderer = lineObject.AddComponent<LineRenderer>();
            assignedPathLineRenderer.useWorldSpace = true;
            assignedPathLineRenderer.alignment = LineAlignment.View;
            assignedPathLineRenderer.textureMode = LineTextureMode.Tile;
            assignedPathLineRenderer.numCapVertices = 4;
            assignedPathLineRenderer.numCornerVertices = 4;
            assignedPathLineRenderer.enabled = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader != null)
            {
                assignedPathLineRenderer.material = new Material(shader);
            }
        }

        private void ApplyAssignedPathLineRenderer()
        {
            if (!showAssignedPath || !showAssignedPathInGame || assignedPathPoints.Count < 2)
            {
                if (assignedPathLineRenderer != null)
                {
                    assignedPathLineRenderer.positionCount = 0;
                    assignedPathLineRenderer.enabled = false;
                }

                return;
            }

            EnsureAssignedPathLineRenderer();
            assignedPathLineRenderer.enabled = true;
            assignedPathLineRenderer.startWidth = assignedPathLineWidth;
            assignedPathLineRenderer.endWidth = assignedPathLineWidth;
            assignedPathLineRenderer.startColor = assignedPathColor;
            assignedPathLineRenderer.endColor = assignedPathColor;
            if (assignedPathLineRenderer.material != null)
            {
                assignedPathLineRenderer.material.color = assignedPathColor;
            }

            assignedPathLineRenderer.positionCount = assignedPathPoints.Count;
            assignedPathLineRenderer.SetPositions(assignedPathPoints.ToArray());
        }

        private void LogPathAssignment(DRTStop targetStop, Vector3 servicePoint, List<int> path, int routeStartWaypointIndex)
        {
            string endpointDescription = DescribePathEndpoint(path, servicePoint, out float endToServiceDistance);
            float warningDistance = Mathf.Max(15f, arrivalDistanceMeters + stopWaypointSnapDistanceMeters);
            string integrity = endToServiceDistance <= warningDistance ? "ok" : "warning";
            string pathPreview = BuildPathPreview(path, 12);

            string message =
                $"[BUSCONTROLLER] PathAssigned integrity={integrity}, driver={ControlledDriverName}, vehicle={ControlledVehicleName}, " +
                $"targetStop={targetStop.StopId}, targetObject={targetStop.name}, routeStartWaypoint={routeStartWaypointIndex}, " +
                $"pathWaypoints={path.Count}, visualPathDistance={FormatMeters(assignedPathDistanceMeters)}, servicePoint={FormatVector(servicePoint)}, {endpointDescription}, " +
                $"pathPreview=[{pathPreview}]";

            if (integrity == "warning")
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }
        }

        private string BuildPathPreview(List<int> path, int maxItems)
        {
            if (path == null || path.Count == 0)
            {
                return "-";
            }

            var builder = new System.Text.StringBuilder();
            int count = Mathf.Min(path.Count, Mathf.Max(1, maxItems));
            for (int i = 0; i < count; i++)
            {
                var waypoint = API.GetWaypointFromIndex(path[i]);
                if (i > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(waypoint != null ? waypoint.Name : path[i].ToString());
            }

            if (path.Count > count)
            {
                builder.Append(" -> ...");
            }

            return builder.ToString();
        }

        private string DescribePathEndpoint(List<int> path, Vector3 servicePoint, out float endToServiceDistance)
        {
            endToServiceDistance = float.PositiveInfinity;

            if (path == null || path.Count == 0)
            {
                return "pathEndpoint=none";
            }

            int firstWaypointIndex = path[0];
            int lastWaypointIndex = path[path.Count - 1];
            var lastWaypoint = API.GetWaypointFromIndex(lastWaypointIndex);
            if (lastWaypoint == null)
            {
                return $"firstWaypoint={firstWaypointIndex}, lastWaypoint={lastWaypointIndex}, endpointPosition=unknown, endToService=n/a";
            }

            endToServiceDistance = GetPlanarDistance(lastWaypoint.Position, servicePoint);
            return
                $"firstWaypoint={firstWaypointIndex}, lastWaypoint={lastWaypointIndex}, " +
                $"endpointPosition={FormatVector(lastWaypoint.Position)}, endToService={FormatMeters(endToServiceDistance)}";
        }

        private void MonitorVehicleFailureIfNeeded()
        {
            if (!failEpisodeOnVehicleFault || !initialized || episodeFinished || !API.IsInitialized())
            {
                return;
            }

            if (!ResolveControlledVehicle(true) || vehicleDriver == null || vehicleDriver.VehicleTransform == null || !vehicleDriver.VehicleTransform.gameObject.activeSelf)
            {
                FinishFailedEpisode("Controlled vehicle is missing or inactive.");
                return;
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            if (bodyPosition.y <= fallYThreshold)
            {
                FinishFailedEpisode($"Controlled vehicle fell below safety Y threshold. y={bodyPosition.y:0.00}");
                return;
            }

            if (IsTooFarFromTrafficWaypoint(bodyPosition, out float waypointDistance))
            {
                FinishFailedEpisode($"Controlled vehicle left traffic network. nearestWaypointDistance={FormatMeters(waypointDistance)}");
                return;
            }

            if (!driving || targetStopId <= 0)
            {
                return;
            }

            float targetDistance = GetTargetDistanceMeters();
            if (float.IsInfinity(targetDistance))
            {
                FinishFailedEpisode($"Target distance unavailable. targetStop={targetStopId}");
                return;
            }

            if (targetDistance <= arrivalDistanceMeters)
            {
                BeginDwellAtTarget();
                return;
            }

            float movedDistance = hasVehicleMovementSample
                ? GetPlanarDistance(bodyPosition, lastVehicleMovementPosition)
                : float.PositiveInfinity;

            if (!hasVehicleMovementSample || movedDistance >= minimumVehicleMovementMeters)
            {
                ResetLegSafetyState(bodyPosition);
                return;
            }

            if (vehicleDriver.IsTemporarilyBlocked)
            {
                TrackTrafficBlockWait(bodyPosition, targetDistance, movedDistance, vehicleDriver.TemporaryBlockReason);
                return;
            }

            float stillSeconds = Time.realtimeSinceStartup - lastVehicleMovementRealtime;
            if (stillSeconds >= noMovementTimeoutRealSeconds)
            {
                FinishFailedEpisode(
                    $"Controlled vehicle body unchanged for {noMovementTimeoutRealSeconds:0.0}s real time. " +
                    $"targetStop={targetStopId}, moved={FormatMeters(movedDistance)}, " +
                    $"distance={FormatMeters(targetDistance)}, speed={GetVehicleSpeedMS():0.00}m/s");
            }
        }

        private void TrackTrafficBlockWait(Vector3 bodyPosition, float targetDistance, float movedDistance, string reason)
        {
            string currentReason = string.IsNullOrWhiteSpace(reason) ? "traffic wait" : reason;
            if (!hasTrafficBlockSample || trafficBlockReason != currentReason)
            {
                hasTrafficBlockSample = true;
                trafficBlockStartRealtime = Time.realtimeSinceStartup;
                trafficBlockReason = currentReason;
            }

            lastVehicleMovementPosition = bodyPosition;
            lastVehicleMovementRealtime = Time.realtimeSinceStartup;
            hasVehicleMovementSample = true;

            if (trafficBlockTimeoutRealSeconds <= 0f)
            {
                return;
            }

            float blockedSeconds = Time.realtimeSinceStartup - trafficBlockStartRealtime;
            if (blockedSeconds >= trafficBlockTimeoutRealSeconds)
            {
                FinishFailedEpisode(
                    $"Controlled vehicle remained blocked by {trafficBlockReason} for {trafficBlockTimeoutRealSeconds:0.0}s real time. " +
                    $"targetStop={targetStopId}, moved={FormatMeters(movedDistance)}, " +
                    $"distance={FormatMeters(targetDistance)}, speed={GetVehicleSpeedMS():0.00}m/s");
            }
        }

        private bool IsTooFarFromTrafficWaypoint(Vector3 position, out float waypointDistance)
        {
            waypointDistance = 0f;

            if (maxRoadWaypointDistanceMeters <= 0f)
            {
                return false;
            }

            var waypoint = API.GetClosestWaypoint(position);
            if (waypoint == null)
            {
                waypointDistance = float.PositiveInfinity;
                return true;
            }

            waypointDistance = GetPlanarDistance(position, waypoint.Position);
            return waypointDistance > maxRoadWaypointDistanceMeters;
        }

        private void ResetLegSafetyState(Vector3 vehiclePosition)
        {
            lastVehicleMovementPosition = vehiclePosition;
            lastVehicleMovementRealtime = Time.realtimeSinceStartup;
            hasVehicleMovementSample = true;
            hasTrafficBlockSample = false;
            trafficBlockReason = string.Empty;
        }

        private void ResetTravelDistanceSample(Vector3 vehiclePosition)
        {
            lastTravelDistanceSamplePosition = vehiclePosition;
            hasTravelDistanceSample = true;
        }

        private void TrackPhysicalTravelDistanceIfNeeded()
        {
            if (UsesMatrixTeleport || !initialized || episodeFinished || GetControlledVehicleTransform() == null)
            {
                return;
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            if (!hasTravelDistanceSample)
            {
                ResetTravelDistanceSample(bodyPosition);
                return;
            }

            float distance = GetPlanarDistance(lastTravelDistanceSamplePosition, bodyPosition);
            if (distance > 0.001f)
            {
                episodeTravelDistanceMeters += distance;
                lastTravelDistanceSamplePosition = bodyPosition;
            }
        }

        private void RecordVehicleTraceIfNeeded()
        {
            // Route analysis exports are stop/leg based. Per-second samples add noise and are not persisted.
        }

        private void RecordVehicleTrace(string eventName)
        {
            if (!ShouldCollectEpisodeExportData())
            {
                return;
            }

            Vector3 position = GetControlledVehicleBodyPosition();
            vehicleTraceRecords.Add(new DRTVehicleTraceRecord
            {
                SampleIndex = vehicleTraceRecords.Count + 1,
                EventName = eventName,
                EpisodeTimeSeconds = episodeTimeSeconds,
                CurrentStopId = currentStopId,
                TargetStopId = targetStopId,
                Position = position,
                CumulativeDistanceMeters = episodeTravelDistanceMeters,
                SpeedMetersPerSecond = GetVehicleSpeedMS(),
                Blocked = IsVehicleTemporarilyBlocked,
                BlockReason = TemporaryBlockReason
            });
        }

        private void LogMovementDiagnosticsIfNeeded()
        {
            if (!logMovementDiagnostics || !initialized || Time.time < nextMovementDiagnosticTime)
            {
                return;
            }

            nextMovementDiagnosticTime = Time.time + movementDiagnosticsIntervalSeconds;

            if (!driving && !waitingForArrivalProximity)
            {
                return;
            }

            Debug.Log(
                $"[BUSCONTROLLER] MoveStatus driver={ControlledDriverName}, vehicle={ControlledVehicleName}, targetStop={targetStopId}, " +
                $"distance={FormatMeters(GetTargetDistanceMeters())}, speed={GetVehicleSpeedMS():0.00}m/s, " +
                $"pathPoints={vehicleDriver?.PathPointCount}, remainingPoints={vehicleDriver?.RemainingPathPointCount}, " +
                $"waitingForArrival={waitingForArrivalProximity}, blocked={IsVehicleTemporarilyBlocked}:{TemporaryBlockReason}");
        }

        private float GetVehicleSpeedMS()
        {
            if (vehicleDriver != null)
            {
                return vehicleDriver.CurrentSpeedMS;
            }

            Transform controlledTransform = GetControlledVehicleTransform();
            if (controlledTransform == null)
            {
                return 0f;
            }

            var body = controlledTransform.GetComponent<Rigidbody>();
            if (body == null)
            {
                return 0f;
            }

#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity.magnitude;
#else
            return body.velocity.magnitude;
#endif
        }

        private float GetTargetDistanceMeters()
        {
            return targetStopId > 0 ? GetDistanceToStopMeters(targetStopId) : float.PositiveInfinity;
        }

        private float GetDistanceToStopMeters(int stopId)
        {
            if (!API.IsInitialized())
            {
                return float.PositiveInfinity;
            }

            if (!TryGetStop(stopId, out DRTStop stop))
            {
                return float.PositiveInfinity;
            }

            if (!ResolveControlledVehicle(false))
            {
                return float.PositiveInfinity;
            }

            return GetPlanarDistance(GetControlledVehicleBodyPosition(), GetStopServicePoint(stop));
        }

        private Vector3 GetStopServicePoint(DRTStop stop)
        {
            if (stop == null)
            {
                return Vector3.zero;
            }

            var closestStopWaypoint = API.GetClosestWaypoint(stop.Position);
            if (closestStopWaypoint == null)
            {
                return stop.Position;
            }

            float stopToWaypointDistance = GetPlanarDistance(stop.Position, closestStopWaypoint.Position);
            return stopToWaypointDistance <= stopWaypointSnapDistanceMeters
                ? closestStopWaypoint.Position
                : stop.Position;
        }

        private Vector3 GetControlledVehicleBodyPosition()
        {
            if (vehicleDriver != null)
            {
                return vehicleDriver.BodyPosition;
            }

            Transform controlledTransform = GetControlledVehicleTransform();
            return controlledTransform != null ? controlledTransform.position : Vector3.zero;
        }

        private static float GetPlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static string FormatMeters(float value)
        {
            return float.IsInfinity(value) ? "n/a" : $"{value:0.00}m";
        }

        private void LogInfo(string message)
        {
            if (SuppressUnityLogsDuringMatrixTeleport)
            {
                return;
            }

            Debug.Log(message);
        }

        private void FinishFailedEpisode(string reason)
        {
            if (!Mathf.Approximately(failurePenalty, 0f))
            {
                nextStopSelector?.RecordExternalPenalty(failurePenalty, reason);
            }

            FinishEpisode(reason);
        }

        private void FinishEpisode(string reason)
        {
            if (episodeFinished)
            {
                return;
            }

            bool completedAllRequests = UsesAllStationRunner
                ? nextStopSelector != null && nextStopSelector.IsAllStationRunComplete
                : !HasUnfinishedOrPendingRequests();
            episodeFinished = true;
            driving = false;
            waitingForArrivalProximity = false;
            CompleteActiveRouteLeg(-1, null);
            RecordVehicleTrace("episode_finished");
            vehicleDriver?.StopAndHold(true);

            LogInfo($"[BUSCONTROLLER] Episode finished. {reason}");

            if (logEpisodeSummary && passengerManager != null && !SuppressUnityLogsDuringMatrixTeleport)
            {
                passengerManager.LogSummary();
            }

            float averageWaitSeconds = passengerManager != null ? passengerManager.GetAverageConfirmedWaitTime() : 0f;
            float averageRideSeconds = passengerManager != null ? passengerManager.GetAverageCompletedRideTime() : 0f;
            float serviceRate = passengerManager != null ? passengerManager.GetServiceRate() : 0f;
            int completedCount = passengerManager != null ? passengerManager.GetCompletedCount() : 0;
            ExportEpisodeCsvIfNeeded(reason, completedAllRequests);
            ExportAllStationMatrixCsvIfNeeded(reason);

            nextStopSelector?.NotifyEpisodeFinished(
                completedAllRequests,
                episodeTravelDistanceMeters,
                averageWaitSeconds,
                averageRideSeconds,
                serviceRate,
                completedCount);
        }

        private void ExportEpisodeCsvIfNeeded(string finishReason, bool completedAllRequests)
        {
            if (episodeCsvExported || !ShouldCollectEpisodeExportData() || passengerManager == null)
            {
                return;
            }

            episodeCsvExported = true;

            try
            {
                DateTime exportTimestamp = DateTime.Now;
                string exportDirectory = GetEpisodeExportDirectory();
                Directory.CreateDirectory(exportDirectory);

                string fileStem = BuildEpisodeExportFileStem();
                string episodeFileName = $"{fileStem}_episode.csv";
                string traceFileName = $"{fileStem}_trace.csv";
                string episodePath = System.IO.Path.Combine(exportDirectory, episodeFileName);
                string tracePath = System.IO.Path.Combine(exportDirectory, traceFileName);
                File.WriteAllText(episodePath, BuildEpisodeCsv(finishReason, completedAllRequests, exportTimestamp), Encoding.UTF8);
                File.WriteAllText(tracePath, BuildTraceCsv(), Encoding.UTF8);

                LogInfo(
                    $"[BUSCONTROLLER] Episode CSV exported. episode={episodeFileName}, trace={traceFileName}, " +
                    $"directory={exportDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BUSCONTROLLER] Failed to export episode CSV. {exception}");
            }
        }

        private void ExportPartialAllStationMatrixCsvIfNeeded(DRTRouteLegRecord completedLeg)
        {
            if (completedLeg == null ||
                !completedLeg.Completed ||
                !UsesAllStationRunner ||
                travelExecutionMode != DRTTravelExecutionMode.PhysicalDrive)
            {
                return;
            }

            ExportAllStationMatrixCsv(
                true,
                $"partial leg {completedLeg.FromStopId}->{completedLeg.ToStopId}",
                false);
        }

        private void ExportAllStationMatrixCsvIfNeeded(string finishReason)
        {
            if (allStationMatrixCsvExported || !UsesAllStationRunner)
            {
                return;
            }

            allStationMatrixCsvExported = true;

            if (travelExecutionMode != DRTTravelExecutionMode.PhysicalDrive)
            {
                Debug.LogWarning("[BUSCONTROLLER] All Station Runner finished, but matrix CSV was not updated because travel mode is not Physical Drive.");
                return;
            }

            bool completedAllStationRun = nextStopSelector != null && nextStopSelector.IsAllStationRunComplete;
            if (!completedAllStationRun)
            {
                Debug.LogWarning($"[BUSCONTROLLER] All Station Runner ended before completion. Saving partial matrix CSV. reason={finishReason}");
            }

            ExportAllStationMatrixCsv(!completedAllStationRun, finishReason, true);
        }

        private void ExportAllStationMatrixCsv(bool allowPartial, string reason, bool finalExport)
        {
            if (!TryBuildAllStationMatrixCsv(
                    allowPartial,
                    out string matrixCsv,
                    out string samplesCsv,
                    out string buildError,
                    out int measuredPairs,
                    out int updatedPairs,
                    out int preservedPairs,
                    out int totalPairs))
            {
                string message = $"[BUSCONTROLLER] All Station Runner matrix CSV skipped. {buildError}";
                if (finalExport)
                {
                    Debug.LogError(message);
                }
                else
                {
                    Debug.LogWarning(message);
                }

                return;
            }

            try
            {
                string matrixPath = GetTravelTimeMatrixCsvPath();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(matrixPath));
                File.WriteAllText(matrixPath, matrixCsv, new UTF8Encoding(false));

                string exportDirectory = GetEpisodeExportDirectory();
                Directory.CreateDirectory(exportDirectory);
                string samplesPath = System.IO.Path.Combine(exportDirectory, $"{BuildEpisodeExportFileStem()}_matrix_samples.csv");
                File.WriteAllText(samplesPath, samplesCsv, Encoding.UTF8);

#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif

                InvalidateTravelTimeMatrix();
                Debug.Log(
                    $"[BUSCONTROLLER] All Station Runner matrix CSV {(finalExport ? "exported" : "partial save")}. " +
                    $"reason={reason}, measured={measuredPairs}/{totalPairs}, updated={updatedPairs}, preserved={preservedPairs}, " +
                    $"matrix={matrixPath}, samples={samplesPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BUSCONTROLLER] Failed to export All Station Runner matrix CSV. {exception}");
            }
        }

        private bool TryBuildAllStationMatrixCsv(
            bool allowPartial,
            out string matrixCsv,
            out string samplesCsv,
            out string error,
            out int measuredPairs,
            out int updatedPairs,
            out int preservedPairs,
            out int totalPairs)
        {
            matrixCsv = string.Empty;
            samplesCsv = string.Empty;
            error = null;
            measuredPairs = 0;
            updatedPairs = 0;
            preservedPairs = 0;
            totalPairs = 0;

            var sortedStops = stops
                .Where(stop => stop != null)
                .OrderBy(stop => stop.StopId)
                .ToList();
            if (sortedStops.Count < 2)
            {
                error = "At least two stops are required.";
                return false;
            }

            totalPairs = sortedStops.Count * (sortedStops.Count - 1);
            Dictionary<string, float> baselineByPair = null;
            if (allowPartial && !TryLoadExistingMatrixValues(sortedStops, out baselineByPair, out string baselineError))
            {
                error = $"Cannot build partial matrix because the existing matrix could not be loaded. {baselineError}";
                return false;
            }

            var samplesByPair = new Dictionary<string, List<float>>();
            for (int i = 0; i < routeLegRecords.Count; i++)
            {
                DRTRouteLegRecord leg = routeLegRecords[i];
                if (leg == null ||
                    !leg.Completed ||
                    leg.FromStopId <= 0 ||
                    leg.ToStopId <= 0 ||
                    leg.FromStopId == leg.ToStopId ||
                    leg.ArrivedStopId != leg.ToStopId ||
                    leg.TravelTimeSeconds <= 0f)
                {
                    continue;
                }

                string key = BuildStopPairKey(leg.FromStopId, leg.ToStopId);
                if (!samplesByPair.TryGetValue(key, out List<float> samples))
                {
                    samples = new List<float>();
                    samplesByPair[key] = samples;
                }

                samples.Add(leg.TravelTimeSeconds);
            }

            var matrixBuilder = new StringBuilder();
            var samplesBuilder = new StringBuilder();
            samplesBuilder.AppendLine("from_stop_id,to_stop_id,sample_count,matrix_time_seconds,sample_time_seconds");

            for (int row = 0; row < sortedStops.Count; row++)
            {
                if (row > 0)
                {
                    matrixBuilder.AppendLine();
                }

                int fromStopId = sortedStops[row].StopId;
                for (int column = 0; column < sortedStops.Count; column++)
                {
                    if (column > 0)
                    {
                        matrixBuilder.Append(',');
                    }

                    int toStopId = sortedStops[column].StopId;
                    if (fromStopId == toStopId)
                    {
                        matrixBuilder.Append('0');
                        continue;
                    }

                    string key = BuildStopPairKey(fromStopId, toStopId);
                    bool hasSample = samplesByPair.TryGetValue(key, out List<float> samples) && samples.Count > 0;
                    float matrixSeconds;
                    if (hasSample)
                    {
                        matrixSeconds = Median(samples);
                        measuredPairs++;
                        updatedPairs++;
                    }
                    else if (allowPartial && baselineByPair != null && baselineByPair.TryGetValue(key, out float baselineSeconds))
                    {
                        matrixSeconds = baselineSeconds;
                        preservedPairs++;
                    }
                    else
                    {
                        error = $"Missing completed route leg sample. from={fromStopId}, to={toStopId}";
                        return false;
                    }

                    matrixBuilder.Append(FormatCsvFloat(matrixSeconds));
                    samplesBuilder
                        .Append(fromStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(toStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append((hasSample ? samples.Count : 0).ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(FormatCsvFloat(matrixSeconds)).Append(',')
                        .Append(CsvEscape(hasSample ? FormatCsvFloatList(samples) : string.Empty))
                        .AppendLine();
                }
            }

            matrixCsv = matrixBuilder.ToString();
            samplesCsv = samplesBuilder.ToString();
            return true;
        }

        private bool TryLoadExistingMatrixValues(
            IReadOnlyList<DRTStop> sortedStops,
            out Dictionary<string, float> valuesByPair,
            out string error)
        {
            valuesByPair = new Dictionary<string, float>();
            error = null;

            if (sortedStops == null || sortedStops.Count < 2)
            {
                error = "At least two stops are required.";
                return false;
            }

            string csvText = null;
            string matrixPath = GetTravelTimeMatrixCsvPath();
            if (File.Exists(matrixPath))
            {
                csvText = File.ReadAllText(matrixPath, Encoding.UTF8);
            }
            else
            {
                TextAsset csvAsset = Resources.Load<TextAsset>(travelTimeMatrixResourceName);
                if (csvAsset != null)
                {
                    csvText = csvAsset.text;
                }
            }

            if (string.IsNullOrWhiteSpace(csvText))
            {
                error = $"Existing matrix CSV not found. path={matrixPath}, resource={travelTimeMatrixResourceName}";
                return false;
            }

            string[] rawLines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i].Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add(line);
            }

            if (lines.Count != sortedStops.Count)
            {
                error = $"Existing matrix must have {sortedStops.Count} rows, but has {lines.Count}.";
                return false;
            }

            for (int row = 0; row < lines.Count; row++)
            {
                string[] columns = lines[row].Split(',');
                if (columns.Length != sortedStops.Count)
                {
                    error = $"Existing matrix row {row + 1} must have {sortedStops.Count} columns, but has {columns.Length}.";
                    return false;
                }

                int fromStopId = sortedStops[row].StopId;
                for (int column = 0; column < columns.Length; column++)
                {
                    int toStopId = sortedStops[column].StopId;
                    if (fromStopId == toStopId)
                    {
                        continue;
                    }

                    if (!float.TryParse(columns[column].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ||
                        seconds <= 0f)
                    {
                        error = $"Existing matrix row {row + 1}, column {column + 1} must be a positive number.";
                        return false;
                    }

                    valuesByPair[BuildStopPairKey(fromStopId, toStopId)] = seconds;
                }
            }

            return true;
        }

        private string GetTravelTimeMatrixCsvPath()
        {
            string resourcesPath = System.IO.Path.Combine(Application.dataPath, "DRT", "Resources");
            string fileName = string.IsNullOrWhiteSpace(travelTimeMatrixResourceName)
                ? "drt_stop_travel_time_matrix"
                : travelTimeMatrixResourceName;
            return System.IO.Path.Combine(resourcesPath, fileName + ".csv");
        }

        private bool ShouldCollectEpisodeExportData()
        {
            return exportEpisodeCsvOnEpisodeEnd || UsesAllStationRunner;
        }

        private static bool IsMlAgentsTrainingSession()
        {
            return Academy.Instance != null && Academy.Instance.IsCommunicatorOn;
        }

        private string GetEpisodeExportRootDirectory()
        {
#if UNITY_EDITOR
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            return System.IO.Path.Combine(projectRoot, "DRT_Episode_Exports");
#else
            return System.IO.Path.Combine(Application.persistentDataPath, "DRT_Episode_Exports");
#endif
        }

        private string GetEpisodeExportDirectory()
        {
            EnsureEpisodeExportRunTimestamp();
            string policyDirectory = GetNextStopPolicyExportToken();
            string timestampDirectory = episodeExportRunTimestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return System.IO.Path.Combine(GetEpisodeExportRootDirectory(), policyDirectory, timestampDirectory);
        }

        private void EnsureEpisodeExportRunTimestamp()
        {
            if (hasEpisodeExportRunTimestamp)
            {
                return;
            }

            episodeExportRunTimestamp = DateTime.Now;
            hasEpisodeExportRunTimestamp = true;
        }

        private string BuildEpisodeExportFileStem()
        {
            string modeToken = GetTravelExecutionExportToken();
            string policyToken = GetNextStopPolicyExportToken();
            string scenarioId = demandGenerator != null
                ? demandGenerator.ExportScenarioId
                : Mathf.Max(0, passengerManager != null ? passengerManager.Requests.Count : 0).ToString(CultureInfo.InvariantCulture);
            return SanitizeFileName($"drt_{modeToken}_{policyToken}_scenario_{scenarioId}_ep{episodeIndex:000}");
        }

        private string GetTravelExecutionExportToken()
        {
            return travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive
                ? "physical"
                : "matrix";
        }

        private string GetNextStopPolicyExportToken()
        {
            switch (NextStopPolicy)
            {
                case DRTNextStopPolicy.ONNXInference:
                    return "inference";
                case DRTNextStopPolicy.VanillaSequential:
                    return "vanilla";
                case DRTNextStopPolicy.AllStationRunner:
                    return "all_station";
                default:
                    return "train";
            }
        }

        private string BuildEpisodeCsv(string finishReason, bool completedAllRequests, DateTime exportTimestamp)
        {
            var builder = new StringBuilder();
            builder.AppendLine("section,key,value");
            AppendEpisodeRow(builder, "metadata", "schema_version", "1");
            AppendEpisodeRow(builder, "metadata", "generated_at", exportTimestamp.ToString("o", CultureInfo.InvariantCulture));
            AppendEpisodeRow(builder, "summary", "episode_index", episodeIndex.ToString(CultureInfo.InvariantCulture));
            AppendEpisodeRow(builder, "summary", "travel_mode", GetTravelExecutionExportToken());
            AppendEpisodeRow(builder, "summary", "travel_execution_mode", TravelExecutionModeName);
            AppendEpisodeRow(builder, "summary", "policy", GetNextStopPolicyExportToken());
            AppendEpisodeRow(builder, "summary", "next_stop_policy", NextStopPolicyName);
            AppendEpisodeRow(builder, "summary", "scenario_id", demandGenerator != null ? demandGenerator.ExportScenarioId : string.Empty);
            AppendEpisodeRow(builder, "summary", "scenario_description", demandGenerator != null ? demandGenerator.LoadedScenarioDescription : string.Empty);
            AppendEpisodeRow(builder, "summary", "finish_reason", finishReason);
            AppendEpisodeRow(builder, "summary", "completed_all_requests", completedAllRequests ? "1" : "0");
            AppendEpisodeRow(builder, "summary", "episode_time_seconds", FormatCsvFloat(episodeTimeSeconds));
            AppendEpisodeRow(builder, "summary", "episode_distance_meters", FormatCsvFloat(episodeTravelDistanceMeters));
            AppendEpisodeRow(builder, "summary", "total_passengers", passengerManager != null ? passengerManager.Requests.Count.ToString(CultureInfo.InvariantCulture) : "0");
            AppendEpisodeRow(builder, "summary", "completed_passengers", passengerManager != null ? passengerManager.GetCompletedCount().ToString(CultureInfo.InvariantCulture) : "0");
            AppendEpisodeRow(builder, "summary", "service_rate", passengerManager != null ? FormatCsvFloat(passengerManager.GetServiceRate()) : "0");
            AppendEpisodeRow(builder, "summary", "average_wait_seconds", passengerManager != null ? FormatCsvFloat(passengerManager.GetAverageConfirmedWaitTime()) : "0");
            AppendEpisodeRow(builder, "summary", "average_ride_seconds", passengerManager != null ? FormatCsvFloat(passengerManager.GetAverageCompletedRideTime()) : "0");
            builder.AppendLine(",");
            AppendRouteLegsCsv(builder);
            builder.AppendLine(",");
            AppendPassengersCsv(builder);
            return builder.ToString();
        }

        private void AppendRouteLegsCsv(StringBuilder builder)
        {
            builder.AppendLine("section,leg_index,mode,from_stop_id,to_stop_id,arrived_stop_id,completed,departure_time_seconds,arrival_time_seconds,travel_time_seconds,leg_distance_meters,cumulative_distance_meters,planned_path_distance_meters,path_waypoint_count,boarded_count,dropped_off_count,boarded_passenger_ids,dropped_off_passenger_ids,waiting_count,on_board_count,completed_passenger_count");
            foreach (var leg in routeLegRecords)
            {
                builder
                    .Append("route_leg").Append(',')
                    .Append(leg.LegIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(CsvEscape(leg.Mode)).Append(',')
                    .Append(leg.FromStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.ToStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(FormatCsvIntOrBlank(leg.ArrivedStopId)).Append(',')
                    .Append(leg.Completed ? "1" : "0").Append(',')
                    .Append(FormatCsvFloat(leg.DepartureTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.ArrivalTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.TravelTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.LegDistanceMeters)).Append(',')
                    .Append(FormatCsvFloat(leg.ArrivalCumulativeDistanceMeters)).Append(',')
                    .Append(FormatCsvFloat(leg.PlannedPathDistanceMeters)).Append(',')
                    .Append(leg.PathWaypointCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.BoardedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.DroppedOffCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(CsvEscape(FormatPassengerIdList(GetBoardedPassengerIds(leg)))).Append(',')
                    .Append(CsvEscape(FormatPassengerIdList(GetDroppedOffPassengerIds(leg)))).Append(',')
                    .Append(leg.WaitingCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.OnBoardCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.CompletedPassengerCount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        private void AppendPassengersCsv(StringBuilder builder)
        {
            builder.AppendLine("section,passenger_id,origin_stop_id,destination_stop_id,request_time_seconds,status,pickup_time_seconds,dropoff_time_seconds,actual_pickup_stop_id,actual_dropoff_stop_id,wait_time_seconds,ride_time_seconds,total_service_time_seconds");
            foreach (var request in passengerManager.Requests.OrderBy(request => request.PassengerId))
            {
                builder
                    .Append("passenger").Append(',')
                    .Append(request.PassengerId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(request.OriginStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(request.DestinationStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(FormatCsvFloat(request.RequestTimeSeconds)).Append(',')
                    .Append(CsvEscape(request.Status.ToString())).Append(',')
                    .Append(FormatCsvFloatOrBlank(request.PickupTimeSeconds)).Append(',')
                    .Append(FormatCsvFloatOrBlank(request.DropoffTimeSeconds)).Append(',')
                    .Append(FormatCsvIntOrBlank(request.ActualPickupStopId)).Append(',')
                    .Append(FormatCsvIntOrBlank(request.ActualDropoffStopId)).Append(',')
                    .Append(FormatCsvFloat(request.GetWaitTime(episodeTimeSeconds))).Append(',')
                    .Append(FormatCsvFloat(request.GetRideTime(episodeTimeSeconds))).Append(',')
                    .Append(FormatCsvFloat(request.GetTotalServiceTime(episodeTimeSeconds)))
                    .AppendLine();
            }
        }

        private string BuildTraceCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("leg_index,from_stop_id,to_stop_id,arrived_stop_id,completed,departure_time_seconds,arrival_time_seconds,travel_time_seconds,leg_distance_meters,cumulative_distance_meters,planned_path_distance_meters,path_waypoint_count,boarded_passenger_ids,dropped_off_passenger_ids,arrival_x,arrival_y,arrival_z,blocked,block_reason");

            foreach (var leg in routeLegRecords)
            {
                DRTVehicleTraceRecord arrivalTrace = FindNearestVehicleTrace(leg.ArrivalTimeSeconds);
                builder
                    .Append(leg.LegIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.FromStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.ToStopId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(FormatCsvIntOrBlank(leg.ArrivedStopId)).Append(',')
                    .Append(leg.Completed ? "1" : "0").Append(',')
                    .Append(FormatCsvFloat(leg.DepartureTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.ArrivalTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.TravelTimeSeconds)).Append(',')
                    .Append(FormatCsvFloat(leg.LegDistanceMeters)).Append(',')
                    .Append(FormatCsvFloat(leg.ArrivalCumulativeDistanceMeters)).Append(',')
                    .Append(FormatCsvFloat(leg.PlannedPathDistanceMeters)).Append(',')
                    .Append(leg.PathWaypointCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(CsvEscape(FormatPassengerIdList(GetBoardedPassengerIds(leg)))).Append(',')
                    .Append(CsvEscape(FormatPassengerIdList(GetDroppedOffPassengerIds(leg)))).Append(',')
                    .Append(arrivalTrace != null ? FormatCsvFloat(arrivalTrace.Position.x) : string.Empty).Append(',')
                    .Append(arrivalTrace != null ? FormatCsvFloat(arrivalTrace.Position.y) : string.Empty).Append(',')
                    .Append(arrivalTrace != null ? FormatCsvFloat(arrivalTrace.Position.z) : string.Empty).Append(',')
                    .Append(arrivalTrace != null && arrivalTrace.Blocked ? "1" : "0").Append(',')
                    .Append(CsvEscape(arrivalTrace != null ? arrivalTrace.BlockReason : string.Empty))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private DRTVehicleTraceRecord FindNearestVehicleTrace(float episodeTime)
        {
            if (vehicleTraceRecords.Count == 0)
            {
                return null;
            }

            DRTVehicleTraceRecord nearestTrace = null;
            float nearestDelta = float.PositiveInfinity;
            for (int i = 0; i < vehicleTraceRecords.Count; i++)
            {
                DRTVehicleTraceRecord trace = vehicleTraceRecords[i];
                float delta = Mathf.Abs(trace.EpisodeTimeSeconds - episodeTime);
                if (delta < nearestDelta)
                {
                    nearestTrace = trace;
                    nearestDelta = delta;
                }
            }

            return nearestTrace;
        }

        private List<int> GetBoardedPassengerIds(DRTRouteLegRecord leg)
        {
            if (passengerManager == null || leg.ArrivedStopId <= 0)
            {
                return new List<int>();
            }

            return passengerManager.Requests
                .Where(request => request != null &&
                                  request.ActualPickupStopId == leg.ArrivedStopId &&
                                  IsSameEpisodeTime(request.PickupTimeSeconds, leg.ArrivalTimeSeconds))
                .Select(request => request.PassengerId)
                .OrderBy(passengerId => passengerId)
                .ToList();
        }

        private List<int> GetDroppedOffPassengerIds(DRTRouteLegRecord leg)
        {
            if (passengerManager == null || leg.ArrivedStopId <= 0)
            {
                return new List<int>();
            }

            return passengerManager.Requests
                .Where(request => request != null &&
                                  request.ActualDropoffStopId == leg.ArrivedStopId &&
                                  IsSameEpisodeTime(request.DropoffTimeSeconds, leg.ArrivalTimeSeconds))
                .Select(request => request.PassengerId)
                .OrderBy(passengerId => passengerId)
                .ToList();
        }

        private static bool IsSameEpisodeTime(float recordedTime, float currentEpisodeTime)
        {
            return recordedTime >= 0f && Mathf.Abs(recordedTime - currentEpisodeTime) <= 0.05f;
        }

        private static string SanitizeFileName(string fileName)
        {
            string sanitized = fileName;
            foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }

            return sanitized;
        }

        private static void AppendEpisodeRow(StringBuilder builder, string section, string key, string value)
        {
            builder
                .Append(CsvEscape(section))
                .Append(',')
                .Append(CsvEscape(key))
                .Append(',')
                .Append(CsvEscape(value))
                .AppendLine();
        }

        private static string FormatPassengerIdList(List<int> passengerIds)
        {
            return passengerIds == null || passengerIds.Count == 0
                ? string.Empty
                : string.Join("|", passengerIds);
        }

        private static string FormatCsvFloatList(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("|", values.Select(FormatCsvFloat));
        }

        private static float Median(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0f;
            }

            var sortedValues = new List<float>(values);
            sortedValues.Sort();
            int middle = sortedValues.Count / 2;
            return sortedValues.Count % 2 == 1
                ? sortedValues[middle]
                : (sortedValues[middle - 1] + sortedValues[middle]) * 0.5f;
        }

        private static string BuildStopPairKey(int fromStopId, int toStopId)
        {
            return $"{fromStopId}->{toStopId}";
        }

        private static string FormatCsvFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatCsvFloatOrBlank(float value)
        {
            return value >= 0f ? FormatCsvFloat(value) : string.Empty;
        }

        private static string FormatCsvIntOrBlank(int value)
        {
            return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool mustQuote = value.Contains(",") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n");
            if (!mustQuote)
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private void OnDrawGizmos()
        {
            if (!showAssignedPath || !showAssignedPathGizmos || assignedPathPoints == null || assignedPathPoints.Count < 2)
            {
                return;
            }

            Gizmos.color = assignedPathColor;
            for (int i = 1; i < assignedPathPoints.Count; i++)
            {
                Gizmos.DrawLine(assignedPathPoints[i - 1], assignedPathPoints[i]);
            }

            Gizmos.color = assignedPathWaypointColor;
            for (int i = 0; i < assignedPathPoints.Count; i++)
            {
                Gizmos.DrawSphere(assignedPathPoints[i], assignedPathWaypointRadius);
            }
        }

        private void OnValidate()
        {
            vehicleIndex = Mathf.Max(0, vehicleIndex);
            startStopId = Mathf.Max(1, startStopId);
            dwellSeconds = Mathf.Max(0f, dwellSeconds);
            arrivalDistanceMeters = Mathf.Max(0.05f, arrivalDistanceMeters);
            stopWaypointSnapDistanceMeters = Mathf.Max(0.05f, stopWaypointSnapDistanceMeters);
            arrivalWaitTimeoutSeconds = Mathf.Max(0.5f, arrivalWaitTimeoutSeconds);
            controlledVehicleSpeedMultiplier = Mathf.Max(0.1f, controlledVehicleSpeedMultiplier);
            gleyControlledVehicleSpeedMultiplier = Mathf.Clamp(gleyControlledVehicleSpeedMultiplier, 0.1f, 2f);
            playerWaypointReachDistanceMeters = Mathf.Max(0.5f, playerWaypointReachDistanceMeters);
            vehicleTraceSampleIntervalSeconds = Mathf.Max(0.1f, vehicleTraceSampleIntervalSeconds);
            matrixNominalSpeedMetersPerSecond = Mathf.Max(0.1f, matrixNominalSpeedMetersPerSecond);
            enabledTrafficDensity = Mathf.Max(1, enabledTrafficDensity);
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
            simulationSecondsPerRealSecond = Mathf.Max(0.01f, simulationSecondsPerRealSecond);
            movementDiagnosticsIntervalSeconds = Mathf.Max(0.25f, movementDiagnosticsIntervalSeconds);
            noMovementTimeoutRealSeconds = Mathf.Max(1f, noMovementTimeoutRealSeconds);
            trafficBlockTimeoutRealSeconds = Mathf.Max(0f, trafficBlockTimeoutRealSeconds);
            minimumVehicleMovementMeters = Mathf.Max(0.01f, minimumVehicleMovementMeters);
            maxRoadWaypointDistanceMeters = Mathf.Max(0f, maxRoadWaypointDistanceMeters);
            assignedPathLineWidth = Mathf.Max(0.01f, assignedPathLineWidth);
            assignedPathWaypointRadius = Mathf.Max(0.01f, assignedPathWaypointRadius);
            assignedPathVerticalOffset = Mathf.Max(0f, assignedPathVerticalOffset);
            ApplyAssignedPathLineRenderer();
        }

        private sealed class DRTRouteLegRecord
        {
            public int LegIndex;
            public string Mode;
            public int FromStopId;
            public int ToStopId;
            public int ArrivedStopId;
            public bool Completed;
            public float DepartureTimeSeconds;
            public float ArrivalTimeSeconds;
            public float TravelTimeSeconds;
            public float LegDistanceMeters;
            public float DepartureCumulativeDistanceMeters;
            public float ArrivalCumulativeDistanceMeters;
            public float PlannedPathDistanceMeters;
            public int PathWaypointCount;
            public int BoardedCount;
            public int DroppedOffCount;
            public int WaitingCount;
            public int OnBoardCount;
            public int CompletedPassengerCount;
        }

        private sealed class DRTVehicleTraceRecord
        {
            public int SampleIndex;
            public string EventName;
            public float EpisodeTimeSeconds;
            public int CurrentStopId;
            public int TargetStopId;
            public Vector3 Position;
            public float CumulativeDistanceMeters;
            public float SpeedMetersPerSecond;
            public bool Blocked;
            public string BlockReason;
        }

        private void LogStopMap()
        {
            if (stops.Count == 0)
            {
                return;
            }

            string stopMap = string.Join(
                "; ",
                stops.Select(stop => $"S{stop.StopId}={stop.name}@{FormatVector(stop.Position)}"));
            Debug.Log($"[BUSCONTROLLER] StopMap {stopMap}");
        }

        private static bool TryParseStopIdFromName(string objectName, out int stopId)
        {
            stopId = -1;
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            int end = objectName.Length - 1;
            while (end >= 0 && !char.IsDigit(objectName[end]))
            {
                end--;
            }

            if (end < 0)
            {
                return false;
            }

            int start = end;
            while (start >= 0 && char.IsDigit(objectName[start]))
            {
                start--;
            }

            string numberText = objectName.Substring(start + 1, end - start);
            return int.TryParse(numberText, out stopId) && stopId >= 1;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.00},{value.y:0.00},{value.z:0.00})";
        }
    }
}
