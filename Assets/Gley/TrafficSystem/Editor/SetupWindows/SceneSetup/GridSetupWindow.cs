using Gley.UrbanSystem.Editor;

namespace Gley.TrafficSystem.Editor
{
    public class GridSetupWindow : GridSetupWindowBase
    {
        public override SetupWindowBase Initialize(WindowProperties windowProperties, SettingsWindowBase window)
        {
            base.Initialize(windowProperties, window);
            var trafficLayers = FileCreator.LoadOrCreateLayers<LayerSetup>(TrafficSystemConstants.layerPath);
            if (trafficLayers != null)
            {
               AddTrafficGround(trafficLayers.roadLayers);
            }
            return this;
        }

        public override void DrawInScene()
        {
            if (_viewGrid)
            {
                _gridDrawer.DrawGrid(true);
            }
            base.DrawInScene();
        }
    }
}
