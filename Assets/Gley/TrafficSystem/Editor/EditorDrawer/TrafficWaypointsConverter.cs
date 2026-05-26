using Gley.UrbanSystem;
using Gley.UrbanSystem.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gley.TrafficSystem.Editor
{
    /// <summary>
    /// Convert editor waypoints to play mode waypoints.
    /// </summary>
    public class TrafficWaypointsConverter : IWaypointsConverter
    {
        private readonly TrafficWaypointEditorData _trafficWaypointEditorData;
        private readonly IntersectionEditorData _intersectionEditorData;

        private Dictionary<WaypointSettings, int> _editorWaypointsIndex;

        public TrafficWaypointsConverter()
        {
            _trafficWaypointEditorData = new TrafficWaypointEditorData();
            _intersectionEditorData = new IntersectionEditorData();
        }


        public void ConvertWaypoints()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            VerifyTrafficWaypoints();
            SetWaypointDistance();
            MapWaypoints();
            SetIntersectionProperties();
            ConvertTrafficWaypoints();
            AssignTrafficWaypointsToCell();
            AssignZipperGiveWay();
            GeneratePathfindingWaypoints();
            AddBlinkingOption();
            stopwatch.Stop();
            Debug.Log($"Done converting waypoints in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }


        private void AddBlinkingOption()
        {
            var trafficWaypointsData = MonoBehaviourUtilities.GetOrCreateObjectScript<TrafficWaypointsData>(TrafficSystemConstants.PlayHolder, false);
            for (int i = 0; i < trafficWaypointsData.AllTrafficWaypoints.Length; i++)
            {
                trafficWaypointsData.AllTrafficWaypoints[i].ComputeBlinkerData(trafficWaypointsData);
            }
        }


        private void VerifyTrafficWaypoints()
        {
            WaypointSettings[] allTrafficEditorWaypoints = _trafficWaypointEditorData.GetAllWaypoints();

            if (allTrafficEditorWaypoints.Length <= 0)
            {
                Debug.LogWarning("No waypoints found. Go to Tools->Gley->Traffic System->Road Setup and create a road");
                return;
            }
            for (int i = 0; i < allTrafficEditorWaypoints.Length; i++)
            {
                allTrafficEditorWaypoints[i].VerifyAssignments(true);
                allTrafficEditorWaypoints[i].ResetProperties();
            }
        }


        private void SetWaypointDistance()
        {
            var allTrafficEditorWaypoints = _trafficWaypointEditorData.GetAllWaypoints();
            for (int i = 0; i < allTrafficEditorWaypoints.Length; i++)
            {
                allTrafficEditorWaypoints[i].distance = new List<int>();
                for (int j = 0; j < allTrafficEditorWaypoints[i].neighbors.Count; j++)
                {
                    allTrafficEditorWaypoints[i].distance.Add((int)Vector3.Distance(allTrafficEditorWaypoints[i].transform.position, allTrafficEditorWaypoints[i].neighbors[j].transform.position));
                }
            }
        }


        private void SetIntersectionProperties()
        {
            var allEditorIntersections = _intersectionEditorData.GetAllIntersections();
            for (int i = 0; i < allEditorIntersections.Length; i++)
            {
                if (!allEditorIntersections[i].VerifyAssignments())
                    return;

                List<IntersectionStopWaypointsSettings> intersectionWaypoints = allEditorIntersections[i].GetAssignedWaypoints();

                for (int j = 0; j < intersectionWaypoints.Count; j++)
                {
                    for (int k = 0; k < intersectionWaypoints[j].roadWaypoints.Count; k++)
                    {
                        intersectionWaypoints[j].roadWaypoints[k].enter = true;
                    }
                }

                List<WaypointSettings> exitWaypoints = allEditorIntersections[i].GetExitWaypoints();

                for (int j = 0; j < exitWaypoints.Count; j++)
                {
                    exitWaypoints[j].exit = true;
                }
            }
        }


        private void AssignZipperGiveWay()
        {
            if (MonoBehaviourUtilities.TryGetSceneScript<TrafficWaypointsData>(out var result))
            {
                result.Value.AssignZipperGiveWay();
            }
            else
            {
                Debug.LogError(result.Error);
            }
        }


        private void AssignTrafficWaypointsToCell()
        {
            WaypointSettings[] allWaypoints = _trafficWaypointEditorData.GetAllWaypoints();

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


            WaypointSettings[] giveWayList = GetWaypointsIncludedInGiveWayList(allWaypoints);

            for (int i = allWaypoints.Length - 1; i >= 0; i--)
            {
                if (allWaypoints[i].allowedCars.Count != 0)
                {
                    var cell = gridData.GetCell(allWaypoints[i].transform.position);
                    gridData.AddTrafficWaypoint(cell, i);

                    // Waypoints hat are not allowed to spawn on 
                    if (!allWaypoints[i].name.Contains(UrbanSystemConstants.Connect) &&
                        !allWaypoints[i].name.Contains(UrbanSystemConstants.OutWaypointEnding) &&
                        allWaypoints[i].enter == false &&
                        allWaypoints[i].exit == false &&
                        allWaypoints[i].giveWay == false &&
                        !giveWayList.Contains(allWaypoints[i])
                        )
                    {
                        gridData.AddTrafficSpawnWaypoint(cell, i, allWaypoints[i].allowedCars.Cast<int>().ToArray(), allWaypoints[i].priority);
                    }
                }
            }
        }


        private WaypointSettings[] GetWaypointsIncludedInGiveWayList(WaypointSettings[] allWaypoints)
        {
            List<WaypointSettings> result = new List<WaypointSettings>();
            for (int i = 0; i < allWaypoints.Length; i++)
            {
                result.AddRange(allWaypoints[i].giveWayList);
            }
            return result.Distinct().ToArray();
        }


        private void ConvertTrafficWaypoints()
        {

            WaypointSettings[] allTrafficEditorWaypoints = _trafficWaypointEditorData.GetAllWaypoints();

            // Assign waypoints to MonoBehaviour script.
            var trafficWaypointsData = MonoBehaviourUtilities.GetOrCreateObjectScript<TrafficWaypointsData>(TrafficSystemConstants.PlayHolder, false);

            var trafficWaypoints = ConvertToPlayWaypoints(allTrafficEditorWaypoints);

            trafficWaypointsData.SetTrafficWaypoints(trafficWaypoints);
            SetParentTagsRecursively(trafficWaypointsData.gameObject);
        }


        private TrafficWaypoint[] ConvertToPlayWaypoints(WaypointSettings[] allTrafficEditorWaypoints)
        {
            var result = new TrafficWaypoint[allTrafficEditorWaypoints.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = ConvertToPlayWaypoint(allTrafficEditorWaypoints[i]);
            }
            return result;
        }

        private TrafficWaypoint ConvertToPlayWaypoint(WaypointSettings editorWaypoint)
        {
            int[] angle = new int[editorWaypoint.neighbors.Count];
            for (int i = 0; i < editorWaypoint.neighbors.Count; i++)
            {
                angle[i] = (int)Vector3.Angle(editorWaypoint.Left, ((WaypointSettings)editorWaypoint.neighbors[i]).Left);
            }

            return new TrafficWaypoint(editorWaypoint.name,
                GetListIndex(editorWaypoint),
                editorWaypoint.transform.position,
                editorWaypoint.allowedCars,
                GetListIndex(editorWaypoint.neighbors),
                GetListIndex(editorWaypoint.prev),
                GetListIndex(editorWaypoint.otherLanes),
                editorWaypoint.maxSpeed,
                editorWaypoint.giveWay,
                editorWaypoint.complexGiveWay,
                editorWaypoint.zipperGiveWay,
                editorWaypoint.triggerEvent,
                editorWaypoint.laneWidth,
                editorWaypoint.Left,
                editorWaypoint.eventData,
                GetListIndex(editorWaypoint.giveWayList),
                angle);
        }

        private void MapWaypoints()
        {
            if (_trafficWaypointEditorData == null)
            {
                Debug.LogError("TrafficWaypointEditorData is null");
                _editorWaypointsIndex = new Dictionary<WaypointSettings, int>();
                return;
            }


            WaypointSettings[] allTrafficEditorWaypoints = _trafficWaypointEditorData.GetAllWaypoints();

            if (allTrafficEditorWaypoints == null || allTrafficEditorWaypoints.Length == 0)
            {
                Debug.LogWarning("No waypoints found");
                _editorWaypointsIndex = new Dictionary<WaypointSettings, int>();
                return;
            }

            _editorWaypointsIndex = new Dictionary<WaypointSettings, int>(allTrafficEditorWaypoints.Length);

            for (int i = 0; i < allTrafficEditorWaypoints.Length; i++)
            {
                var wp = allTrafficEditorWaypoints[i];

                if (wp == null)
                {
                    continue;
                }

                if (!_editorWaypointsIndex.ContainsKey(wp))
                {
                    _editorWaypointsIndex.Add(wp, i);
                }
                else
                {
                    Debug.Log(wp.name + " already exists", wp);
                }
            }
        }

        private int GetListIndex(WaypointSettings editorWaypoint)
        {
            if (_editorWaypointsIndex == null)
            {
                Debug.LogError("Waypoint index not initialized. Call MapWaypoints() first.");
                return -1;
            }

            if (editorWaypoint == null)
            {
                Debug.LogError("Editor waypoint is null");
                return -1;
            }

            if (_editorWaypointsIndex.TryGetValue(editorWaypoint, out var index))
            {
                return index;
            }

            Debug.LogWarning("Waypoint not found in index: " + editorWaypoint.name, editorWaypoint);
            return -1;
        }

        public int[] GetListIndex(List<WaypointSettings> editorWaypoint)
        {
            var result = new int[editorWaypoint.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = GetListIndex(editorWaypoint[i]);
            }
            return result;
        }
        public int[] GetListIndex(List<WaypointSettingsBase> editorWaypoints)
        {
            var result = new int[editorWaypoints.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = GetListIndex((WaypointSettings)editorWaypoints[i]);
            }
            return result;
        }

        private PriorityStopWaypoints ConvertPriorityStopWaypoint(IntersectionStopWaypointsSettings editorWaypoints)
        {
            return new PriorityStopWaypoints(GetListIndex(editorWaypoints.roadWaypoints), editorWaypoints.greenLightTime);
        }
        public PriorityStopWaypoints[] ConvertPriorityStopWaypoint(List<IntersectionStopWaypointsSettings> editorWaypoints)
        {
            var result = new PriorityStopWaypoints[editorWaypoints.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = ConvertPriorityStopWaypoint(editorWaypoints[i]);
            }
            return result;
        }

        private LightsStopWaypoints ConvertLightStopWaypoints(IntersectionStopWaypointsSettings editorWaypoints)
        {
            return new LightsStopWaypoints(
                GetListIndex(editorWaypoints.roadWaypoints),
                editorWaypoints.redLightObjects.ToArray(),
                editorWaypoints.yellowLightObjects.ToArray(),
                editorWaypoints.greenLightObjects.ToArray(),
                editorWaypoints.greenLightTime);
        }

        public LightsStopWaypoints[] ConvertLightStopWaypoints(List<IntersectionStopWaypointsSettings> editorWaypoints)
        {
            var result = new LightsStopWaypoints[editorWaypoints.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = ConvertLightStopWaypoints(editorWaypoints[i]);
            }
            return result;
        }

        private void SetParentTagsRecursively(GameObject obj)
        {
            Transform currentParent = obj.transform.parent;

            while (currentParent != null)
            {
                if (currentParent.gameObject.tag == UrbanSystemConstants.EDITOR_TAG)
                {
                    currentParent.gameObject.tag = "Untagged";
                }
                currentParent = currentParent.parent;
            }
        }


        private void GeneratePathfindingWaypoints()
        {
            bool pathfindingEnabled = new SettingsLoader(TrafficSystemConstants.windowSettingsPath).LoadSettingsAsset<TrafficSettingsWindowData>().PathFindingEnabled;
            var modules = MonoBehaviourUtilities.GetOrCreateObjectScript<TrafficModules>(TrafficSystemConstants.PlayHolder, false);

            if (pathfindingEnabled)
            {
                var allTrafficEditorWaypoints = _trafficWaypointEditorData.GetAllWaypoints();
                var trafficPathFindingCreator = new TrafficPathFindingCreator();
                trafficPathFindingCreator.GenerateWaypoints(allTrafficEditorWaypoints, this);
                modules.SetModules(true);
            }
            else
            {
                modules.SetModules(false);
                if (MonoBehaviourUtilities.TryGetObjectScript<PathFindingData>(TrafficSystemConstants.PlayHolder, out var result))
                {
                    GleyPrefabUtilities.DestroyImmediate(result.Value);
                }
            }
        }
    }
}