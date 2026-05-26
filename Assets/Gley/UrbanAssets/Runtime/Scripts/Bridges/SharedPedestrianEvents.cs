using System.Collections.Generic;
using UnityEngine;

namespace Gley.UrbanSystem
{
    public static class SharedPedestrianEvents
    {
        public static event System.Action<int> OnPedestrianRemoved;
        public static void TriggerPedestrianRemoved(int pedestrianIndex)
        {
            OnPedestrianRemoved?.Invoke(pedestrianIndex);
        }

        public static event System.Action<int, List<IIntersection>, int> OnStreetCrossing;
        public static void TriggerOnStreetCrossing(int pedestrianIndex, List<IIntersection> intersection, int waypointIndex)
        {
            OnStreetCrossing?.Invoke(pedestrianIndex, intersection, waypointIndex);
        }

        /// <summary>
        /// Triggered every time the stop property of a waypoint changed.
        /// </summary>
        /// <param name="waypointIndex">The index of the waypoint that changed.</param>
        /// <param name="stop">The new stop state.</param>
        public delegate void StopStateChanged(int waypointIndex, bool stop);
        public static event StopStateChanged OnStopStateChanged;
        public static void TriggerStopStateChangedEvent(int waypointIndex, bool stop)
        {
            OnStopStateChanged?.Invoke(waypointIndex, stop);
        }
    }
}
