using UnityEngine;

namespace Gley.UrbanSystem
{
    public interface IPedestrianBridge
    {
        bool IsInitialized { get; }
        bool IsStopWaypoint(int waypointIndex);
        Vector3 GetWaypointPosition(int waypointIndex);
    }
}
