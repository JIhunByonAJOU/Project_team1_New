using System.Collections.Generic;
using UnityEngine;

namespace Gley.UrbanSystem.Editor
{
    public interface IPedestrianEditorBridge 
    {
        delegate void WaypointClicked(WaypointSettingsBase clickedWaypoint, bool leftClick);
        event WaypointClicked OnWaypointClicked;
        void TriggerWaypointClickedEvent(WaypointSettingsBase clickedWaypoint, bool leftClick);

        IWaypointsConverter GetWaypointsConverter();
        void AppendGroundLayers(ref LayerMask groundLayers);
        void ApplyWaypoints(IWaypointsConverter converter);

        List<WaypointSettingsBase> GetAllPedestrianWaypoints();

        public int[] GetWaypointIndices(List<WaypointSettingsBase> selectedWaypoints, IWaypointsConverter converter);

        void ShowIntersectionWaypoints(Color waypointColor);
        void DrawPossibleDirectionWaypoints(List<WaypointSettingsBase> waypoints, Color waypointColor);
    }
}
