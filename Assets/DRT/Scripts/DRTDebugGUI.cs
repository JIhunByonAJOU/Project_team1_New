using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DRT
{
    public class DRTDebugGUI : MonoBehaviour
    {
        [SerializeField] private DRTPassengerManager passengerManager;
        [SerializeField] private DRTBusController busController;
        [SerializeField] private bool showPassengerTable = true;
        [SerializeField] private bool showStopOverview = true;
        [SerializeField] private int maxPassengerRows = 40;

        private Vector2 passengerScroll;
        private Vector2 overviewScroll;
        private Rect passengerWindow = new Rect(12, 12, 850, 360);
        private Rect overviewWindow = new Rect(12, 384, 520, 420);
        private GUIStyle headerStyle;
        private GUIStyle smallStyle;
        private GUIStyle cellStyle;

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
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (showPassengerTable)
            {
                passengerWindow = GUI.Window(5201, passengerWindow, DrawPassengerWindow, "DRT Passenger Requests");
            }

            if (showStopOverview)
            {
                overviewWindow = GUI.Window(5202, overviewWindow, DrawOverviewWindow, "DRT Stop / Bus Status");
            }
        }

        private void DrawPassengerWindow(int windowId)
        {
            if (passengerManager == null)
            {
                GUILayout.Label("PassengerManager not found.", headerStyle);
                GUI.DragWindow();
                return;
            }

            float currentTime = busController != null ? busController.EpisodeTimeSeconds : Time.time;
            GUILayout.Label($"time,{currentTime:0.0}, total,{passengerManager.Requests.Count}, waiting,{passengerManager.GetWaitingCount(currentTime)}, onboard,{passengerManager.GetOnBoardCount()}, completed,{passengerManager.GetCompletedCount()}", smallStyle);
            GUILayout.Space(4);

            DrawCsvHeader();
            passengerScroll = GUILayout.BeginScrollView(passengerScroll, GUILayout.Height(285));

            int rows = 0;
            foreach (var request in passengerManager.Requests.OrderBy(request => request.PassengerId))
            {
                if (rows >= maxPassengerRows)
                {
                    GUILayout.Label($"... {passengerManager.Requests.Count - rows} more rows", smallStyle);
                    break;
                }

                DrawCsvRow(request, currentTime);
                rows++;
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void DrawOverviewWindow(int windowId)
        {
            if (passengerManager == null || busController == null)
            {
                GUILayout.Label("DRT runtime objects not found.", headerStyle);
                GUI.DragWindow();
                return;
            }

            float currentTime = busController.EpisodeTimeSeconds;
            GUILayout.Label($"Vehicle {busController.VehicleIndex} | current stop {busController.CurrentStopId} | target stop {busController.TargetStopId}", headerStyle);
            GUILayout.Label($"initialized={busController.IsInitialized}, driving={busController.IsDriving}, finished={busController.IsEpisodeFinished}", smallStyle);
            GUILayout.Label($"speed={busController.VehicleSpeedMS:0.00}m/s, targetDist={FormatDistance(busController.TargetDistanceMeters)}, arrival<= {busController.ArrivalDistanceMeters:0.00}m, waiting1m={busController.IsWaitingForArrivalProximity}", smallStyle);
            GUILayout.Space(6);

            overviewScroll = GUILayout.BeginScrollView(overviewScroll, GUILayout.Height(320));
            GUILayout.Label("Stops", headerStyle);

            foreach (var stop in busController.Stops)
            {
                if (stop == null)
                {
                    continue;
                }

                int waiting = passengerManager.GetWaitingCountAtStop(stop.StopId, currentTime);
                int scheduled = passengerManager.GetScheduledCountAtStop(stop.StopId, currentTime);
                int dropOff = passengerManager.GetOnBoardDestinationCount(stop.StopId);
                GUILayout.Label($"Stop {stop.StopId}: waiting {waiting}, future {scheduled}, onboard-to-here {dropOff}", cellStyle);
            }

            GUILayout.Space(8);
            GUILayout.Label("On Board", headerStyle);
            DrawRequestList(passengerManager.Requests.Where(request => request.Status == DRTPassengerStatus.OnBoard), currentTime);

            GUILayout.Space(8);
            GUILayout.Label("Waiting", headerStyle);
            DrawRequestList(passengerManager.Requests.Where(request => request.Status == DRTPassengerStatus.Waiting), currentTime);

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void DrawCsvHeader()
        {
            GUILayout.BeginHorizontal();
            DrawCell("id", 42, true);
            DrawCell("from", 50, true);
            DrawCell("to", 50, true);
            DrawCell("req", 70, true);
            DrawCell("status", 90, true);
            DrawCell("pickup", 70, true);
            DrawCell("dropoff", 70, true);
            DrawCell("wait", 70, true);
            DrawCell("ride", 70, true);
            DrawCell("elapsed", 80, true);
            GUILayout.EndHorizontal();
        }

        private void DrawCsvRow(DRTPassengerRequest request, float currentTime)
        {
            GUILayout.BeginHorizontal();
            DrawCell(request.PassengerId.ToString(), 42);
            DrawCell(request.OriginStopId.ToString(), 50);
            DrawCell(request.DestinationStopId.ToString(), 50);
            DrawCell(request.RequestTimeSeconds.ToString("0"), 70);
            DrawCell(request.Status.ToString(), 90);
            DrawCell(FormatTime(request.PickupTimeSeconds), 70);
            DrawCell(FormatTime(request.DropoffTimeSeconds), 70);
            DrawCell(request.GetWaitTime(currentTime).ToString("0"), 70);
            DrawCell(request.GetRideTime(currentTime).ToString("0"), 70);
            DrawCell(request.GetElapsedSinceRequest(currentTime).ToString("0"), 80);
            GUILayout.EndHorizontal();
        }

        private void DrawRequestList(IEnumerable<DRTPassengerRequest> requests, float currentTime)
        {
            int count = 0;
            foreach (var request in requests.OrderBy(request => request.RequestTimeSeconds))
            {
                GUILayout.Label($"#{request.PassengerId} Stop {request.OriginStopId} -> {request.DestinationStopId}, wait {request.GetWaitTime(currentTime):0}s, ride {request.GetRideTime(currentTime):0}s", cellStyle);
                count++;
                if (count >= 12)
                {
                    GUILayout.Label("...", smallStyle);
                    break;
                }
            }

            if (count == 0)
            {
                GUILayout.Label("none", smallStyle);
            }
        }

        private void DrawCell(string value, float width, bool header = false)
        {
            GUILayout.Label(value, header ? headerStyle : cellStyle, GUILayout.Width(width));
        }

        private string FormatTime(float value)
        {
            return value >= 0f ? value.ToString("0") : "-";
        }

        private string FormatDistance(float value)
        {
            return float.IsInfinity(value) ? "-" : $"{value:0.00}m";
        }

        private void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.86f, 0.9f, 0.94f) }
            };

            cellStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
        }
    }
}
