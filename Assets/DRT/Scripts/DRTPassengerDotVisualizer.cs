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
        [SerializeField, InspectorName("Dot Radius")] private float dotRadius = 0.18f;
        [SerializeField, InspectorName("Dot Spacing")] private float dotSpacing = 0.4f;
        [SerializeField, InspectorName("Bus Dot Spacing")] private float busDotSpacing = 0.5f;
        [SerializeField, InspectorName("Dots Per Row")] private int dotsPerRow = 8;
        [SerializeField, InspectorName("Max Dots")] private int maxDotsPerGroup = 80;
        [SerializeField, InspectorName("Stop Height")] private float stopVerticalOffset = 1.9f;
        [SerializeField, InspectorName("Bus Height")] private float busVerticalOffset = 2.8f;
        [SerializeField, InspectorName("Stop Forward Offset")] private float stopForwardOffset = 0.5f;
        [SerializeField, InspectorName("Stop Side Offset")] private float stopSideOffset = 2.2f;
        [SerializeField, InspectorName("Bus Forward Offset")] private float busForwardOffset = 0f;
        [SerializeField, InspectorName("Bus Side Offset")] private float busSideOffset = 0f;
        [SerializeField, InspectorName("Refresh (s)")] private float refreshIntervalSeconds = 0.05f;

        private readonly Dictionary<int, List<GameObject>> stopDotPools = new Dictionary<int, List<GameObject>>();
        private readonly List<GameObject> busDotPool = new List<GameObject>();
        private Material dotMaterial;
        private float nextRefreshTime;

        public void Configure(DRTPassengerManager newPassengerManager, DRTBusController newBusController)
        {
            passengerManager = newPassengerManager;
            busController = newBusController;
        }

        private void Awake()
        {
            if (passengerManager == null)
            {
                passengerManager = FindObjectOfType<DRTPassengerManager>();
            }

            if (busController == null)
            {
                busController = FindObjectOfType<DRTBusController>();
            }

            EnsureMaterial();
        }

        private void Update()
        {
            if (Time.time < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.time + refreshIntervalSeconds;
            RefreshDots();
        }

        private void RefreshDots()
        {
            if (passengerManager == null || busController == null)
            {
                return;
            }

            float currentTime = busController.EpisodeTimeSeconds;

            if (showStopDots)
            {
                RefreshStopDots(currentTime);
            }
            else
            {
                HideAllStopDots();
            }

            if (showBusDots)
            {
                RefreshBusDots();
            }
            else
            {
                SetActiveCount(busDotPool, 0);
            }
        }

        private void RefreshStopDots(float currentTime)
        {
            foreach (var stop in busController.Stops)
            {
                if (stop == null)
                {
                    continue;
                }

                if (!stopDotPools.TryGetValue(stop.StopId, out List<GameObject> dots))
                {
                    dots = new List<GameObject>();
                    stopDotPools.Add(stop.StopId, dots);
                }

                int waitingCount = passengerManager.GetWaitingCountAtStop(stop.StopId, currentTime);
                Vector3 right = stop.transform.right.sqrMagnitude > 0.001f ? stop.transform.right.normalized : Vector3.right;
                Vector3 forward = stop.transform.forward.sqrMagnitude > 0.001f ? stop.transform.forward.normalized : Vector3.forward;
                Vector3 center = stop.Position + Vector3.up * stopVerticalOffset + right * stopSideOffset + forward * stopForwardOffset;
                SyncDotPool(dots, waitingCount, center, right, forward, $"PassengerDot_Stop_{stop.StopId}");
            }
        }

        private void RefreshBusDots()
        {
            Transform vehicleTransform = busController.ControlledVehicleTransform;
            if (vehicleTransform == null)
            {
                SetActiveCount(busDotPool, 0);
                return;
            }

            Vector3 bodyPosition = busController.ControlledVehicleBodyPosition;
            Vector3 forward = vehicleTransform.forward.sqrMagnitude > 0.001f ? vehicleTransform.forward.normalized : transform.forward.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = bodyPosition + Vector3.up * busVerticalOffset;

            SyncDotPool(busDotPool, passengerManager.GetOnBoardCount(), center, side, forward, "PassengerDot_Bus", busDotSpacing);
        }

        private void SyncDotPool(List<GameObject> dots, int desiredCount, Vector3 center, Vector3 right, Vector3 forward, string namePrefix, float spacing = -1f)
        {
            int visibleCount = Mathf.Clamp(desiredCount, 0, maxDotsPerGroup);
            EnsurePoolSize(dots, visibleCount, namePrefix);
            SetActiveCount(dots, visibleCount);

            float effectiveSpacing = spacing > 0f ? spacing : dotSpacing;
            int columns = Mathf.Max(1, dotsPerRow);
            for (int i = 0; i < visibleCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int rowColumns = Mathf.Min(columns, visibleCount - row * columns);
                float xOffset = (column - (rowColumns - 1) * 0.5f) * effectiveSpacing;
                float zOffset = row * effectiveSpacing;
                dots[i].transform.position = center + right.normalized * xOffset + forward.normalized * zOffset;
                dots[i].name = $"{namePrefix}_{i + 1}";
            }
        }

        private void EnsurePoolSize(List<GameObject> dots, int visibleCount, string namePrefix)
        {
            while (dots.Count < visibleCount)
            {
                int dotIndex = dots.Count + 1;
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"{namePrefix}_{dotIndex}";
                dot.transform.SetParent(transform, false);
                dot.transform.localScale = Vector3.one * dotRadius * 2f;

                var collider = dot.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                var renderer = dot.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = dotMaterial;
                }

                dots.Add(dot);
            }
        }

        private static void SetActiveCount(List<GameObject> dots, int visibleCount)
        {
            for (int i = 0; i < dots.Count; i++)
            {
                bool shouldBeActive = i < visibleCount;
                if (dots[i] != null && dots[i].activeSelf != shouldBeActive)
                {
                    dots[i].SetActive(shouldBeActive);
                }
            }
        }

        private void HideAllStopDots()
        {
            foreach (var pair in stopDotPools)
            {
                SetActiveCount(pair.Value, 0);
            }
        }

        private void EnsureMaterial()
        {
            if (dotMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            dotMaterial = new Material(shader)
            {
                name = "DRT Passenger Dot Red"
            };
            dotMaterial.color = Color.red;

            if (dotMaterial.HasProperty("_BaseColor"))
            {
                dotMaterial.SetColor("_BaseColor", Color.red);
            }

            if (dotMaterial.HasProperty("_Color"))
            {
                dotMaterial.SetColor("_Color", Color.red);
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
            refreshIntervalSeconds = Mathf.Max(0.01f, refreshIntervalSeconds);
        }
    }
}
