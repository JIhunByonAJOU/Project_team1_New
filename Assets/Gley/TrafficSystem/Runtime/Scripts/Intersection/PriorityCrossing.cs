using Gley.UrbanSystem;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.TrafficSystem
{
    /// <summary>
    /// Controls the priority crossing intersection type.
    /// </summary>
    public class PriorityCrossing : GenericIntersection, IDestroyable
    {
        private Dictionary<int, PedestrianCrossing> _pedestriansCrossing;
        private PriorityCrossingData _priorityCrossingData;
        private Vector3 _position;
        private Color _waypointColor;
        private int _nrOfCarsInIntersection;
        private bool _stopCars;
        private bool _stopUpdate;
        private bool _hasExitWaypoints;


        public PriorityCrossing(PriorityCrossingData priorityCrossingData, TrafficWaypointsData trafficWaypointsData, IPedestrianWaypointsDataHandler pedestrianWaypointsDataHandler)
        {
            _priorityCrossingData = priorityCrossingData;

            for (int i = 0; i < _priorityCrossingData.ExitWaypoints.Length; i++)
            {
                trafficWaypointsData.AllTrafficWaypoints[_priorityCrossingData.ExitWaypoints[i]].SetIntersection(this, false, false, false, true, false);
            }
            if (_priorityCrossingData.ExitWaypoints.Length > 0)
            {
                _hasExitWaypoints = true;
            }
            int nr = 0;
            for (int i = 0; i < _priorityCrossingData.StopWaypoints.Length; i++)
            {
                for (int j = 0; j < _priorityCrossingData.StopWaypoints[i].roadWaypoints.Length; j++)
                {
                    trafficWaypointsData.AllTrafficWaypoints[_priorityCrossingData.StopWaypoints[i].roadWaypoints[j]].SetIntersection(this, true, false, true, false, true);
                    _position += trafficWaypointsData.AllTrafficWaypoints[_priorityCrossingData.StopWaypoints[i].roadWaypoints[j]].Position;
                    nr++;
                }
            }
            _position = _position / nr;

            InitializePedestrianWaypoints(pedestrianWaypointsDataHandler);

            _carsInIntersection = new List<int>();
            _waypointColor = Color.green;
            Assign();
        }


        public void Assign()
        {
            DestroyableManager.Instance.Register(this);
        }


        /// <summary>
        /// Check if the intersection road is free and update intersection priority
        /// </summary>
        /// <param name="waypointIndex"></param>
        /// <returns></returns>
        public override bool IsPathFree(int waypointIndex)
        {
            if (!_stopUpdate)
            {
                _stopCars = IsPedestrianCrossing(0);
            }
            if (_stopCars)
            {
                if (_waypointColor != Color.red)
                {
                    _waypointColor = Color.red;
                }
                return false;
            }
            else
            {
                if (_waypointColor != Color.green)
                {
                    _waypointColor = Color.green;
                }
            }

            return true;
        }


        public override string GetName()
        {
            return _priorityCrossingData.Name;
        }

        public override void PedestriansSystemInitialized()
        {
        }


        public override void PedestrianPassed(int pedestrianIndex)
        {
#if GLEY_PEDESTRIAN_SYSTEM
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
#endif
        }


        public int[] GetWaypointsToCkeck()
        {
            return _priorityCrossingData.StopWaypoints[0].roadWaypoints;
        }


        public Color GetWaypointColors()
        {
            return _waypointColor;
        }


        public override List<int> GetStopWaypoints()
        {
            var result = new List<int>();
            for (int i = 0; i < _priorityCrossingData.StopWaypoints.Length; i++)
            {
                result.AddRange(_priorityCrossingData.StopWaypoints[i].roadWaypoints);
            }
            return result;
        }


        public void SetPriorityCrossingState(bool stop, bool stopUpdate)
        {
            _stopCars = stop;
            _stopUpdate = stopUpdate;
            if (!_stopCars)
            {
                StopPedestriansCrossing();
            }
            else
            {
                MakePedestriansCross(0);
            }
        }


        public bool GetPriorityCrossingState()
        {
            return _waypointColor == Color.red;
        }


        public override void UpdateIntersection(float realtimeSinceStartup)
        {
            if (!_stopUpdate)
            {
                if (_hasExitWaypoints)
                {
                    if (_nrOfCarsInIntersection != _carsInIntersection.Count)
                    {
                        _nrOfCarsInIntersection = _carsInIntersection.Count;
                        if (_nrOfCarsInIntersection > 0)
                        {
                            StopPedestriansCrossing();
                        }
                        else
                        {
                            MakePedestriansCross(0);
                        }
                    }
                }
            }
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


        public Vector3 GetPosition()
        {
            return _position;
        }

        public override void ResetIntersection()
        {
            base.ResetIntersection();
            _pedestriansCrossing = new Dictionary<int, PedestrianCrossing>();
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
            for (int i = 0; i < _priorityCrossingData.StopWaypoints.Length; i++)
            {
                pedestrianWaypointsDataHandler.SetIntersection(_priorityCrossingData.StopWaypoints[i].pedestrianWaypoints, this);
            }
        }


        private void MakePedestriansCross(int road)
        {
#if GLEY_PEDESTRIAN_SYSTEM
            for (int i = 0; i < _priorityCrossingData.StopWaypoints[road].pedestrianWaypoints.Length; i++)
            {
                SharedPedestrianEvents.TriggerStopStateChangedEvent(_priorityCrossingData.StopWaypoints[road].pedestrianWaypoints[i], false);
            }
#endif
        }

        private void StopPedestriansCrossing()
        {

#if GLEY_PEDESTRIAN_SYSTEM
            for (int i = 0; i < _priorityCrossingData.StopWaypoints[0].pedestrianWaypoints.Length; i++)
            {
                SharedPedestrianEvents.TriggerStopStateChangedEvent(_priorityCrossingData.StopWaypoints[0].pedestrianWaypoints[i], true);
            }
#endif
        }


        private void PedestrianWantsToCross(int pedestrianIndex, List<IIntersection> intersections, int waypointIndex)
        {
            foreach (IIntersection intersection in intersections)
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
            for (int i = 0; i < _priorityCrossingData.StopWaypoints.Length; i++)
            {
                for (int j = 0; j < _priorityCrossingData.StopWaypoints[i].pedestrianWaypoints.Length; j++)
                {
                    if (_priorityCrossingData.StopWaypoints[i].pedestrianWaypoints[j] == waypoint)
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
            return true;
        }


        public void OnDestroy()
        {
#if GLEY_PEDESTRIAN_SYSTEM
            SharedPedestrianEvents.OnStreetCrossing -= PedestrianWantsToCross;
#endif
        }
    }
}