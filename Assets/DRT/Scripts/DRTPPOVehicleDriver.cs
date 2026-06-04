using System;
using System.Collections.Generic;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
using Unity.Barracuda;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;

namespace DRT
{
    [AddComponentMenu("DRT/DRT PPO Vehicle Driver")]
    [RequireComponent(typeof(BehaviorParameters))]
    public class DRTPPOVehicleDriver : Agent, IDRTVehicleDriver
    {
        private const string BehaviorName = "DRTDrivePPO";
        private const int GlobalObservationCount = 10;
        private const int LookaheadWaypointCount = 5;
        private const int ObservationsPerWaypoint = 8;
        private const int RayCount = 9;
        private const int ObservationsPerRay = 3;
        private const float MaxWaypointSpeedKmh = 100f;
        private const float MaxLaneWidthMeters = 8f;

        [SerializeField] private PlayerCar playerCar;
        [SerializeField] private Rigidbody vehicleRigidbody;
        [SerializeField] private Transform bodyTransform;
        [SerializeField] private Transform vehicleRoot;
        [SerializeField] private VehicleTypes vehicleType = VehicleTypes.Car;
        [SerializeField] private float speedMultiplier = 1f;
        [SerializeField] private float waypointReachDistanceMeters = 6f;
        [SerializeField] private float finalReachDistanceMeters = 4f;

        [Header("Policy")]
        [SerializeField, InspectorName("Mode")] private DRTPPODrivePolicy drivePolicy = DRTPPODrivePolicy.MLAgentsTraining;
        [SerializeField, InspectorName("ONNX Model")] private NNModel onnxInferenceModel;
        [SerializeField] private InferenceDevice onnxInferenceDevice = InferenceDevice.Default;

        [Header("Control")]
        [SerializeField] private float baseCruiseSpeedMetersPerSecond = 6f;
        [SerializeField] private float maxPolicySpeedMetersPerSecond = 7f;
        [SerializeField] private float speedLimitBrakeInput = -0.45f;
        [SerializeField] private float maxObservationSpeedMetersPerSecond = 15f;
        [SerializeField] private float maxSteeringAngleForFullInput = 45f;
        [SerializeField] private float hardTurnAngle = 75f;
        [SerializeField] private float slowDownDistanceMeters = 24f;
        [SerializeField] private float lookAheadTimeSeconds = 0.35f;
        [SerializeField] private float minLookAheadMeters = 4f;
        [SerializeField] private float maxLookAheadMeters = 16f;
        [SerializeField] private float steeringInputSmoothing = 8f;
        [SerializeField] private float throttleInputSmoothing = 6f;

        [Header("Observation")]
        [SerializeField] private float maxObservationDistanceMeters = 80f;
        [SerializeField] private float maxCrossTrackErrorMeters = 6f;
        [SerializeField] private float maxRoadWaypointDistanceMeters = 30f;
        [SerializeField] private float rayLengthMeters = 25f;
        [SerializeField] private float rayHeightMeters = 0.8f;
        [SerializeField] private LayerMask rayLayerMask = ~0;

        [Header("Reward")]
        [SerializeField] private float waypointProgressRewardPerMeter = 0.08f;
        [SerializeField] private float waypointRegressionPenaltyPerMeter = -0.04f;
        [SerializeField] private float destinationProgressRewardPerMeter = 0.002f;
        [SerializeField] private float headingAlignmentReward = 0.03f;
        [SerializeField] private float waypointHeadingReward = 0.02f;
        [SerializeField] private float curvePenalty = -0.03f;
        [SerializeField] private float crossTrackPenalty = -0.12f;
        [SerializeField] private float steeringCorrectionReward = 0.02f;
        [SerializeField] private float waypointPassedReward = 0.35f;
        [SerializeField] private float waypointStuckPenalty = -0.75f;
        [SerializeField] private float destinationReward = 2f;
        [SerializeField] private float collisionPenalty = -2f;
        [SerializeField] private float roadExitPenalty = -1f;
        [SerializeField] private float assignedRouteExitPenalty = -1.5f;
        [SerializeField] private float reversePenalty = -0.5f;
        [SerializeField] private float stuckPenalty = -1f;
        [SerializeField] private float rayRiskPenalty = -0.2f;
        [SerializeField] private float stopViolationPenalty = -0.3f;
        [SerializeField] private float correctStopReward = 0.05f;

        [Header("Safety")]
        [SerializeField] private float noProgressTimeoutRealSeconds = 8f;
        [SerializeField] private float waypointTimeoutSeconds = 6f;
        [SerializeField] private float minimumProgressMeters = 0.4f;
        [SerializeField] private float hardCrossTrackLimitMeters = 10f;
        [SerializeField] private float reverseGraceSeconds = 2f;
        [SerializeField] private float stopRuleDistanceMeters = 8f;
        [SerializeField] private float stopRuleSpeedMetersPerSecond = 3f;

        private readonly List<int> pathWaypointIndexes = new List<int>();
        private readonly List<TrafficWaypoint> pathWaypoints = new List<TrafficWaypoint>();
        private readonly List<Vector3> pathPoints = new List<Vector3>();
        private Collider[] ownColliders;
        private int targetPointIndex;
        private Vector3 finalDestination;
        private bool driving;
        private float targetSteeringInput;
        private float targetThrottleInput;
        private float currentSteeringInput;
        private float currentThrottleInput;
        private float lastWaypointDistance;
        private float lastDestinationDistance;
        private float bestDestinationDistance;
        private float lastProgressRealtime;
        private float lastWaypointProgressRealtime;
        private int lastTargetPointIndex;
        private float reverseSeconds;
        private float previousYawDegrees;
        private float yawRateDegreesPerSecond;
        private bool criticalFault;
        private string criticalFaultReason = string.Empty;
        private bool warnedSharedBehaviorHost;

        private int ObservationSize =>
            GlobalObservationCount +
            LookaheadWaypointCount * ObservationsPerWaypoint +
            RayCount * ObservationsPerRay;

