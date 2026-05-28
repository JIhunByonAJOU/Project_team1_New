using System.Collections.Generic;
using System.Text;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace DRT
{
    [RequireComponent(typeof(BehaviorParameters))]
    public class DRTNextStopSelector : Agent
    {
        private const string BehaviorName = "DRTNextStopPPO";
        private const int GlobalObservationCount = 6;
        private const int ObservationsPerStop = 8;

        [Header("PPO Decision Space")]
        [SerializeField, Min(2)] private int maxStops = 16;
        [SerializeField] private bool skipCurrentStop = true;
        [SerializeField] private float episodeLengthSeconds = 3600f;
        [SerializeField] private float maxDistanceForObservation = 500f;
        [SerializeField] private float maxWaitSecondsForObservation = 1800f;
        [SerializeField] private float maxDecisionWaitSeconds = 1f;

        [Header("Reward")]
        [SerializeField] private float boardedPassengerReward = 0.6f;
        [SerializeField] private float droppedOffPassengerReward = 1.2f;
        [SerializeField] private float invalidActionPenalty = -0.5f;
        [SerializeField] private float noServiceStopPenalty = -0.05f;
        [SerializeField] private float travelDistancePenaltyPerMeter = 0.001f;
        [SerializeField] private float travelTimePenaltyPerSecond = 0.002f;
        [SerializeField] private float waitingPassengerPenaltyPerSecond = 0.001f;
        [SerializeField] private float onBoardPassengerPenaltyPerSecond = 0.0015f;
        [SerializeField] private float completedEpisodeReward = 2f;

        [Header("Heuristic Fallback")]
        [SerializeField] private float waitingPassengerWeight = 3f;
        [SerializeField] private float onBoardDestinationWeight = 5f;
        [SerializeField] private float scheduledPassengerWeight = 0.5f;
        [SerializeField] private float distancePenaltyWeight = 0.002f;
        [SerializeField] private bool logDecision = true;

        private IReadOnlyList<DRTStop> decisionStops;
        private DRTPassengerManager decisionPassengerManager;
        private int decisionCurrentStopId;
        private float decisionEpisodeTime;
        private bool decisionPending;
        private bool decisionReady;
        private int selectedStopId = -1;
        private int lastSelectedStopId = -1;

        public float MaxDecisionWaitSeconds => maxDecisionWaitSeconds;
        public int LastSelectedStopId => lastSelectedStopId;

        private int ObservationSize => GlobalObservationCount + maxStops * ObservationsPerStop;

        private void Awake()
        {
            ConfigureBehaviorParameters();
        }

        public override void Initialize()
        {
            ConfigureBehaviorParameters();
        }

        public bool BeginDecision(
            int currentStopId,
            IReadOnlyList<DRTStop> stops,
            DRTPassengerManager passengerManager,
            float currentEpisodeTime)
        {
            if (stops == null || stops.Count == 0 || passengerManager == null)
            {
                return false;
            }

            decisionStops = stops;
            decisionPassengerManager = passengerManager;
            decisionCurrentStopId = currentStopId;
            decisionEpisodeTime = currentEpisodeTime;
            selectedStopId = -1;
            decisionReady = false;
            decisionPending = true;

            RequestDecision();
            return true;
        }

        public bool TryConsumeDecision(out int stopId)
        {
            if (!decisionReady)
            {
                stopId = -1;
                return false;
            }

            stopId = selectedStopId;
            decisionReady = false;
            return stopId >= 1;
        }

        public void CancelDecision()
        {
            decisionPending = false;
            decisionReady = false;
            selectedStopId = -1;
        }

        public void RecordStopArrival(DRTStopProcessResult result, float travelSeconds, float plannedDistanceMeters)
        {
            float reward =
                result.BoardedCount * boardedPassengerReward +
                result.DroppedOffCount * droppedOffPassengerReward -
                Mathf.Max(0f, plannedDistanceMeters) * travelDistancePenaltyPerMeter -
                Mathf.Max(0f, travelSeconds) * travelTimePenaltyPerSecond -
                result.WaitingCount * Mathf.Max(0f, travelSeconds) * waitingPassengerPenaltyPerSecond -
                result.OnBoardCount * Mathf.Max(0f, travelSeconds) * onBoardPassengerPenaltyPerSecond;

            if (result.BoardedCount == 0 && result.DroppedOffCount == 0)
            {
                reward += noServiceStopPenalty;
            }

            AddReward(reward);
        }

        public void NotifyEpisodeFinished(bool completedAllRequests)
        {
            AddReward(completedAllRequests ? completedEpisodeReward : -completedEpisodeReward);
            EndEpisode();
            CancelDecision();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            int stopCount = decisionStops != null ? Mathf.Min(decisionStops.Count, maxStops) : 0;
            int onBoardCount = decisionPassengerManager != null ? decisionPassengerManager.GetOnBoardCount() : 0;
            int waitingCount = decisionPassengerManager != null ? decisionPassengerManager.GetWaitingCount(decisionEpisodeTime) : 0;
            int capacity = decisionPassengerManager != null ? Mathf.Max(1, decisionPassengerManager.BusCapacity) : 1;

            sensor.AddObservation(NormalizeStopIndex(decisionCurrentStopId));
            sensor.AddObservation(Mathf.Clamp01(decisionEpisodeTime / Mathf.Max(1f, episodeLengthSeconds)));
            sensor.AddObservation(Mathf.Clamp01((float)waitingCount / capacity));
            sensor.AddObservation(Mathf.Clamp01((float)onBoardCount / capacity));
            sensor.AddObservation(Mathf.Clamp01((float)(capacity - onBoardCount) / capacity));
            sensor.AddObservation(decisionPassengerManager != null ? decisionPassengerManager.GetServiceRate() : 0f);

            DRTStop currentStop = FindStop(decisionStops, decisionCurrentStopId);

            for (int i = 0; i < maxStops; i++)
            {
                DRTStop stop = i < stopCount ? decisionStops[i] : null;
                bool valid = stop != null;
                bool isCurrent = valid && stop.StopId == decisionCurrentStopId;

                int waitingAtStop = valid && decisionPassengerManager != null
                    ? decisionPassengerManager.GetWaitingCountAtStop(stop.StopId, decisionEpisodeTime)
                    : 0;
                int dropOffAtStop = valid && decisionPassengerManager != null
                    ? decisionPassengerManager.GetOnBoardDestinationCount(stop.StopId)
                    : 0;
                int scheduledAtStop = valid && decisionPassengerManager != null
                    ? decisionPassengerManager.GetScheduledCountAtStop(stop.StopId, decisionEpisodeTime)
                    : 0;

                float distance = valid && currentStop != null
                    ? Vector3.Distance(currentStop.Position, stop.Position)
                    : maxDistanceForObservation;

                GetStopPassengerTimeFeatures(
                    valid ? stop.StopId : -1,
                    decisionEpisodeTime,
                    out float maxWaitSeconds,
                    out float maxRideSeconds);

                sensor.AddObservation(valid ? 1f : 0f);
                sensor.AddObservation(isCurrent ? 1f : 0f);
                sensor.AddObservation(Mathf.Clamp01(distance / Mathf.Max(1f, maxDistanceForObservation)));
                sensor.AddObservation(Mathf.Clamp01((float)waitingAtStop / capacity));
                sensor.AddObservation(Mathf.Clamp01((float)dropOffAtStop / capacity));
                sensor.AddObservation(Mathf.Clamp01((float)scheduledAtStop / capacity));
                sensor.AddObservation(Mathf.Clamp01(maxWaitSeconds / Mathf.Max(1f, maxWaitSecondsForObservation)));
                sensor.AddObservation(Mathf.Clamp01(maxRideSeconds / Mathf.Max(1f, maxWaitSecondsForObservation)));
            }
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            int stopCount = decisionStops != null ? Mathf.Min(decisionStops.Count, maxStops) : 0;
            for (int i = 0; i < maxStops; i++)
            {
                bool enabled = i < stopCount && decisionStops[i] != null;
                if (enabled && skipCurrentStop && stopCount > 1)
                {
                    enabled = decisionStops[i].StopId != decisionCurrentStopId;
                }

                actionMask.SetActionEnabled(0, i, enabled);
            }
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!decisionPending)
            {
                return;
            }

            int actionIndex = actionBuffers.DiscreteActions.Length > 0 ? actionBuffers.DiscreteActions[0] : -1;
            selectedStopId = GetStopIdFromAction(actionIndex);

            if (selectedStopId < 1)
            {
                AddReward(invalidActionPenalty);
                selectedStopId = SelectNextStopId(
                    decisionCurrentStopId,
                    decisionStops,
                    decisionPassengerManager,
                    decisionEpisodeTime);
            }

            lastSelectedStopId = selectedStopId;
            decisionPending = false;
            decisionReady = true;
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discreteActionsOut = actionsOut.DiscreteActions;
            int heuristicStopId = SelectNextStopId(
                decisionCurrentStopId,
                decisionStops,
                decisionPassengerManager,
                decisionEpisodeTime);

            discreteActionsOut[0] = FindActionIndexForStop(heuristicStopId);
        }

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
            var scoreSummary = new StringBuilder();

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

                if (scoreSummary.Length > 0)
                {
                    scoreSummary.Append("; ");
                }

                scoreSummary.Append("S")
                    .Append(stop.StopId)
                    .Append("(w=").Append(waitingCount)
                    .Append(",drop=").Append(dropOffCount)
                    .Append(",future=").Append(scheduledCount)
                    .Append(",score=").Append(score.ToString("0.00"))
                    .Append(")");

                if (score > bestScore)
                {
                    bestScore = score;
                    bestStopId = stop.StopId;
                }
            }

            if (bestStopId >= 1 && bestScore > 0f)
            {
                LogSelectedStop("weighted-score", bestStopId, currentStopId, currentEpisodeTime, passengerManager, bestScore, scoreSummary.ToString());
                return bestStopId;
            }

            if (passengerManager.TryGetNextScheduledOrigin(currentEpisodeTime, out int scheduledOriginStopId))
            {
                LogSelectedStop("next-scheduled-origin", scheduledOriginStopId, currentStopId, currentEpisodeTime, passengerManager, bestScore, scoreSummary.ToString());
                return scheduledOriginStopId;
            }

            int sequentialStopId = GetNextSequentialStopId(currentStopId, stops);
            LogSelectedStop("sequential-fallback", sequentialStopId, currentStopId, currentEpisodeTime, passengerManager, bestScore, scoreSummary.ToString());
            return sequentialStopId;
        }

        private void LogSelectedStop(
            string reason,
            int selectedStopId,
            int currentStopId,
            float currentEpisodeTime,
            DRTPassengerManager passengerManager,
            float bestScore,
            string scoreSummary)
        {
            if (!logDecision)
            {
                return;
            }

            int waiting = passengerManager.GetWaitingCountAtStop(selectedStopId, currentEpisodeTime);
            int dropOff = passengerManager.GetOnBoardDestinationCount(selectedStopId);
            int scheduled = passengerManager.GetScheduledCountAtStop(selectedStopId, currentEpisodeTime);

            Debug.Log(
                $"[NEXTSTOPSELECTOR] t={currentEpisodeTime:0.0}s current={currentStopId} selected={selectedStopId} " +
                $"reason={reason} selectedDemand(wait={waiting},drop={dropOff},future={scheduled}) " +
                $"bestScore={bestScore:0.00} scores=[{scoreSummary}]");
        }

        private int GetStopIdFromAction(int actionIndex)
        {
            if (decisionStops == null || actionIndex < 0 || actionIndex >= decisionStops.Count || actionIndex >= maxStops)
            {
                return -1;
            }

            DRTStop stop = decisionStops[actionIndex];
            if (stop == null)
            {
                return -1;
            }

            if (skipCurrentStop && decisionStops.Count > 1 && stop.StopId == decisionCurrentStopId)
            {
                return -1;
            }

            return stop.StopId;
        }

        private int FindActionIndexForStop(int stopId)
        {
            int stopCount = decisionStops != null ? Mathf.Min(decisionStops.Count, maxStops) : 0;
            int index = FindStopIndex(decisionStops, stopId);
            if (index >= 0 && index < stopCount)
            {
                return index;
            }

            for (int i = 0; i < stopCount; i++)
            {
                if (decisionStops[i] == null)
                {
                    continue;
                }

                if (!skipCurrentStop || stopCount <= 1 || decisionStops[i].StopId != decisionCurrentStopId)
                {
                    return i;
                }
            }

            return 0;
        }

        private void GetStopPassengerTimeFeatures(int stopId, float currentEpisodeTime, out float maxWaitSeconds, out float maxRideSeconds)
        {
            maxWaitSeconds = 0f;
            maxRideSeconds = 0f;

            if (decisionPassengerManager == null || stopId < 1)
            {
                return;
            }

            var requests = decisionPassengerManager.Requests;
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request == null)
                {
                    continue;
                }

                if (request.OriginStopId == stopId && request.Status == DRTPassengerStatus.Waiting)
                {
                    maxWaitSeconds = Mathf.Max(maxWaitSeconds, request.GetWaitTime(currentEpisodeTime));
                }

                if (request.DestinationStopId == stopId && request.Status == DRTPassengerStatus.OnBoard)
                {
                    maxRideSeconds = Mathf.Max(maxRideSeconds, request.GetRideTime(currentEpisodeTime));
                }
            }
        }

        private float NormalizeStopIndex(int stopId)
        {
            if (stopId <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)(stopId - 1) / Mathf.Max(1, maxStops - 1));
        }

        private void ConfigureBehaviorParameters()
        {
            var behaviorParameters = GetComponent<BehaviorParameters>();
            if (behaviorParameters == null)
            {
                return;
            }

            behaviorParameters.BehaviorName = BehaviorName;
            behaviorParameters.BehaviorType = BehaviorType.Default;
            behaviorParameters.BrainParameters.VectorObservationSize = ObservationSize;
            behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
            behaviorParameters.BrainParameters.ActionSpec = new ActionSpec(0, new[] { maxStops });
        }

        private static DRTStop FindStop(IReadOnlyList<DRTStop> stops, int stopId)
        {
            if (stops == null)
            {
                return null;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] != null && stops[i].StopId == stopId)
                {
                    return stops[i];
                }
            }

            return null;
        }

        private static int FindStopIndex(IReadOnlyList<DRTStop> stops, int stopId)
        {
            if (stops == null)
            {
                return -1;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] != null && stops[i].StopId == stopId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int GetNextSequentialStopId(int currentStopId, IReadOnlyList<DRTStop> stops)
        {
            if (stops == null || stops.Count == 0)
            {
                return -1;
            }

            int currentIndex = FindStopIndex(stops, currentStopId);
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % stops.Count : 0;
            return stops[nextIndex] != null ? stops[nextIndex].StopId : -1;
        }

        private void OnValidate()
        {
            maxStops = Mathf.Max(2, maxStops);
            episodeLengthSeconds = Mathf.Max(1f, episodeLengthSeconds);
            maxDistanceForObservation = Mathf.Max(1f, maxDistanceForObservation);
            maxWaitSecondsForObservation = Mathf.Max(1f, maxWaitSecondsForObservation);
            maxDecisionWaitSeconds = Mathf.Max(0.05f, maxDecisionWaitSeconds);
            boardedPassengerReward = Mathf.Max(0f, boardedPassengerReward);
            droppedOffPassengerReward = Mathf.Max(0f, droppedOffPassengerReward);
            travelDistancePenaltyPerMeter = Mathf.Max(0f, travelDistancePenaltyPerMeter);
            travelTimePenaltyPerSecond = Mathf.Max(0f, travelTimePenaltyPerSecond);
            waitingPassengerPenaltyPerSecond = Mathf.Max(0f, waitingPassengerPenaltyPerSecond);
            onBoardPassengerPenaltyPerSecond = Mathf.Max(0f, onBoardPassengerPenaltyPerSecond);
            waitingPassengerWeight = Mathf.Max(0f, waitingPassengerWeight);
            onBoardDestinationWeight = Mathf.Max(0f, onBoardDestinationWeight);
            scheduledPassengerWeight = Mathf.Max(0f, scheduledPassengerWeight);
            distancePenaltyWeight = Mathf.Max(0f, distancePenaltyWeight);
        }
    }
}
