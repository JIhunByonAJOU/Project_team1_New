using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Used to store intersection objects
    /// </summary>
    [System.Serializable]
    public class LightsStopWaypoints
    {
        public int[] roadWaypoints;
        public GameObject[] redLightObjects;
        public GameObject[] yellowLightObjects;
        public GameObject[] greenLightObjects;
        public float greenLightTime;

        //pedestrians
        public int[] PedestrianWaypoints;
        public GameObject[] PedestrianRedLightObjects;
        public GameObject[] PedestrianGreenLightObjects;

        public LightsStopWaypoints(int[] roadWaypoints, GameObject[] redLightObjects, GameObject[] yellowLightObjects, GameObject[] greenLightObjects, float greenLightTime)
        {
            this.roadWaypoints = roadWaypoints;
            this.redLightObjects = redLightObjects;
            this.yellowLightObjects = yellowLightObjects;
            this.greenLightObjects = greenLightObjects;
            this.greenLightTime = greenLightTime;
        }
    }
}