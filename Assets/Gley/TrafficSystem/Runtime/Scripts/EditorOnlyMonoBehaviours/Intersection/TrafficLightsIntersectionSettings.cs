#if UNITY_EDITOR
using Gley.UrbanSystem;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Stores traffic lights intersection properties
    /// </summary>
    public class TrafficLightsIntersectionSettings : GenericIntersectionSettings
    {

        public List<WaypointSettingsBase> pedestrianWaypoints;
        public List<WaypointSettingsBase> directionWaypoints;
        public List<GameObject> pedestrianRedLightObjects;
        public List<GameObject> pedestrianGreenLightObjects;
        public float pedestrianGreenLightTime;

        public List<IntersectionStopWaypointsSettings> stopWaypoints;
        public List<WaypointSettings> exitWaypoints;
        public float greenLightTime = 10;
        public float yellowLightTime = 2;
        public bool setGreenLightTimePerRoad;
        public bool ShowPerRoadPedestrians;

        public override GenericIntersectionSettings Initialize()
        {
            base.Initialize();
            stopWaypoints = new List<IntersectionStopWaypointsSettings>
            {
                new()
            };
            exitWaypoints = new List<WaypointSettings>();


            pedestrianRedLightObjects = new List<GameObject>();
            pedestrianGreenLightObjects = new List<GameObject>();
            pedestrianWaypoints = new List<WaypointSettingsBase>();
            directionWaypoints = new List<WaypointSettingsBase>();

            return this;
        }


        public override List<IntersectionStopWaypointsSettings> GetAssignedWaypoints()
        {
            return stopWaypoints;
        }


        public override List<WaypointSettings> GetStopWaypoints(int road)
        {
            return stopWaypoints[road].roadWaypoints;
        }


        public override List<WaypointSettings> GetExitWaypoints()
        {
            return exitWaypoints;
        }


        public override bool VerifyAssignments()
        {
            bool correct = true;
            if (stopWaypoints == null)
            {
                stopWaypoints = new List<IntersectionStopWaypointsSettings>();
            }

            if (!justCreated && stopWaypoints.Count < 2)
            {
                Debug.LogError($"Traffic Lights Intersection {name} has only {stopWaypoints.Count} roads. Please assign at least 2 or create a Traffic Lights Crossing", gameObject);
                correct=false;
            }

            for (int i = 0; i < stopWaypoints.Count; i++)
            {
                if (stopWaypoints[i].roadWaypoints == null)
                {
                    stopWaypoints[i].roadWaypoints = new List<WaypointSettings>();
                }
                for (int j = stopWaypoints[i].roadWaypoints.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].roadWaypoints[j] == null)
                    {
                        stopWaypoints[i].roadWaypoints.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].redLightObjects == null)
                {
                    stopWaypoints[i].redLightObjects = new List<GameObject>();
                }
                for (int j = stopWaypoints[i].redLightObjects.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].redLightObjects[j] == null)
                    {
                        stopWaypoints[i].redLightObjects.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].yellowLightObjects == null)
                {
                    stopWaypoints[i].yellowLightObjects = new List<GameObject>();
                }
                for (int j = stopWaypoints[i].yellowLightObjects.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].yellowLightObjects[j] == null)
                    {
                        stopWaypoints[i].yellowLightObjects.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].greenLightObjects == null)
                {
                    stopWaypoints[i].greenLightObjects = new List<GameObject>();
                }
                for (int j = stopWaypoints[i].greenLightObjects.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].greenLightObjects[j] == null)
                    {
                        stopWaypoints[i].greenLightObjects.RemoveAt(j);
                    }
                }

