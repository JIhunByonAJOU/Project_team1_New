using UnityEngine;

namespace Gley.UrbanSystem.Editor
{
    public static class Customizations 
    {
        private const float _referenceDistance = 35;
        private const float _anchorSize = 0.5f;
        private const float _controlSize = 1;
        private const float _roadConnectorSize = 1;


        public static float GetZoomPercentage(Vector3 cameraPoz, Vector3 objPoz)
        {
            float cameraDistace = Vector3.Distance(cameraPoz, objPoz);
            return  cameraDistace / _referenceDistance;
        }


        public static float GetRoadConnectorSize(Vector3 camPoz, Vector3 objPoz)
        {
            return GetZoomPercentage(camPoz,objPoz) * _roadConnectorSize;
        }


        public static float GetControlPointSize(Vector3 camPoz, Vector3 objPoz)
        {
            return GetZoomPercentage(camPoz,objPoz) * _controlSize;
        }


        public static float GetAnchorPointSize(Vector3 camPoz, Vector3 objPoz)
        {
            return GetZoomPercentage(camPoz, objPoz) * _anchorSize;
        }
    }
}
