using Gley.UrbanSystem;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if GLEY_TRAFFIC_SYSTEM
using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;
#else
using VehicleTypes = Gley.TrafficSystem.VehicleTypes;
#endif

namespace Gley.TrafficSystem.Editor
{
    public class TrafficWaypointCreator
    {
        public TrafficWaypointCreator Initialize()
        {
            return this;
        }


        public Transform CreateWaypoint(Transform parent, Vector3 waypointPosition, string name, List<int> allowedCars, int maxSpeed, float laneWidth)
        {
            GameObject go = MonoBehaviourUtilities.CreateGameObject(name, parent, waypointPosition, true);
            WaypointSettings waypointScript = go.AddComponent<WaypointSettings>();
            waypointScript.Initialize();
            waypointScript.allowedCars = allowedCars.Cast<VehicleTypes>().ToList();
            waypointScript.maxSpeed = maxSpeed;
            waypointScript.laneWidth = laneWidth;
            waypointScript.position = waypointPosition;
            return go.transform;
        }
    }
}