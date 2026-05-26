#if UNITY_EDITOR
using Gley.UrbanSystem;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Stores stop waypoints properties
    /// </summary>
    [System.Serializable]
    public class IntersectionStopWaypointsSettings
    {
        public List<WaypointSettings> roadWaypoints = new List<WaypointSettings>();
        public List<GameObject> redLightObjects = new List<GameObject>();
        public List<GameObject> yellowLightObjects = new List<GameObject>();
        public List<GameObject> greenLightObjects = new List<GameObject>();
        public float greenLightTime;
        public bool draw = true;


        public List<WaypointSettingsBase> pedestrianWaypoints =  new List<WaypointSettingsBase>();
        public List<WaypointSettingsBase> directionWaypoints = new List<WaypointSettingsBase>();
        public List<GameObject> PedestrianRedLightObjects = new List<GameObject>();
        public List<GameObject> PedestrianGreenLightObjects = new List<GameObject>();
    }
}
#endif