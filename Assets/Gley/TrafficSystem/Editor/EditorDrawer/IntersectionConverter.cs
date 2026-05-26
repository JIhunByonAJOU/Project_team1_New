using Gley.UrbanSystem;
using Gley.UrbanSystem.Editor;
using System.Collections.Generic;
using UnityEngine;

namespace Gley.TrafficSystem.Editor
{
    /// <summary>
    /// Convert editor intersections to play mode intersections.
    /// </summary>
    public class IntersectionConverter
    {
        private readonly IntersectionEditorData _intersectionData;
        private readonly TrafficWaypointsConverter _trafficWaypointsConverter;
        private readonly IWaypointsConverter _pedestrianWaypointsConverter;

        public IntersectionConverter(IWaypointsConverter trafficWaypointsConverter, IWaypointsConverter pedestrianWaypointsConverter)
        {
            _intersectionData = new IntersectionEditorData();
            _trafficWaypointsConverter = (TrafficWaypointsConverter)trafficWaypointsConverter;
            _pedestrianWaypointsConverter = pedestrianWaypointsConverter;
        }


        public void ConvertAllIntersections()
        {
            ConvertIntersections();
            AssignIntersectionsToCell();
            AddPedestrianWaypoints();
        }


        private void ConvertIntersections()
        {
            var allEditorIntersections = _intersectionData.GetAllIntersections();

            List<PriorityIntersectionData> priorityIntersections = new List<PriorityIntersectionData>();
            List<TrafficLightsIntersectionData> lightsIntersections = new List<TrafficLightsIntersectionData>();
            List<TrafficLightsCrossingData> trafficLightsCrossings = new List<TrafficLightsCrossingData>();
            List<PriorityCrossingData> priorityCrossings = new List<PriorityCrossingData>();
            var AllIntersections = new IntersectionDataType[allEditorIntersections.Length];

            for (int i = 0; i < allEditorIntersections.Length; i++)
            {
                if (allEditorIntersections[i].GetType().Equals(typeof(TrafficLightsIntersectionSettings)))
                {
                    TrafficLightsIntersectionData intersection = ConvertTrafficLightsIntersection((TrafficLightsIntersectionSettings)allEditorIntersections[i]);
                    lightsIntersections.Add(intersection);
                    AllIntersections[i] = new IntersectionDataType(IntersectionType.TrafficLights, lightsIntersections.Count - 1, intersection.Name);
                }

                if (allEditorIntersections[i].GetType().Equals(typeof(TrafficLightsCrossingSettings)))
                {
                    TrafficLightsCrossingData intersection = ConvertTrafficLightsCrossing((TrafficLightsCrossingSettings)allEditorIntersections[i]);
                    trafficLightsCrossings.Add(intersection);
                    AllIntersections[i] = new IntersectionDataType(IntersectionType.LightsCrossing, trafficLightsCrossings.Count - 1, intersection.Name);
                }


                if (allEditorIntersections[i].GetType().Equals(typeof(PriorityIntersectionSettings)))
                {
                    PriorityIntersectionData intersection = ConvertPriorityIntersection((PriorityIntersectionSettings)allEditorIntersections[i]);
                    priorityIntersections.Add(intersection);
                    AllIntersections[i] = new IntersectionDataType(IntersectionType.Priority, priorityIntersections.Count - 1, intersection.Name);
                }

                if (allEditorIntersections[i].GetType().Equals(typeof(PriorityCrossingSettings)))
                {
                    PriorityCrossingData intersection = ConvertPriorityCrossing((PriorityCrossingSettings)allEditorIntersections[i]);
                    priorityCrossings.Add(intersection);
                    AllIntersections[i] = new IntersectionDataType(IntersectionType.PriorityCrossing, priorityCrossings.Count - 1, intersection.Name);
                }
            }

            var trafficIntersectionsData = MonoBehaviourUtilities.GetOrCreateObjectScript<IntersectionsData>(TrafficSystemConstants.PlayHolder, false);
            trafficIntersectionsData.SetTrafficIntersectionData(
                AllIntersections,
                lightsIntersections.ToArray(),
                priorityIntersections.ToArray(),
                trafficLightsCrossings.ToArray(),
                priorityCrossings.ToArray());
        }

