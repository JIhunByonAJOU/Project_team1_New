using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
using Unity.Barracuda;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;

namespace DRT
{
    [AddComponentMenu("DRT/DRT Bus Controller")]
    public class DRTBusController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform busStopsRoot;
        [SerializeField] private DRTPassengerManager passengerManager;
        [SerializeField] private DRTDemandGenerator demandGenerator;
        [SerializeField] private DRTNextStopSelector nextStopSelector;
        [SerializeField] private Transform controlledPlayerVehicle;

        [Header("Vehicle")]
        [SerializeField] private int vehicleIndex = 0;
        [SerializeField] private int startStopId = 1;
        [SerializeField] private VehicleTypes controlledVehicleType = VehicleTypes.Car;
        [SerializeField] private float dwellSeconds = 5f;
        [SerializeField] private float arrivalDistanceMeters = 5f;
        [SerializeField] private float stopWaypointSnapDistanceMeters = 5f;
        [SerializeField] private float arrivalWaitTimeoutSeconds = 12f;
        [SerializeField] private float controlledVehicleSpeedMultiplier = 1.5f;
        [Tooltip("Only used when PhysicalDrive uses the Gley vehicle driver. 1.0 keeps Gley speed unchanged; 1.12 means roughly +12%.")]
        [SerializeField] private float gleyControlledVehicleSpeedMultiplier = 1.12f;
        [SerializeField] private float playerWaypointReachDistanceMeters = 6f;
        [SerializeField] private bool autoFollowControlledVehicle = true;
        [SerializeField] private bool useGleyVehicleControlInPhysicalDrive = true;

        [Header("Run Mode")]
        [Tooltip("MatrixTeleportTraining is for fast PPO training. PhysicalDrive is for ONNX inference/demo with the configured vehicle driver.")]
        [InspectorName("Run Mode (Training / Inference)")]
        [SerializeField] private DRTTravelExecutionMode travelExecutionMode = DRTTravelExecutionMode.MatrixTeleportTraining;
        [SerializeField] private string travelTimeMatrixResourceName = "drt_stop_travel_time_matrix";
        [SerializeField] private float matrixNominalSpeedMetersPerSecond = 15f;
        [SerializeField] private bool preferGeneratedMatrixFromGley = true;
        [SerializeField] private bool autoGenerateMatrixFromGleyWhenMissing = true;
        [SerializeField] private bool saveGeneratedMatrixAssetInEditor;
        [SerializeField] private bool logMatrixTravel = true;
        [SerializeField] private bool suppressUnityLogsDuringMatrixTraining = true;

        [Header("Physical Drive Inference")]
        [Tooltip("Optional ONNX/NNModel asset used when Run Mode is PhysicalDrive. Import the .onnx under Assets first, then assign it here.")]
        [SerializeField] private NNModel physicalDriveInferenceModel;
        [SerializeField] private InferenceDevice physicalDriveInferenceDevice = InferenceDevice.Default;

        [Header("Inference CSV Export")]
        [SerializeField] private bool exportInferenceCsvOnEpisodeEnd = true;
        [SerializeField] private float vehicleTraceSampleIntervalSeconds = 1f;

        [Header("Diagnostics")]
        [SerializeField] private bool logMovementDiagnostics;
        [SerializeField] private float movementDiagnosticsIntervalSeconds = 2f;

        [Header("Path Visualization")]
        [SerializeField] private bool showAssignedPath = true;
        [SerializeField] private bool showAssignedPathInGame = true;
        [SerializeField] private bool showAssignedPathGizmos = true;
        [SerializeField] private Color assignedPathColor = new Color(0f, 0.85f, 1f, 0.9f);
        [SerializeField] private Color assignedPathWaypointColor = new Color(1f, 0.85f, 0f, 0.95f);
        [SerializeField] private float assignedPathLineWidth = 0.7f;
        [SerializeField] private float assignedPathWaypointRadius = 1.2f;
        [SerializeField] private float assignedPathVerticalOffset = 0.35f;

        [Header("Background Traffic")]
        [SerializeField] private bool backgroundTrafficEnabledOnStart = true;
        [SerializeField] private int enabledTrafficDensity = 30;