        public bool IsDriving => driving;
        public VehicleTypes VehicleType => vehicleType;
        public Transform VehicleTransform => GetVehicleRoot();
        public string VehicleName => VehicleTransform != null ? VehicleTransform.name : name;
        public int PathPointCount => pathPoints.Count;
        public int RemainingPathPointCount => driving ? Mathf.Max(0, pathPoints.Count - targetPointIndex) : 0;
        public Vector3 BodyPosition => GetBodyPosition();
        public bool IsTemporarilyBlocked => false;
        public string TemporaryBlockReason => string.Empty;
        public bool HasCriticalFault => criticalFault;
        public string CriticalFaultReason => criticalFaultReason;

        public float CurrentSpeedMS
        {
            get
            {
                ResolveReferences();
                if (vehicleRigidbody == null)
                {
                    return 0f;
                }

#if UNITY_6000_0_OR_NEWER
                return vehicleRigidbody.linearVelocity.magnitude;
#else
                return vehicleRigidbody.velocity.magnitude;
#endif
            }
        }

        public void Configure(
            VehicleTypes newVehicleType,
            float newSpeedMultiplier,
            float newWaypointReachDistance,
            float newFinalReachDistance,
            DRTPPODrivePolicy newDrivePolicy,
            NNModel newOnnxInferenceModel,
            InferenceDevice newOnnxInferenceDevice)
        {
            Configure(
                null,
                newVehicleType,
                newSpeedMultiplier,
                newWaypointReachDistance,
                newFinalReachDistance,
                newDrivePolicy,
                newOnnxInferenceModel,
                newOnnxInferenceDevice);
        }

        public void Configure(
            Transform newVehicleRoot,
            VehicleTypes newVehicleType,
            float newSpeedMultiplier,
            float newWaypointReachDistance,
            float newFinalReachDistance,
            DRTPPODrivePolicy newDrivePolicy,
            NNModel newOnnxInferenceModel,
            InferenceDevice newOnnxInferenceDevice)
        {
            if (newVehicleRoot != null)
            {
                vehicleRoot = newVehicleRoot;
            }

            vehicleType = newVehicleType;
            speedMultiplier = Mathf.Max(0.1f, newSpeedMultiplier);
            waypointReachDistanceMeters = Mathf.Max(0.5f, newWaypointReachDistance);
            finalReachDistanceMeters = Mathf.Max(0.25f, newFinalReachDistance);
            drivePolicy = newDrivePolicy;
            onnxInferenceModel = newOnnxInferenceModel;
            onnxInferenceDevice = newOnnxInferenceDevice;
            ResolveReferences();
            ConfigureBehaviorParameters();
        }

        public bool SetPath(List<int> waypointIndexes, Vector3 destination)
        {
            ResolveReferences();
            ClearCriticalFault();
            pathWaypointIndexes.Clear();
            pathWaypoints.Clear();
            pathPoints.Clear();
            targetPointIndex = 0;
            finalDestination = destination;
            targetSteeringInput = 0f;
            targetThrottleInput = 0f;
            currentSteeringInput = 0f;
            currentThrottleInput = 0f;
            reverseSeconds = 0f;

            if (waypointIndexes != null)
            {
                for (int i = 0; i < waypointIndexes.Count; i++)
                {
                    TrafficWaypoint waypoint = API.GetWaypointFromIndex(waypointIndexes[i]);
                    if (waypoint == null)
                    {
                        continue;
                    }

                    pathWaypointIndexes.Add(waypointIndexes[i]);
                    pathWaypoints.Add(waypoint);
                    AddPathPoint(waypoint.Position);
                }
            }

            AddPathPoint(destination);
            driving = pathPoints.Count > 0 && playerCar != null;
            Vector3 bodyPosition = GetBodyPosition();
            lastWaypointDistance = GetCurrentWaypointDistance(bodyPosition);
            lastDestinationDistance = GetPlanarDistance(bodyPosition, finalDestination);
            bestDestinationDistance = lastDestinationDistance;
            lastProgressRealtime = Time.realtimeSinceStartup;
            lastWaypointProgressRealtime = lastProgressRealtime;
            lastTargetPointIndex = targetPointIndex;
            previousYawDegrees = GetVehicleYaw();

            if (playerCar != null)
            {
                playerCar.SetExternalInput(0f, 0f, true);
            }

            return driving;
        }

        public void StopAndHold(bool zeroVelocity)
        {
            driving = false;
            pathWaypointIndexes.Clear();
            pathWaypoints.Clear();
            pathPoints.Clear();
            targetPointIndex = 0;
            targetSteeringInput = 0f;
            targetThrottleInput = 0f;
            currentSteeringInput = 0f;
            currentThrottleInput = 0f;
            lastWaypointDistance = 0f;
            lastTargetPointIndex = 0;

            if (playerCar != null)
            {
                playerCar.SetExternalInput(0f, 0f, true);
            }

            if (zeroVelocity)
            {
                SetVelocity(Vector3.zero, Vector3.zero);
            }
        }

        public void ReleaseControl()
        {
            driving = false;
            pathWaypointIndexes.Clear();
            pathWaypoints.Clear();
            pathPoints.Clear();
            targetPointIndex = 0;
            targetSteeringInput = 0f;
            targetThrottleInput = 0f;
            currentSteeringInput = 0f;
            currentThrottleInput = 0f;
            lastWaypointDistance = 0f;
            lastTargetPointIndex = 0;
            ClearCriticalFault();

            if (playerCar != null)
            {
                playerCar.ClearExternalInput();
            }
        }

        public void TeleportTo(Vector3 position, Quaternion rotation, int nextWaypointIndex)
        {
            ResolveReferences();
            StopAndHold(true);
            ClearCriticalFault();

            if (vehicleRigidbody != null)
            {
                vehicleRigidbody.position = position;
                vehicleRigidbody.rotation = rotation;
            }

            Transform root = GetVehicleRoot();
            if (root != null)
            {
                root.SetPositionAndRotation(position, rotation);
            }

            ResetAgentLocalPose();
            SetVelocity(Vector3.zero, Vector3.zero);
            Physics.SyncTransforms();
        }

