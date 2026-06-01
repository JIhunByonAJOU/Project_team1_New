using UnityEngine;

namespace DRT
{
    public enum DRTTravelExecutionMode
    {
        [InspectorName("Matrix Teleport")]
        MatrixTeleport,

        [InspectorName("Physical Drive")]
        PhysicalDrive
    }

    public enum DRTNextStopPolicy
    {
        [InspectorName("ML-Agents Training")]
        MLAgentsTraining,

        [InspectorName("ONNX Inference")]
        ONNXInference,

        [InspectorName("Vanilla Sequential")]
        VanillaSequential,

        [InspectorName("All Station Runner")]
        AllStationRunner
    }
}