        [Header("Episode")]
        [SerializeField] private float episodeLengthSeconds = 3600f;
        [SerializeField] private float simulationSecondsPerRealSecond = 1f;
        [SerializeField] private bool stopWhenAllRequestsCompleted = true;
        [SerializeField] private bool logEpisodeSummary = true;

        [Header("Failure Safety")]
        [SerializeField] private bool failEpisodeOnVehicleFault = true;
        [SerializeField] private float failurePenalty = -1f;
        [SerializeField] private float noMovementTimeoutRealSeconds = 20f;
        [Tooltip("A longer timeout used only when Gley reports a normal stop caused by traffic lights, give-way, or obstacles. Set 0 to never fail on traffic waits.")]
        [SerializeField] private float trafficBlockTimeoutRealSeconds = 180f;
        [SerializeField] private float minimumVehicleMovementMeters = 1f;
        [SerializeField] private float maxRoadWaypointDistanceMeters = 30f;
        [SerializeField] private float fallYThreshold = -10f;

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
        private float nextVehicleTraceSampleTime;
        private bool inferenceCsvExported;
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
        public int AssignedPathPointCount => assignedPathPoints.Count;
        public float AssignedPathDistanceMeters => assignedPathDistanceMeters;
        public bool IsVehicleTemporarilyBlocked => vehicleDriver != null && vehicleDriver.IsTemporarilyBlocked;
        public string TemporaryBlockReason => vehicleDriver != null ? vehicleDriver.TemporaryBlockReason : string.Empty;
        public float ArrivalDistanceMeters => arrivalDistanceMeters;
        public float VehicleSpeedMS => GetVehicleSpeedMS();
        public float TargetDistanceMeters => GetTargetDistanceMeters();
        public float EpisodeTravelDistanceMeters => episodeTravelDistanceMeters;
        public string TargetStopObjectName => TryGetStop(targetStopId, out DRTStop stop) ? stop.name : "-";
        public bool BackgroundTrafficEnabled => backgroundTrafficEnabled;
        public int ActiveBackgroundVehicleCount => CountActiveBackgroundTrafficVehicles();
        public DRTTravelExecutionMode TravelExecutionMode => travelExecutionMode;
        public string TravelExecutionModeName => travelExecutionMode.ToString();
        public bool UsesMatrixTeleportTraining => travelExecutionMode == DRTTravelExecutionMode.MatrixTeleportTraining;
        public bool UsesGleyVehicleControl => travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive && useGleyVehicleControlInPhysicalDrive;
        public bool SuppressUnityLogsDuringMatrixTraining => UsesMatrixTeleportTraining && suppressUnityLogsDuringMatrixTraining;
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
            WireNextStopSelector();
            ResolveControlledVehicle(false);
        }

        private void Awake()
        {
            ResolveReferences();
            LoadStops(false);
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
            ResetInferenceExportState();
            nextMovementDiagnosticTime = 0f;
            hasVehicleMovementSample = false;
            hasTrafficBlockSample = false;
            trafficBlockReason = string.Empty;
            episodeTravelDistanceMeters = 0f;
            hasTravelDistanceSample = false;
            travelTimeMatrixLoadAttempted = false;
            lastVehicleMovementRealtime = Time.realtimeSinceStartup;
            lastVehicleMovementPosition = Vector3.zero;

            if (demandGenerator != null && passengerManager != null && stops.Count > 0)
            {
                demandGenerator.Configure(passengerManager, stops.Count);
            }

            demandGenerator?.ResetDemand(SuppressUnityLogsDuringMatrixTraining);

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

            if (!UsesMatrixTeleportTraining)
            {
                episodeTimeSeconds += Time.deltaTime * simulationSecondsPerRealSecond;
            }

            AdvanceDemandToCurrentTime();
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            TrackPhysicalTravelDistanceIfNeeded();
            RecordVehicleTraceIfNeeded();
            LogMovementDiagnosticsIfNeeded();
            if (!UsesMatrixTeleportTraining)
            {
                MonitorVehicleFailureIfNeeded();
            }

            if (episodeFinished)
            {
                return;
            }

            if (episodeTimeSeconds >= episodeLengthSeconds && !HasUnfinishedOrPendingRequests())
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
                if (stop == null)
                {
                    stop = child.gameObject.AddComponent<DRTStop>();
                }

                if (TryParseStopIdFromName(child.name, out int parsedStopId))
                {
                    stop.SetStopId(parsedStopId);
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
                .Select(group => group.Key)
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
                ApplyPhysicalDriveInferencePolicy();
            }
        }

        private void ApplyPhysicalDriveInferencePolicy()
        {
            if (nextStopSelector == null)
            {
                return;
            }

            var behaviorParameters = nextStopSelector.GetComponent<BehaviorParameters>();
            if (behaviorParameters == null)
            {
                return;
            }

            if (travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive && physicalDriveInferenceModel != null)
            {
                behaviorParameters.Model = physicalDriveInferenceModel;
                behaviorParameters.InferenceDevice = physicalDriveInferenceDevice;
            }

            if (travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive && behaviorParameters.Model != null)
            {
                behaviorParameters.BehaviorType = BehaviorType.InferenceOnly;

                return;
            }

            if (travelExecutionMode == DRTTravelExecutionMode.MatrixTeleportTraining)
            {
                behaviorParameters.BehaviorType = BehaviorType.Default;
            }
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
            if (UsesMatrixTeleportTraining && !EnsureTravelTimeMatrix())
            {
                initialized = false;
                return;
            }

            ApplyCameraFollow(GetControlledVehicleTransform());
            LogInfo(
                $"[BUSCONTROLLER] Initialized mode={TravelExecutionModeName}, " +
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

            if (UsesMatrixTeleportTraining)
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

            if (nextStopId < 1)
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

            if (UsesMatrixTeleportTraining)
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

            if (logMatrixTravel && !SuppressUnityLogsDuringMatrixTraining)
            {
                Debug.Log(
                    $"[BUSCONTROLLER] MatrixTeleport from={originStopId}, to={nextStop.StopId}, " +
                    $"travel={travelSeconds:0.00}s, episodeTime={episodeTimeSeconds:0.00}s");
            }

            if (ProcessStopArrivalAndMaybeFinish(nextStop.StopId))
            {
                yield break;
            }

            yield return null;
            SendToNextStop();
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
                SuppressUnityLogsDuringMatrixTraining);
            CompleteActiveRouteLeg(currentStopId, stopResult);
            nextStopSelector.RecordStopArrival(stopResult, episodeTimeSeconds);

            if (stopWhenAllRequestsCompleted && !HasUnfinishedOrPendingRequests())
            {
                FinishEpisode("All passenger requests completed.");
                return true;
            }

            if (episodeTimeSeconds >= episodeLengthSeconds && !HasUnfinishedOrPendingRequests())
            {
                FinishEpisode("Episode time ended.");
                return true;
            }

            return false;
        }

        private void AdvanceDemandToCurrentTime()
        {
            demandGenerator?.SpawnDueRequests(episodeTimeSeconds, SuppressUnityLogsDuringMatrixTraining);
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
            if (!ShouldCollectInferenceExportData())
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
                Mode = GetInferenceExportModeToken(),
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

            routeLegRecords.Add(activeRouteLeg);
            string traceEvent = activeRouteLeg.Completed ? "stop_arrival" : "leg_incomplete";
            activeRouteLeg = null;
            RecordVehicleTrace(traceEvent);
        }

        private void ResetInferenceExportState()
        {
            routeLegRecords.Clear();
            vehicleTraceRecords.Clear();
            activeRouteLeg = null;
            nextVehicleTraceSampleTime = 0f;
            inferenceCsvExported = false;
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

        [ContextMenu("Regenerate DRT Stop Travel Time Matrix CSV")]
        public void RegenerateTravelTimeMatrixCsv()
        {
            ResolveReferences();
            LoadStops(false);

            if (!API.IsInitialized())
            {
                Debug.LogWarning("[BUSCONTROLLER] Gley Traffic API is not initialized. Start Play Mode before regenerating the matrix.");
                return;
            }

            if (!TryGenerateTravelTimeMatrixFromGley(out string csvText, out string error))
            {
                Debug.LogError($"[BUSCONTROLLER] Travel time matrix generation failed. {error}");
                return;
            }

            SaveTravelTimeMatrixCsvAsset(csvText, true);
            travelTimeMatrixLoadAttempted = false;
            Debug.Log($"[BUSCONTROLLER] Travel time matrix regenerated. stops={travelTimeMatrix.StopCount}");
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

            if (preferGeneratedMatrixFromGley && API.IsInitialized() && autoGenerateMatrixFromGleyWhenMissing)
            {
                if (TryGenerateTravelTimeMatrixFromGley(out string generatedCsvFromGley, out string generatedErrorFromGley))
                {
                    SaveTravelTimeMatrixCsvAsset(generatedCsvFromGley, false);
                    LogInfo(
                        $"[BUSCONTROLLER] Generated travel time matrix from Gley paths. " +
                        $"stops={travelTimeMatrix.StopCount}, speed={matrixNominalSpeedMetersPerSecond:0.00}m/s");
                    return true;
                }

                Debug.LogWarning($"[BUSCONTROLLER] Preferred Gley travel time matrix generation failed. {generatedErrorFromGley}");
            }

            TextAsset csvAsset = Resources.Load<TextAsset>(travelTimeMatrixResourceName);
            string loadError = null;
            if (csvAsset != null && travelTimeMatrix.LoadFromCsv(csvAsset.text, stops, out loadError))
            {
                LogInfo(
                    $"[BUSCONTROLLER] Loaded travel time matrix resource={travelTimeMatrixResourceName}, " +
                    $"stops={travelTimeMatrix.StopCount}");
                return true;
            }

            if (csvAsset != null)
            {
                Debug.LogWarning($"[BUSCONTROLLER] Travel time matrix CSV invalid. {loadError}");
            }

            if (!autoGenerateMatrixFromGleyWhenMissing)
            {
                Debug.LogError($"[BUSCONTROLLER] Travel time matrix unavailable. resource={travelTimeMatrixResourceName}");
                return false;
            }

            if (!TryGenerateTravelTimeMatrixFromGley(out string generatedCsv, out string generationError))
            {
                Debug.LogError($"[BUSCONTROLLER] Travel time matrix auto-generation failed. {generationError}");
                return false;
            }

            SaveTravelTimeMatrixCsvAsset(generatedCsv, false);
            LogInfo(
                $"[BUSCONTROLLER] Generated travel time matrix from Gley paths. " +
                $"stops={travelTimeMatrix.StopCount}, speed={matrixNominalSpeedMetersPerSecond:0.00}m/s");
            return true;
        }

        private bool TryGenerateTravelTimeMatrixFromGley(out string csvText, out string error)
        {
            return travelTimeMatrix.GenerateFromGleyPaths(
                stops,
                GetStopServicePoint,
                controlledVehicleType,
                matrixNominalSpeedMetersPerSecond,
                out csvText,
                out error);
        }

        private void SaveTravelTimeMatrixCsvAsset(string csvText, bool force)
        {
#if UNITY_EDITOR
            if (!force && !saveGeneratedMatrixAssetInEditor)
            {
                return;
            }

            string resourcesPath = System.IO.Path.Combine(Application.dataPath, "DRT", "Resources");
            Directory.CreateDirectory(resourcesPath);

            string fileName = string.IsNullOrWhiteSpace(travelTimeMatrixResourceName)
                ? "drt_stop_travel_time_matrix"
                : travelTimeMatrixResourceName;
            string csvPath = System.IO.Path.Combine(resourcesPath, fileName + ".csv");
            File.WriteAllText(csvPath, csvText);
            AssetDatabase.Refresh();
            Debug.Log($"[BUSCONTROLLER] Saved travel time matrix CSV to {csvPath}");
#endif
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
            if (UsesMatrixTeleportTraining || !initialized || episodeFinished || GetControlledVehicleTransform() == null)
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
            if (!ShouldCollectInferenceExportData() || vehicleTraceSampleIntervalSeconds <= 0f)
            {
                return;
            }

            if (episodeTimeSeconds + 0.0001f < nextVehicleTraceSampleTime)
            {
                return;
            }

            RecordVehicleTrace("sample");
            nextVehicleTraceSampleTime = episodeTimeSeconds + vehicleTraceSampleIntervalSeconds;
        }

        private void RecordVehicleTrace(string eventName)
        {
            if (!ShouldCollectInferenceExportData())
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
            if (SuppressUnityLogsDuringMatrixTraining)
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

            bool completedAllRequests = !HasUnfinishedOrPendingRequests();
            episodeFinished = true;
            driving = false;
            waitingForArrivalProximity = false;
            CompleteActiveRouteLeg(-1, null);
            RecordVehicleTrace("episode_finished");
            vehicleDriver?.StopAndHold(true);

            LogInfo($"[BUSCONTROLLER] Episode finished. {reason}");

            if (logEpisodeSummary && passengerManager != null && !SuppressUnityLogsDuringMatrixTraining)
            {
                passengerManager.LogSummary();
            }

            float averageWaitSeconds = passengerManager != null ? passengerManager.GetAverageConfirmedWaitTime() : 0f;
            float averageRideSeconds = passengerManager != null ? passengerManager.GetAverageCompletedRideTime() : 0f;
            float serviceRate = passengerManager != null ? passengerManager.GetServiceRate() : 0f;
            int completedCount = passengerManager != null ? passengerManager.GetCompletedCount() : 0;
            ExportInferenceCsvIfNeeded(reason, completedAllRequests);

            nextStopSelector?.NotifyEpisodeFinished(
                completedAllRequests,
                episodeTravelDistanceMeters,
                averageWaitSeconds,
                averageRideSeconds,
                serviceRate,
                completedCount);
        }

        private void ExportInferenceCsvIfNeeded(string finishReason, bool completedAllRequests)
        {
            if (inferenceCsvExported || !ShouldCollectInferenceExportData() || passengerManager == null)
            {
                return;
            }

            inferenceCsvExported = true;

            try
            {
                string exportDirectory = GetInferenceExportDirectory();
                Directory.CreateDirectory(exportDirectory);

                string prefix = BuildInferenceExportPrefix();
                string passengerPath = System.IO.Path.Combine(exportDirectory, $"{prefix}_passengers.csv");
                string routePath = System.IO.Path.Combine(exportDirectory, $"{prefix}_route_legs.csv");
                string tracePath = System.IO.Path.Combine(exportDirectory, $"{prefix}_vehicle_trace.csv");
                string summaryPath = System.IO.Path.Combine(exportDirectory, $"{prefix}_summary.csv");

                File.WriteAllText(passengerPath, BuildPassengerCsv(), Encoding.UTF8);
                File.WriteAllText(routePath, BuildRouteLegCsv(), Encoding.UTF8);
                File.WriteAllText(tracePath, BuildVehicleTraceCsv(), Encoding.UTF8);
                File.WriteAllText(summaryPath, BuildSummaryCsv(finishReason, completedAllRequests), Encoding.UTF8);

                Debug.Log(
                    $"[BUSCONTROLLER] Inference CSV exported. prefix={prefix}, " +
                    $"directory={exportDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BUSCONTROLLER] Failed to export inference CSV. {exception}");
            }
        }

        private bool ShouldCollectInferenceExportData()
        {
            return exportInferenceCsvOnEpisodeEnd && !IsMlAgentsTrainingSession();
        }

        private static bool IsMlAgentsTrainingSession()
        {
            return Academy.Instance != null && Academy.Instance.IsCommunicatorOn;
        }

        private string GetInferenceExportDirectory()
        {
#if UNITY_EDITOR
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            return System.IO.Path.Combine(projectRoot, "DRT_Inference_Exports");
#else
            return System.IO.Path.Combine(Application.persistentDataPath, "DRT_Inference_Exports");
#endif
        }

        private string BuildInferenceExportPrefix()
        {
            string modeToken = GetInferenceExportModeToken();
            string scenarioId = demandGenerator != null
                ? demandGenerator.ExportScenarioId
                : Mathf.Max(0, passengerManager != null ? passengerManager.Requests.Count : 0).ToString(CultureInfo.InvariantCulture);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return SanitizeFileName($"임퍼런스_{modeToken}_시나리오_{scenarioId}_ep{episodeIndex:000}_{timestamp}");
        }

        private string GetInferenceExportModeToken()
        {
            return travelExecutionMode == DRTTravelExecutionMode.PhysicalDrive
                ? "리얼드라이브"
                : "매트릭스";
        }

        private string BuildPassengerCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("passenger_id,origin_stop_id,destination_stop_id,request_time_seconds,status,pickup_time_seconds,dropoff_time_seconds,actual_pickup_stop_id,actual_dropoff_stop_id,wait_time_seconds,ride_time_seconds,total_service_time_seconds");

            foreach (var request in passengerManager.Requests.OrderBy(request => request.PassengerId))
            {
                builder
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

            return builder.ToString();
        }

        private string BuildRouteLegCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("leg_index,mode,from_stop_id,to_stop_id,arrived_stop_id,completed,departure_time_seconds,arrival_time_seconds,travel_time_seconds,leg_distance_meters,cumulative_distance_meters,planned_path_distance_meters,path_waypoint_count,boarded_count,dropped_off_count,waiting_count,on_board_count,completed_passenger_count");

            foreach (var leg in routeLegRecords)
            {
                builder
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
                    .Append(leg.WaitingCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.OnBoardCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(leg.CompletedPassengerCount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private string BuildVehicleTraceCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("sample_index,event,episode_time_seconds,current_stop_id,target_stop_id,x,y,z,cumulative_distance_meters,speed_mps,blocked,block_reason");

            foreach (var trace in vehicleTraceRecords)
            {
                builder
                    .Append(trace.SampleIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(CsvEscape(trace.EventName)).Append(',')
                    .Append(FormatCsvFloat(trace.EpisodeTimeSeconds)).Append(',')
                    .Append(FormatCsvIntOrBlank(trace.CurrentStopId)).Append(',')
                    .Append(FormatCsvIntOrBlank(trace.TargetStopId)).Append(',')
                    .Append(FormatCsvFloat(trace.Position.x)).Append(',')
                    .Append(FormatCsvFloat(trace.Position.y)).Append(',')
                    .Append(FormatCsvFloat(trace.Position.z)).Append(',')
                    .Append(FormatCsvFloat(trace.CumulativeDistanceMeters)).Append(',')
                    .Append(FormatCsvFloat(trace.SpeedMetersPerSecond)).Append(',')
                    .Append(trace.Blocked ? "1" : "0").Append(',')
                    .Append(CsvEscape(trace.BlockReason))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private string BuildSummaryCsv(string finishReason, bool completedAllRequests)
        {
            var builder = new StringBuilder();
            builder.AppendLine("key,value");
            AppendSummaryRow(builder, "episode_index", episodeIndex.ToString(CultureInfo.InvariantCulture));
            AppendSummaryRow(builder, "mode", GetInferenceExportModeToken());
            AppendSummaryRow(builder, "travel_execution_mode", TravelExecutionModeName);
            AppendSummaryRow(builder, "scenario_id", demandGenerator != null ? demandGenerator.ExportScenarioId : string.Empty);
            AppendSummaryRow(builder, "scenario_description", demandGenerator != null ? demandGenerator.LoadedScenarioDescription : string.Empty);
            AppendSummaryRow(builder, "finish_reason", finishReason);
            AppendSummaryRow(builder, "completed_all_requests", completedAllRequests ? "1" : "0");
            AppendSummaryRow(builder, "episode_time_seconds", FormatCsvFloat(episodeTimeSeconds));
            AppendSummaryRow(builder, "episode_distance_meters", FormatCsvFloat(episodeTravelDistanceMeters));
            AppendSummaryRow(builder, "total_passengers", passengerManager != null ? passengerManager.Requests.Count.ToString(CultureInfo.InvariantCulture) : "0");
            AppendSummaryRow(builder, "completed_passengers", passengerManager != null ? passengerManager.GetCompletedCount().ToString(CultureInfo.InvariantCulture) : "0");
            AppendSummaryRow(builder, "service_rate", passengerManager != null ? FormatCsvFloat(passengerManager.GetServiceRate()) : "0");
            AppendSummaryRow(builder, "average_wait_seconds", passengerManager != null ? FormatCsvFloat(passengerManager.GetAverageConfirmedWaitTime()) : "0");
            AppendSummaryRow(builder, "average_ride_seconds", passengerManager != null ? FormatCsvFloat(passengerManager.GetAverageCompletedRideTime()) : "0");
            return builder.ToString();
        }

        private static void AppendSummaryRow(StringBuilder builder, string key, string value)
        {
            builder
                .Append(CsvEscape(key))
                .Append(',')
                .Append(CsvEscape(value))
                .AppendLine();
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