        private PriorityCrossingData ConvertPriorityCrossing(PriorityCrossingSettings priorityCrossing)
        {
            return new PriorityCrossingData(
                priorityCrossing.name,
                _trafficWaypointsConverter.ConvertPriorityStopWaypoint(priorityCrossing.enterWaypoints),
                _trafficWaypointsConverter.GetListIndex(priorityCrossing.exitWaypoints));
        }

        private PriorityIntersectionData ConvertPriorityIntersection(PriorityIntersectionSettings priorityIntersection)
        {
            return new PriorityIntersectionData(
                priorityIntersection.name,
                _trafficWaypointsConverter.ConvertPriorityStopWaypoint(priorityIntersection.enterWaypoints),
                _trafficWaypointsConverter.GetListIndex(priorityIntersection.exitWaypoints));
        }

        private TrafficLightsIntersectionData ConvertTrafficLightsIntersection(TrafficLightsIntersectionSettings trafficLightsIntersection)
        {
            return new TrafficLightsIntersectionData(
               trafficLightsIntersection.name,
               _trafficWaypointsConverter.ConvertLightStopWaypoints(trafficLightsIntersection.stopWaypoints),
               trafficLightsIntersection.greenLightTime,
               trafficLightsIntersection.yellowLightTime,
               _trafficWaypointsConverter.GetListIndex(trafficLightsIntersection.exitWaypoints)
           );
        }

        private TrafficLightsCrossingData ConvertTrafficLightsCrossing(TrafficLightsCrossingSettings trafficLightsCrossing)
        {
            return new TrafficLightsCrossingData(
               trafficLightsCrossing.name,
               _trafficWaypointsConverter.ConvertLightStopWaypoints(trafficLightsCrossing.stopWaypoints),
               trafficLightsCrossing.greenLightTime,
               trafficLightsCrossing.yellowLightTime,
               trafficLightsCrossing.redLightTime,
               _trafficWaypointsConverter.GetListIndex(trafficLightsCrossing.exitWaypoints)
           );
        }

        private void AssignIntersectionsToCell()
        {
            GridData gridData;
            if (MonoBehaviourUtilities.TryGetSceneScript<GridData>(out var result))
            {
                gridData = result.Value;
            }
            else
            {
                Debug.LogError(result.Error);
                return;
            }

            var allEditorIntersections = _intersectionData.GetAllIntersections();

            for (int i = 0; i < allEditorIntersections.Length; i++)
            {
                List<IntersectionStopWaypointsSettings> intersectionWaypoints = allEditorIntersections[i].GetAssignedWaypoints();
                for (int j = 0; j < intersectionWaypoints.Count; j++)
                {
                    for (int k = 0; k < intersectionWaypoints[j].roadWaypoints.Count; k++)
                    {
                        var cellData = gridData.GetCell(intersectionWaypoints[j].roadWaypoints[k].transform.position);
                        gridData.AddIntersection(cellData, i);
                    }
                }
            }
        }


