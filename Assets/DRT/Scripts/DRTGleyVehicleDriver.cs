using System.Collections.Generic;
using Gley.TrafficSystem;
using UnityEngine;

using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;

namespace DRT
{
    [AddComponentMenu("DRT/DRT Gley Vehicle Driver")]
    public class DRTGleyVehicleDriver : MonoBehaviour, IDRTVehicleDriver
    {
        [SerializeField] private int vehicleIndex;
        [SerializeField] private VehicleTypes vehicleType = VehicleTypes.Car;

        private VehicleComponent vehicleComponent;

        public Transform VehicleTransform => vehicleComponent != null ? vehicleComponent.transform : null;
        public string VehicleName => vehicleComponent != null ? vehicleComponent.name : $"GleyVehicle[{vehicleIndex}]";
        public int VehicleIndex => vehicleIndex;
        public VehicleTypes VehicleType => vehicleType;
        public int PathPointCount => vehicleComponent != null ? vehicleComponent.MovementInfo.PathLength : 0;
        public int RemainingPathPointCount => vehicleComponent != null ? vehicleComponent.MovementInfo.RemainingPathLength : 0;
        public Vector3 BodyPosition => GetBodyPosition();

        public float CurrentSpeedMS
        {
            get
            {
                return ResolveVehicleComponent(false) && vehicleComponent != null
                    ? vehicleComponent.GetCurrentSpeedMS()
                    : 0f;
            }
        }

        public void Configure(int newVehicleIndex, VehicleTypes newVehicleType)
        {
            vehicleIndex = Mathf.Max(0, newVehicleIndex);
            vehicleType = newVehicleType;
            ResolveVehicleComponent(false);
        }

        public bool SetPath(List<int> waypointIndexes, Vector3 destination)
        {
            if (!ResolveVehicleComponent(true) || waypointIndexes == null || waypointIndexes.Count == 0)
            {
                return false;
            }

            API.DontRemoveVehicle(vehicleIndex, true);
            API.ResumeVehicleDriving(vehicleComponent.gameObject);
            API.SetVehiclePath(vehicleIndex, waypointIndexes);
            return true;
        }

        public void StopAndHold(bool zeroVelocity)
        {
            if (!ResolveVehicleComponent(false) || vehicleComponent == null)
            {
                return;
            }

            API.StopVehicleDriving(vehicleComponent.gameObject);
            API.RemoveVehiclePath(vehicleIndex);

            if (zeroVelocity)
            {
                vehicleComponent.SetVelocity(Vector3.zero, Vector3.zero);
            }
        }

        public void ReleaseControl()
        {
            if (!ResolveVehicleComponent(false) || vehicleComponent == null)
            {
                return;
            }

            API.DontRemoveVehicle(vehicleIndex, false);
            API.RemoveVehiclePath(vehicleIndex);
        }

        public void TeleportTo(Vector3 position, Quaternion rotation, int nextWaypointIndex)
        {
            if (!ResolveVehicleComponent(true))
            {
                return;
            }

            API.DontRemoveVehicle(vehicleIndex, true);
            API.InstantiateVehicleOnTheSpot(
                vehicleIndex,
                position,
                rotation,
                Vector3.zero,
                Vector3.zero,
                nextWaypointIndex);

            ResolveVehicleComponent(false);
            if (vehicleComponent != null)
            {
                vehicleComponent.SetVelocity(Vector3.zero, Vector3.zero);
            }
        }

        private bool ResolveVehicleComponent(bool logIfMissing)
        {
            if (!API.IsInitialized())
            {
                return false;
            }

            vehicleComponent = API.GetVehicleComponent(vehicleIndex);
            if (vehicleComponent == null && logIfMissing)
            {
                Debug.LogWarning($"[DRT] Gley vehicle component not found. vehicleIndex={vehicleIndex}");
            }

            return vehicleComponent != null;
        }

        private Vector3 GetBodyPosition()
        {
            if (!ResolveVehicleComponent(false) || vehicleComponent == null)
            {
                return Vector3.zero;
            }

            return vehicleComponent.rb != null
                ? vehicleComponent.rb.position
                : vehicleComponent.transform.position;
        }
    }
}
