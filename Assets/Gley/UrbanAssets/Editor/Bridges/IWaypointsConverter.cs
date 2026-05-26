using System.Collections.Generic;

namespace Gley.UrbanSystem.Editor
{
    public interface IWaypointsConverter
    {
        void ConvertWaypoints();
        int[] GetListIndex(List<WaypointSettingsBase> selectedWaypoints);
    }
}
