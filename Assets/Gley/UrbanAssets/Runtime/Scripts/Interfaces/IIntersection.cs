namespace Gley.UrbanSystem
{
    /// <summary>
    /// Used to set the intersection on waypoint
    /// </summary>
    public interface IIntersection
    {
        bool IsPathFree(int waypointIndex);
        void VehicleLeft(int vehicleIndex);
        void VehicleEnter(int vehicleIndex);
        void PedestrianPassed(int agentIndex);
    }
}