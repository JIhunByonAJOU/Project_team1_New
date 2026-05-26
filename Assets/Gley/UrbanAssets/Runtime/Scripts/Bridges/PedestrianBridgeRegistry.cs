using UnityEngine;

namespace Gley.UrbanSystem
{
    public static class PedestrianBridgeRegistry
    {
        public static event System.Action<IPedestrianWaypointsDataHandler> OnRegistered;

        public static IPedestrianWaypointsDataHandler WaypointsHandler { get; private set; }
        public static IPedestrianBridge DebugProvider { get; private set; }

        public static void Register(
            IPedestrianWaypointsDataHandler waypoints,
            IPedestrianBridge debug)
        {
            WaypointsHandler = waypoints;
            DebugProvider = debug;
            OnRegistered?.Invoke(waypoints);
        }

        public static bool HasPedestrians => WaypointsHandler != null;
    }
}
