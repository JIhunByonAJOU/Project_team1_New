using UnityEngine;

namespace Gley.UrbanSystem
{
    /// <summary>
    /// Stores path finding waypoints.
    /// </summary>
    public class PathFindingData : MonoBehaviour
    {
        [SerializeField] private PathFindingWaypoint[] _allPathFindingWaypoints;


        public PathFindingWaypoint[] AllPathFindingWaypoints => _allPathFindingWaypoints;


        public void SetPathFindingWaypoints(PathFindingWaypoint[] waypoints)
        {
            _allPathFindingWaypoints = waypoints;
        }


        public bool IsValid(out string error)
        {
            error = string.Empty;
            if (_allPathFindingWaypoints == null)
            {
                error= UrbanSystemErrors.NullPathFindingData;
                return false;
            }

            if (_allPathFindingWaypoints.Length <= 0)
            {
                error = UrbanSystemErrors.NoPathFindingWaypoints;
                return false;
            }
            return true;
        }

        public int[] GetAllowedAgents(int waypointIndex)
        {
            return _allPathFindingWaypoints[waypointIndex].AllowedAgents;
        }
    }
}