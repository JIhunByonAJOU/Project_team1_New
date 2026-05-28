using System;
using System.Collections.Generic;
using UnityEngine;

namespace DRT
{
    [Serializable]
    public class DRTDemandScheduleEntry
    {
        [Min(0f)] public float requestTimeSeconds;
        [Min(1)] public int originStopId = 1;
        [Min(1)] public int destinationStopId = 2;
        [Min(1)] public int passengerCount = 1;
    }

    public class DRTDemandGenerator : MonoBehaviour
    {
        [SerializeField] private DRTPassengerManager passengerManager;
        [SerializeField] private bool generateOnStart;
        [SerializeField] private bool clearExistingRequests = true;
        [SerializeField] private int stopCount = 8;
        [SerializeField] private float episodeLengthSeconds = 3600f;
        [SerializeField, Range(6, 22)] private int defaultRequestCount = 14;
        [SerializeField] private List<DRTDemandScheduleEntry> demandSchedule = new List<DRTDemandScheduleEntry>();

        public bool HasGenerated { get; private set; }

        public void Configure(DRTPassengerManager newPassengerManager, int newStopCount)
        {
            passengerManager = newPassengerManager;
            stopCount = Mathf.Max(2, newStopCount);
        }

        private void Start()
        {
            if (generateOnStart && !HasGenerated)
            {
                GenerateDemand();
            }
        }

        [ContextMenu("Generate DRT Demand")]
        public void GenerateDemand()
        {
            if (passengerManager == null)
            {
                passengerManager = FindObjectOfType<DRTPassengerManager>();
            }

            if (passengerManager == null)
            {
                Debug.LogError("[DRT] Cannot generate demand. PassengerManager is missing.");
                return;
            }

            if (clearExistingRequests)
            {
                passengerManager.ClearRequests();
            }

            var sourceSchedule = demandSchedule.Count > 0 ? demandSchedule : BuildDefaultRuleBasedSchedule();

            for (int i = 0; i < sourceSchedule.Count; i++)
            {
                var entry = sourceSchedule[i];
                if (!IsValidEntry(entry))
                {
                    continue;
                }

                for (int passenger = 0; passenger < entry.passengerCount; passenger++)
                {
                    passengerManager.AddRequest(
                        entry.originStopId,
                        entry.destinationStopId,
                        entry.requestTimeSeconds);
                }
            }

            HasGenerated = true;
            Debug.Log(
                $"[DEMANDGENERATOR] Generated requests={passengerManager.Requests.Count}, " +
                $"stopCount={stopCount}, scheduleEntries={sourceSchedule.Count}, " +
                $"firstRequest={FormatFirstRequest()}");
        }

        public void ResetDemand()
        {
            HasGenerated = false;
            GenerateDemand();
        }

        private List<DRTDemandScheduleEntry> BuildDefaultRuleBasedSchedule()
        {
            var result = new List<DRTDemandScheduleEntry>();
            int safeStopCount = Mathf.Max(2, stopCount);
            int safeRequestCount = Mathf.Clamp(defaultRequestCount, 6, 22);
            float interval = episodeLengthSeconds / safeRequestCount;

            for (int i = 0; i < safeRequestCount; i++)
            {
                int origin = i % safeStopCount + 1;
                int destination = (i * 3 + safeStopCount / 2) % safeStopCount + 1;
                if (origin == destination)
                {
                    destination = destination % safeStopCount + 1;
                }

                result.Add(new DRTDemandScheduleEntry
                {
                    requestTimeSeconds = Mathf.Round(i * interval),
                    originStopId = origin,
                    destinationStopId = destination,
                    passengerCount = 1
                });
            }

            return result;
        }

        private bool IsValidEntry(DRTDemandScheduleEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (entry.originStopId == entry.destinationStopId)
            {
                Debug.LogWarning($"[DEMANDGENERATOR] Demand ignored. Origin and destination are both Stop {entry.originStopId}.");
                return false;
            }

            if (entry.originStopId < 1 || entry.destinationStopId < 1)
            {
                Debug.LogWarning("[DEMANDGENERATOR] Demand ignored. Stop IDs must start at 1.");
                return false;
            }

            if (entry.passengerCount < 1)
            {
                return false;
            }

            return true;
        }

        private string FormatFirstRequest()
        {
            if (passengerManager == null || passengerManager.Requests.Count == 0)
            {
                return "-";
            }

            var request = passengerManager.Requests[0];
            return $"#{request.PassengerId}:{request.OriginStopId}->{request.DestinationStopId}@{request.RequestTimeSeconds:0}s";
        }

        private void OnValidate()
        {
            stopCount = Mathf.Max(2, stopCount);
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
        }
    }
}
