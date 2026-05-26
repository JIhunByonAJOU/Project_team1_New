using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Struct to store the intersection road properties for each traffic light intersection.
    /// </summary>
    [System.Serializable]
    public class TrafficLightsIntersectionData
    {
        public string Name;
        public LightsStopWaypoints[] StopWaypoints;
        public float GreenLightTime;
        public float YellowLightTime;
        public int[] ExitWaypoints;

        public int[] PedestrianWaypoints;
        public int[] DirectionWaypoints;
        public GameObject[] RedLightObjects;
        public GameObject[] GreenLightObjects;
        public float PedestrianGreenLightTime;


        public TrafficLightsIntersectionData(string name, LightsStopWaypoints[] stopWaypoints, float greenLightTime, float yellowLightTime, int[] exitWaypoints)
        {
            Name = name;
            StopWaypoints = stopWaypoints;
            GreenLightTime = greenLightTime;
            YellowLightTime = yellowLightTime;
            ExitWaypoints = exitWaypoints;
        }


        public void AddPedestrianWaypoints(int[] pedestrianWaypoints, int[] directionWaypoints, GameObject[] redLightObjects, GameObject[] greenLightObjects, float pedestrianGreenLightTime)
        {
            PedestrianWaypoints = pedestrianWaypoints;
            DirectionWaypoints = directionWaypoints;
            RedLightObjects = redLightObjects;
            GreenLightObjects = greenLightObjects;
            PedestrianGreenLightTime = pedestrianGreenLightTime;
        }

        public void AddPedestrianWaypoints(int road, int[] pedestrianWaypoints, GameObject[] redLightObjects, GameObject[] greenLightObjects)
        {
            StopWaypoints[road].PedestrianWaypoints = pedestrianWaypoints;
            StopWaypoints[road].PedestrianRedLightObjects = redLightObjects;
            StopWaypoints[road].PedestrianGreenLightObjects = greenLightObjects;
        }
        public void AddDirectionWaypoints(int[] directionWaypoints)
        {
            DirectionWaypoints = directionWaypoints;
        }
    }
}