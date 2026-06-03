using System.Collections.Generic;
using UnityEngine;

namespace DRT
{
    [AddComponentMenu("DRT/DRT Passenger Dot Visualizer")]
    public class DRTPassengerDotVisualizer : MonoBehaviour
    {
        [HideInInspector, SerializeField] private DRTPassengerManager passengerManager;
        [HideInInspector, SerializeField] private DRTBusController busController;

        [Header("Dots")]
        [SerializeField, InspectorName("Show Stops")] private bool showStopDots = true;
        [SerializeField, InspectorName("Show Bus")] private bool showBusDots = true;
        [SerializeField, InspectorName("Dot Radius")] private float dotRadius = 4.5f;
        [SerializeField, InspectorName("Dot Spacing")] private float dotSpacing = 3.5f;
        [SerializeField, InspectorName("Bus Dot Spacing")] private float busDotSpacing = 4.0f;
        [SerializeField, InspectorName("Dots Per Row")] private int dotsPerRow = 4;
        [SerializeField, InspectorName("Max Dots")] private int maxDotsPerGroup = 80;
        [SerializeField, InspectorName("Stop Height")] private float stopVerticalOffset = 8f;
        [SerializeField, InspectorName("Bus Height")] private float busVerticalOffset = 8f;
        [SerializeField, InspectorName("Stop Forward Offset")] private float stopForwardOffset = 0.5f;
        [SerializeField, InspectorName("Stop Side Offset")] private float stopSideOffset = 2.2f;
        [SerializeField, InspectorName("Bus Forward Offset")] private float busForwardOffset = 0f;
        [SerializeField, InspectorName("Bus Side Offset")] private float busSideOffset = 0f;

        public void Configure(DRTPassengerManager newPassengerManager, DRTBusController newBusController)
        {
            passengerManager = newPassengerManager;
            busController = newBusController;
        }

        private void OnDrawGizmos()
        {
            if (passengerManager == null || busController == null)
            {
                passengerManager = FindObjectOfType<DRTPassengerManager>();
                busController = FindObjectOfType<DRTBusController>();
                if (passengerManager == null || busController == null)
                {
                    return;
                }
            }

            float currentTime = busController.EpisodeTimeSeconds;

            if (showStopDots)
            {
                DrawStopDots(currentTime);
            }

            if (showBusDots)
            {
                DrawBusDots();
            }
        }

        private void DrawStopDots(float currentTime)
        {
            foreach (var stop in busController.Stops)
            {
                if (stop == null)
                {
                    continue;
                }

                int waitingCount = passengerManager.GetWaitingCountAtStop(stop.StopId, currentTime);
                if (waitingCount <= 0)
                {
                    continue;
                }

                Vector3 right = stop.transform.right.sqrMagnitude > 0.001f ? stop.transform.right.normalized : Vector3.right;
                Vector3 forward = stop.transform.forward.sqrMagnitude > 0.001f ? stop.transform.forward.normalized : Vector3.forward;
                Vector3 center = stop.Position + Vector3.up * stopVerticalOffset + right * stopSideOffset + forward * stopForwardOffset;

                DrawDotGrid(center, right, forward, waitingCount, dotSpacing);
            }
        }

        private void DrawBusDots()
        {
            Transform vehicleTransform = busController.ControlledVehicleTransform;
            if (vehicleTransform == null)
            {
                return;
            }

            int onBoardCount = passengerManager.GetOnBoardCount();
            if (onBoardCount <= 0)
            {
                return;
            }

            Vector3 bodyPosition = busController.ControlledVehicleBodyPosition;
            Vector3 forward = vehicleTransform.forward.sqrMagnitude > 0.001f ? vehicleTransform.forward.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = bodyPosition + Vector3.up * busVerticalOffset;

            DrawDotGrid(center, side, forward, onBoardCount, busDotSpacing);
        }

        private void DrawDotGrid(Vector3 center, Vector3 right, Vector3 forward, int dotCount, float spacing)
        {
            Gizmos.color = Color.red;
            int visibleCount = Mathf.Clamp(dotCount, 0, maxDotsPerGroup);
            int columns = Mathf.Max(1, dotsPerRow);

            for (int i = 0; i < visibleCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int rowColumns = Mathf.Min(columns, visibleCount - row * columns);
                float xOffset = (column - (rowColumns - 1) * 0.5f) * spacing;
                float zOffset = row * spacing;
                Vector3 dotPos = center + right.normalized * xOffset + forward.normalized * zOffset;
                Gizmos.DrawSphere(dotPos, dotRadius);
            }
        }

        private void OnValidate()
        {
            dotRadius = Mathf.Max(0.05f, dotRadius);
            dotSpacing = Mathf.Max(0.05f, dotSpacing);
            busDotSpacing = Mathf.Max(0.05f, busDotSpacing);
            dotsPerRow = Mathf.Max(1, dotsPerRow);
            maxDotsPerGroup = Mathf.Max(1, maxDotsPerGroup);
            stopVerticalOffset = Mathf.Max(0f, stopVerticalOffset);
            busVerticalOffset = Mathf.Max(0f, busVerticalOffset);
            stopForwardOffset = Mathf.Max(0f, stopForwardOffset);
            stopSideOffset = Mathf.Max(0f, stopSideOffset);
            busForwardOffset = Mathf.Max(0f, busForwardOffset);
            busSideOffset = Mathf.Max(0f, busSideOffset);
        }
    }
}
