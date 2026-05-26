#if GLEY_CIDY_TRAFFIC
using Gley.UrbanSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;


namespace Gley.TrafficSystem.Editor
{
    public class CidyMethods : UnityEditor.Editor
    {
        struct TrafficLight
        {
            public Transform lightObject;
            public int intersectionIndex;
            public int roadIndex;

            public TrafficLight(Transform lightObject, int intersectionIndex, int roadIndex)
            {
                this.lightObject = lightObject;
                this.intersectionIndex = intersectionIndex;
                this.roadIndex = roadIndex;
            }
        }

        private static List<GenericIntersectionSettings> allGleyIntersections;
        private static List<TrafficLight> trafficLights;
        private static List<Transform> allWaypoints;
        private static List<Transform> allConnectors;
        private static List<TempWaypoint> connectors;

        private static string CidyWaypointsHolder
        {
            get
            {
                return $"{TrafficSystemConstants.PACKAGE_NAME}/{UrbanSystemConstants.EDITOR_HOLDER}/CiDyEditorWaypoints";
            }
        }

        private static string CidyIntersectionsHolder
        {
            get
            {
                return $"{TrafficSystemConstants.PACKAGE_NAME}/{UrbanSystemConstants.EDITOR_HOLDER}/CiDyIntersections";
            }
        }

        class TempWaypoint
        {
            internal string Name { get; set; }
            internal Vector3 Position { get; set; }
            internal float LaneWidth { get; set; }
            internal int MaxSpeed { get; set; }
            internal int ListIndex { get; set; }
            internal bool Enter { get; set; }
            internal bool Exit { get; set; }
        }

        internal static void ExtractWaypoints(IntersectionType intersectionType, float greenLightTime, float yellowLightTime, int maxSpeed, List<int> vehicleTypes, int waypointDistance)
        {
            DestroyImmediate(GameObject.Find(CidyWaypointsHolder));
            DestroyImmediate(GameObject.Find(CidyIntersectionsHolder));


            allWaypoints = new List<Transform>();
            allConnectors = new List<Transform>();
            allGleyIntersections = new List<GenericIntersectionSettings>();
            connectors = new List<TempWaypoint>();
            trafficLights = new List<TrafficLight>();

            Transform intersectionHolder = MonoBehaviourUtilities.GetOrCreateGameObject(CidyIntersectionsHolder, true).transform;
            Transform waypointsHolder = MonoBehaviourUtilities.GetOrCreateGameObject(CidyWaypointsHolder, true).transform;

            // Get CiDyGraph type
            var graphType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("CiDy.CiDyGraph"))
                .FirstOrDefault(t => t != null);

            var graph = FindObjectOfType(graphType);
            graphType.GetMethod("BuildTrafficData")?.Invoke(graph, null);

            //extract road waypoints
            var roadsMember = graphType.GetField("roads") ?? (MemberInfo)graphType.GetProperty("roads");
            var roads = roadsMember is FieldInfo f
                ? f.GetValue(graph) as System.Collections.IList
                : ((PropertyInfo)roadsMember).GetValue(graph) as System.Collections.IList;

            for (int i = 0; i < roads.Count; i++)
            {
                var roadGO = roads[i] as UnityEngine.GameObject;


                GameObject road = MonoBehaviourUtilities.CreateGameObject("Road_" + i, waypointsHolder, waypointsHolder.position, true);

                var roadType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("CiDy.CiDyRoad"))
                    .FirstOrDefault(t => t != null);

                var cidyRoad = roadGO.GetComponent(roadType);
                // leftRoutes.routes
                var leftRoutesObj = roadType.GetField("leftRoutes")?.GetValue(cidyRoad)
                                  ?? roadType.GetProperty("leftRoutes")?.GetValue(cidyRoad);

                var leftRoutes = leftRoutesObj.GetType().GetField("routes")?.GetValue(leftRoutesObj)
                                ?? leftRoutesObj.GetType().GetProperty("routes")?.GetValue(leftRoutesObj);

                // rightRoutes.routes
                var rightRoutesObj = roadType.GetField("rightRoutes")?.GetValue(cidyRoad)
                                   ?? roadType.GetProperty("rightRoutes")?.GetValue(cidyRoad);

                var rightRoutes = rightRoutesObj.GetType().GetField("routes")?.GetValue(rightRoutesObj)
                                 ?? rightRoutesObj.GetType().GetProperty("routes")?.GetValue(rightRoutesObj);

