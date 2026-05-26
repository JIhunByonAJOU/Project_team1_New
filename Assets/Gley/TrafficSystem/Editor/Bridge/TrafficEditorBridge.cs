using Gley.UrbanSystem;
using Gley.UrbanSystem.Editor;
using UnityEditor;
using UnityEngine;

namespace Gley.TrafficSystem.Editor
{
    public class TrafficEditorBridge : ITrafficEditorBridge
    {
        public void AppendGroundLayers(ref LayerMask groundLayers)
        {
            var layers = FileCreator.LoadOrCreateLayers<LayerSetup>(
                TrafficSystemConstants.layerPath);
            if (layers != null)
            {
                groundLayers |= layers.roadLayers;
            }
        }

        public IWaypointsConverter GetWaypointsConverter()
        {
            return new TrafficWaypointsConverter();
        }

        public void ApplyWaypoints(IWaypointsConverter converter)
        {
            converter.ConvertWaypoints();
        }

        public void ConvertIntersections(IWaypointsConverter trafficWaypointsConverter, IWaypointsConverter pedestrianWaypointConverter)
        {
            var converter = new IntersectionConverter(trafficWaypointsConverter, pedestrianWaypointConverter);
            converter.ConvertAllIntersections();
        }

        public IGenericIntersectionSettings[] GetAllIntersections()
        {
            return new IntersectionEditorData().GetAllIntersections();
        }
    }
    [InitializeOnLoad]
    public static class TrafficEditorBridgeInitializer
    {
        static TrafficEditorBridgeInitializer()
        {
            TrafficEditorBridgeRegistry.Register(new TrafficEditorBridge());
        }
    }
}