        public void ClearCriticalFault()
        {
            criticalFault = false;
            criticalFaultReason = string.Empty;
        }

        public void ReportExternalCriticalFault(string reason, float penalty)
        {
            RegisterCriticalFault(reason, penalty);
        }

        private void Awake()
        {
            if (DisableIfSharingNextStopBehaviorParameters())
            {
                return;
            }

            ResolveReferences();
            ConfigureBehaviorParameters();
        }

        public override void Initialize()
        {
            if (DisableIfSharingNextStopBehaviorParameters())
            {
                return;
            }

            ResolveReferences();
            ConfigureBehaviorParameters();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            ResolveReferences();

            Vector3 bodyPosition = GetBodyPosition();
            Vector3 localVelocity = InverseVehicleDirection(GetVelocity());
            float destinationDistance = pathPoints.Count > 0
                ? GetPlanarDistance(bodyPosition, finalDestination)
                : maxObservationDistanceMeters;
            float crossTrackError = GetCrossTrackError(bodyPosition, out Vector3 routeTangent);
            GetHeadingFeatures(routeTangent, out float headingDot, out float headingCross);

            sensor.AddObservation(Mathf.Clamp01(CurrentSpeedMS / Mathf.Max(0.1f, maxObservationSpeedMetersPerSecond)));
            sensor.AddObservation(Mathf.Clamp(localVelocity.z / Mathf.Max(0.1f, maxObservationSpeedMetersPerSecond), -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(localVelocity.x / Mathf.Max(0.1f, maxObservationSpeedMetersPerSecond), -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(yawRateDegreesPerSecond / 180f, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp01(destinationDistance / Mathf.Max(1f, maxObservationDistanceMeters)));
            sensor.AddObservation(pathPoints.Count > 1 ? Mathf.Clamp01((float)targetPointIndex / Mathf.Max(1, pathPoints.Count - 1)) : 0f);
            sensor.AddObservation(Mathf.Clamp01(crossTrackError / Mathf.Max(0.1f, maxCrossTrackErrorMeters)));
            sensor.AddObservation(headingDot);
            sensor.AddObservation(headingCross);
            sensor.AddObservation(driving ? 1f : 0f);

            for (int i = 0; i < LookaheadWaypointCount; i++)
            {
                AddWaypointObservation(sensor, bodyPosition, targetPointIndex + i);
            }

            AddRayObservations(sensor);
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!driving)
            {
                return;
            }

            var continuousActions = actionBuffers.ContinuousActions;
            targetSteeringInput = continuousActions.Length > 0 ? Mathf.Clamp(continuousActions[0], -1f, 1f) : 0f;
            targetThrottleInput = continuousActions.Length > 1 ? Mathf.Clamp(continuousActions[1], -1f, 1f) : 0f;
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ComputeHeuristicControl(out float steering, out float throttle);
            var continuousActionsOut = actionsOut.ContinuousActions;
            if (continuousActionsOut.Length > 0)
            {
                continuousActionsOut[0] = steering;
            }

            if (continuousActionsOut.Length > 1)
            {
                continuousActionsOut[1] = throttle;
            }
        }

        private void FixedUpdate()
        {
            UpdateYawRate();

            if (!driving)
            {
                return;
            }

            ResolveReferences();
            if (playerCar == null || vehicleRigidbody == null || pathPoints.Count == 0)
            {
                RegisterCriticalFault("PPO vehicle references missing.", stuckPenalty);
                return;
            }

            if (criticalFault)
            {
                return;
            }

            RequestDecision();
            ApplyControl();
            AdvancePathProgress();
            ApplyStepRewardsAndSafety();
        }

        private void OnCollisionEnter(Collision collision)
        {
            NotifyVehicleCollision(collision);
        }

        public void NotifyVehicleCollision(Collision collision)
        {
            if (!driving || collision == null || IsOwnCollider(collision.collider))
            {
                return;
            }

            RegisterCriticalFault($"Collision with {collision.collider.name}.", collisionPenalty);
        }

        private void ConfigureBehaviorParameters()
        {
            if (DisableIfSharingNextStopBehaviorParameters())
            {
                return;
            }

            var behaviorParameters = GetComponent<BehaviorParameters>();
            if (behaviorParameters == null)
            {
                return;
            }

            behaviorParameters.hideFlags = HideFlags.HideInInspector;
            switch (drivePolicy)
            {
                case DRTPPODrivePolicy.ONNXInference:
                    behaviorParameters.Model = onnxInferenceModel;
                    behaviorParameters.InferenceDevice = onnxInferenceDevice;
                    behaviorParameters.BehaviorType = BehaviorType.InferenceOnly;
                    break;
                case DRTPPODrivePolicy.HeuristicPurePursuit:
                    behaviorParameters.Model = null;
                    behaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;
                    break;
                default:
                    behaviorParameters.Model = null;
                    behaviorParameters.BehaviorType = BehaviorType.Default;
                    break;
            }

            behaviorParameters.BehaviorName = BehaviorName;
            behaviorParameters.BrainParameters.VectorObservationSize = ObservationSize;
            behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
            behaviorParameters.BrainParameters.ActionSpec = new ActionSpec(2, Array.Empty<int>());
        }

        private bool DisableIfSharingNextStopBehaviorParameters()
        {
            if (GetComponent<DRTNextStopSelector>() == null)
            {
                return false;
            }

            ReleaseControl();
            enabled = false;
            if (!warnedSharedBehaviorHost)
            {
                Debug.LogWarning(
                    "[DRTPPOVehicleDriver] Disabled root PPO driver because it shares BehaviorParameters " +
                    "with DRTNextStopSelector. DRTBusController will use the DRTDrivePPOAgent child instead.");
                warnedSharedBehaviorHost = true;
            }

            return true;
        }

        private void ResolveReferences()
        {
            if (vehicleRoot == null)
            {
                vehicleRoot = playerCar != null
                    ? playerCar.transform
                    : vehicleRigidbody != null
                        ? vehicleRigidbody.transform
                        : transform;
            }

            Transform root = GetVehicleRoot();

            if (playerCar == null)
            {
                playerCar = root != null
                    ? root.GetComponent<PlayerCar>() ?? root.GetComponentInChildren<PlayerCar>() ?? root.GetComponentInParent<PlayerCar>()
                    : GetComponent<PlayerCar>();
            }

            if (vehicleRigidbody == null)
            {
                vehicleRigidbody = root != null
                    ? root.GetComponent<Rigidbody>() ?? root.GetComponentInChildren<Rigidbody>() ?? root.GetComponentInParent<Rigidbody>()
                    : GetComponent<Rigidbody>();
            }

            if (bodyTransform == null)
            {
                bodyTransform = FindChildRecursive(root, "BodyHolder");
            }

            if (ownColliders == null || ownColliders.Length == 0)
            {
                ownColliders = root != null
                    ? root.GetComponentsInChildren<Collider>()
                    : GetComponentsInChildren<Collider>();
            }
        }

        private void AddPathPoint(Vector3 point)
        {
            if (pathPoints.Count > 0 && GetPlanarDistance(pathPoints[pathPoints.Count - 1], point) < 0.5f)
            {
                return;
            }

            pathPoints.Add(point);
        }

        private void AddWaypointObservation(VectorSensor sensor, Vector3 bodyPosition, int pointIndex)
        {
            bool valid = pointIndex >= 0 && pointIndex < pathPoints.Count;
            if (!valid)
            {
                for (int i = 0; i < ObservationsPerWaypoint; i++)
                {
                    sensor.AddObservation(0f);
                }
                return;
            }

            Vector3 localPoint = InverseVehiclePoint(pathPoints[pointIndex]);
            localPoint.y = 0f;
            float distance = GetPlanarDistance(bodyPosition, pathPoints[pointIndex]);
            TrafficWaypoint waypoint = pointIndex < pathWaypoints.Count ? pathWaypoints[pointIndex] : null;

            sensor.AddObservation(1f);
            sensor.AddObservation(Mathf.Clamp(localPoint.x / Mathf.Max(1f, maxObservationDistanceMeters), -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(localPoint.z / Mathf.Max(1f, maxObservationDistanceMeters), -1f, 1f));
            sensor.AddObservation(Mathf.Clamp01(distance / Mathf.Max(1f, maxObservationDistanceMeters)));
            sensor.AddObservation(waypoint != null && waypoint.Stop ? 1f : 0f);
            sensor.AddObservation(waypoint != null && waypoint.GiveWay ? 1f : 0f);
            sensor.AddObservation(waypoint != null ? Mathf.Clamp01(waypoint.MaxSpeed / MaxWaypointSpeedKmh) : 0f);
            sensor.AddObservation(waypoint != null ? Mathf.Clamp01(waypoint.LaneWidth / MaxLaneWidthMeters) : 0f);
        }

        private void AddRayObservations(VectorSensor sensor)
        {
            for (int i = 0; i < RayCount; i++)
            {
                float angle = Mathf.Lerp(-80f, 80f, RayCount == 1 ? 0.5f : (float)i / (RayCount - 1));
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * GetVehicleForward();
                bool hit = TryRaycast(direction, out RaycastHit rayHit, out bool vehicleHit);
                float normalizedDistance = hit
                    ? Mathf.Clamp01(rayHit.distance / Mathf.Max(0.1f, rayLengthMeters))
                    : 1f;

                sensor.AddObservation(normalizedDistance);
                sensor.AddObservation(vehicleHit ? 1f : 0f);
                sensor.AddObservation(hit && !vehicleHit ? 1f : 0f);
            }
        }

        private void ApplyControl()
        {
            currentSteeringInput = Mathf.MoveTowards(
                currentSteeringInput,
                targetSteeringInput,
                Mathf.Max(0.1f, steeringInputSmoothing) * Time.fixedDeltaTime);
            currentThrottleInput = Mathf.MoveTowards(
                currentThrottleInput,
                targetThrottleInput,
                Mathf.Max(0.1f, throttleInputSmoothing) * Time.fixedDeltaTime);

            float speedLimit = GetPolicySpeedLimit();
            if (speedLimit > 0f && CurrentSpeedMS > speedLimit)
            {
                currentThrottleInput = Mathf.Min(currentThrottleInput, speedLimitBrakeInput);
            }

            playerCar.SetExternalInput(currentSteeringInput, currentThrottleInput, true);
        }

        private void AdvancePathProgress()
        {
            Vector3 bodyPosition = GetBodyPosition();
            while (targetPointIndex < pathPoints.Count - 1)
            {
                Vector3 toTarget = pathPoints[targetPointIndex] - bodyPosition;
                toTarget.y = 0f;

                Vector3 forward = GetVehicleForward();
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.forward;
                }

                float targetDistance = toTarget.magnitude;
                float dotProduct = Vector3.Dot(toTarget, forward.normalized);
                float reachDistance = GetWaypointChangeDistance();
                if (targetDistance >= reachDistance && !(dotProduct < 0f && targetDistance < reachDistance * 5f))
                {
                    return;
                }

                targetPointIndex++;
                lastWaypointDistance = GetCurrentWaypointDistance(bodyPosition);
                lastWaypointProgressRealtime = Time.realtimeSinceStartup;
                lastProgressRealtime = lastWaypointProgressRealtime;
                lastTargetPointIndex = targetPointIndex;
                AddReward(waypointPassedReward);
                RecordStat("DRTDrive/WaypointPassed", 1f, StatAggregationMethod.Sum);
            }
        }

        private void ApplyStepRewardsAndSafety()
        {
            Vector3 bodyPosition = GetBodyPosition();
            float destinationDistance = GetPlanarDistance(bodyPosition, finalDestination);
            float crossTrackError = GetCrossTrackError(bodyPosition, out Vector3 routeTangent);
            if (hardCrossTrackLimitMeters > 0f && crossTrackError > hardCrossTrackLimitMeters)
            {
                RegisterCriticalFault($"PPO vehicle left assigned route. crossTrackError={crossTrackError:0.00}m", assignedRouteExitPenalty);
                return;
            }

            if (destinationDistance <= finalReachDistanceMeters)
            {
                AddReward(destinationReward);
                RecordStat("DRTDrive/DestinationReached", 1f, StatAggregationMethod.Sum);
                EndEpisode();
                StopAndHold(true);
                return;
            }

            if (lastTargetPointIndex != targetPointIndex)
            {
                lastWaypointDistance = GetCurrentWaypointDistance(bodyPosition);
                lastWaypointProgressRealtime = Time.realtimeSinceStartup;
                lastTargetPointIndex = targetPointIndex;
            }

            float waypointDistance = GetCurrentWaypointDistance(bodyPosition);
            float waypointProgressMeters = lastWaypointDistance - waypointDistance;
            if (waypointProgressMeters > 0f)
            {
                AddReward(waypointProgressMeters * waypointProgressRewardPerMeter);
                if (waypointProgressMeters >= Mathf.Max(0.01f, minimumProgressMeters))
                {
                    lastWaypointProgressRealtime = Time.realtimeSinceStartup;
                    lastProgressRealtime = lastWaypointProgressRealtime;
                }
            }
            else if (waypointProgressMeters < 0f)
            {
                AddReward((-waypointProgressMeters) * waypointRegressionPenaltyPerMeter);
            }

            float destinationProgressMeters = lastDestinationDistance - destinationDistance;
            if (destinationProgressMeters > 0f)
            {
                AddReward(destinationProgressMeters * destinationProgressRewardPerMeter);
                if (destinationDistance < bestDestinationDistance - Mathf.Max(0.01f, minimumProgressMeters))
                {
                    bestDestinationDistance = destinationDistance;
                }
            }

            lastWaypointDistance = waypointDistance;
            lastDestinationDistance = destinationDistance;

            GetHeadingFeatures(routeTangent, out float headingDot, out float headingCross);
            float normalizedCrossTrackError = Mathf.Clamp01(crossTrackError / Mathf.Max(0.1f, maxCrossTrackErrorMeters));
            float nextWaypointHeadingDot = GetCurrentWaypointHeadingDot(bodyPosition);
            float curveStrength = GetCurveStrength();
            float steeringCorrection = -Mathf.Sign(headingCross) * targetSteeringInput;

            AddReward(headingAlignmentReward * headingDot * Time.fixedDeltaTime);
            AddReward(waypointHeadingReward * nextWaypointHeadingDot * Time.fixedDeltaTime);
            AddReward(curvePenalty * Mathf.Abs(headingCross) * curveStrength * Time.fixedDeltaTime);
            AddReward(crossTrackPenalty * normalizedCrossTrackError * normalizedCrossTrackError * Time.fixedDeltaTime);
            AddReward(steeringCorrectionReward * steeringCorrection * normalizedCrossTrackError * Time.fixedDeltaTime);

            ApplyRayRiskReward();
            ApplyReversePenalty();
            ApplyWaypointRuleReward();

            if (IsTooFarFromTrafficWaypoint(bodyPosition, out float roadWaypointDistance))
            {
                RegisterCriticalFault($"PPO vehicle left traffic network. nearestWaypointDistance={roadWaypointDistance:0.00}m", roadExitPenalty);
                return;
            }

            float waypointNoProgressSeconds = Time.realtimeSinceStartup - lastWaypointProgressRealtime;
            if (waypointNoProgressSeconds >= waypointTimeoutSeconds)
            {
                AddReward(waypointStuckPenalty);
                lastWaypointProgressRealtime = Time.realtimeSinceStartup;
                RecordStat("DRTDrive/WaypointStuck", 1f, StatAggregationMethod.Sum);
            }

            float noProgressSeconds = Time.realtimeSinceStartup - lastProgressRealtime;
            if (noProgressSeconds >= noProgressTimeoutRealSeconds)
            {
                RegisterCriticalFault($"PPO vehicle made no progress for {noProgressTimeoutRealSeconds:0.0}s.", stuckPenalty);
            }
        }

        private void ApplyRayRiskReward()
        {
            float closestNormalized = 1f;
            for (int i = 0; i < RayCount; i++)
            {
                float angle = Mathf.Lerp(-80f, 80f, RayCount == 1 ? 0.5f : (float)i / (RayCount - 1));
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * GetVehicleForward();
                if (TryRaycast(direction, out RaycastHit hit, out _) && hit.distance < rayLengthMeters)
                {
                    closestNormalized = Mathf.Min(closestNormalized, Mathf.Clamp01(hit.distance / Mathf.Max(0.1f, rayLengthMeters)));
                }
            }

            if (closestNormalized < 0.35f)
            {
                AddReward(rayRiskPenalty * (1f - closestNormalized) * Time.fixedDeltaTime);
            }
        }

        private void ApplyReversePenalty()
        {
            Vector3 localVelocity = InverseVehicleDirection(GetVelocity());
            if (localVelocity.z < -0.5f)
            {
                reverseSeconds += Time.fixedDeltaTime;
                if (reverseSeconds >= reverseGraceSeconds)
                {
                    AddReward(reversePenalty * Time.fixedDeltaTime);
                }
                return;
            }

            reverseSeconds = 0f;
        }

        private void ApplyWaypointRuleReward()
        {
            TrafficWaypoint waypoint = targetPointIndex >= 0 && targetPointIndex < pathWaypoints.Count
                ? pathWaypoints[targetPointIndex]
                : null;
            if (waypoint == null || (!waypoint.Stop && !waypoint.GiveWay))
            {
                return;
            }

            float distance = GetPlanarDistance(GetBodyPosition(), waypoint.Position);
            if (distance > stopRuleDistanceMeters)
            {
                return;
            }

            float speed = CurrentSpeedMS;
            float reward = speed > stopRuleSpeedMetersPerSecond
                ? stopViolationPenalty
                : correctStopReward;
            AddReward(reward * Time.fixedDeltaTime);
        }

        private void ComputeHeuristicControl(out float steering, out float throttle)
        {
            steering = 0f;
            throttle = 0f;

            if (!driving || pathPoints.Count == 0)
            {
                return;
            }

            Vector3 bodyPosition = GetBodyPosition();
            Vector3 targetPoint = GetLookAheadPoint(bodyPosition);
            Vector3 toTarget = targetPoint - bodyPosition;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 forward = GetVehicleForward();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            float requestedSteeringAngle = Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up);
            float maxSteerDegrees = GetMaxSteerDegrees();
            steering = Mathf.Clamp(requestedSteeringAngle / Mathf.Max(1f, maxSteerDegrees), -1f, 1f);

            float finalDistance = GetPlanarDistance(bodyPosition, finalDestination);
            float targetSpeed = GetHeuristicTargetSpeed(Mathf.Abs(requestedSteeringAngle), finalDistance);
            throttle = GetThrottleForTargetSpeed(targetSpeed, finalDistance);
        }

        private Vector3 GetLookAheadPoint(Vector3 bodyPosition)
        {
            float lookAheadDistance = Mathf.Clamp(
                CurrentSpeedMS * Mathf.Max(0.01f, lookAheadTimeSeconds),
                minLookAheadMeters,
                maxLookAheadMeters);

            Vector3 previousPoint = bodyPosition;
            previousPoint.y = 0f;

            for (int i = Mathf.Clamp(targetPointIndex, 0, pathPoints.Count - 1); i < pathPoints.Count; i++)
            {
                Vector3 nextPoint = pathPoints[i];
                nextPoint.y = 0f;
                float segmentDistance = Vector3.Distance(previousPoint, nextPoint);
                if (segmentDistance >= lookAheadDistance)
                {
                    return Vector3.Lerp(previousPoint, nextPoint, lookAheadDistance / Mathf.Max(0.001f, segmentDistance));
                }

                lookAheadDistance -= segmentDistance;
                previousPoint = nextPoint;
            }

            return pathPoints[pathPoints.Count - 1];
        }

        private float GetHeuristicTargetSpeed(float absSteeringAngle, float finalDistance)
        {
            float targetSpeed = baseCruiseSpeedMetersPerSecond * Mathf.Max(0.1f, speedMultiplier);
            float speedLimit = GetPolicySpeedLimit();
            if (speedLimit > 0f)
            {
                targetSpeed = Mathf.Min(targetSpeed, speedLimit);
            }

            if (absSteeringAngle >= hardTurnAngle)
            {
                targetSpeed *= 0.35f;
            }
            else if (absSteeringAngle >= maxSteeringAngleForFullInput)
            {
                targetSpeed *= 0.6f;
            }

            if (finalDistance < slowDownDistanceMeters)
            {
                float slowFactor = Mathf.Clamp01(finalDistance / Mathf.Max(1f, slowDownDistanceMeters));
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(1.5f, targetSpeed, slowFactor));
            }

            return Mathf.Max(0.5f, targetSpeed);
        }

