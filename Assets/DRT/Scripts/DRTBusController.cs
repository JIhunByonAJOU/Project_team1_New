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
        [SerializeField] private float arrivalDistanceMeters = 1f;
        [SerializeField] private float stopWaypointSnapDistanceMeters = 1f;
        [SerializeField] private float arrivalWaitTimeoutSeconds = 12f;
        [SerializeField] private bool autoFollowControlledVehicle = true;

        [Header("Diagnostics")]
        [SerializeField] private bool logMovementDiagnostics = true;
        [SerializeField] private float movementDiagnosticsIntervalSeconds = 2f;

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
        private int currentStopId;
        private int targetStopId;
        private Coroutine dwellRoutine;
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

            Debug.Log($"[DRT] Loaded {stops.Count} stops from {busStopsRoot.name}.");
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
            ApplyCameraFollow(vehicle);
            Debug.Log($"[DRT] Bus controller initialized. Vehicle={vehicleIndex}, FirstTargetHint={startStopId}");
            SendToNextStop();
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
                        $"[DRT] Vehicle {vehicleIndex} reached Gley destination for Stop {reachedStopId}, " +
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
            passengerManager.ProcessStopArrival(currentStopId, episodeTimeSeconds);

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
            if (episodeFinished)
            {
                return;
            }

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null || !vehicle.gameObject.activeSelf)
            {
                driving = false;
                Debug.LogWarning($"[DRT] Vehicle {vehicleIndex} is not active. Cannot send it to next stop yet.");
                return;
            }

            int nextStopId = nextStopSelector.SelectNextStopId(
                currentStopId,
                stops,
                passengerManager,
                episodeTimeSeconds);

            if (!TryGetStop(nextStopId, out DRTStop nextStop))
            {
                FinishEpisode($"No valid next stop found. Requested Stop={nextStopId}");
                return;
            }

            targetStopId = nextStop.StopId;
            driving = true;
            LogRouteDiagnostics(nextStop);

            Vector3 servicePoint = GetStopServicePoint(nextStop);
            var path = API.GetPath(vehicle.transform.position, servicePoint, vehicle.VehicleType);
            if (path == null || path.Count == 0)
            {
                driving = false;
                Debug.LogWarning(
                    $"[DRT] No drivable path for Vehicle {vehicleIndex} -> Stop {targetStopId}. " +
                    "Check that this BusStop is close to a Gley traffic waypoint and allowed for this vehicle type.");
                return;
            }

            API.SetVehiclePath(vehicleIndex, path);

            Debug.Log($"[DRT] Vehicle {vehicleIndex} -> Stop {targetStopId} at t={episodeTimeSeconds:0.0}s, pathWaypoints={path.Count}");
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

        private void LogRouteDiagnostics(DRTStop stop)
        {
            if (!logMovementDiagnostics)
            {
                return;
            }

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null || stop == null)
            {
                return;
            }

            var closestStopWaypoint = API.GetClosestWaypoint(stop.Position);
            float stopToWaypointDistance = closestStopWaypoint != null
                ? GetPlanarDistance(stop.Position, closestStopWaypoint.Position)
                : float.PositiveInfinity;
            Vector3 servicePoint = GetStopServicePoint(stop);
            float frontToStopDistance = GetPlanarDistance(GetVehicleArrivalPoint(vehicle), stop.Position);
            float frontToServiceDistance = GetPlanarDistance(GetVehicleArrivalPoint(vehicle), servicePoint);

            int currentWaypoint = vehicle.MovementInfo != null
                ? vehicle.MovementInfo.GetWaypointIndex(0)
                : -1;

            Debug.Log(
                $"[DRT] Route request. vehicle={vehicleIndex}, currentWaypoint={currentWaypoint}, " +
                $"targetStop={stop.StopId}, frontToService={FormatMeters(frontToServiceDistance)}, " +
                $"frontToStopMarker={FormatMeters(frontToStopDistance)}, " +
                $"rootToStop={FormatMeters(GetPlanarDistance(vehicle.transform.position, stop.Position))}, " +
                $"stopToClosestWaypoint={FormatMeters(stopToWaypointDistance)}");
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

            var vehicle = API.GetVehicleComponent(vehicleIndex);
            if (vehicle == null)
            {
                Debug.LogWarning($"[DRT] Vehicle {vehicleIndex} not found while monitoring movement.");
                return;
            }

            var movement = vehicle.MovementInfo;
            int customPathCount = movement?.CustomPath != null ? movement.CustomPath.Count : 0;
            Debug.Log(
                $"[DRT] Move status. vehicle={vehicleIndex}, targetStop={targetStopId}, " +
                $"distance={FormatMeters(GetTargetDistanceMeters())}, speed={vehicle.GetCurrentSpeedMS():0.00}m/s, " +
                $"hasPath={movement?.HasPath}, pathLength={movement?.PathLength}, remaining={movement?.RemainingPathLength}, " +
                $"customPath={customPathCount}, waitingFor1m={waitingForArrivalProximity}");
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

            Debug.Log($"[DRT] Episode finished. {reason}");

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
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
            simulationSecondsPerRealSecond = Mathf.Max(0.01f, simulationSecondsPerRealSecond);
            movementDiagnosticsIntervalSeconds = Mathf.Max(0.25f, movementDiagnosticsIntervalSeconds);
        }
    }
}
