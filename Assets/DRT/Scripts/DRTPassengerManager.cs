using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DRT
{
    public class DRTPassengerManager : MonoBehaviour
    {
        [SerializeField] private int busCapacity = 12;
        [SerializeField] private bool logStopProcessing = true;
        [SerializeField] private List<DRTPassengerRequest> requests = new List<DRTPassengerRequest>();

        private int nextPassengerId = 1;

        public IReadOnlyList<DRTPassengerRequest> Requests => requests;
        public int BusCapacity => busCapacity;

        public void ClearRequests()
        {
            requests.Clear();
            nextPassengerId = 1;
        }

        public DRTPassengerRequest AddRequest(int originStopId, int destinationStopId, float requestTimeSeconds)
        {
            if (originStopId == destinationStopId)
            {
                Debug.LogWarning($"DRT request ignored. Origin and destination are both Stop {originStopId}.");
                return null;
            }

            var request = new DRTPassengerRequest(nextPassengerId++, originStopId, destinationStopId, requestTimeSeconds);
            requests.Add(request);
            return request;
        }

        public void AddRequest(DRTPassengerRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (request.OriginStopId == request.DestinationStopId)
            {
                Debug.LogWarning($"DRT request ignored. Origin and destination are both Stop {request.OriginStopId}.");
                return;
            }

            if (request.PassengerId <= 0)
            {
                request.SetPassengerId(nextPassengerId++);
            }
            else
            {
                nextPassengerId = Mathf.Max(nextPassengerId, request.PassengerId + 1);
            }

            requests.Add(request);
        }

        public void UpdateRequestStates(float currentEpisodeTime)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].ShouldStartWaiting(currentEpisodeTime))
                {
                    requests[i].StartWaiting();
                }
            }
        }

        public DRTStopProcessResult ProcessStopArrival(int stopId, float currentEpisodeTime)
        {
            UpdateRequestStates(currentEpisodeTime);

            int droppedOffCount = DropOffPassengers(stopId, currentEpisodeTime);
            int boardedCount = BoardPassengers(stopId, currentEpisodeTime);

            var result = new DRTStopProcessResult(
                stopId,
                boardedCount,
                droppedOffCount,
                GetWaitingCount(currentEpisodeTime),
                GetOnBoardCount(),
                GetCompletedCount());

            if (logStopProcessing)
            {
                Debug.Log(
                    $"[DRT] Stop {stopId} arrived. " +
                    $"Boarded={result.BoardedCount}, DroppedOff={result.DroppedOffCount}, " +
                    $"Waiting={result.WaitingCount}, OnBoard={result.OnBoardCount}, Completed={result.CompletedCount}");
            }

            return result;
        }

        public int GetWaitingCount(float currentEpisodeTime)
        {
            return requests.Count(request =>
                request.Status == DRTPassengerStatus.Waiting ||
                (request.Status == DRTPassengerStatus.Scheduled && currentEpisodeTime >= request.RequestTimeSeconds));
        }

        public int GetWaitingCountAtStop(int stopId, float currentEpisodeTime)
        {
            return requests.Count(request =>
                request.OriginStopId == stopId &&
                (request.Status == DRTPassengerStatus.Waiting ||
                 (request.Status == DRTPassengerStatus.Scheduled && currentEpisodeTime >= request.RequestTimeSeconds)));
        }

        public int GetScheduledCountAtStop(int stopId, float currentEpisodeTime)
        {
            return requests.Count(request =>
                request.OriginStopId == stopId &&
                request.Status == DRTPassengerStatus.Scheduled &&
                request.RequestTimeSeconds > currentEpisodeTime);
        }

        public int GetOnBoardCount()
        {
            return requests.Count(request => request.Status == DRTPassengerStatus.OnBoard);
        }

        public int GetOnBoardDestinationCount(int stopId)
        {
            return requests.Count(request =>
                request.Status == DRTPassengerStatus.OnBoard &&
                request.DestinationStopId == stopId);
        }

        public int GetCompletedCount()
        {
            return requests.Count(request => request.Status == DRTPassengerStatus.Completed);
        }

        public bool HasUnfinishedRequests(float currentEpisodeTime)
        {
            return requests.Any(request => request.Status != DRTPassengerStatus.Completed);
        }

        public bool TryGetNextScheduledOrigin(float currentEpisodeTime, out int stopId)
        {
            DRTPassengerRequest nextRequest = null;

            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request.Status != DRTPassengerStatus.Scheduled || request.RequestTimeSeconds <= currentEpisodeTime)
                {
                    continue;
                }

                if (nextRequest == null || request.RequestTimeSeconds < nextRequest.RequestTimeSeconds)
                {
                    nextRequest = request;
                }
            }

            if (nextRequest != null)
            {
                stopId = nextRequest.OriginStopId;
                return true;
            }

            stopId = -1;
            return false;
        }

        public float GetAverageConfirmedWaitTime()
        {
            var boardedRequests = requests.Where(request => request.PickupTimeSeconds >= 0f).ToList();
            if (boardedRequests.Count == 0)
            {
                return 0f;
            }

            return boardedRequests.Average(request => request.PickupTimeSeconds - request.RequestTimeSeconds);
        }

        public float GetAverageCompletedRideTime()
        {
            var completedRequests = requests.Where(request => request.Status == DRTPassengerStatus.Completed).ToList();
            if (completedRequests.Count == 0)
            {
                return 0f;
            }

            return completedRequests.Average(request => request.DropoffTimeSeconds - request.PickupTimeSeconds);
        }

        public float GetServiceRate()
        {
            if (requests.Count == 0)
            {
                return 0f;
            }

            return (float)GetCompletedCount() / requests.Count;
        }

        public void LogSummary()
        {
            Debug.Log(
                $"[DRT] Summary. Total={requests.Count}, Completed={GetCompletedCount()}, " +
                $"ServiceRate={GetServiceRate():P1}, AvgWait={GetAverageConfirmedWaitTime():0.0}s, " +
                $"AvgRide={GetAverageCompletedRideTime():0.0}s");
        }

        private int DropOffPassengers(int stopId, float currentEpisodeTime)
        {
            int droppedOffCount = 0;

            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].CanDropOffAtStop(stopId))
                {
                    requests[i].MarkDroppedOff(currentEpisodeTime, stopId);
                    droppedOffCount++;
                }
            }

            return droppedOffCount;
        }

        private int BoardPassengers(int stopId, float currentEpisodeTime)
        {
            int freeSeats = Mathf.Max(0, busCapacity - GetOnBoardCount());
            int boardedCount = 0;

            if (freeSeats == 0)
            {
                return 0;
            }

            var candidates = requests
                .Where(request => request.CanBoardAtStop(stopId, currentEpisodeTime))
                .OrderBy(request => request.RequestTimeSeconds)
                .ToList();

            for (int i = 0; i < candidates.Count && boardedCount < freeSeats; i++)
            {
                candidates[i].MarkBoarded(currentEpisodeTime, stopId);
                boardedCount++;
            }

            return boardedCount;
        }

        private void OnValidate()
        {
            busCapacity = Mathf.Max(1, busCapacity);
        }
    }
}
