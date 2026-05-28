using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
using UnityEngine;

namespace DRT
{
    public class DRTBusController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform busStopsRoot;
        [SerializeField] private DRTPassengerManager passengerManager;
        [SerializeField] private DRTDemandGenerator demandGenerator;
        [SerializeField] private DRTNextStopSelector nextStopSelector;

        [Header("Vehicle")]
        [SerializeField] private int vehicleIndex = 0;
        [SerializeField] private int startStopId = 1;
        [SerializeField] private float dwellSeconds = 5f;
        [SerializeField] private float arrivalDistanceMeters = 5f;
        [SerializeField] private float stopWaypointSnapDistanceMeters = 5f;
        [SerializeField] private float arrivalWaitTimeoutSeconds = 12f;
        [SerializeField] private float controlledVehicleSpeedMultiplier = 1.5f;
        [SerializeField] private bool autoFollowControlledVehicle = true;

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

        private readonly List<DRTStop> stops = new List<DRTStop>();
        private float episodeTimeSeconds;
        private bool initialized;
        private bool driving;
        private bool episodeFinished;
        private bool waitingForArrivalProximity;
        private bool cameraFollowApplied;
        private bool backgroundTrafficEnabled;
        private int currentStopId;
        private int targetStopId;
        private Coroutine dwellRoutine;
        private Coroutine decisionRoutine;
        private float nextMovementDiagnosticTime;

