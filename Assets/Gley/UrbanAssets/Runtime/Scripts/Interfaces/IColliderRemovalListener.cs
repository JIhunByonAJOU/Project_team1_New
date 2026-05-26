using UnityEngine;

namespace Gley.UrbanSystem
{
    public interface IColliderRemovalListener
    {
        bool IsInitialized();
        void TriggerColliderRemoved(Collider[] colliders);
    }
}