        private float GetPolicySpeedLimit()
        {
            return maxPolicySpeedMetersPerSecond > 0f
                ? maxPolicySpeedMetersPerSecond
                : baseCruiseSpeedMetersPerSecond * Mathf.Max(0.1f, speedMultiplier);
        }

        private float GetThrottleForTargetSpeed(float targetSpeed, float finalDistance)
        {
            float currentSpeed = CurrentSpeedMS;
            if (finalDistance <= finalReachDistanceMeters * 2f && currentSpeed > 1f)
            {
                return -0.6f;
            }

            if (currentSpeed > targetSpeed + 1f)
            {
                return -0.35f;
            }

            return Mathf.Clamp((targetSpeed - currentSpeed) / Mathf.Max(1f, targetSpeed), 0.2f, 1f);
        }

        private float GetWaypointChangeDistance()
        {
            float speedKmh = CurrentSpeedMS.ToKMH();
            float gleyDistance = speedKmh < 50f
                ? 1.5f
                : 4f + (speedKmh - 50f) * 0.02f;

            return Mathf.Max(0.5f, Mathf.Min(waypointReachDistanceMeters, gleyDistance));
        }

        private float GetCurrentWaypointDistance(Vector3 bodyPosition)
        {
            if (pathPoints.Count == 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(targetPointIndex, 0, pathPoints.Count - 1);
            return GetPlanarDistance(bodyPosition, pathPoints[index]);
        }

        private float GetCurrentWaypointHeadingDot(Vector3 bodyPosition)
        {
            if (pathPoints.Count == 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(targetPointIndex, 0, pathPoints.Count - 1);
            Vector3 toWaypoint = pathPoints[index] - bodyPosition;
            toWaypoint.y = 0f;
            if (toWaypoint.sqrMagnitude < 0.001f)
            {
                return 1f;
            }

            return Mathf.Clamp(Vector3.Dot(GetVehicleForward(), toWaypoint.normalized), -1f, 1f);
        }

        private float GetCurveStrength()
        {
            if (pathPoints.Count < 3)
            {
                return 0f;
            }

            int centerIndex = Mathf.Clamp(targetPointIndex, 1, pathPoints.Count - 2);
            Vector3 previousSegment = pathPoints[centerIndex] - pathPoints[centerIndex - 1];
            Vector3 nextSegment = pathPoints[centerIndex + 1] - pathPoints[centerIndex];
            previousSegment.y = 0f;
            nextSegment.y = 0f;
            if (previousSegment.sqrMagnitude < 0.001f || nextSegment.sqrMagnitude < 0.001f)
            {
                return 0f;
            }

            float angle = Vector3.Angle(previousSegment.normalized, nextSegment.normalized);
            return Mathf.Clamp01(angle / Mathf.Max(1f, hardTurnAngle));
        }

        private float GetMaxSteerDegrees()
        {
            return playerCar != null && playerCar.maxSteeringAngle > 0f
                ? playerCar.maxSteeringAngle
                : Mathf.Max(1f, maxSteeringAngleForFullInput);
        }

        private float GetCrossTrackError(Vector3 bodyPosition, out Vector3 tangent)
        {
            tangent = GetVehicleForward();
            if (pathPoints.Count == 0)
            {
                return 0f;
            }

            if (pathPoints.Count == 1)
            {
                Vector3 toOnlyPoint = pathPoints[0] - bodyPosition;
                toOnlyPoint.y = 0f;
                if (toOnlyPoint.sqrMagnitude > 0.001f)
                {
                    tangent = toOnlyPoint.normalized;
                }

                return toOnlyPoint.magnitude;
            }

            float bestDistance = float.PositiveInfinity;
            Vector3 bestTangent = tangent;
            int start = Mathf.Clamp(targetPointIndex - 2, 0, pathPoints.Count - 2);
            int end = Mathf.Clamp(targetPointIndex + LookaheadWaypointCount, start, pathPoints.Count - 2);
            Vector3 planarBody = bodyPosition;
            planarBody.y = 0f;

            for (int i = start; i <= end; i++)
            {
                Vector3 a = pathPoints[i];
                Vector3 b = pathPoints[i + 1];
                a.y = 0f;
                b.y = 0f;
                Vector3 segment = b - a;
                float segmentMagnitude = segment.magnitude;
                if (segmentMagnitude < 0.001f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(Vector3.Dot(planarBody - a, segment) / (segmentMagnitude * segmentMagnitude));
                Vector3 closest = a + segment * t;
                float distance = Vector3.Distance(planarBody, closest);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTangent = segment / segmentMagnitude;
                }
            }

            tangent = bestTangent;
            return float.IsInfinity(bestDistance) ? 0f : bestDistance;
        }

        private void GetHeadingFeatures(Vector3 routeTangent, out float dot, out float cross)
        {
            Vector3 forward = GetVehicleForward();
            forward.y = 0f;
            routeTangent.y = 0f;

            if (forward.sqrMagnitude < 0.001f || routeTangent.sqrMagnitude < 0.001f)
            {
                dot = 0f;
                cross = 0f;
                return;
            }

            forward.Normalize();
            routeTangent.Normalize();
            dot = Mathf.Clamp(Vector3.Dot(forward, routeTangent), -1f, 1f);
            cross = Mathf.Clamp(Vector3.Cross(forward, routeTangent).y, -1f, 1f);
        }

        private bool TryRaycast(Vector3 direction, out RaycastHit selectedHit, out bool vehicleHit)
        {
            selectedHit = default;
            vehicleHit = false;

            Vector3 origin = GetBodyPosition() + Vector3.up * rayHeightMeters;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                Mathf.Max(0.1f, rayLengthMeters),
                rayLayerMask,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsOwnCollider(hits[i].collider))
                {
                    continue;
                }

                selectedHit = hits[i];
                vehicleHit = IsVehicleCollider(selectedHit.collider);
                return true;
            }

            return false;
        }

