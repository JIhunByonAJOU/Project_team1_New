#if UNITY_EDITOR
using Gley.UrbanSystem;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.TrafficSystem
{
    public abstract class GenericIntersectionSettings : MonoBehaviour, IGenericIntersectionSettings
    {
        public Vector3 position;
        public bool inView;
        public bool justCreated;

        public virtual GenericIntersectionSettings Initialize()
        {
            justCreated = true;
            return this;
        }

        public abstract List<IntersectionStopWaypointsSettings> GetAssignedWaypoints();
        public abstract List<WaypointSettings> GetStopWaypoints(int road);
        public abstract List<WaypointSettings> GetExitWaypoints();

        public virtual bool VerifyAssignments()
        {
            justCreated = false;
            if (position != transform.position)
            {
                position = transform.position;
            }
            return false;
        }

        public abstract List<WaypointSettingsBase> GetPedestrianWaypoints();
        public abstract List<WaypointSettingsBase> GetPedestrianWaypoints(int road);
        public abstract List<WaypointSettingsBase> GetDirectionWaypoints();
    }
}
#endif