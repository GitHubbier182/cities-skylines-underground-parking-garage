using UnityEngine;

namespace UndergroundParkingGarage
{
    internal class UndergroundParkingBuildingMonitor : MonoBehaviour
    {
        private float _nextAuditTime;
        private int _lastSelectionRefreshRevision = -1;

        public static void EnsureOnRoot(GameObject root)
        {
            if (root != null && root.GetComponent<UndergroundParkingBuildingMonitor>() == null)
                root.AddComponent<UndergroundParkingBuildingMonitor>();
        }

        private void LateUpdate()
        {
            if (Time.realtimeSinceStartup < _nextAuditTime)
                return;

            _nextAuditTime = Time.realtimeSinceStartup + 0.75f;
            UndergroundParkingBuildingPrefab.EnsureSelectableBounds();
            if (_lastSelectionRefreshRevision != UndergroundParkingRegistry.Revision)
            {
                UndergroundParkingRegistry.RefreshEntranceBuildingSelection();
                _lastSelectionRefreshRevision = UndergroundParkingRegistry.Revision;
            }

            UndergroundParkingRegistry.RemoveFacilitiesWithMissingBuildings();
        }
    }
}
