namespace Gley.UrbanSystem.Editor
{
    public static class TrafficEditorBridgeRegistry 
    {
        public static ITrafficEditorBridge Bridge { get; private set; }

        public static void Register(ITrafficEditorBridge bridge)
        {
            Bridge = bridge;
        }
    }
}
