namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingConnectorCleanup
    {
        public static void ReleaseConnector(UndergroundParkingFacility facility)
        {
            if (!facility.ConnectorCreated)
                return;

            NetManager netManager = NetManager.instance;
            if (netManager == null)
                return;

            try
            {
                bool releasedSegment = false;
                if (IsLiveSegment(netManager, facility.ConnectorSegmentId))
                {
                    int startSegmentCount = CountNodeSegments(netManager, facility.ConnectorStartNodeId);
                    int endSegmentCount = CountNodeSegments(netManager, facility.ConnectorEndNodeId);
                    netManager.ReleaseSegment(facility.ConnectorSegmentId, false);
                    releasedSegment = true;
                    UndergroundParkingLog.Advanced("Released owned underground parking connector segment: facility="
                                                + facility.Id
                                                + " segment="
                                                + facility.ConnectorSegmentId
                                                + " startSegmentsBefore="
                                                + startSegmentCount
                                                + " endSegmentsBefore="
                                                + endSegmentCount);
                }

                if (releasedSegment || !IsLiveSegment(netManager, facility.ConnectorSegmentId))
                    ReleaseUnusedConnectorNode(netManager, facility.ConnectorStartNodeId, facility.Id);

                if (releasedSegment || !IsLiveSegment(netManager, facility.ConnectorSegmentId))
                    ReleaseUnusedConnectorNode(netManager, facility.ConnectorEndNodeId, facility.Id);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning("Failed to release old underground parking connector for facility "
                                               + facility.Id
                                               + ": "
                                               + e.Message);
            }
        }

        private static bool IsLiveSegment(NetManager netManager, ushort segmentId)
        {
            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_size)
                return false;

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            return (segment.m_flags & NetSegment.Flags.Created) != 0
                   && (segment.m_flags & NetSegment.Flags.Deleted) == 0;
        }

        private static bool IsLiveNode(NetManager netManager, ushort nodeId)
        {
            if (netManager == null || nodeId == 0 || nodeId >= netManager.m_nodes.m_size)
                return false;

            NetNode node = netManager.m_nodes.m_buffer[nodeId];
            return (node.m_flags & NetNode.Flags.Created) != 0
                   && (node.m_flags & NetNode.Flags.Deleted) == 0;
        }

        private static int CountNodeSegments(NetManager netManager, ushort nodeId)
        {
            if (!IsLiveNode(netManager, nodeId))
                return 0;

            return netManager.m_nodes.m_buffer[nodeId].CountSegments();
        }

        private static void ReleaseUnusedConnectorNode(NetManager netManager, ushort nodeId, int facilityId)
        {
            if (!IsLiveNode(netManager, nodeId))
                return;

            NetNode node = netManager.m_nodes.m_buffer[nodeId];
            if (node.CountSegments() == 0)
            {
                netManager.ReleaseNode(nodeId);
                UndergroundParkingLog.Advanced("Released old underground parking garage connector node: facility="
                                            + facilityId
                                            + " node="
                                            + nodeId);
            }
            else
            {
                UndergroundParkingLog.Advanced("Leaving connected underground parking node in place: facility="
                                            + facilityId
                                            + " node="
                                            + nodeId
                                            + " segments="
                                            + node.CountSegments());
            }
        }
    }
}