                // laneWidth
                var laneWidth = (float)(
                    roadType.GetField("laneWidth")?.GetValue(cidyRoad)
                    ?? roadType.GetProperty("laneWidth")?.GetValue(cidyRoad)
                );

                ExtractLaneWaypoints((System.Collections.IList)leftRoutes, road, "Left", i, maxSpeed, laneWidth, vehicleTypes);
                ExtractLaneWaypoints((System.Collections.IList)rightRoutes, road, "Right", i, maxSpeed, laneWidth, vehicleTypes);
            }

            //extract connectors

            // masterGraph
            var masterGraphObj = graphType.GetField("masterGraph")?.GetValue(graph)
                               ?? graphType.GetProperty("masterGraph")?.GetValue(graph);

            if (masterGraphObj is System.Collections.IList nodes)
            {
                // cache CiDyNode type
                var nodeType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("CiDy.CiDyNode"))
                    .FirstOrDefault(t => t != null);

                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];

                    var nType = node.GetType();

                    // position
                    var position = (Vector3)(
                        nType.GetField("position")?.GetValue(node)
                        ?? nType.GetProperty("position")?.GetValue(node)
                    );

                    // connectedRoads[0].laneWidth
                    var connectedRoadsObj = nType.GetField("connectedRoads")?.GetValue(node)
                                           ?? nType.GetProperty("connectedRoads")?.GetValue(node);

                    float laneWidth = 0;
                    if (connectedRoadsObj is System.Collections.IList roadsList && roadsList.Count > 0)
                    {
                        var roadObj = roadsList[0];
                        var roadType = roadObj.GetType();

                        laneWidth = (float)(
                            roadType.GetField("laneWidth")?.GetValue(roadObj)
                            ?? roadType.GetProperty("laneWidth")?.GetValue(roadObj)
                        );
                    }

                    // node.type (enum)
                    var typeValue = nType.GetField("type")?.GetValue(node)
                                 ?? nType.GetProperty("type")?.GetValue(node);

                    string typeName = typeValue.ToString();

                    GameObject lane;

                    if (typeName == "continuedSection" || typeName == "culDeSac")
                    {
                        string name = typeName == "continuedSection" ? "Connector" : "CulDeSac";
                        lane = MonoBehaviourUtilities.CreateGameObject(name + i, waypointsHolder, position, true);

                        ExtractNodeRoutes(node, nType, "leftRoutes", lane, i, maxSpeed, laneWidth, -1, vehicleTypes);
                        ExtractNodeRoutes(node, nType, "rightRoutes", lane, i, maxSpeed, laneWidth, -1, vehicleTypes);
                    }
                    else if (typeName == "tConnect")
                    {
                        lane = MonoBehaviourUtilities.CreateGameObject("Intersection" + i, waypointsHolder, position, true);

                        var nodeName = (string)(
                            nType.GetField("name")?.GetValue(node)
                            ?? nType.GetProperty("name")?.GetValue(node)
                        );

                        allGleyIntersections.Add(
                            AddIntersection(intersectionHolder, intersectionType, greenLightTime, yellowLightTime, nodeName, position));

                        // intersectionRoutes
                        var interRoutesObj = nType.GetField("intersectionRoutes")?.GetValue(node)
                                             ?? nType.GetProperty("intersectionRoutes")?.GetValue(node);

                        var interRoutes = interRoutesObj.GetType()
                            .GetField("intersectionRoutes")?.GetValue(interRoutesObj)
                            ?? interRoutesObj.GetType()
                            .GetProperty("intersectionRoutes")?.GetValue(interRoutesObj);

                        if (interRoutes is System.Collections.IList list)
                        {
                            for (int j = 0; j < list.Count; j++)
                            {
                                var item = list[j];
                                var itemType = item.GetType();

                                var light = itemType.GetField("light")?.GetValue(item)
                                         ?? itemType.GetProperty("light")?.GetValue(item);

                                var sequenceIndex = (int)(
                                    itemType.GetField("sequenceIndex")?.GetValue(item)
                                    ?? itemType.GetProperty("sequenceIndex")?.GetValue(item)
                                );

                                AddTrafficlights(new TrafficLight((Transform)light, allGleyIntersections.Count - 1, sequenceIndex));

                                var route = itemType.GetField("route")?.GetValue(item)
                                         ?? itemType.GetProperty("route")?.GetValue(item);

                                ExtractLaneConnectors(route, lane.transform, j, sequenceIndex, maxSpeed, laneWidth, allGleyIntersections.Count - 1, vehicleTypes);
                            }
                        }
                    }
                }
            }

            LinkAllWaypoints(waypointsHolder);

            LinkOvertakeLanes(waypointsHolder, waypointDistance);

            LinkConnectorsToRoadWaypoints();

            AssignIntersections(intersectionType);

            if (intersectionType == IntersectionType.TrafficLights)
            {
                AssignTrafficLights();
            }

            RemoveNonRequiredWaypoints();
        }
        static void ExtractNodeRoutes(object node, Type nodeType, string fieldName, GameObject lane, int i, int maxSpeed, float laneWidth, int intersectionIndex, List<int> vehicleTypes)
        {
            var routesObj = nodeType.GetField(fieldName)?.GetValue(node)
                           ?? nodeType.GetProperty(fieldName)?.GetValue(node);

            var routes = routesObj.GetType().GetField("routes")?.GetValue(routesObj)
                       ?? routesObj.GetType().GetProperty("routes")?.GetValue(routesObj);

            if (routes is System.Collections.IList list)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    ExtractLaneConnectors(list[j], lane.transform, j, i, maxSpeed, laneWidth, intersectionIndex, vehicleTypes);
                }
            }
        }

        private static void AddTrafficlights(TrafficLight trafficLight)
        {
            if (!trafficLights.Contains(trafficLight))
            {
                trafficLights.Add(trafficLight);
            }
        }


        private static void AssignTrafficLights()
        {
            for (int i = 0; i < allGleyIntersections.Count; i++)
            {
                TrafficLightsIntersectionSettings currentIntersection = (TrafficLightsIntersectionSettings)allGleyIntersections[i];

                if (currentIntersection.stopWaypoints != null)
                {
                    for (int j = 0; j < currentIntersection.stopWaypoints.Count; j++)
                    {
                        List<TrafficLight> currentRoadLights = trafficLights.Where(cond => cond.intersectionIndex == i && cond.roadIndex == j).ToList();
                        for (int k = 0; k < currentRoadLights.Count; k++)
                        {
                            Transform colorObject = currentRoadLights[k].lightObject.Find("RedLight");
                            if (colorObject != null)
                            {
                                EnableRenderer(colorObject.GetComponent<Renderer>());
                                currentIntersection.stopWaypoints[j].redLightObjects.Add(colorObject.gameObject);
                            }
                            colorObject = currentRoadLights[k].lightObject.Find("YellowLight");
                            if (colorObject != null)
                            {
                                EnableRenderer(colorObject.GetComponent<Renderer>());
                                currentIntersection.stopWaypoints[j].yellowLightObjects.Add(colorObject.gameObject);
                            }
                            colorObject = currentRoadLights[k].lightObject.Find("GreenLight");
                            if (colorObject != null)
                            {
                                EnableRenderer(colorObject.GetComponent<Renderer>());
                                currentIntersection.stopWaypoints[j].greenLightObjects.Add(colorObject.gameObject);
                            }
                        }
                    }
                }
            }
        }


        static void EnableRenderer(Renderer renderer)
        {
            if (renderer != null)
            {
                if (renderer.enabled == false)
                {
                    renderer.enabled = true;
                }
            }
        }


        private static void AssignIntersections(IntersectionType intersectionType)
        {
            for (int i = 0; i < connectors.Count; i++)
            {
                if (connectors[i].ListIndex != -1)
                {
                    switch (intersectionType)
                    {
                        case IntersectionType.Priority:
                            {
                                PriorityIntersectionSettings currentIntersection = (PriorityIntersectionSettings)allGleyIntersections[connectors[i].ListIndex];
                                if (connectors[i].Enter == true)
                                {
                                    AssignEnterWaypoints(currentIntersection.enterWaypoints, (WaypointSettings)allConnectors[i].GetComponent<WaypointSettings>().prev[0]);
                                }

                                if (connectors[i].Exit)
                                {
                                    if (currentIntersection.exitWaypoints == null)
                                    {
                                        currentIntersection.exitWaypoints = new List<WaypointSettings>();
                                    }
                                    WaypointSettings waypointToAdd = allConnectors[i].GetComponent<WaypointSettings>();
                                    if (!currentIntersection.exitWaypoints.Contains(waypointToAdd))
                                    {
                                        currentIntersection.exitWaypoints.Add(waypointToAdd);
                                    }
                                }
                            }
                            break;

                        case IntersectionType.TrafficLights:
                            {
                                TrafficLightsIntersectionSettings currentIntersection = (TrafficLightsIntersectionSettings)allGleyIntersections[connectors[i].ListIndex];
                                if (connectors[i].Enter == true)
                                {
                                    WaypointSettings waypoint = allConnectors[i].GetComponent<WaypointSettings>();
                                    if (waypoint.prev.Count > 0)
                                    {
                                        AssignEnterWaypoints(currentIntersection.stopWaypoints, (WaypointSettings)allConnectors[i].GetComponent<WaypointSettings>().prev[0]);
                                    }
                                    else
                                    {
                                        Debug.Log(waypoint.name + " is not properly linked", waypoint);
                                    }
                                }
                            }
                            break;

                        default:
                            Debug.LogWarning($"{intersectionType} is not supported");
                            break;

                    }
                }
            }
        }


        private static GenericIntersectionSettings AddIntersection(Transform intersectionHolder, IntersectionType intersectionType, float greenLightTime, float yellowLightTime, string name, Vector3 position)
        {
            GameObject intersection = MonoBehaviourUtilities.CreateGameObject(name, intersectionHolder, position, true);
            GenericIntersectionSettings intersectionScript = null;
            switch (intersectionType)
            {
                case IntersectionType.Priority:
                    intersectionScript = intersection.AddComponent<PriorityIntersectionSettings>();
                    intersectionScript.position = position;
                    ((PriorityIntersectionSettings)intersectionScript).enterWaypoints = new List<IntersectionStopWaypointsSettings>();
                    break;
                case IntersectionType.TrafficLights:
                    intersectionScript = intersection.AddComponent<TrafficLightsIntersectionSettings>();
                    intersectionScript.position = position;
                    ((TrafficLightsIntersectionSettings)intersectionScript).stopWaypoints = new List<IntersectionStopWaypointsSettings>();
                    ((TrafficLightsIntersectionSettings)intersectionScript).greenLightTime = greenLightTime;
                    ((TrafficLightsIntersectionSettings)intersectionScript).yellowLightTime = yellowLightTime;
                    break;
                default:
                    Debug.LogWarning(intersectionType + " not supported");
                    break;
            }

            return intersectionScript;
        }


        private static void RemoveNonRequiredWaypoints()
        {
            for (int j = allWaypoints.Count - 1; j >= 0; j--)
            {
                if (allWaypoints[j].GetComponent<WaypointSettings>().neighbors.Count == 0 ||
                    allWaypoints[j].GetComponent<WaypointSettings>().prev.Count == 0)
                {
                    DestroyImmediate(allWaypoints[j].gameObject);
                }
            }
        }


        private static void LinkConnectorsToRoadWaypoints()
        {
            for (int i = 0; i < allConnectors.Count; i++)
            {
                if (allConnectors[i].name.Contains(UrbanSystemConstants.ConnectionEdgeName))
                {
                    bool found = false;
                    for (int j = 0; j < allWaypoints.Count; j++)
                    {
                        if (Vector3.Distance(allConnectors[i].position, allWaypoints[j].position) < 0.01f)
                        {
                            found = true;
                            WaypointSettings connectorScript = allConnectors[i].GetComponent<WaypointSettings>();
                            WaypointSettings waypointScript = allWaypoints[j].GetComponent<WaypointSettings>();

                            if (connectorScript.prev.Count == 0)
                            {
                                connectorScript.prev = waypointScript.prev;
                                waypointScript.prev[0].neighbors.Remove(waypointScript);
                                waypointScript.prev[0].neighbors.Add(connectorScript);
                                break;
                            }

                            if (connectorScript.neighbors.Count == 0)
                            {
                                connectorScript.neighbors = waypointScript.neighbors;
                                waypointScript.neighbors[0].prev.Add(connectorScript);
                                break;
                            }
                            found = false;
                        }

                    }
                    if (found == false)
                    {
                        Debug.Log("Not Found " + allConnectors[i].name, allConnectors[i]);
                    }
                }
            }
        }


        private static void ExtractLaneConnectors(object routeData, Transform node, int laneIndex, int roadIndex, int speedLimit, float laneWidth, int intersectionIndex, List<int> vehicleTypes)
        {
            Transform connectorsHolder = MonoBehaviourUtilities
                .CreateGameObject("Connectors_" + laneIndex, node, node.position, true).transform;

            var routeType = routeData.GetType();

            // waypoints
            var waypointsObj = routeType.GetField("waypoints")?.GetValue(routeData)
                              ?? routeType.GetProperty("waypoints")?.GetValue(routeData);

            // newRoutePoints
            var newRoutePointsObj = routeType.GetField("newRoutePoints")?.GetValue(routeData)
                                   ?? routeType.GetProperty("newRoutePoints")?.GetValue(routeData);

            var laneConnectors = new List<Vector3>();

            if (waypointsObj is System.Collections.IEnumerable wpEnum)
            {
                foreach (var p in wpEnum)
                    laneConnectors.Add((Vector3)p);
            }

            if (newRoutePointsObj is System.Collections.IEnumerable nrpEnum)
            {
                foreach (var p in nrpEnum)
                    laneConnectors.Add((Vector3)p);
            }

            TrafficWaypointCreator waypointCreator = new TrafficWaypointCreator();

            for (int i = 0; i < laneConnectors.Count; i++)
            {
                var waypoint = new TempWaypoint();
                waypoint.ListIndex = -1;

                if (i == 0 || i == laneConnectors.Count - 1)
                {
                    waypoint.ListIndex = intersectionIndex;
                    waypoint.Name = "Road_" + roadIndex + "-" +
                                    UrbanSystemConstants.LaneNamePrefix + laneIndex + "-" +
                                    UrbanSystemConstants.ConnectionEdgeName + i;

                    if (i == 0)
                        waypoint.Enter = true;
                    else
                        waypoint.Exit = true;
                }
                else
                {
                    waypoint.Name = "Road_" + roadIndex + "-" +
                                    UrbanSystemConstants.LaneNamePrefix + laneIndex + "-" +
                                    UrbanSystemConstants.ConnectionWaypointName + i;
                }

                waypoint.Position = laneConnectors[i];
                waypoint.MaxSpeed = speedLimit;
                waypoint.LaneWidth = laneWidth;

                connectors.Add(waypoint);

                allConnectors.Add(
                    waypointCreator.CreateWaypoint(
                        connectorsHolder,
                        waypoint.Position,
                        waypoint.Name,
                        vehicleTypes,
                        waypoint.MaxSpeed,
                        waypoint.LaneWidth));
            }
        }


        private static void AssignEnterWaypoints(List<IntersectionStopWaypointsSettings> enterWaypoints, WaypointSettings waypointToAdd)
        {
            string roadName = waypointToAdd.name.Split('-')[0];
            int index = -1;
            for (int j = 0; j < enterWaypoints.Count; j++)
            {
                if (enterWaypoints[j].roadWaypoints.Count > 0)
                {
                    if (enterWaypoints[j].roadWaypoints[0].name.Contains(roadName))
                    {
                        index = j;
                    }
                }
            }
            if (index == -1)
            {
                enterWaypoints.Add(new IntersectionStopWaypointsSettings());
                index = enterWaypoints.Count - 1;
                enterWaypoints[index].roadWaypoints = new List<WaypointSettings>();
            }

            if (!enterWaypoints[index].roadWaypoints.Contains(waypointToAdd))
            {
                enterWaypoints[index].roadWaypoints.Add(waypointToAdd);
            }
        }


        private static void LinkAllWaypoints(Transform holder)
        {
            for (int i = 0; i < holder.childCount; i++)
            {
                for (int j = 0; j < holder.GetChild(i).childCount; j++)
                {
                    Transform laneHolder = holder.GetChild(i).GetChild(j);
                    LinkWaypoints(laneHolder);
                }
            }
        }


        private static void LinkWaypoints(Transform laneHolder)
        {
            WaypointSettings previousWaypoint = laneHolder.GetChild(0).GetComponent<WaypointSettings>();
            for (int j = 1; j < laneHolder.childCount; j++)
            {
                string waypointName = laneHolder.GetChild(j).name;
                WaypointSettings waypointScript = laneHolder.GetChild(j).GetComponent<WaypointSettings>();
                if (previousWaypoint != null)
                {
                    previousWaypoint.neighbors.Add(waypointScript);
                    waypointScript.prev.Add(previousWaypoint);
                }
                if (!waypointName.Contains("Output"))
                {
                    previousWaypoint = waypointScript;
                }
                else
                {
                    previousWaypoint = null;
                }
            }
        }


        static void ExtractLaneWaypoints(System.Collections.IList lanes, GameObject lanesHolder, string side, int roadIndex, int maxSpeed, float laneWidth, List<int> vehicleTypes)
        {
            if (lanes != null && lanes.Count > 0)
            {
                TrafficWaypointCreator waypointCreator = new TrafficWaypointCreator();

                for (int i = 0; i < lanes.Count; i++)
                {
                    GameObject lane = MonoBehaviourUtilities.CreateGameObject(
                        "Lane_" + lanesHolder.transform.childCount + "_" + side,
                        lanesHolder.transform,
                        lanesHolder.transform.position,
                        true);

                    var laneObj = lanes[i];
                    var laneType = laneObj.GetType();

                    // waypoints
                    var waypointsObj = laneType.GetField("waypoints")?.GetValue(laneObj)
                                      ?? laneType.GetProperty("waypoints")?.GetValue(laneObj);

                    // newRoutePoints
                    var newRoutePointsObj = laneType.GetField("newRoutePoints")?.GetValue(laneObj)
                                           ?? laneType.GetProperty("newRoutePoints")?.GetValue(laneObj);

                    var positions = new List<Vector3>();

                    if (waypointsObj is System.Collections.IEnumerable wpEnum)
                    {
                        foreach (var p in wpEnum)
                            positions.Add((Vector3)p);
                    }

                    if (newRoutePointsObj is System.Collections.IEnumerable nrpEnum)
                    {
                        foreach (var p in nrpEnum)
                            positions.Add((Vector3)p);
                    }

                    for (int k = 0; k < positions.Count; k++)
                    {
                        if (k > 0 && positions[k - 1] == positions[k])
                            continue;

                        var waypoint = new TempWaypoint();
                        waypoint.MaxSpeed = maxSpeed;
                        waypoint.Name = "Road_" + roadIndex + "-" +
                                        UrbanSystemConstants.LaneNamePrefix +
                                        (lanesHolder.transform.childCount - 1) + "-" +
                                        UrbanSystemConstants.WaypointNamePrefix + k;

                        waypoint.Position = positions[k];
                        waypoint.LaneWidth = laneWidth;

                        allWaypoints.Add(
                            waypointCreator.CreateWaypoint(
                                lane.transform,
                                waypoint.Position,
                                waypoint.Name,
                                vehicleTypes,
                                waypoint.MaxSpeed,
                                waypoint.LaneWidth));
                    }
                }
            }
        }


        private static void LinkOvertakeLanes(Transform holder, int waypointDistance)
        {
            for (int i = 0; i < holder.childCount; i++)
            {
                if (holder.GetChild(i).name.Contains("Road"))
                {
                    for (int j = 0; j < holder.GetChild(i).childCount; j++)
                    {
                        Transform firstLane = holder.GetChild(i).GetChild(j);
                        int laneToLink = j - 1;
                        if (laneToLink >= 0)
                        {
                            LinkLanes(firstLane, holder.GetChild(i).GetChild(laneToLink), waypointDistance);
                        }
                        laneToLink = j + 1;
                        if (laneToLink < holder.GetChild(i).childCount)
                        {
                            LinkLanes(firstLane, holder.GetChild(i).GetChild(laneToLink), waypointDistance);
                        }
                    }
                }
            }
        }


        private static void LinkLanes(Transform firstLane, Transform secondLane, int waypointDistance)
        {
            if (secondLane.name.Split('_')[2] == firstLane.name.Split('_')[2])
            {
                LinkLaneWaypoints(firstLane, secondLane, waypointDistance);
            }
        }


        private static void LinkLaneWaypoints(Transform currentLane, Transform otherLane, int waypointDistance)
        {
            for (int i = 0; i < currentLane.childCount; i++)
            {
                int otherLaneIndex = i + waypointDistance;
                if (otherLaneIndex < currentLane.childCount - 1)
                {
                    WaypointSettings currentLaneWaypoint = currentLane.GetChild(i).GetComponent<WaypointSettings>();
                    WaypointSettings otherLaneWaypoint = otherLane.GetChild(otherLaneIndex).GetComponent<WaypointSettings>();
                    currentLaneWaypoint.otherLanes.Add(otherLaneWaypoint);
                }
            }
        }
    }
}
#endif
