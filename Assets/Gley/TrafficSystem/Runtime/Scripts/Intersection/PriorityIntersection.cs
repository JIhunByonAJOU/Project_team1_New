using Gley.UrbanSystem;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Controls the priority intersection.
    /// </summary>
    public class PriorityIntersection : GenericIntersection, IDestroyable
    {
        private readonly List<int> _waypointsToCkeck;
        private readonly List<Color> _waypointColor;
        private readonly float _requiredTime;

        private Dictionary<int, PedestrianCrossing> _pedestriansCrossing;
        private PriorityIntersectionData _priorityIntersectionData;
        private Vector3 _position;
        private float _currentTime;
        private int _currentRoadIndex;
        private int _nrOfRoads;
        private bool _currentRoadIsActive = true;

        public PriorityIntersection(PriorityIntersectionData priorityIntersectionData, TrafficWaypointsData trafficWaypointsData, IPedestrianWaypointsDataHandler pedestrianWaypointsDataHandler)
        {
            _priorityIntersectionData = priorityIntersectionData;
            for (int i = 0; i < _priorityIntersectionData.ExitWaypoints.Length; i++)
            {
                trafficWaypointsData.AllTrafficWaypoints[_priorityIntersectionData.ExitWaypoints[i]].SetIntersection(this, false, false, false, true, false);
            }
            int nr = 0;
            for (int i = 0; i < _priorityIntersectionData.StopWaypoints.Length; i++)
            {
                for (int j = 0; j < _priorityIntersectionData.StopWaypoints[i].roadWaypoints.Length; j++)
                {
                    trafficWaypointsData.AllTrafficWaypoints[_priorityIntersectionData.StopWaypoints[i].roadWaypoints[j]].SetIntersection(this, true, false, true, false, true);
                    _position += trafficWaypointsData.AllTrafficWaypoints[_priorityIntersectionData.StopWaypoints[i].roadWaypoints[j]].Position;
                    nr++;
                }
            }
            _position = _position / nr;

            InitializePedestrianWaypoints(pedestrianWaypointsDataHandler);

            _carsInIntersection = new List<int>();
            _requiredTime = 3;
            _waypointsToCkeck = new List<int>();
            _waypointColor = new List<Color>();
            _nrOfRoads = _priorityIntersectionData.StopWaypoints.Length;
            _currentRoadIndex = Random.Range(0, _nrOfRoads);
            _currentTime = Time.timeSinceLevelLoad;
            Assign();
        }


        public void Assign()
        {
            DestroyableManager.Instance.Register(this);
        }


        public override bool IsPathFree(int waypointIndex)
        {
            if (_currentRoadIsActive)
            {
                var roadWaypoints = _priorityIntersectionData.StopWaypoints[_currentRoadIndex].roadWaypoints;
                for (int i = 0; i < roadWaypoints.Length; i++)
                {
                    if (roadWaypoints[i] == waypointIndex)
                    {
                        if (IsPedestrianCrossing(_currentRoadIndex))
                        {
                            _currentRoadIsActive = false;
                            return false;
                        }
                        _currentTime = Time.timeSinceLevelLoad;
                        return true;
                    }
                }
                // reset road after an amount of time and after all the cars left intersections
                if (Time.timeSinceLevelLoad - _currentTime > _requiredTime && _carsInIntersection.Count <= 0)
                {
                    _currentRoadIsActive = false;
                    _currentTime = Time.timeSinceLevelLoad;
                }
            }
            else
            {
                for (int i = 0; i < _priorityIntersectionData.StopWaypoints.Length; i++)
                {
                    //set the current road as the road of the current waypoint and make the road active
                    var roadWaypoints = _priorityIntersectionData.StopWaypoints[i].roadWaypoints;
                    for (int j = 0; j < roadWaypoints.Length; j++)
                    {
                        if (roadWaypoints[j] == waypointIndex)
                        {
                            if (!IsPedestrianCrossing(i))
                            {
                                _currentRoadIndex = i;
                                _currentRoadIsActive = true;
                            }
                        }
                    }
                }
            }
            return false;
        }


        public override void PedestrianPassed(int pedestrianIndex)
        {
            if (_pedestriansCrossing.TryGetValue(pedestrianIndex, out var ped))
            {
                if (ped.Crossing == false)
                {
                    ped.Crossing = true;
                }
                else
                {
                    _pedestriansCrossing.Remove(pedestrianIndex);
                }
            }
        }


        public override string GetName()
        {
            return _priorityIntersectionData.Name;
        }


        public override void ResetIntersection()
        {
            base.ResetIntersection();
            _pedestriansCrossing = new Dictionary<int, PedestrianCrossing>();
        }

        public Vector3 GetPosition()
        {
            return _position;
        }


        public List<int> GetWaypointsToCkeck()
        {
            return _waypointsToCkeck;
        }


        public List<Color> GetWaypointColors()
        {
            return _waypointColor;
        }

        public override List<int> GetStopWaypoints()
        {
            var result = new List<int>();
            for (int i = 0; i < _priorityIntersectionData.StopWaypoints.Length; i++)
            {
                result.AddRange(_priorityIntersectionData.StopWaypoints[i].roadWaypoints);
            }
            return result;
        }


        public override void UpdateIntersection(float realtimeSinceStartup)
        {

        }


        public int GetCarsInIntersection()
        {
            return _carsInIntersection.Count;
        }


        public int GetNrPedestriansCrossing()
        {
            return _pedestriansCrossing.Count;
        }


        public override int[] GetPedStopWaypoint()
        {
            return new int[0];
        }


        private void InitializePedestrianWaypoints(IPedestrianWaypointsDataHandler pedestrianWaypointsDataHandler)
        {
            _pedestriansCrossing = new Dictionary<int, PedestrianCrossing>();
#if GLEY_PEDESTRIAN_SYSTEM
            SetPedestrianIntersection(pedestrianWaypointsDataHandler);
            SharedPedestrianEvents.OnStreetCrossing += PedestrianWantsToCross;
#endif
        }

        public override void SetPedestrianIntersection(IPedestrianWaypointsDataHandler pedestrianWaypointsDataHandler)
        {
            for (int i = 0; i < _priorityIntersectionData.StopWaypoints.Length; i++)
            {
                pedestrianWaypointsDataHandler.SetIntersection(_priorityIntersectionData.StopWaypoints[i].pedestrianWaypoints, this);
            }
        }


        private void PedestrianWantsToCross(int pedestrianIndex, List<IIntersection> intersections, int waypointIndex)
        {
            foreach (var intersection in intersections)
            {
                if (intersection == this)
                {
                    int road = GetRoadToCross(waypointIndex);
                    _pedestriansCrossing.TryAdd(pedestrianIndex, new PedestrianCrossing(pedestrianIndex, road));
                }
            }
        }


        private int GetRoadToCross(int waypoint)
        {
            for (int i = 0; i < _priorityIntersectionData.StopWaypoints.Length; i++)
            {
                for (int j = 0; j < _priorityIntersectionData.StopWaypoints[i].pedestrianWaypoints.Length; j++)
                {
                    if (_priorityIntersectionData.StopWaypoints[i].pedestrianWaypoints[j] == waypoint)
                    {
                        return i;
                    }
                }
            }
            Debug.LogError("Not Good - verify pedestrians assignments in priority intersection");
            return -1;
        }


        private bool IsPedestrianCrossing(int road)
        {
            if (_pedestriansCrossing.Count == 0)
            {
                return false;
            }

            foreach(var ped in _pedestriansCrossing)
            {
                if(ped.Value.Road==road)
                {
                    return true;
                }
            }

            return false;
        }


        public void OnDestroy()
        {
#if GLEY_PEDESTRIAN_SYSTEM
            SharedPedestrianEvents.OnStreetCrossing -= PedestrianWantsToCross;
#endif
        }
    }
}