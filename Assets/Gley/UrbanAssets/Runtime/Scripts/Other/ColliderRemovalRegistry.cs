using System.Collections.Generic;
using UnityEngine;

namespace Gley.UrbanSystem
{
    public static class ColliderRemovalRegistry
    {
        private static readonly List<IColliderRemovalListener> _listeners = new();

        public static void Register(IColliderRemovalListener listener)
        {
            if (!_listeners.Contains(listener))
                _listeners.Add(listener);
        }

        public static void Unregister(IColliderRemovalListener listener)
        {
            _listeners.Remove(listener);
        }

        public static void Trigger(Collider[] colliders)
        {
            foreach (var listener in _listeners)
            {
                if (listener.IsInitialized())
                {
                    listener.TriggerColliderRemoved(colliders);
                }
            }
        }
    }
}
