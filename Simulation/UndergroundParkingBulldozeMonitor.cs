using UnityEngine;

namespace UndergroundParkingGarage
{
    internal class UndergroundParkingBulldozeMonitor : MonoBehaviour
    {
        private float _nextAuditTime;

        public static void EnsureOnRoot(GameObject root)
        {
            if (root != null && root.GetComponent<UndergroundParkingBulldozeMonitor>() == null)
                root.AddComponent<UndergroundParkingBulldozeMonitor>();
        }

        private void LateUpdate()
        {
            if (Time.realtimeSinceStartup < _nextAuditTime)
                return;

            _nextAuditTime = Time.realtimeSinceStartup + 0.35f;
            UndergroundParkingRegistry.RemoveFacilitiesWithMissingAnchors();
        }
    }
}