        public float EpisodeTimeSeconds => episodeTimeSeconds;
        public bool IsInitialized => initialized;
        public bool IsDriving => driving;
        public bool IsEpisodeFinished => episodeFinished;
        public bool IsWaitingForArrivalProximity => waitingForArrivalProximity;
        public int CurrentStopId => currentStopId;
        public int TargetStopId => targetStopId;
        public int VehicleIndex => vehicleIndex;
        public float ArrivalDistanceMeters => arrivalDistanceMeters;
        public float VehicleSpeedMS => GetVehicleSpeedMS();
        public float TargetDistanceMeters => GetTargetDistanceMeters();
        public string TargetStopObjectName => TryGetStop(targetStopId, out DRTStop stop) ? stop.name : "-";
        public bool BackgroundTrafficEnabled => backgroundTrafficEnabled;
        public int ActiveBackgroundVehicleCount => CountActiveBackgroundTrafficVehicles();
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
        }

        private void Awake()
        {
            ResolveReferences();
            LoadStops(false);
        }

        private void OnEnable()
        {
            Events.OnDestinationReached += DestinationReachedHandler;
        }

        private void Start()
        {
            if (demandGenerator != null && !demandGenerator.HasGenerated)
            {
                demandGenerator.GenerateDemand();
            }
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

            episodeTimeSeconds += Time.deltaTime * simulationSecondsPerRealSecond;
            passengerManager?.UpdateRequestStates(episodeTimeSeconds);
            LogMovementDiagnosticsIfNeeded();

            if (episodeTimeSeconds >= episodeLengthSeconds && passengerManager != null && passengerManager.GetOnBoardCount() == 0)
            {
                FinishEpisode("Episode time ended.");
            }
        }

        private void OnDisable()
        {
            Events.OnDestinationReached -= DestinationReachedHandler;
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

            Debug.Log($"[BUSCONTROLLER] Loaded stops count={stops.Count}, root={busStopsRoot.name}");
            LogStopMap();
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

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null || !vehicle.gameObject.activeSelf)
            {
                return;
            }

            initialized = true;
            currentStopId = 0;
            targetStopId = 0;
            API.DontRemoveVehicle(vehicleIndex, true);
            backgroundTrafficEnabled = backgroundTrafficEnabledOnStart;
            ApplyBackgroundTrafficState();
            ApplyControlledVehicleSettings(vehicle);
            ApplyCameraFollow(vehicle);
            Debug.Log($"[BUSCONTROLLER] Initialized vehicle={vehicleIndex}, firstTargetHint={startStopId}, vehicleName={vehicle.name}");
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
            ApplyBackgroundTrafficState();
        }

        private void DestinationReachedHandler(int reachedVehicleIndex)
        {
            if (!initialized || episodeFinished || reachedVehicleIndex != vehicleIndex || !driving)
            {
                return;
            }

            driving = false;

            if (dwellRoutine != null)
            {
                StopCoroutine(dwellRoutine);
            }

            dwellRoutine = StartCoroutine(WaitForProximityThenDwell(targetStopId));
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
                        $"[BUSCONTROLLER] Vehicle {vehicleIndex} reached Gley destination for Stop {reachedStopId}, " +
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

            currentStopId = reachedStopId;
            var stopResult = passengerManager.ProcessStopArrival(currentStopId, episodeTimeSeconds);
            nextStopSelector.RecordStopArrival(stopResult, episodeTimeSeconds);

            if (stopWhenAllRequestsCompleted && !passengerManager.HasUnfinishedRequests(episodeTimeSeconds))
            {
                FinishEpisode("All passenger requests completed.");
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

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null || !vehicle.gameObject.activeSelf)
            {
                driving = false;
                Debug.LogWarning($"[BUSCONTROLLER] Vehicle {vehicleIndex} is not active. Cannot send it to next stop yet.");
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

            ApplyControlledVehicleSettings(vehicle);

            Vector3 servicePoint = GetStopServicePoint(nextStop);
            LogRouteDiagnostics(nextStop, servicePoint);
            var path = API.GetPath(vehicle.transform.position, servicePoint, vehicle.VehicleType);
            if (path == null || path.Count == 0)
            {
                driving = false;
                Debug.LogWarning(
                    $"[BUSCONTROLLER] PathAssignmentSkipped vehicle={vehicleIndex}, candidateStop={nextStop.StopId}, " +
                    $"candidateObject={nextStop.name}. " +
                    "Check that this BusStop is close to a Gley traffic waypoint and allowed for this vehicle type.");
                yield break;
            }

            API.SetVehiclePath(vehicleIndex, path);
            targetStopId = nextStop.StopId;
            driving = true;
            LogPathAssignment(vehicle, nextStop, servicePoint, path);
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

        private bool HasStop(int stopId)
        {
            return TryGetStop(stopId, out _);
        }

        private void ApplyCameraFollow(VehicleComponent vehicle)
        {
            if (!autoFollowControlledVehicle || cameraFollowApplied || vehicle == null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var cameraFollow = mainCamera.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    cameraFollow.target = vehicle.transform;
                }
            }

            API.SetCamera(vehicle.transform);
            cameraFollowApplied = true;
        }

        private void ApplyControlledVehicleSettings(VehicleComponent vehicle)
        {
            if (vehicle == null || vehicle.MovementInfo == null)
            {
                return;
            }

            float multiplier = Mathf.Max(0.1f, controlledVehicleSpeedMultiplier);
            float exactReductionPercent = 1f - multiplier;
            float exactIncreasePercent = multiplier - 1f;
            vehicle.MovementInfo.SetSpeedVariationPercent(exactReductionPercent, exactIncreasePercent);
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
                Debug.Log($"[BUSCONTROLLER] Background traffic enabled. density={enabledTrafficDensity}");
                return;
            }

            API.SetTrafficDensity(0);
            int removedCount = RemoveBackgroundTrafficVehicles();
            Debug.Log($"[BUSCONTROLLER] Background traffic disabled. removed={removedCount}, busVehicle={vehicleIndex}");
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
                if (i == vehicleIndex)
                {
                    continue;
                }

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
                if (i != vehicleIndex && vehicles[i] != null && vehicles[i].gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        private void LogRouteDiagnostics(DRTStop stop, Vector3 servicePoint)
        {
            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null || stop == null)
            {
                return;
            }

            var closestStopWaypoint = API.GetClosestWaypoint(stop.Position);
            float stopToWaypointDistance = closestStopWaypoint != null
                ? GetPlanarDistance(stop.Position, closestStopWaypoint.Position)
                : float.PositiveInfinity;
            float frontToStopDistance = GetPlanarDistance(GetVehicleArrivalPoint(vehicle), stop.Position);
            float frontToServiceDistance = GetPlanarDistance(GetVehicleArrivalPoint(vehicle), servicePoint);

            int currentWaypoint = vehicle.MovementInfo != null
                ? vehicle.MovementInfo.GetWaypointIndex(0)
                : -1;

            Debug.Log(
                $"[BUSCONTROLLER] RouteRequest vehicle={vehicleIndex}, currentWaypoint={currentWaypoint}, " +
                $"targetStop={stop.StopId}, targetObject={stop.name}, frontToService={FormatMeters(frontToServiceDistance)}, " +
                $"frontToStopMarker={FormatMeters(frontToStopDistance)}, " +
                $"servicePoint={FormatVector(servicePoint)}, stopMarker={FormatVector(stop.Position)}, " +
                $"rootToStop={FormatMeters(GetPlanarDistance(vehicle.transform.position, stop.Position))}, " +
                $"stopToClosestWaypoint={FormatMeters(stopToWaypointDistance)}");
        }

        private void LogPathAssignment(VehicleComponent vehicle, DRTStop targetStop, Vector3 servicePoint, List<int> path)
        {
            string endpointDescription = DescribePathEndpoint(path, servicePoint, out float endToServiceDistance);
            float warningDistance = Mathf.Max(15f, arrivalDistanceMeters + stopWaypointSnapDistanceMeters);
            string integrity = endToServiceDistance <= warningDistance ? "ok" : "warning";

            string message =
                $"[BUSCONTROLLER] PathAssigned integrity={integrity}, vehicle={vehicleIndex}, " +
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

            if (!API.IsInitialized())
            {
                return;
            }

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null)
            {
                Debug.LogWarning($"[BUSCONTROLLER] Vehicle {vehicleIndex} not found while monitoring movement.");
                return;
            }

            var movement = vehicle.MovementInfo;
            int customPathCount = movement?.CustomPath != null ? movement.CustomPath.Count : 0;
            Debug.Log(
                $"[BUSCONTROLLER] MoveStatus vehicle={vehicleIndex}, targetStop={targetStopId}, " +
                $"distance={FormatMeters(GetTargetDistanceMeters())}, speed={vehicle.GetCurrentSpeedMS():0.00}m/s, " +
                $"hasPath={movement?.HasPath}, pathLength={movement?.PathLength}, remaining={movement?.RemainingPathLength}, " +
                $"customPath={customPathCount}, waitingForArrival={waitingForArrivalProximity}");
        }

        private float GetVehicleSpeedMS()
        {
            if (!API.IsInitialized())
            {
                return 0f;
            }

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            return vehicle != null ? vehicle.GetCurrentSpeedMS() : 0f;
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

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null)
            {
                return float.PositiveInfinity;
            }

            return GetPlanarDistance(GetVehicleArrivalPoint(vehicle), GetStopServicePoint(stop));
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

        private static Vector3 GetVehicleArrivalPoint(VehicleComponent vehicle)
        {
            if (vehicle == null)
            {
                return Vector3.zero;
            }

            return vehicle.FrontPosition != null ? vehicle.FrontPosition.position : vehicle.transform.position;
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

        private void FinishEpisode(string reason)
        {
            if (episodeFinished)
            {
                return;
            }

            episodeFinished = true;
            driving = false;
            nextStopSelector?.NotifyEpisodeFinished(
                passengerManager != null && !passengerManager.HasUnfinishedRequests(episodeTimeSeconds));

            Debug.Log($"[BUSCONTROLLER] Episode finished. {reason}");

            if (logEpisodeSummary && passengerManager != null)
            {
                passengerManager.LogSummary();
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
            enabledTrafficDensity = Mathf.Max(1, enabledTrafficDensity);
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
            simulationSecondsPerRealSecond = Mathf.Max(0.01f, simulationSecondsPerRealSecond);
            movementDiagnosticsIntervalSeconds = Mathf.Max(0.25f, movementDiagnosticsIntervalSeconds);
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
