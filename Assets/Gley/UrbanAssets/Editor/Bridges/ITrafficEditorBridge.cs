using UnityEngine;

namespace Gley.UrbanSystem.Editor
{
    public interface ITrafficEditorBridge 
    {
        void AppendGroundLayers(ref LayerMask groundLayers);
        IWaypointsConverter GetWaypointsConverter();
        void ApplyWaypoints(IWaypointsConverter waypointsConverter);
        void ConvertIntersections(IWaypointsConverter trafficWaypointsConverter, IWaypointsConverter tpedestrianWaypointsConverter);
        IGenericIntersectionSettings[] GetAllIntersections();
    }
}
