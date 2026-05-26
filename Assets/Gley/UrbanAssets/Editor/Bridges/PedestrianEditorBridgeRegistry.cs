namespace Gley.UrbanSystem.Editor
{
    public static class PedestrianEditorBridgeRegistry
    {
        private static IPedestrianEditorBridge _bridge;

        public static void Register(IPedestrianEditorBridge bridge)
        {
            Unregister();

            _bridge = bridge;
        }

        public static void Unregister()
        {
            if (_bridge == null)
                return;

            if (_bridge is System.IDisposable disposable)
            {
                disposable.Dispose();
            }

            _bridge = null;
        }

        public static bool HasBridge => _bridge != null;

        public static IPedestrianEditorBridge Bridge => _bridge;
    }
}