#if GLEY_PEDESTRIAN_SYSTEM
                if (stopWaypoints[i].pedestrianWaypoints == null)
                {
                    stopWaypoints[i].pedestrianWaypoints = new List<WaypointSettingsBase>();
                }
                for (int j = stopWaypoints[i].pedestrianWaypoints.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].pedestrianWaypoints[j] == null)
                    {
                        stopWaypoints[i].pedestrianWaypoints.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].directionWaypoints == null)
                {
                    stopWaypoints[i].directionWaypoints = new List<WaypointSettingsBase>();
                }
                for (int j = stopWaypoints[i].directionWaypoints.Count - 1; j >= 0; j--)
                {
                    if (stopWaypoints[i].directionWaypoints[j] == null)
                    {
                        stopWaypoints[i].directionWaypoints.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].PedestrianGreenLightObjects == null)
                {
                    stopWaypoints[i].PedestrianGreenLightObjects = new List<GameObject>();
                }
                for(int j = stopWaypoints[i].PedestrianGreenLightObjects.Count-1;j>=0;j--)
                {
                    if (stopWaypoints[i].PedestrianGreenLightObjects[j]==null)
                    {
                        stopWaypoints[i].PedestrianGreenLightObjects.RemoveAt(j);
                    }
                }

                if (stopWaypoints[i].PedestrianRedLightObjects == null)
                {
                    stopWaypoints[i].PedestrianRedLightObjects = new List<GameObject>();
                }
                for (int j = stopWaypoints[i].PedestrianRedLightObjects.Count-1; j >= 0; j--)
                {
                    if (stopWaypoints[i].PedestrianRedLightObjects[j] == null)
                    {
                        stopWaypoints[i].PedestrianRedLightObjects.RemoveAt(j);
                    }
                }
#endif
            }
            if (exitWaypoints == null)
            {
                exitWaypoints = new List<WaypointSettings>();
            }
            for (int i = exitWaypoints.Count - 1; i >= 0; i--)
            {
                if (exitWaypoints[i] == null)
                {
                    exitWaypoints.RemoveAt(i);
                }
            }

#if GLEY_PEDESTRIAN_SYSTEM
            if (directionWaypoints == null)
            {
                directionWaypoints = new List<WaypointSettingsBase>();
            }

            if (pedestrianWaypoints == null)
            {
                pedestrianWaypoints = new List<WaypointSettingsBase>();
            }

            var pedWaypoints = GetPedestrianWaypoints();

            for (int i = directionWaypoints.Count - 1; i >= 0; i--)
            {
                if (directionWaypoints[i] == null)
                {
                    directionWaypoints.RemoveAt(i);
                }
                else
                {
                    if (!directionWaypoints[i].neighbors.Intersect(pedWaypoints).Any() && !directionWaypoints[i].prev.Intersect(pedWaypoints).Any())
                    {
                        directionWaypoints.RemoveAt(i);
                    }
                }
            }


           
            for (int i = pedWaypoints.Count - 1; i >= 0; i--)
            {
                if (pedWaypoints[i] == null)
                {
                    pedWaypoints.RemoveAt(i);
                }
                else
                {
                    if (!pedWaypoints[i].neighbors.Intersect(directionWaypoints).Any() && !pedWaypoints[i].prev.Intersect(directionWaypoints).Any())
                    {
                        Debug.LogError($"Pedestrian waypoint {pedWaypoints[i].name} from intersection {name} has no direction assigned", gameObject);
                        correct = false;
                    }
                }
            }

            if (pedestrianRedLightObjects == null)
            {
                pedestrianRedLightObjects = new List<GameObject>();
            }
            for (int i = pedestrianRedLightObjects.Count - 1; i >= 0; i--)
            {
                if (pedestrianRedLightObjects[i] == null)
                {
                    pedestrianRedLightObjects.RemoveAt(i);
                }
            }

            if (pedestrianGreenLightObjects == null)
            {
                pedestrianGreenLightObjects = new List<GameObject>();
            }
            for (int i = pedestrianGreenLightObjects.Count - 1; i >= 0; i--)
            {
                if (pedestrianGreenLightObjects[i] == null)
                {
                    pedestrianGreenLightObjects.RemoveAt(i);
                }
            }
#endif
            base.VerifyAssignments();
            return correct;
        }



        public override List<WaypointSettingsBase> GetPedestrianWaypoints()
        {
            if(ShowPerRoadPedestrians)
            {
                return GetPedestrianWaypointsForAllRoads();
            }

            return pedestrianWaypoints;
        }


        public override List<WaypointSettingsBase> GetPedestrianWaypoints(int road)
        {
            return stopWaypoints[road].pedestrianWaypoints;
        }


        public override List<WaypointSettingsBase> GetDirectionWaypoints()
        {
            return directionWaypoints;
        }

        private  List<WaypointSettingsBase> GetPedestrianWaypointsForAllRoads()
        {
            var result = new List<WaypointSettingsBase>();
            for (int i = 0; i < stopWaypoints.Count; i++)
            {
                result.AddRange(stopWaypoints[i].pedestrianWaypoints);
            }
            return result;
        }
    }
}
#endif