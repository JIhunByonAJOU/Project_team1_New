using System.Collections.Generic;
using UnityEngine;

namespace DRT
{
    public class DRTNextStopSelector : MonoBehaviour
    {
        [SerializeField] private float waitingPassengerWeight = 3f;
        [SerializeField] private float onBoardDestinationWeight = 5f;
        [SerializeField] private float scheduledPassengerWeight = 0.5f;
        [SerializeField] private float distancePenaltyWeight = 0.002f;
        [SerializeField] private bool skipCurrentStop = true;

        public int SelectNextStopId(
            int currentStopId,
            IReadOnlyList<DRTStop> stops,
            DRTPassengerManager passengerManager,
            float currentEpisodeTime)
        {
            if (stops == null || stops.Count == 0 || passengerManager == null)
            {
                return -1;
            }

            DRTStop currentStop = FindStop(stops, currentStopId);
            int bestStopId = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop == null)
                {
                    continue;
                }

                if (skipCurrentStop && stops.Count > 1 && stop.StopId == currentStopId)
                {
                    continue;
                }

                int waitingCount = passengerManager.GetWaitingCountAtStop(stop.StopId, currentEpisodeTime);
                int dropOffCount = passengerManager.GetOnBoardDestinationCount(stop.StopId);
                int scheduledCount = passengerManager.GetScheduledCountAtStop(stop.StopId, currentEpisodeTime);

                float distancePenalty = 0f;
                if (currentStop != null)
                {
                    distancePenalty = Vector3.Distance(currentStop.Position, stop.Position) * distancePenaltyWeight;
                }

                float score =
                    waitingCount * waitingPassengerWeight +
                    dropOffCount * onBoardDestinationWeight +
                    scheduledCount * scheduledPassengerWeight -
                    distancePenalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestStopId = stop.StopId;
                }
            }

            if (bestStopId >= 1 && bestScore > 0f)
            {
                return bestStopId;
            }

            if (passengerManager.TryGetNextScheduledOrigin(currentEpisodeTime, out int scheduledOriginStopId))
            {
                return scheduledOriginStopId;
            }

            return GetNextSequentialStopId(currentStopId, stops);
        }

        private static DRTStop FindStop(IReadOnlyList<DRTStop> stops, int stopId)
        {
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] != null && stops[i].StopId == stopId)
                {
                    return stops[i];
                }
            }

            return null;
        }

        private static int GetNextSequentialStopId(int currentStopId, IReadOnlyList<DRTStop> stops)
        {
            if (stops == null || stops.Count == 0)
            {
                return -1;
            }

            int currentIndex = -1;
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] != null && stops[i].StopId == currentStopId)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % stops.Count : 0;
            return stops[nextIndex] != null ? stops[nextIndex].StopId : -1;
        }

        private void OnValidate()
        {
            waitingPassengerWeight = Mathf.Max(0f, waitingPassengerWeight);
            onBoardDestinationWeight = Mathf.Max(0f, onBoardDestinationWeight);
            scheduledPassengerWeight = Mathf.Max(0f, scheduledPassengerWeight);
            distancePenaltyWeight = Mathf.Max(0f, distancePenaltyWeight);
        }
    }
}
