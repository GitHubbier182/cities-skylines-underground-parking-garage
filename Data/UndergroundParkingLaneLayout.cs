using System.Collections.Generic;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal struct UndergroundParkingBay
    {
        public readonly Vector3 LocalPosition;
        public readonly Vector3 LocalLanePosition;
        public readonly Vector3 LocalParkingDirection;
        public readonly bool AisleAlongForward;

        public UndergroundParkingBay(
            Vector3 localPosition,
            Vector3 localLanePosition,
            Vector3 localParkingDirection,
            bool aisleAlongForward)
        {
            LocalPosition = localPosition;
            LocalLanePosition = localLanePosition;
            LocalParkingDirection = localParkingDirection;
            AisleAlongForward = aisleAlongForward;
        }
    }

    internal sealed class UndergroundParkingLaneLayout
    {
        public const int Version = 25;
        public const float BayWidth = 2.6f;
        public const float BayDepth = 4.8f;
        public const float AisleWidth = 5.5f;
        public const float CrossAisleWidth = 3.2f;
        private const int RampReservedBayCount = 2;
        private const float PreferredRampRun = 6.5f;
        private const float TunnelGarageWallOverlap = 0.85f;
        private const float GarageEntryChamberHalfWidth = 2.5f;
        private const float GarageEntryCornerClearance = 0.75f;
        private const float TunnelTurningChamberLength = 5f;
        private const float TunnelTurningChamberDepth = 4f;
        private const float TargetTunnelGrade = 0.18f;

        public readonly List<UndergroundParkingBay> Bays;
        public readonly List<UndergroundParkingBay> PaintedBays;
        public readonly float BayPitch;
        public readonly bool AislesAlongForward;
        public readonly float CrossAisleCoordinate;
        public readonly float CrossAisleSpan;
        public readonly int EntranceSign;
        public readonly Vector3 LocalRampTopPosition;
        public readonly Vector3 LocalIngressPosition;
        public readonly bool SupportsAutomatedTunnel;

        private UndergroundParkingLaneLayout(
            List<UndergroundParkingBay> bays,
            List<UndergroundParkingBay> paintedBays,
            float bayWidth,
            bool aislesAlongForward,
            float crossAisleCoordinate,
            float crossAisleSpan,
            int entranceSign,
            Vector3 localRampTopPosition,
            Vector3 localIngressPosition,
            bool supportsAutomatedTunnel)
        {
            Bays = bays;
            PaintedBays = paintedBays ?? bays;
            BayPitch = bayWidth;
            AislesAlongForward = aislesAlongForward;
            CrossAisleCoordinate = crossAisleCoordinate;
            CrossAisleSpan = crossAisleSpan;
            EntranceSign = entranceSign;
            LocalRampTopPosition = localRampTopPosition;
            LocalIngressPosition = localIngressPosition;
            SupportsAutomatedTunnel = supportsAutomatedTunnel;
        }

        public static bool TryCreate(
            UndergroundParkingFacility facility,
            int requiredBays,
            out UndergroundParkingLaneLayout layout)
        {
            layout = null;
            if (!facility.IsValid || requiredBays <= 0)
                return false;

            float usableWidth = Mathf.Max(
                BayDepth * 2f + AisleWidth,
                facility.GarageWidth - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f);
            float usableLength = Mathf.Max(
                BayDepth * 2f + AisleWidth,
                facility.GarageLength - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f);
            Quaternion inverse = Quaternion.Inverse(
                Quaternion.LookRotation(facility.GarageForward, Vector3.up));
            Vector3 localEntrance = inverse
                                    * (facility.VehicleNodePosition - facility.GarageCenter);

            bool standalone = facility.TargetBuildingId == 0;
            int attachedCandidateCapacity = requiredBays + RampReservedBayCount;
            // Every kiosk candidate, including the capacity fallback, must
            // split its parking rows around the entrance-aligned midpoint
            // aisle. Attached floors retain their established complete grid;
            // their approved tunnel resolver attaches to its nearest aisle.
            LayoutCandidate[] candidates =
            {
                BuildCandidate(usableWidth, usableLength, true, 1, standalone, standalone ? AisleWidth : CrossAisleWidth,
                    standalone ? requiredBays : attachedCandidateCapacity, true),
                BuildCandidate(usableWidth, usableLength, true, -1, standalone, standalone ? AisleWidth : CrossAisleWidth,
                    standalone ? requiredBays : attachedCandidateCapacity, true),
                BuildCandidate(usableWidth, usableLength, false, 1, standalone, standalone ? AisleWidth : CrossAisleWidth,
                    standalone ? requiredBays : attachedCandidateCapacity, true),
                BuildCandidate(usableWidth, usableLength, false, -1, standalone, standalone ? AisleWidth : CrossAisleWidth,
                    standalone ? requiredBays : attachedCandidateCapacity, true)
            };

            LayoutCandidate selected = default(LayoutCandidate);
            float selectedScore = float.MaxValue;
            bool selectedFeasible = false;
            bool foundCandidate = false;
            // Preserve the established bay-row orientation and wall choice.
            // Kiosk centring is a mouth-placement change only; choosing an axis
            // from the kiosk position rotates every parking space and can still
            // leave the physical entrance facing a bay row.
            if (facility.TargetBuildingId == 0)
            {
                // A kiosk always sits on the garage's local-forward wall. Keep
                // the established bay orientation and make the perpendicular
                // cross aisle the actual central entrance spine; selecting a
                // different module axis merely rotates the complete bay map.
                LayoutCandidate retainedOrientation =
                    candidates[localEntrance.x >= 0f ? 2 : 3];
                LayoutCandidate capacityFallback =
                    candidates[localEntrance.z >= 0f ? 0 : 1];
                selected = retainedOrientation.Capacity >= requiredBays
                    ? retainedOrientation
                    : capacityFallback;
                foundCandidate = selected.Capacity >= requiredBays;
                selectedFeasible = foundCandidate;
            }
            else for (int i = 0; i < candidates.Length; i++)
            {
                LayoutCandidate candidate = candidates[i];
                if (candidate.Capacity < requiredBays)
                {
                    continue;
                }

                float candidateRampAcross;
                float candidateIngressAisle;
                float candidateRampAlong;
                float candidateGrade;
                if (!TryResolveRampMouth(
                        facility,
                        localEntrance,
                        candidate,
                        false,
                        out candidateRampAcross,
                        out candidateIngressAisle,
                        out candidateRampAlong,
                        out candidateGrade))
                {
                    continue;
                }

                float gradeError = Mathf.Abs(candidateGrade - TargetTunnelGrade);
                float excessiveGradePenalty = candidateGrade > 0.25f
                    ? (candidateGrade - 0.25f) * 20f
                    : 0f;
                float score = gradeError + excessiveGradePenalty;
                bool feasible = candidateGrade <= 0.25f
                                && candidate.Capacity - RampReservedBayCount
                                   >= requiredBays;
                if (foundCandidate
                    && feasible == selectedFeasible
                    && score >= selectedScore)
                {
                    continue;
                }
                if (foundCandidate && !feasible && selectedFeasible)
                    continue;
                selected = candidate;
                selectedScore = score;
                selectedFeasible = feasible;
                foundCandidate = true;
            }
            if (!foundCandidate)
                return false;

            if (facility.TargetBuildingId == 0)
            {
                float entranceSign = localEntrance.z >= 0f ? 1f : -1f;
                // The exact-colour kiosk animation already finishes five
                // metres inside the entrance. Starting the neutral route back
                // at the wall made it reverse over that same ground before it
                // corrected inward. The internal ramp begins at the identical
                // handoff coordinate and remains monotonic from there.
                float rampTopZ = localEntrance.z
                                 - entranceSign * TunnelTurningChamberLength;
                float rampTopLimit = Mathf.Max(
                    1f,
                    facility.GarageLength * 0.5f
                    - TunnelGarageWallOverlap);
                rampTopZ = Mathf.Clamp(rampTopZ, -rampTopLimit, rampTopLimit);
                float rampBottomZ = rampTopZ
                                    - entranceSign * PreferredRampRun;
                layout = new UndergroundParkingLaneLayout(
                    new List<UndergroundParkingBay>(selected.Bays.GetRange(0, requiredBays)),
                    new List<UndergroundParkingBay>(selected.Bays),
                    selected.BayWidth,
                    selected.AislesAlongForward,
                    selected.CrossAisleCoordinate,
                    selected.CrossAisleSpan,
                    entranceSign > 0f ? 1 : -1,
                    new Vector3(0f, 0f, rampTopZ),
                    new Vector3(0f, 0f, rampBottomZ),
                    true);
                return true;
            }

            List<UndergroundParkingBay> allBays = selected.Bays;
            float rampTopAcross;
            float ingressAisle;
            float rampTopAlong;
            float selectedGrade;
            if (!TryResolveRampMouth(
                    facility,
                    localEntrance,
                    selected,
                    facility.TargetBuildingId == 0,
                    out rampTopAcross,
                    out ingressAisle,
                    out rampTopAlong,
                    out selectedGrade))
            {
                return false;
            }

            // Only the two spaces directly flanking the ramp endpoint are
            // discounted. Keep every other candidate in its original order;
            // proportional resampling created scattered holes throughout the
            // painted map whenever candidate capacity exceeded logical slots.
            List<UndergroundParkingBay> routableBays =
                new List<UndergroundParkingBay>(allBays.Count);
            float desiredRampBottomAlong = rampTopAlong
                                           - selected.EntranceSign
                                           * PreferredRampRun;
            float rampBottomAlong = 0f;
            float closestRampBottomDistance = float.MaxValue;
            for (int i = 0; i < allBays.Count; i++)
            {
                UndergroundParkingBay bay = allBays[i];
                float aisle = selected.AislesAlongForward
                    ? bay.LocalLanePosition.x
                    : bay.LocalLanePosition.z;
                if (Mathf.Abs(aisle - ingressAisle) > 0.05f)
                    continue;

                float along = selected.AislesAlongForward
                    ? bay.LocalPosition.z
                    : bay.LocalPosition.x;
                float distance = Mathf.Abs(along - desiredRampBottomAlong);
                if (distance >= closestRampBottomDistance)
                    continue;
                closestRampBottomDistance = distance;
                rampBottomAlong = along;
            }
            if (closestRampBottomDistance == float.MaxValue)
                return false;

            for (int i = 0; i < allBays.Count; i++)
            {
                UndergroundParkingBay bay = allBays[i];
                float aisle = selected.AislesAlongForward
                    ? bay.LocalLanePosition.x
                    : bay.LocalLanePosition.z;
                float along = selected.AislesAlongForward
                    ? bay.LocalPosition.z
                    : bay.LocalPosition.x;
                bool flanksRamp = Mathf.Abs(aisle - ingressAisle) <= 0.05f;
                bool blocksRampBottom = selectedFeasible
                                        && flanksRamp
                                        && Mathf.Abs(along - rampBottomAlong) <= 0.05f;
                if (!blocksRampBottom)
                    routableBays.Add(bay);
            }
            if (routableBays.Count < requiredBays)
                return false;

            List<UndergroundParkingBay> stableBays = new List<UndergroundParkingBay>(requiredBays);
            for (int slot = 0; slot < requiredBays; slot++)
                stableBays.Add(routableBays[slot]);

            Vector3 localRampTopPosition = selected.AislesAlongForward
                ? new Vector3(rampTopAcross, 0f, rampTopAlong)
                : new Vector3(rampTopAlong, 0f, rampTopAcross);
            Vector3 localIngressPosition = selected.AislesAlongForward
                ? new Vector3(ingressAisle, 0f, rampBottomAlong)
                : new Vector3(rampBottomAlong, 0f, ingressAisle);

            layout = new UndergroundParkingLaneLayout(
                stableBays,
                stableBays,
                selected.BayWidth,
                selected.AislesAlongForward,
                selected.CrossAisleCoordinate,
                selected.CrossAisleSpan,
                selected.EntranceSign,
                localRampTopPosition,
                localIngressPosition,
                selectedFeasible);
            return true;
        }

        private static bool TryResolveRampMouth(
            UndergroundParkingFacility facility,
            Vector3 localEntrance,
            LayoutCandidate candidate,
            bool centreStandaloneIngress,
            out float rampTopAcross,
            out float ingressAisle,
            out float rampTopAlong,
            out float tunnelGrade)
        {
            rampTopAcross = 0f;
            ingressAisle = 0f;
            rampTopAlong = 0f;
            tunnelGrade = float.MaxValue;
            if (candidate.Bays == null || candidate.Bays.Count == 0)
                return false;

            float entranceAcross = candidate.AislesAlongForward
                ? localEntrance.x
                : localEntrance.z;
            float physicalAcrossExtent = candidate.AislesAlongForward
                ? facility.GarageWidth
                : facility.GarageLength;
            float physicalAlongExtent = candidate.AislesAlongForward
                ? facility.GarageLength
                : facility.GarageWidth;
            rampTopAlong = candidate.EntranceSign
                           * Mathf.Max(
                               1f,
                               physicalAlongExtent * 0.5f
                               - TunnelGarageWallOverlap);

            float tunnelLevelDrop = Mathf.Max(
                0f,
                facility.EntrancePosition.y
                - TunnelTurningChamberDepth
                - UndergroundParkingOccupancyManager.GetGarageLevelY(facility, 0));
            float acrossLimit = Mathf.Max(
                1f,
                physicalAcrossExtent * 0.5f
                - GarageEntryChamberHalfWidth
                - GarageEntryCornerClearance);
            float boundedEntranceAcross = Mathf.Clamp(
                entranceAcross,
                -acrossLimit,
                acrossLimit);

            float closestAisleDistance = float.MaxValue;
            for (int i = 0; i < candidate.Bays.Count; i++)
            {
                float aisle = candidate.AislesAlongForward
                    ? candidate.Bays[i].LocalLanePosition.x
                    : candidate.Bays[i].LocalLanePosition.z;
                if (Mathf.Abs(aisle) > acrossLimit)
                    continue;
                // A kiosk replaces the former edge ingress with the middle
                // module aisle. BuildCandidate already supplies one parking
                // row on each side of every aisle, so this splits the retained
                // bay orientation around the new central driving corridor.
                float distance = centreStandaloneIngress
                    ? Mathf.Abs(aisle)
                    : Mathf.Abs(aisle - boundedEntranceAcross);
                if (distance >= closestAisleDistance)
                    continue;
                closestAisleDistance = distance;
                ingressAisle = aisle;
            }
            if (closestAisleDistance == float.MaxValue)
                return false;

            // The mouth itself is aisle-owned and must use the aisle nearest
            // the entrance's coordinate on this wall. Do not slide it sideways
            // to manufacture a preferred grade: that creates a long diagonal
            // detour even when a direct route across the footprint reaches a
            // sensible aisle. Grade is evaluated only after this honest snap,
            // so a different wall may win when the closest direct route here
            // is genuinely too steep.
            rampTopAcross = ingressAisle;
            Vector2 localMouth = candidate.AislesAlongForward
                ? new Vector2(rampTopAcross, rampTopAlong)
                : new Vector2(rampTopAlong, rampTopAcross);
            Vector2 localSurface = new Vector2(localEntrance.x, localEntrance.z);
            Vector2 towardMouth = localMouth - localSurface;
            float entranceToMouth = towardMouth.magnitude;
            Vector2 surfaceTunnel = entranceToMouth > 0.001f
                ? localSurface
                  + towardMouth / entranceToMouth * TunnelTurningChamberLength
                : localSurface;
            Vector2 outward = candidate.AislesAlongForward
                ? new Vector2(0f, candidate.EntranceSign)
                : new Vector2(candidate.EntranceSign, 0f);
            bool externalApproach = Vector2.Dot(surfaceTunnel - localMouth, outward) > 0f;
            Vector2 garageTunnel = externalApproach
                ? localMouth + outward * TunnelTurningChamberLength
                : localMouth;
            float tunnelRun = Mathf.Max(0.1f, Vector2.Distance(surfaceTunnel, garageTunnel));
            tunnelGrade = tunnelLevelDrop / tunnelRun;
            return true;
        }

        private static LayoutCandidate BuildCandidate(
            float usableWidth,
            float usableLength,
            bool aislesAlongForward,
            int entranceSign,
            bool centreCrossAisle,
            float crossAisleSpan,
            int requiredCapacity,
            bool allowBayPitchFit)
        {
            float moduleSpan = BayDepth * 2f + AisleWidth;
            float crossExtent = aislesAlongForward ? usableWidth : usableLength;
            float alongExtent = aislesAlongForward ? usableLength : usableWidth;
            int modules = Mathf.Max(1, Mathf.FloorToInt(crossExtent / moduleSpan));
            float parkingRun = Mathf.Max(BayWidth, alongExtent - crossAisleSpan);
            int alongCount = Mathf.Max(1, Mathf.FloorToInt(parkingRun / BayWidth));
            float bayWidth = BayWidth;
            if (allowBayPitchFit && modules > 0 && modules * alongCount * 2 < requiredCapacity)
            {
                int requiredAlongCount = Mathf.CeilToInt(
                    requiredCapacity / Mathf.Max(1f, modules * 2f));
                float packedBayWidth = parkingRun / Mathf.Max(1, requiredAlongCount);
                // Every facility retains its established logical count. A
                // small, even credible fit keeps the intended axis and full
                // aisle instead of rotating or falling back to legacy dense
                // rows. Grand normally needs no fit. Never go below 2.3 m.
                if (packedBayWidth >= 2.3f)
                {
                    alongCount = requiredAlongCount;
                    bayWidth = packedBayWidth;
                }
            }
            float moduleSpacing = crossExtent / modules;
            float firstModule = -crossExtent * 0.5f + moduleSpacing * 0.5f;
            // The entry ramp occupies the clear aisle nearest the entrance and
            // descends to the cross aisle at the opposite end. Parking is
            // enumerated from the entrance toward that cross aisle, preserving
            // the complete bay count without placing a bay in the ramp lane.
            float parkingStart = entranceSign > 0
                ? alongExtent * 0.5f - bayWidth * 0.5f
                : -alongExtent * 0.5f + bayWidth * 0.5f;
            float parkingDirection = entranceSign > 0 ? -1f : 1f;
            float crossAisleCoordinate = centreCrossAisle
                ? 0f
                : entranceSign > 0
                    ? -alongExtent * 0.5f + crossAisleSpan * 0.5f
                    : alongExtent * 0.5f - crossAisleSpan * 0.5f;

            List<UndergroundParkingBay> bays =
                new List<UndergroundParkingBay>(modules * alongCount * 2);
            for (int along = 0; along < alongCount; along++)
            {
                float alongPosition;
                if (centreCrossAisle)
                {
                    int sideCount = alongCount / 2;
                    bool positiveHalf = along < sideCount;
                    int halfIndex = positiveHalf ? along : along - sideCount;
                    float innerBayCenter = crossAisleSpan * 0.5f
                                           + bayWidth * 0.5f;
                    alongPosition = (positiveHalf ? 1f : -1f)
                                    * (innerBayCenter + halfIndex * bayWidth);
                }
                else
                {
                    alongPosition = parkingStart
                                    + parkingDirection * along * bayWidth;
                }
                for (int module = 0; module < modules; module++)
                {
                    float aisle = firstModule + module * moduleSpacing;
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                    {
                        float side = sideIndex == 0 ? -1f : 1f;
                        float bayOffset = side * (AisleWidth * 0.5f + BayDepth * 0.5f);
                        Vector3 bayPosition;
                        Vector3 lanePosition;
                        Vector3 parkingDirectionVector;
                        if (aislesAlongForward)
                        {
                            bayPosition = new Vector3(aisle + bayOffset, 0f, alongPosition);
                            lanePosition = new Vector3(aisle, 0f, alongPosition);
                            parkingDirectionVector = new Vector3(side, 0f, 0f);
                        }
                        else
                        {
                            bayPosition = new Vector3(alongPosition, 0f, aisle + bayOffset);
                            lanePosition = new Vector3(alongPosition, 0f, aisle);
                            parkingDirectionVector = new Vector3(0f, 0f, side);
                        }

                        bays.Add(new UndergroundParkingBay(
                            bayPosition,
                            lanePosition,
                            parkingDirectionVector,
                            aislesAlongForward));
                    }
                }
            }

            return new LayoutCandidate(
                bays,
                bayWidth,
                aislesAlongForward,
                crossAisleCoordinate,
                crossAisleSpan,
                entranceSign,
                alongExtent);
        }

        private struct LayoutCandidate
        {
            public readonly List<UndergroundParkingBay> Bays;
            public readonly float BayWidth;
            public readonly bool AislesAlongForward;
            public readonly float CrossAisleCoordinate;
            public readonly float CrossAisleSpan;
            public readonly int EntranceSign;
            public readonly float AlongExtent;

            public int Capacity
            {
                get { return Bays == null ? 0 : Bays.Count; }
            }

            public LayoutCandidate(
                List<UndergroundParkingBay> bays,
                float bayWidth,
                bool aislesAlongForward,
                float crossAisleCoordinate,
                float crossAisleSpan,
                int entranceSign,
                float alongExtent)
            {
                Bays = bays;
                BayWidth = bayWidth;
                AislesAlongForward = aislesAlongForward;
                CrossAisleCoordinate = crossAisleCoordinate;
                CrossAisleSpan = crossAisleSpan;
                EntranceSign = entranceSign;
                AlongExtent = alongExtent;
            }
        }
    }
}
