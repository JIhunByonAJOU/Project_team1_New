namespace Gley.UrbanSystem
{
    public interface IPedestrianWaypointsDataHandler
    {
        void SetIntersection(int[] pedestrianWaypointIndexes, IIntersection intersection);
    }
}