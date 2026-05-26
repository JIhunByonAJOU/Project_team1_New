using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.UrbanSystem
{
    public interface IGenericIntersectionSettings
    {
        bool VerifyAssignments();
#if GLEY_PEDESTRIAN_SYSTEM
        List<WaypointSettingsBase> GetPedestrianWaypoints();
        List<WaypointSettingsBase> GetDirectionWaypoints();
#endif
    }
}