        private bool IsVehicleCollider(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            return collider.GetComponentInParent<VehicleComponent>() != null ||
                   collider.GetComponentInParent<PlayerCar>() != null;
        }

        private bool IsOwnCollider(Collider collider)
        {
            if (collider == null || ownColliders == null)
            {
                return false;
            }

            for (int i = 0; i < ownColliders.Length; i++)
            {
                if (ownColliders[i] == collider)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsTooFarFromTrafficWaypoint(Vector3 position, out float waypointDistance)
        {
            waypointDistance = 0f;
            if (maxRoadWaypointDistanceMeters <= 0f || !API.IsInitialized())
            {
                return false;
            }

            TrafficWaypoint waypoint = API.GetClosestWaypoint(position);
            if (waypoint == null)
            {
                waypointDistance = float.PositiveInfinity;
                return true;
            }

            waypointDistance = GetPlanarDistance(position, waypoint.Position);
            return waypointDistance > maxRoadWaypointDistanceMeters;
        }

        private void RegisterCriticalFault(string reason, float penalty)
        {
            if (criticalFault)
            {
                return;
            }

            criticalFault = true;
            criticalFaultReason = string.IsNullOrWhiteSpace(reason) ? "PPO vehicle critical fault." : reason;
            AddReward(penalty);
            RecordStat("DRTDrive/CriticalFault", 1f, StatAggregationMethod.Sum);
            EndEpisode();
            StopAndHold(false);
        }

        private void UpdateYawRate()
        {
            float currentYaw = GetVehicleYaw();
            yawRateDegreesPerSecond = Mathf.DeltaAngle(previousYawDegrees, currentYaw) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
            previousYawDegrees = currentYaw;
        }

        private Vector3 GetBodyPosition()
        {
            ResolveReferences();
            if (bodyTransform != null)
            {
                return bodyTransform.position;
            }

            if (vehicleRigidbody != null)
            {
                return vehicleRigidbody.position;
            }

            Transform root = GetVehicleRoot();
            return root != null ? root.position : transform.position;
        }

        private Transform GetVehicleRoot()
        {
            if (vehicleRoot != null)
            {
                return vehicleRoot;
            }

            if (playerCar != null)
            {
                return playerCar.transform;
            }

            return vehicleRigidbody != null ? vehicleRigidbody.transform : transform;
        }

        private Vector3 GetVehicleForward()
        {
            Transform root = GetVehicleRoot();
            Vector3 forward = root != null ? root.forward : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        private float GetVehicleYaw()
        {
            Transform root = GetVehicleRoot();
            return root != null ? root.eulerAngles.y : transform.eulerAngles.y;
        }

        private Vector3 InverseVehiclePoint(Vector3 point)
        {
            Transform root = GetVehicleRoot();
            return root != null ? root.InverseTransformPoint(point) : transform.InverseTransformPoint(point);
        }

        private Vector3 InverseVehicleDirection(Vector3 direction)
        {
            Transform root = GetVehicleRoot();
            return root != null ? root.InverseTransformDirection(direction) : transform.InverseTransformDirection(direction);
        }

        private void ResetAgentLocalPose()
        {
            Transform root = GetVehicleRoot();
            if (root == null || transform == root || transform.parent != root)
            {
                return;
            }

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private Vector3 GetVelocity()
        {
            if (vehicleRigidbody == null)
            {
                return Vector3.zero;
            }

#if UNITY_6000_0_OR_NEWER
            return vehicleRigidbody.linearVelocity;
#else
            return vehicleRigidbody.velocity;
#endif
        }

        private void SetVelocity(Vector3 velocity, Vector3 angularVelocity)
        {
            if (vehicleRigidbody == null)
            {
                return;
            }

#if UNITY_6000_0_OR_NEWER
            vehicleRigidbody.linearVelocity = velocity;
#else
            vehicleRigidbody.velocity = velocity;
#endif
            vehicleRigidbody.angularVelocity = angularVelocity;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform match = FindChildRecursive(child, childName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static float GetPlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static void RecordStat(
            string name,
            float value,
            StatAggregationMethod aggregationMethod = StatAggregationMethod.Average)
        {
            Academy.Instance.StatsRecorder.Add(name, value, aggregationMethod);
        }

        private void OnValidate()
        {
            speedMultiplier = Mathf.Max(0.1f, speedMultiplier);
            waypointReachDistanceMeters = Mathf.Max(0.5f, waypointReachDistanceMeters);
            finalReachDistanceMeters = Mathf.Max(0.25f, finalReachDistanceMeters);
            baseCruiseSpeedMetersPerSecond = Mathf.Max(0.5f, baseCruiseSpeedMetersPerSecond);
            maxPolicySpeedMetersPerSecond = Mathf.Max(0f, maxPolicySpeedMetersPerSecond);
            speedLimitBrakeInput = Mathf.Clamp(speedLimitBrakeInput, -1f, 0f);
            maxObservationSpeedMetersPerSecond = Mathf.Max(0.5f, maxObservationSpeedMetersPerSecond);
            maxSteeringAngleForFullInput = Mathf.Max(1f, maxSteeringAngleForFullInput);
            hardTurnAngle = Mathf.Max(maxSteeringAngleForFullInput, hardTurnAngle);
            slowDownDistanceMeters = Mathf.Max(1f, slowDownDistanceMeters);
            lookAheadTimeSeconds = Mathf.Max(0.01f, lookAheadTimeSeconds);
            minLookAheadMeters = Mathf.Max(0.5f, minLookAheadMeters);
            maxLookAheadMeters = Mathf.Max(minLookAheadMeters, maxLookAheadMeters);
            steeringInputSmoothing = Mathf.Max(0.1f, steeringInputSmoothing);
            throttleInputSmoothing = Mathf.Max(0.1f, throttleInputSmoothing);
            maxObservationDistanceMeters = Mathf.Max(1f, maxObservationDistanceMeters);
            maxCrossTrackErrorMeters = Mathf.Max(0.1f, maxCrossTrackErrorMeters);
            maxRoadWaypointDistanceMeters = Mathf.Max(0f, maxRoadWaypointDistanceMeters);
            rayLengthMeters = Mathf.Max(0.1f, rayLengthMeters);
            rayHeightMeters = Mathf.Max(0f, rayHeightMeters);
            waypointProgressRewardPerMeter = Mathf.Max(0f, waypointProgressRewardPerMeter);
            waypointRegressionPenaltyPerMeter = Mathf.Min(0f, waypointRegressionPenaltyPerMeter);
            destinationProgressRewardPerMeter = Mathf.Max(0f, destinationProgressRewardPerMeter);
            headingAlignmentReward = Mathf.Max(0f, headingAlignmentReward);
            waypointHeadingReward = Mathf.Max(0f, waypointHeadingReward);
            curvePenalty = Mathf.Min(0f, curvePenalty);
            crossTrackPenalty = Mathf.Min(0f, crossTrackPenalty);
            steeringCorrectionReward = Mathf.Max(0f, steeringCorrectionReward);
            waypointPassedReward = Mathf.Max(0f, waypointPassedReward);
            waypointStuckPenalty = Mathf.Min(0f, waypointStuckPenalty);
            destinationReward = Mathf.Max(0f, destinationReward);
            assignedRouteExitPenalty = Mathf.Min(0f, assignedRouteExitPenalty);
            noProgressTimeoutRealSeconds = Mathf.Max(0.5f, noProgressTimeoutRealSeconds);
            waypointTimeoutSeconds = Mathf.Max(0.5f, waypointTimeoutSeconds);
            minimumProgressMeters = Mathf.Max(0.01f, minimumProgressMeters);
            hardCrossTrackLimitMeters = Mathf.Max(maxCrossTrackErrorMeters, hardCrossTrackLimitMeters);
            reverseGraceSeconds = Mathf.Max(0f, reverseGraceSeconds);
            stopRuleDistanceMeters = Mathf.Max(0.1f, stopRuleDistanceMeters);
            stopRuleSpeedMetersPerSecond = Mathf.Max(0.1f, stopRuleSpeedMetersPerSecond);
        }
    }

    [AddComponentMenu("")]
    public class DRTPPOVehicleCollisionRelay : MonoBehaviour
    {
        private DRTPPOVehicleDriver owner;

        public void Configure(DRTPPOVehicleDriver newOwner)
        {
            owner = newOwner;
        }

        private void OnCollisionEnter(Collision collision)
        {
            owner?.NotifyVehicleCollision(collision);
        }
    }
}
