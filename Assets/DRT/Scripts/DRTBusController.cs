using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
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
        [SerializeField] private float playerWaypointReachDistanceMeters = 6f;
        [SerializeField] private bool autoFollowControlledVehicle = true;

        [Header("Travel Execution")]
        [SerializeField] private DRTTravelExecutionMode travelExecutionMode = DRTTravelExecutionMode.MatrixTeleportTraining;
        [SerializeField] private string travelTimeMatrixResourceName = "drt_stop_travel_time_matrix";
        [SerializeField] private float matrixNominalSpeedMetersPerSecond = 15f;
        [SerializeField] private bool preferGeneratedMatrixFromGley = true;
        [SerializeField] private bool autoGenerateMatrixFromGleyWhenMissing = true;
        [SerializeField] private bool saveGeneratedMatrixAssetInEditor;
        [SerializeField] private bool logMatrixTravel = true;
        [SerializeField] private bool suppressUnityLogsDuringMatrixTraining = true;

        [Header("Diagnostics")]
        [SerializeField] private bool logMovementDiagnostics;
        [SerializeField] private float movementDiagnosticsIntervalSeconds = 2f;

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
        [SerializeField] private float minimumVehicleMovementMeters = 1f;
        [SerializeField] private float maxRoadWaypointDistanceMeters = 30f;
        [SerializeField] private float fallYThreshold = -10f;

        private readonly List<DRTStop> stops = new List<DRTStop>();
        private readonly DRTStopTravelTimeMatrix travelTimeMatrix = new DRTStopTravelTimeMatrix();
        private DRTPlayerVehicleDriver playerVehicleDriver;
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
        private float nextMovementDiagnosticTime;
        private Vector3 lastVehicleMovementPosition;
        private float lastVehicleMovementRealtime;
        private bool hasVehicleMovementSample;
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
        public string ControlledVehicleName => controlledPlayerVehicle != null ? controlledPlayerVehicle.name : "-";
        public float ArrivalDistanceMeters => arrivalDistanceMeters;
        public float VehicleSpeedMS => GetVehicleSpeedMS();
        public float TargetDistanceMeters => GetTargetDistanceMeters();
        public string TargetStopObjectName => TryGetStop(targetStopId, out DRTStop stop) ? stop.name : "-";
        public bool BackgroundTrafficEnabled => backgroundTrafficEnabled;
        public int ActiveBackgroundVehicleCount => CountActiveBackgroundTrafficVehicles();
        public DRTTravelExecutionMode TravelExecutionMode => travelExecutionMode;
        public string TravelExecutionModeName => travelExecutionMode.ToString();
        public bool UsesMatrixTeleportTraining => travelExecutionMode == DRTTravelExecutionMode.MatrixTeleportTraining;
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
            ResolveControlledPlayerVehicle(false);
        }

        private void Awake()
        {
            ResolveReferences();
            LoadStops(false);
            WireNextStopSelector();
            ResolveControlledPlayerVehicle(false);
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
            currentStopId = UsesMatrixTeleportTraining ? startStopId : 0;
            targetStopId = 0;
            nextMovementDiagnosticTime = 0f;
            hasVehicleMovementSample = false;
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

            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            LogMovementDiagnosticsIfNeeded();
            if (!UsesMatrixTeleportTraining)
            {
                MonitorVehicleFailureIfNeeded();
            }

            if (episodeFinished)
            {
                return;
            }

            if (episodeTimeSeconds >= episodeLengthSeconds && passengerManager != null && passengerManager.GetOnBoardCount() == 0)
            {
                FinishEpisode("Episode time ended.");
            }
        }

        private void OnDisable()
        {
            playerVehicleDriver?.ReleaseControl();
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
            }
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

            return true;
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

            if (!ResolveControlledPlayerVehicle(true))
            {
                return;
            }

            initialized = true;
            currentStopId = UsesMatrixTeleportTraining ? startStopId : 0;
            targetStopId = 0;
            EnsureBackgroundTrafficStateInitialized();
            ApplyBackgroundTrafficState();
            ResetControlledVehicleForEpisode();
            if (UsesMatrixTeleportTraining && !EnsureTravelTimeMatrix())
            {
                initialized = false;
                return;
            }

            ApplyCameraFollow(controlledPlayerVehicle);
            LogInfo(
                $"[BUSCONTROLLER] Initialized playerVehicle={ControlledVehicleName}, " +
                $"mode={TravelExecutionModeName}, firstTargetHint={startStopId}");
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
            playerVehicleDriver?.StopAndHold(true);

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
            playerVehicleDriver?.StopAndHold(false);
        }

        private void ResetControlledVehicleForEpisode()
        {
            if (!ResolveControlledPlayerVehicle(true))
            {
                return;
            }

            if (TryGetStartWaypoint(out TrafficWaypoint startWaypoint))
            {
                Quaternion rotation = GetWaypointForwardRotation(startWaypoint, controlledPlayerVehicle.rotation);
                playerVehicleDriver.TeleportTo(startWaypoint.Position, rotation);
                ApplyCameraFollow(controlledPlayerVehicle);
                ResetLegSafetyState(GetControlledVehicleBodyPosition());
                return;
            }

            playerVehicleDriver.StopAndHold(true);
            ApplyCameraFollow(controlledPlayerVehicle);
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
                        $"[BUSCONTROLLER] Player vehicle arrived for Stop {reachedStopId}, " +
                        $"but vehicle-stop distance is {FormatMeters(distance)} > {arrivalDistanceMeters:0.00}m. " +
                        "Boarding/dropoff skipped. Move the BusStop closer to the road waypoint or increase the arrival threshold.");
                    waitingForArrivalProximity = false;
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
                ResolveControlledPlayerVehicle(false);
            }
            else if (!ResolveControlledPlayerVehicle(true))
            {
                driving = false;
                yield break;
            }

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
            var path = API.GetPath(GetControlledVehicleBodyPosition(), servicePoint, controlledVehicleType);
            if (path == null || path.Count == 0)
            {
                driving = false;
                Debug.LogWarning(
                    $"[BUSCONTROLLER] PathAssignmentSkipped playerVehicle={ControlledVehicleName}, candidateStop={nextStop.StopId}, " +
                    $"candidateObject={nextStop.name}. " +
                    "Check that this BusStop is close to a Gley traffic waypoint and allowed for this vehicle type.");
                FinishFailedEpisode($"Path assignment failed. Requested Stop={nextStop.StopId}");
                yield break;
            }

            if (!playerVehicleDriver.SetPath(path, servicePoint))
            {
                driving = false;
                FinishFailedEpisode($"Player path assignment failed. Requested Stop={nextStop.StopId}");
                yield break;
            }

            targetStopId = nextStop.StopId;
            driving = true;
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
            LogPathAssignment(nextStop, servicePoint, path);
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

            episodeTimeSeconds += travelSeconds;
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            TeleportControlledVehicleToStop(nextStop);

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

            var stopResult = passengerManager.ProcessStopArrival(
                currentStopId,
                episodeTimeSeconds,
                SuppressUnityLogsDuringMatrixTraining);
            nextStopSelector.RecordStopArrival(stopResult, episodeTimeSeconds);

            if (stopWhenAllRequestsCompleted && !passengerManager.HasUnfinishedRequests(episodeTimeSeconds))
            {
                FinishEpisode("All passenger requests completed.");
                return true;
            }

            if (episodeTimeSeconds >= episodeLengthSeconds && passengerManager.GetOnBoardCount() == 0)
            {
                FinishEpisode("Episode time ended.");
                return true;
            }

            return false;
        }

        private void TeleportControlledVehicleToStop(DRTStop stop)
        {
            if (stop == null || !ResolveControlledPlayerVehicle(false))
            {
                return;
            }

            Vector3 servicePoint = GetStopServicePoint(stop);
            Quaternion rotation = controlledPlayerVehicle != null ? controlledPlayerVehicle.rotation : Quaternion.identity;
            TrafficWaypoint closestWaypoint = API.IsInitialized() ? API.GetClosestWaypoint(servicePoint) : null;
            if (closestWaypoint != null)
            {
                rotation = GetWaypointForwardRotation(closestWaypoint, rotation);
            }

            playerVehicleDriver.TeleportTo(servicePoint, rotation);
            ApplyCameraFollow(controlledPlayerVehicle);
            ResetLegSafetyState(GetControlledVehicleBodyPosition());
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
            if (controlledPlayerVehicle == null || stop == null)
            {
                return;
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            var closestStopWaypoint = API.GetClosestWaypoint(stop.Position);
            var closestPlayerWaypoint = API.GetClosestWaypoint(bodyPosition);
            float stopToWaypointDistance = closestStopWaypoint != null
                ? GetPlanarDistance(stop.Position, closestStopWaypoint.Position)
                : float.PositiveInfinity;
            float bodyToStopDistance = GetPlanarDistance(bodyPosition, stop.Position);
            float bodyToServiceDistance = GetPlanarDistance(bodyPosition, servicePoint);
            int currentWaypoint = closestPlayerWaypoint != null ? closestPlayerWaypoint.ListIndex : -1;

            Debug.Log(
                $"[BUSCONTROLLER] RouteRequest playerVehicle={ControlledVehicleName}, currentWaypoint={currentWaypoint}, " +
                $"targetStop={stop.StopId}, targetObject={stop.name}, bodyToService={FormatMeters(bodyToServiceDistance)}, " +
                $"bodyToStopMarker={FormatMeters(bodyToStopDistance)}, " +
                $"servicePoint={FormatVector(servicePoint)}, stopMarker={FormatVector(stop.Position)}, " +
                $"stopToClosestWaypoint={FormatMeters(stopToWaypointDistance)}");
        }

        private void LogPathAssignment(DRTStop targetStop, Vector3 servicePoint, List<int> path)
        {
            string endpointDescription = DescribePathEndpoint(path, servicePoint, out float endToServiceDistance);
            float warningDistance = Mathf.Max(15f, arrivalDistanceMeters + stopWaypointSnapDistanceMeters);
            string integrity = endToServiceDistance <= warningDistance ? "ok" : "warning";

            string message =
                $"[BUSCONTROLLER] PathAssigned integrity={integrity}, playerVehicle={ControlledVehicleName}, " +
                $"targetStop={targetStop.StopId}, targetObject={targetStop.name}, pathWaypoints={path.Count}, " +
                $"servicePoint={FormatVector(servicePoint)}, {endpointDescription}";

            if (integrity == "warning")
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }
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

            if (!ResolveControlledPlayerVehicle(true) || !controlledPlayerVehicle.gameObject.activeSelf)
            {
                FinishFailedEpisode("Player vehicle is missing or inactive.");
                return;
            }

            Vector3 bodyPosition = GetControlledVehicleBodyPosition();
            if (bodyPosition.y <= fallYThreshold)
            {
                FinishFailedEpisode($"Player vehicle fell below safety Y threshold. y={bodyPosition.y:0.00}");
                return;
            }

            if (IsTooFarFromTrafficWaypoint(bodyPosition, out float waypointDistance))
            {
                FinishFailedEpisode($"Player vehicle left traffic network. nearestWaypointDistance={FormatMeters(waypointDistance)}");
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

            float stillSeconds = Time.realtimeSinceStartup - lastVehicleMovementRealtime;
            if (stillSeconds >= noMovementTimeoutRealSeconds)
            {
                FinishFailedEpisode(
                    $"Player vehicle body unchanged for {noMovementTimeoutRealSeconds:0.0}s real time. " +
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
                $"[BUSCONTROLLER] MoveStatus playerVehicle={ControlledVehicleName}, targetStop={targetStopId}, " +
                $"distance={FormatMeters(GetTargetDistanceMeters())}, speed={GetVehicleSpeedMS():0.00}m/s, " +
                $"pathPoints={playerVehicleDriver?.PathPointCount}, remainingPoints={playerVehicleDriver?.RemainingPathPointCount}, " +
                $"waitingForArrival={waitingForArrivalProximity}");
        }

        private float GetVehicleSpeedMS()
        {
            if (playerVehicleDriver != null)
            {
                return playerVehicleDriver.CurrentSpeedMS;
            }

            if (controlledPlayerVehicle == null)
            {
                return 0f;
            }

            var body = controlledPlayerVehicle.GetComponent<Rigidbody>();
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

            if (!ResolveControlledPlayerVehicle(false))
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
            if (playerVehicleDriver != null)
            {
                return playerVehicleDriver.BodyPosition;
            }

            return controlledPlayerVehicle != null ? controlledPlayerVehicle.position : Vector3.zero;
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

            bool completedAllRequests = passengerManager != null &&
                                        !passengerManager.HasUnfinishedRequests(episodeTimeSeconds);
            episodeFinished = true;
            driving = false;
            waitingForArrivalProximity = false;
            playerVehicleDriver?.StopAndHold(true);

            LogInfo($"[BUSCONTROLLER] Episode finished. {reason}");

            if (logEpisodeSummary && passengerManager != null && !SuppressUnityLogsDuringMatrixTraining)
            {
                passengerManager.LogSummary();
            }

            nextStopSelector?.NotifyEpisodeFinished(completedAllRequests);
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
            playerWaypointReachDistanceMeters = Mathf.Max(0.5f, playerWaypointReachDistanceMeters);
            matrixNominalSpeedMetersPerSecond = Mathf.Max(0.1f, matrixNominalSpeedMetersPerSecond);
            enabledTrafficDensity = Mathf.Max(1, enabledTrafficDensity);
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
            simulationSecondsPerRealSecond = Mathf.Max(0.01f, simulationSecondsPerRealSecond);
            movementDiagnosticsIntervalSeconds = Mathf.Max(0.25f, movementDiagnosticsIntervalSeconds);
            noMovementTimeoutRealSeconds = Mathf.Max(1f, noMovementTimeoutRealSeconds);
            minimumVehicleMovementMeters = Mathf.Max(0.01f, minimumVehicleMovementMeters);
            maxRoadWaypointDistanceMeters = Mathf.Max(0f, maxRoadWaypointDistanceMeters);
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