        private void AddPedestrianWaypoints()
        {
#if GLEY_PEDESTRIAN_SYSTEM
            var bridge = PedestrianEditorBridgeRegistry.Bridge;
            if (bridge == null)
            {
                Debug.LogError("Pedestrian Editor Bridge is not registered. Cannot add pedestrian waypoints to intersections.");
                return;
            }

            IntersectionsData trafficIntersectionsDatahandler;
            if (MonoBehaviourUtilities.TryGetSceneScript<IntersectionsData>(out var trafficIntersectionData))
            {
                trafficIntersectionsDatahandler = trafficIntersectionData.Value;
            }
            else
            {
                Debug.LogError(trafficIntersectionData.Error);
                return;
            }
            var allIntersections = trafficIntersectionsDatahandler.AllIntersections;
            var allEditorIntersections = _intersectionData.GetAllIntersections();

            for (int i = 0; i < allIntersections.Length; i++)
            {
                switch (allIntersections[i].Type)
                {
                    case IntersectionType.Priority:
                        PriorityIntersectionData priorityInersection = trafficIntersectionsDatahandler.AllPriorityIntersections[allIntersections[i].OtherListIndex];
                        PriorityIntersectionSettings priorityIntersectionEditor = (PriorityIntersectionSettings)allEditorIntersections[i];
                        for (int j = 0; j < priorityIntersectionEditor.enterWaypoints.Count; j++)
                        {
                            priorityInersection.AddPedestrianWaypoints(j, 
                                bridge.GetWaypointIndices(priorityIntersectionEditor.enterWaypoints[j].pedestrianWaypoints,_pedestrianWaypointsConverter), 
                                bridge.GetWaypointIndices(priorityIntersectionEditor.enterWaypoints[j].directionWaypoints,_pedestrianWaypointsConverter));
                        }
                        break;
                    case IntersectionType.PriorityCrossing:
                        PriorityCrossingData priorityCrossing = trafficIntersectionsDatahandler.AllPriorityCrossings[allIntersections[i].OtherListIndex];
                        PriorityCrossingSettings priorityCrossingEditor = (PriorityCrossingSettings)allEditorIntersections[i];
                        for (int j = 0; j < priorityCrossingEditor.enterWaypoints.Count; j++)
                        {
                            priorityCrossing.AddPedestrianWaypoints(j, 
                                bridge.GetWaypointIndices(priorityCrossingEditor.enterWaypoints[j].pedestrianWaypoints,_pedestrianWaypointsConverter), 
                                bridge.GetWaypointIndices(priorityCrossingEditor.enterWaypoints[j].directionWaypoints,_pedestrianWaypointsConverter));
                        }
                        break;
                    case IntersectionType.TrafficLights:
                        TrafficLightsIntersectionData trafficLightsIntersection = trafficIntersectionsDatahandler.AllLightsIntersections[allIntersections[i].OtherListIndex];
                        TrafficLightsIntersectionSettings trafficLightsIntersectionEditor = (TrafficLightsIntersectionSettings)allEditorIntersections[i];
                        if (!trafficLightsIntersectionEditor.ShowPerRoadPedestrians)
                        {
                            trafficLightsIntersection.AddPedestrianWaypoints(
                                bridge.GetWaypointIndices(trafficLightsIntersectionEditor.pedestrianWaypoints,_pedestrianWaypointsConverter), 
                                bridge.GetWaypointIndices(trafficLightsIntersectionEditor.directionWaypoints,_pedestrianWaypointsConverter), 
                                trafficLightsIntersectionEditor.pedestrianRedLightObjects.ToArray(), 
                                trafficLightsIntersectionEditor.pedestrianGreenLightObjects.ToArray(), 
                                trafficLightsIntersectionEditor.pedestrianGreenLightTime);
                        }
                        else
                        {
                            for (int j = 0; j < trafficLightsIntersectionEditor.stopWaypoints.Count; j++)
                            {
                                trafficLightsIntersection.AddPedestrianWaypoints(j, 
                                    bridge.GetWaypointIndices(trafficLightsIntersectionEditor.stopWaypoints[j].pedestrianWaypoints,_pedestrianWaypointsConverter), 
                                    trafficLightsIntersectionEditor.stopWaypoints[j].PedestrianRedLightObjects.ToArray(), 
                                    trafficLightsIntersectionEditor.stopWaypoints[j].PedestrianGreenLightObjects.ToArray());
                            }
                            trafficLightsIntersection.AddDirectionWaypoints(bridge.GetWaypointIndices(trafficLightsIntersectionEditor.directionWaypoints,_pedestrianWaypointsConverter));
                        }

                        break;
                    case IntersectionType.LightsCrossing:
                        TrafficLightsCrossingData trafficLightsCrossing = trafficIntersectionsDatahandler.AllLightsCrossings[allIntersections[i].OtherListIndex];
                        TrafficLightsCrossingSettings trafficLightsCrossingEditor = (TrafficLightsCrossingSettings)allEditorIntersections[i];
                        trafficLightsCrossing.AddPedestrianWaypoints(
                            bridge.GetWaypointIndices(trafficLightsCrossingEditor.pedestrianWaypoints,_pedestrianWaypointsConverter), 
                            bridge.GetWaypointIndices(trafficLightsCrossingEditor.directionWaypoints,_pedestrianWaypointsConverter), 
                            trafficLightsCrossingEditor.pedestrianRedLightObjects.ToArray(), 
                            trafficLightsCrossingEditor.pedestrianGreenLightObjects.ToArray());
                        break;
                }
            }
#endif
        }
    }
}