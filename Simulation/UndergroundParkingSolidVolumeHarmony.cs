using System;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Math;
using HarmonyLib;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingSolidVolumeHarmony
    {
        private const string HarmonyId = "ScratchyBald.UndergroundParkingGarage.SolidVolume";

        private static readonly Type[] NetCreateNodeSignature =
        {
            typeof(NetInfo),
            typeof(NetTool.ControlPoint),
            typeof(NetTool.ControlPoint),
            typeof(NetTool.ControlPoint),
            typeof(FastList<NetTool.NodePosition>),
            typeof(int),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(ushort),
            typeof(ushort).MakeByRefType(),
            typeof(ushort).MakeByRefType(),
            typeof(int).MakeByRefType(),
            typeof(int).MakeByRefType()
        };

        private static readonly Type[] NetCreateNodeWithEndsSignature =
        {
            typeof(NetInfo),
            typeof(NetTool.ControlPoint),
            typeof(NetTool.ControlPoint),
            typeof(NetTool.ControlPoint),
            typeof(FastList<NetTool.NodePosition>),
            typeof(int),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(ushort),
            typeof(ushort).MakeByRefType(),
            typeof(ushort).MakeByRefType(),
            typeof(ushort).MakeByRefType(),
            typeof(int).MakeByRefType(),
            typeof(int).MakeByRefType()
        };

        private static Harmony _harmony;
        private static bool _patched;
        private static int _ownedPedestrianConnectorBuildDepth;

        internal static void BeginOwnedPedestrianConnectorBuild()
        {
            _ownedPedestrianConnectorBuildDepth++;
        }

        internal static void EndOwnedPedestrianConnectorBuild()
        {
            if (_ownedPedestrianConnectorBuildDepth > 0)
                _ownedPedestrianConnectorBuildDepth--;
        }

        public static void Apply()
        {
            if (_patched)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                int patchedTargets = 0;

                MethodInfo netCreateNode = typeof(NetTool).GetMethod(
                    "CreateNode",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    NetCreateNodeSignature,
                    null);
                if (netCreateNode == null)
                    throw new MissingMethodException("Required NetTool.CreateNode primary target not found.");
                _harmony.Patch(
                    netCreateNode,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingSolidVolumeHarmony).GetMethod(
                        "NetCreateNodePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodInfo netCreateNodeWithEnds = typeof(NetTool).GetMethod(
                    "CreateNode",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    NetCreateNodeWithEndsSignature,
                    null);
                if (netCreateNodeWithEnds == null)
                    throw new MissingMethodException("Required NetTool.CreateNode end-node target not found.");
                _harmony.Patch(
                    netCreateNodeWithEnds,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingSolidVolumeHarmony).GetMethod(
                        "NetCreateNodeWithEndsPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodInfo zoneBlockUpdate = typeof(ZoneBlock).GetMethod(
                    "UpdateBlock",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ushort) },
                    null);
                if (zoneBlockUpdate == null)
                    throw new MissingMethodException("Required ZoneBlock.UpdateBlock target not found.");
                _harmony.Patch(
                    zoneBlockUpdate,
                    postfix: new HarmonyMethod(typeof(UndergroundParkingSolidVolumeHarmony).GetMethod(
                        "ZoneBlockUpdatePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                const int expectedPatchedTargets = 3;
                if (patchedTargets != expectedPatchedTargets)
                    throw new InvalidOperationException("Incomplete solid-volume patch ledger: expected="
                                                        + expectedPatchedTargets
                                                        + " actual="
                                                        + patchedTargets);
                _patched = true;
                UndergroundParkingLog.Advanced("Solid garage placement guard active: patchedTargets=" + patchedTargets + ".");
            }
            catch (Exception e)
            {
                _patched = false;
                try
                {
                    if (_harmony != null)
                        _harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception cleanupException)
                {
                    UndergroundParkingLog.Warning(
                        "Solid garage placement guard failed to roll back partial patches: "
                        + cleanupException.Message);
                }
                _harmony = null;
                UndergroundParkingLog.Error("Solid garage placement guard failed to apply: " + e);
            }
        }

        public static void Release()
        {
            // Clean up whenever a Harmony instance exists, even if Apply failed
            // before it could mark the complete patch set active.
            if (_harmony == null)
                return;

            try
            {
                _harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning("Solid garage placement guard failed to release cleanly: " + e.Message);
            }

            _harmony = null;
            _patched = false;
        }

        private static bool NetCreateNodePrefix(
            NetTool.ControlPoint startPoint,
            NetTool.ControlPoint middlePoint,
            NetTool.ControlPoint endPoint,
            ref ToolBase.ToolErrors __result,
            ref ushort node,
            ref ushort segment,
            ref int cost,
            ref int productionRate)
        {
            if (_ownedPedestrianConnectorBuildDepth > 0)
                return true;

            if (!UndergroundParkingRegistry.IntersectsGarageReservationPath(startPoint.m_position, middlePoint.m_position, endPoint.m_position))
                return true;

            node = 0;
            segment = 0;
            cost = 0;
            productionRate = 0;
            __result = ToolBase.ToolErrors.ObjectCollision;
            return false;
        }

        private static bool NetCreateNodeWithEndsPrefix(
            NetTool.ControlPoint startPoint,
            NetTool.ControlPoint middlePoint,
            NetTool.ControlPoint endPoint,
            ref ToolBase.ToolErrors __result,
            ref ushort firstNode,
            ref ushort lastNode,
            ref ushort segment,
            ref int cost,
            ref int productionRate)
        {
            if (_ownedPedestrianConnectorBuildDepth > 0)
                return true;

            if (!UndergroundParkingRegistry.IntersectsGarageReservationPath(startPoint.m_position, middlePoint.m_position, endPoint.m_position))
                return true;

            firstNode = 0;
            lastNode = 0;
            segment = 0;
            cost = 0;
            productionRate = 0;
            __result = ToolBase.ToolErrors.ObjectCollision;
            return false;
        }

        private static void ZoneBlockUpdatePostfix(
            ref ZoneBlock __instance,
            ushort blockID)
        {
            UndergroundParkingBuildingAI.PreventZoneBlockOverRegisteredEntrances(
                blockID,
                ref __instance);
        }
    }
}
