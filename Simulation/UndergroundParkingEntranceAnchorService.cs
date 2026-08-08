using System;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingEntranceAnchorService
    {
        private const float ReverseFaceSeparation = 0.08f;
        public const string RequiredWorkshopItemId = "513055494";
        public const string RequiredWorkshopItemName = "parking sign";
        private static PropInfo _anchorPropInfo;

        public static bool IsRequiredParkingSignAvailable()
        {
            return GetAnchorPropInfo() != null;
        }

        public static PropInfo GetRequiredParkingSignInfo()
        {
            return GetAnchorPropInfo();
        }

        public static bool TryEnsureAnchor(ref UndergroundParkingFacility facility)
        {
            if (!facility.IsValid)
                return false;

            bool frontLive = IsPropLive(facility.EntrancePropId);
            bool backLive = IsPropLive(facility.EntranceBackPropId);
            // Recreate both copies during load repair. Earlier candidates used
            // single=false, so PropManager did not mark them as standalone
            // world props and they did not render normally outside info view.
            if (frontLive)
                ReleaseProp(facility.EntrancePropId, "non-authoritative front");
            if (backLive)
                ReleaseProp(facility.EntranceBackPropId, "non-authoritative back");
            frontLive = false;
            backLive = false;

            PropInfo propInfo = GetAnchorPropInfo();
            if (propInfo == null)
            {
                UndergroundParkingLog.Warning("No usable prop prefab found for bulldozable entrance anchor.");
                return false;
            }

            PropManager propManager = Singleton<PropManager>.instance;
            if (propManager == null)
                return false;

            ushort frontPropId = frontLive ? facility.EntrancePropId : (ushort)0;
            ushort backPropId = backLive ? facility.EntranceBackPropId : (ushort)0;
            bool createdFront = false;
            Randomizer randomizer = new Randomizer((uint)(DateTime.UtcNow.Ticks ^ facility.Id));
            float angle = Mathf.Atan2(facility.Direction.x, facility.Direction.z);
            Vector3 position = facility.EntrancePosition;
            position.y = ResolveWorldPropGroundHeight(position);
            if (!frontLive
                && !propManager.CreateProp(out frontPropId, ref randomizer, propInfo, position, angle, true))
            {
                UndergroundParkingLog.Warning("Failed to create front-facing entrance anchor prop: prefab="
                                               + GetPropName(propInfo));
                return false;
            }
            createdFront = !frontLive;

            if (!backLive
                && !propManager.CreateProp(
                    out backPropId,
                    ref randomizer,
                    propInfo,
                    position - facility.Direction * ReverseFaceSeparation,
                    angle + Mathf.PI,
                    true))
            {
                if (createdFront && IsPropLive(frontPropId))
                    propManager.ReleaseProp(frontPropId);

                UndergroundParkingLog.Warning("Failed to create reverse-facing entrance anchor prop: prefab="
                                               + GetPropName(propInfo));
                return false;
            }

            SetFixedWorldHeight(propManager, frontPropId);
            SetFixedWorldHeight(propManager, backPropId);

            string validationMessage;
            if (!ValidateWorldProp(propManager, frontPropId, propInfo, position, out validationMessage)
                || !ValidateWorldProp(
                    propManager,
                    backPropId,
                    propInfo,
                    position - facility.Direction * ReverseFaceSeparation,
                    out validationMessage))
            {
                ReleaseProp(frontPropId, "invalid front");
                ReleaseProp(backPropId, "invalid back");
                UndergroundParkingLog.Warning("Rejected invalid world parking-sign pair: facility="
                                               + facility.Id
                                               + " reason="
                                               + validationMessage);
                return false;
            }

            facility = facility.WithEntranceProps(frontPropId, backPropId);
            UndergroundParkingLog.Advanced("Created double-sided entrance anchor props: facility="
                                        + facility.Id
                                        + " frontProp="
                                        + frontPropId
                                        + " backProp="
                                        + backPropId
                                        + " worldPosition="
                                        + FormatVector(position)
                                        + " prefab="
                                        + GetPropName(propInfo));
            return true;
        }

        private static float ResolveWorldPropGroundHeight(Vector3 position)
        {
            TerrainManager terrain = TerrainManager.instance;
            return terrain == null ? position.y : terrain.SampleDetailHeight(position);
        }

        private static void SetFixedWorldHeight(PropManager manager, ushort propId)
        {
            if (manager == null || !IsPropLive(propId))
                return;

            PropInstance instance = manager.m_props.m_buffer[propId];
            instance.FixedHeight = true;
            manager.m_props.m_buffer[propId] = instance;
            manager.UpdateProp(propId);
        }

        private static bool ValidateWorldProp(
            PropManager manager,
            ushort propId,
            PropInfo expectedInfo,
            Vector3 expectedPosition,
            out string message)
        {
            message = string.Empty;
            if (manager == null || !IsPropLive(propId))
            {
                message = "prop is not live";
                return false;
            }

            PropInstance instance = manager.m_props.m_buffer[propId];
            Vector3 actualPosition = instance.Position;
            if (instance.Info != expectedInfo)
            {
                message = "prefab mismatch";
                return false;
            }
            if (!instance.Single || instance.Hidden || instance.Blocked || !instance.FixedHeight)
            {
                message = "flags single=" + instance.Single
                          + " hidden=" + instance.Hidden
                          + " blocked=" + instance.Blocked
                          + " fixedHeight=" + instance.FixedHeight;
                return false;
            }
            if (UndergroundParkingGeometry.FlatSqrDistance(actualPosition, expectedPosition) > 0.25f
                || Mathf.Abs(actualPosition.y - expectedPosition.y) > 0.1f)
            {
                message = "position expected=" + FormatVector(expectedPosition)
                          + " actual=" + FormatVector(actualPosition);
                return false;
            }

            return true;
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.0")
                   + "," + value.y.ToString("0.0")
                   + "," + value.z.ToString("0.0") + ")";
        }

        public static bool IsAnchorLive(UndergroundParkingFacility facility)
        {
            if (!facility.IsValid || !IsPropLive(facility.EntrancePropId))
                return false;

            // Only the new building-attached flow owns a managed two-prop
            // marker. Current kiosk placements remain valid under their
            // entrance-building lifecycle and must never be removed for
            // lacking a reverse face.
            return facility.TargetBuildingId == 0
                   || IsPropLive(facility.EntranceBackPropId);
        }

        public static void ReleaseAnchor(UndergroundParkingFacility facility)
        {
            ReleaseProp(facility.EntrancePropId, "front");
            ReleaseProp(facility.EntranceBackPropId, "back");
        }

        private static void ReleaseProp(ushort propId, string face)
        {
            if (!IsPropLive(propId))
                return;

            try
            {
                Singleton<PropManager>.instance.ReleaseProp(propId);
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning("Failed to release " + face + " entrance anchor prop "
                                               + propId
                                               + ": "
                                               + e.Message);
            }
        }

        private static bool IsPropLive(ushort propId)
        {
            if (propId == 0)
                return false;

            PropManager propManager = Singleton<PropManager>.instance;
            if (propManager == null
                || propManager.m_props == null
                || propId >= propManager.m_props.m_size)
                return false;

            PropInstance prop = propManager.m_props.m_buffer[propId];
            PropInstance.Flags flags = (PropInstance.Flags)prop.m_flags;
            return (flags & PropInstance.Flags.Created) != 0
                   && (flags & PropInstance.Flags.Deleted) == 0;
        }

        private static PropInfo GetAnchorPropInfo()
        {
            if (_anchorPropInfo != null)
                return _anchorPropInfo;

            string[] preferredNames =
            {
                RequiredWorkshopItemId + ".parking sign_Data",
                RequiredWorkshopItemId + ".parking sign",
                RequiredWorkshopItemName,
                "Parking Sign"
            };

            for (int i = 0; i < preferredNames.Length; i++)
            {
                PropInfo info = PrefabCollection<PropInfo>.FindLoaded(preferredNames[i]);
                if (info != null)
                {
                    _anchorPropInfo = info;
                    UndergroundParkingLog.Advanced("Selected real parking entrance sign prop: " + GetPropName(info));
                    return _anchorPropInfo;
                }
            }

            int loadedCount = PrefabCollection<PropInfo>.LoadedCount();
            string requiredPrefix = RequiredWorkshopItemId + ".";
            for (int i = 0; i < loadedCount; i++)
            {
                PropInfo info = PrefabCollection<PropInfo>.GetLoaded((uint)i);
                string name = GetPropName(info);
                if (info == null
                    || !name.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("parking sign", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                _anchorPropInfo = info;
                UndergroundParkingLog.Advanced("Selected qualified Workshop parking entrance sign prop: " + name);
                return _anchorPropInfo;
            }

            UndergroundParkingLog.Warning("Required parking-sign dependency is not loaded: Workshop item="
                                           + RequiredWorkshopItemId
                                           + " prefab="
                                           + RequiredWorkshopItemName);
            return null;
        }

        private static string GetPropName(PropInfo info)
        {
            return info == null || string.IsNullOrEmpty(info.name) ? string.Empty : info.name;
        }
    }
}
