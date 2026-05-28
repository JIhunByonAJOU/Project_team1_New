using System.Collections.Generic;
using Gley.TrafficSystem;
using Gley.UrbanSystem;
using UnityEngine;

using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;

namespace DRT
{
    public class DRTPlayerVehicleDriver : MonoBehaviour
    {
        [SerializeField] private PlayerCar playerCar;
        [SerializeField] private Rigidbody vehicleRigidbody;
        [SerializeField] private Transform bodyTransform;
        [SerializeField] private VehicleTypes vehicleType = VehicleTypes.Car;
        [SerializeField] private float baseCruiseSpeedMetersPerSecond = 10f;
        [SerializeField] private float speedMultiplier = 1f;
        [SerializeField] private float waypointReachDistanceMeters = 6f;
        [SerializeField] private float finalReachDistanceMeters = 4f;
        [SerializeField] private float slowDownDistanceMeters = 18f;
        [SerializeField] private float maxSteeringAngleForFullInput = 45f;
        [SerializeField] private float hardTurnAngle = 75f;

        private readonly List<Vector3> pathPoints = new List<Vector3>();
        private int targetPointIndex;
        private Vector3 finalDestination;
        private bool driving;

        public bool IsDriving => driving;
        public VehicleTypes VehicleType => vehicleType;
        public int PathPointCount => pathPoints.Count;
        public int RemainingPathPointCount => driving ? Mathf.Max(0, pathPoints.Count - targetPointIndex) : 0;
        public Vector3 BodyPosition => GetBodyPosition();

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

        public void Configure(VehicleTypes newVehicleType, float newSpeedMultiplier, float newWaypointReachDistance, float newFinalReachDistance)
        {
            vehicleType = newVehicleType;
            speedMultiplier = Mathf.Max(0.1f, newSpeedMultiplier);
            waypointReachDistanceMeters = Mathf.Max(0.5f, newWaypointReachDistance);
            finalReachDistanceMeters = Mathf.Max(0.25f, newFinalReachDistance);
            ResolveReferences();
        }

        public bool SetPath(List<int> waypointIndexes, Vector3 destination)
        {
            ResolveReferences();
            pathPoints.Clear();
            targetPointIndex = 0;
            finalDestination = destination;

            if (waypointIndexes != null)
            {
                for (int i = 0; i < waypointIndexes.Count; i++)
                {
                    var waypoint = API.GetWaypointFromIndex(waypointIndexes[i]);
                    if (waypoint != null)
                    {
                        AddPathPoint(waypoint.Position);
                    }
                }
            }

            AddPathPoint(destination);
            driving = pathPoints.Count > 0;

            if (playerCar != null)
            {
                playerCar.SetExternalInput(0f, 0f, true);
            }

            return driving;
        }

        public void StopAndHold(bool zeroVelocity)
        {
            driving = false;
            pathPoints.Clear();
            targetPointIndex = 0;

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
            pathPoints.Clear();
            targetPointIndex = 0;

            if (playerCar != null)
            {
                playerCar.ClearExternalInput();
            }
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            ResolveReferences();
            StopAndHold(true);

            if (vehicleRigidbody != null)
            {
                vehicleRigidbody.position = position;
                vehicleRigidbody.rotation = rotation;
            }

            transform.SetPositionAndRotation(position, rotation);
            SetVelocity(Vector3.zero, Vector3.zero);
            Physics.SyncTransforms();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            ReleaseControl();
        }

        private void FixedUpdate()
        {
            if (!driving)
            {
                return;
            }

            ResolveReferences();
            if (playerCar == null || pathPoints.Count == 0)
            {
                StopAndHold(false);
                return;
            }

            Vector3 bodyPosition = GetBodyPosition();
            float finalDistance = GetPlanarDistance(bodyPosition, finalDestination);
            if (finalDistance <= finalReachDistanceMeters)
            {
                StopAndHold(true);
                return;
            }

            AdvanceTargetPoint(bodyPosition);

            Vector3 targetPoint = pathPoints[Mathf.Clamp(targetPointIndex, 0, pathPoints.Count - 1)];
            Vector3 toTarget = targetPoint - bodyPosition;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.001f)
            {
                playerCar.SetExternalInput(0f, 0f, true);
                return;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            float angle = Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up);
            float steering = Mathf.Clamp(angle / Mathf.Max(1f, maxSteeringAngleForFullInput), -1f, 1f);
            float targetSpeed = GetTargetSpeed(Mathf.Abs(angle), finalDistance);
            float throttle = GetThrottleForTargetSpeed(targetSpeed, finalDistance);

            playerCar.SetExternalInput(steering, throttle, true);
        }

        private void ResolveReferences()
        {
            if (playerCar == null)
            {
                playerCar = GetComponent<PlayerCar>();
            }

            if (vehicleRigidbody == null)
            {
                vehicleRigidbody = GetComponent<Rigidbody>();
            }

            if (bodyTransform == null)
            {
                bodyTransform = FindChildRecursive(transform, "BodyHolder");
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

        private void AdvanceTargetPoint(Vector3 bodyPosition)
        {
            while (targetPointIndex < pathPoints.Count - 1)
            {
                float reachDistance = targetPointIndex == pathPoints.Count - 1
                    ? finalReachDistanceMeters
                    : waypointReachDistanceMeters;

                if (GetPlanarDistance(bodyPosition, pathPoints[targetPointIndex]) > reachDistance)
                {
                    return;
                }

                targetPointIndex++;
            }
        }

        private float GetTargetSpeed(float absSteeringAngle, float finalDistance)
        {
            float targetSpeed = baseCruiseSpeedMetersPerSecond * Mathf.Max(0.1f, speedMultiplier);

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

            return transform.position;
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

        private void OnValidate()
        {
            speedMultiplier = Mathf.Max(0.1f, speedMultiplier);
            baseCruiseSpeedMetersPerSecond = Mathf.Max(0.5f, baseCruiseSpeedMetersPerSecond);
            waypointReachDistanceMeters = Mathf.Max(0.5f, waypointReachDistanceMeters);
            finalReachDistanceMeters = Mathf.Max(0.25f, finalReachDistanceMeters);
            slowDownDistanceMeters = Mathf.Max(1f, slowDownDistanceMeters);
            maxSteeringAngleForFullInput = Mathf.Max(1f, maxSteeringAngleForFullInput);
            hardTurnAngle = Mathf.Max(maxSteeringAngleForFullInput, hardTurnAngle);
        }
    }
}
