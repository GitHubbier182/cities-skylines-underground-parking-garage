# Underground Parking Garage

Version 2.3.0 restores the accepted zero-facility isolation, first-garage
cache and live-pose identity corrections over the published v2.2.0 baseline.
When a loaded city contains no registered UPG facility, UPG installs none of
its parking-occupancy Harmony targets and runs no ordinary occupancy
housekeeping. The first valid facility activates the existing closed
integration ledger; removing the final facility releases it again. Managed
garage creation, staging and road release recalculate the exact live drivable
lane, while routed rollback and parked-record publication reject invalid poses
instead of allowing stale or default coordinates to displace vehicles. Removed
garages now place former parked identities in a persisted native-release ledger;
an arrival already admitted to the validated portal completes its existing
pavement and parking transaction before NUKE removes the facility, rather than
being restored as a stopped road car with no continuing route. Former parked
cars are relocated only to independently validated roadside spaces, and a live
vehicle occupant or pedestrian owner remains held until neither identity can be
published from the former entrance. Active walkers continue their native trips
without having the deferred car link restored by NUKE, and a subsequently
placed garage cannot adopt any parked identity that remains in the release
ledger. This repository is the matching clean public-source snapshot for the
published v2.3.0 release.

Ordinary surface buildings may occupy land above a buried garage. Vanilla
continues to protect the visible entrance building, while UPG independently
prevents two underground garage volumes from overlapping.

## Scope

UPG provides underground car parks with standalone and building-attached
surface entrances, visible portal activity, adjustable underground capacity,
parking management, and persistent parked-vehicle presentation.
For a building-attached garage, drag the `Parking Management` title to keep a
preferred Building Summary-relative position; the panel remembers that offset
across selections and restarts and keeps it within the visible viewport. It
closes with its host building dialogue or any non-UPG selection and transfers
directly to the newly selected garage when another UPG host or entrance opens.
In Transport x-ray, a completed world click preserves any new building selected
by the vanilla tool; clicking empty world instead closes the restored host and
returns to normal view so the next building opens normally with one click.

The compact Public Transport `P` tab offers the established 2x3 entrance with a
50m x 25m garage, a 3x3 Civic pavilion with a 64m x 64m garage, a 4x4 Grand
pavilion with a two-floor 64m x 80m garage, and the building-attached tool in
that left-to-right order.
They provide 39, 170, and 408 initial spaces respectively under the shared
parking-bay rules. The Grand pavilion must retain at least two floors; every
standalone garage supports up to five.
Adding a floor remains available during ordinary garage traffic. Changing the
floor count preserves every parked car and active arrival using a surviving bay;
a lower floor must be free of parked and arriving vehicles before removal.
Each standalone tile uses the game's normal building info tooltip to show its
matching diagonal surface/Transport-view comparison before placement.
Affected older cities are repaired on load: buildings that grew across an
existing standalone entrance's exact surface footprint are bulldozed before
zoning is removed and kept away from that footprint.

The standalone buildings share a blue-and-white painted `P` and its established
night lighting but have distinct architecture: Compact retains its warm slatted
portal, Civic forms a tall folded gateway with white ribs and a blue skylight,
and Grand carries a broad asymmetric ribbon canopy on four raking pylons.
The larger open pavilions leave surrounding terrain exposed rather than laying
a flat slab across their full footprints. Each localized entrance pad uses a
shallow, below-grade-sided plinth to stay visible on contoured sites, and Grand
brings its illuminated painted `P` forward for a clear overhead read.

The mod preserves exact vehicle/citizen identity through adopted parking
transactions. Vanilla retains road movement, occupant release, walking
continuation, and native vehicle lifecycle except at the documented minimum
integration boundaries. TM:PE Parking AI deferred walking is supported. With
realistic parking enabled, UPG participates in TM:PE's pre-trip parking choice;
TM:PE owns the complete vehicle route and exact entrance-offset target, while
UPG reserves the bay during pre-trip planning and adopts the transaction only
after the later live simulation arrival proves the car reached its validated
portal.
Once either the native continuation or TM:PE deferred-walking result is adopted,
one exact vehicle-prefab and citizen-unit token prevents both passenger-car and
vehicle-manager release while the car remains in the carriageway. A later
parking search cannot create a second UPG reservation for that car. Exact portal
proof enters the established garage transaction; otherwise UPG relinquishes the
route only after vanilla successfully accepts a continuing passenger-car path.
For TM:PE realistic-parking arrivals, the successful parking-planning callback
adopts the exact reserved identity but does not prove physical arrival. TM:PE
and vanilla retain movement, collision and traffic-signal ownership while the
real spawned car follows its selected vehicle lane to the captured entrance
offset. The later live simulation gate requires that exact lane, travel
direction and the terminal calculated exactly two metres before the registered
entrance before UPG may stop the car.
TM:PE may replace or advance its active path during that native journey; the
early planning path ID is therefore not an arrival invariant. Once adopted, the
durable transaction plus the live exact-lane, direction and portal proof owns
the handoff decision. Ordinary UPG-owned routes retain their path-ID check. Each
entrance has one shared arrival/departure portal owner. While that owner is
busy, UPG defers the next car's terminal parking callback before marking the
transaction stopped or preparing a garage commit; the exact car and occupants
remain under vanilla road simulation, so ordinary traffic queuing supplies
coherent movement and render frames. Once the entrance is clear, the terminal
proof is accepted once and the road body is replaced at that exact final pose
by a proxy using that exact vehicle prefab shape, without releasing the reserved
vehicle record or its occupants. The entering proxy uses the same neutral grey
x-ray material and final x-ray draw pass as parked cars, with paint, lamp and
wheel-colour accents removed, so the garage overlay cannot make a moving car
darker than its placed state.
Its duration is measured from the complete path length at approximately 30 mph,
so short and long entrances share speed rather than a fixed time. The proxy pulls
over those final two metres,
through the kerb opening and into the existing tunnel's 5×5×4-metre horizontal
turning chamber positioned wholly behind and below the entrance. The exact-colour
surface animation ends at that chamber's tunnel opening, where the neutral-grey
garage animation starts at the identical coordinate and owns the complete
tunnel journey. Its roof is
flush with terrain, and one framed opening in whichever of its left, right or
far faces points toward the selected garage mouth connects directly to the
continuous inward ramp. At the garage end, the tunnel enters the outer wall
of an identical 5×5×4-metre chamber projecting from the selected garage side;
its wall position snaps to the circulation aisle nearest the real entrance
coordinate rather than sliding sideways to manufacture a preferred grade. All
four sides are compared using that honest aisle-aligned endpoint and real
resulting grade; the wall opposite the entrance remains valid when the footprint
provides a long direct internal descent, while unreasonably steep approaches are
rejected. The chamber remains wholly within the
wall bounds and its open garage
side shares the exact centreline of a valid level-0 aisle without using internal
parking space or introducing a vertical drop. When the tunnel approaches the
selected wall from beneath the building it terminates directly at that internal
ramp mouth; an approach from outside the footprint receives the 5×5×4-metre
lower landing before crossing the wall. That lower landing keeps its
garage-facing side open to the selected aisle and accepts the tunnel through
whichever of its near, left or right faces the descent actually reaches. This
lets the car finish its perpendicular alignment, travel briefly forward at road
height and then progressively pitch nose-down without twisting toward the
opposite heading. It uses vanilla's render-time
simulation-speed scale, so portal capacity accelerates with road traffic at
higher game speeds and pauses with the simulation instead of accumulating
stale waiting heads. The surface entrance becomes available to the next road
car 0.10 simulation seconds after that short surface animation finishes;
admission does not wait for the neutral tunnel-and-parking journey or its
parked-record handoff. The
parked identity remains visually withheld in its reserved
garage space until that entrance movement finishes. Only at that endpoint are
occupants handed to the validated pavement, the reserved bay committed, and the
original vehicle record retired.
If none of the four walls can provide a complete tunnel at or below a 25% grade,
the building keeps its complete underground parking capacity but creates no
tunnel, landing, entrance animation or internal journey. Arrivals are committed
directly to their reserved bay and departures use the existing validated road
release; no ingress bays are removed for a route that does not exist.
At admission, the proxy start and initial tangent are rebased to the road
body's exact vanilla render-frame position, heading,
vertical sway, lean and nod, continuously observed until its unspawn is
acknowledged. Unspawn is delayed until the first sample has crossed a complete
native render cycle, and newer observations remain unpublished until the next
spawned frame confirms them, so neither an earlier FIFO-entry position, a stale
request-time sample, an unrendered concurrent sample, a later simulation
interpolation nor a motorcycle snapping upright can create a handoff step. Both
cubic surface controls are then reordered against that final visible pose;
their projections toward the entrance can never decrease, even when the native
car stops slightly ahead of its earlier captured target.
Both TM:PE parking-search stages receive that same road-lane position and a
lane-aligned rotation; the pavement handoff, building boundary and underground
bay are never used as its movement target. The early parked record remains
hidden at the road target until the endpoint commit moves it into its reserved
underground space.
TM:PE's earlier route-planning identity and owner are bound directly to its
exact active vehicle for the transaction lifetime rather than rediscovered from
mutable citizen parking associations. It stays inert and cannot consume the bay
or appear as a parked car before that commit. If TM:PE or vanilla releases that
early planning record during a long approach, the captured owner and exact road
prefab authorize one replacement parked record only at the validated endpoint;
the car no longer remains held at the portal because a disposable planning
record expired. TM:PE conventional parking cannot occupy the entrance-side apron, and a
saved blocker is relocated only when TM:PE supplies a safe replacement on the
same road. TM:PE compatibility arrivals always run this serialized entrance
presentation; the established off-camera optimization remains limited to
non-TM:PE arrivals.

When a resident or tourist requests an exact car stored by UPG, the concrete
owner-AI retrieval call temporarily presents that parked identity at the
validated pavement portal. TM:PE realistic parking performs its final
pedestrian-to-car transition later through its own exact parked-car method, so
that boundary reopens the same identity scope around TM:PE's one native vehicle
creation. The initialized real car is then held behind the shared departure
portal, represented by its matching emergence proxy, and released once into
safe space on the connected road lane before vanilla resumes its ordinary
route. A request that creates no vehicle restores the underground record and
changes neither occupancy nor native lifecycle state.

The entrance presentation has two explicit owners. The real road car stops with
its nose still at least 0.75 metres before the entrance. After atomic removal, a
short exact proxy retains that car's mesh, material, captured colour and full
scale while one curve begins tangent to the live road from that captured pose
and finishes perpendicular through the P opening. Vehicle length and a
compatible TM:PE terminal offset cannot move the entrance centre or make the
turn diagonal, and the short level lead changes only descent height. Only when
that animation completes does a separate neutral-grey clone of the same exact
vehicle shape take over at the endpoint and drive at approximately 30 mph
through the internal ramp and aisles to its assigned bay. Its uniform bay fit
uses the prefab's authored physical dimensions rather than imported mesh bounds.
Departures reverse the accepted underground journey, emerge perpendicular to
the road and finish the same vehicle-length-aware distance after the entrance
that arrivals stop before it. The held real car is reclaimed at that identical
downstream pose facing with traffic as the proxy completes. Only the spatial
coordinates in its stale pre-animation movement buffer are aligned to that same
validated pose immediately before publication; native speed metadata is retained,
after which vanilla alone owns its route and movement.
Each garage now separates stable logical slots from physical bays and dedicated
double-loaded circulation aisles. Every kiosk candidate places its cross aisle
at the exact midpoint, so even a capacity fallback cannot put a parking row
across the entrance-aligned spine; its ramp lands directly on that clear two-way
route. Where fixed pitch is just short, the Compact and Civic kiosks use even
approximately 2.33- and 2.51-metre bay pitches so their proper orientations
retain complete capacity and the full 5.5-metre aisle; Grand needs no fit.
Building-attached garages retain their complete fitted grid before the
approved tunnel resolver attaches to its nearest sensible aisle. Every valid physical
bay remains painted while the established
logical capacity and slot identities stay unchanged. Building-attached garages retain their feasible tunnel-selected aisle and may evenly tighten only their bay pitch, to no less than 2.3 metres, when fixed spacing would otherwise lose a partial boundary row and incorrectly reject a complete layout; kiosk fitting uses the same floor only when needed to preserve its approved axis and aisle. Each
layout owns a ramp-top node at the sole exterior entrance, a clear internal ramp along that aisle and a
ramp-bottom ingress snapped to a logical bay row inside that entrance. Only the
two candidate bays directly flanking that ramp endpoint are omitted from
assignment and paint; every other logical bay remains in its original sequential
map position rather than being proportionally redistributed. The neutral proxy
continues from the exact below-ground endpoint down the one connected internal
ramp, then follows the cross aisle and bay aisle before making its final parking
turn rather than routing through parking cells. Existing slot indices retain
their capacity and floor, so full legacy garages remap deterministically without
releasing or evicting cars. The garage profile corners use one quarter of their
original cutback and a smooth four-segment quarter arc, keeping the opening
clear without a sharp diagonal. The exterior prefab/tunnel is the only
entrance-ramp renderer; its floor publishes the exact road and garage-mouth
centre points consumed by the entrance animation, and its garage mouth is the
lane layout's own ramp-top node. The internal journey begins at that same point,
so no duplicate entrance ramp, parallel route or edge-snapped substitute is
generated. Multiple proxies may overlap safely; leaving x-ray finishes them
immediately. At the below-ground handoff, each running journey creates one
concrete neutral vehicle object with the final parked car's exact neutral material assigned to every prefab
submesh; that object's transform follows the route below camera height 500.
Above that cutoff the moving object is skipped and the committed car appears
directly in its assigned space. The surface/portal proxy uses its own viewport
and height gate while its transaction continues off-screen. Underground work is
limited to visibility, smooth transform updates and one cached neutral unlit
material; weather and entrance-renderer
repair remain surface-only on a separate two-second interval. The parked
presentation retains the same exact prefab shape. New bays use the stable
rotating free-slot sequence without imposing an entrance-distance loading
order. Standalone kiosk and building-attached garages share the same
pre-entrance stop, road-to-perpendicular exact-colour entrance animation,
fully-underground ownership handoff and entry-aligned internal route without
adding a second exterior ramp mesh or crossing parking spaces.
For kiosks, that neutral route begins at the exact five-metre underground
handoff and descends monotonically toward the midpoint aisle; it never revisits
the entrance wall or inserts a vertical correction before continuing inward.
Inside either garage type, arrivals and departures derive from the same route
but offset to opposite map-appropriate sides of the tunnel, ramp and aisle like
ordinary two-way road traffic. The exact portal and bay endpoints remain
centred so the entrance handoff and final parking turn stay seamless.

Retrieval uses the reverse internal presentation. After vanilla successfully
adopts the exact initialized car, its neutral-grey exact-shape proxy leaves the
committed bay and follows the bay aisle, cross aisle and ramp toward the portal
at approximately 30 mph. The existing underground-to-road portal animation and
safe-lane release then continue unchanged; leaving x-ray skips only the hidden
internal leg and never releases or recreates the held real car.

Building-attached garages may add one compensating second floor through the
same `− Floor` and `+ Floor  ₡25,000` Parking Management controls used by kiosk
garages. The attached garage is capped at two floors. Its second floor changes
capacity, depth, ramps, aisles, persistence and parked presentation together;
removal remains blocked while that floor owns a parked car, reservation or
pending arrival and issues no refund.

Moving a building-attached entrance validates the new handoff, then commits it
immediately without changing garage occupancy or parked vehicles. Uncommitted
offers return through TM:PE's native parking search, adopted road arrivals are
retargeted to the new exact lane without replaying their parking transaction,
and staged or presenting departures rebuild against the new entrance. The
garage's previous open or closed state is preserved.
Switching a building-attached garage off uses the same ownership boundary:
every uncommitted pre-trip offer is withdrawn synchronously and returned to
TM:PE through the same native passenger-car pathfinding owner, while only
transactions already adopted by TM:PE retain their exact car, occupants and
reserved bay to finish safely.

TM:PE's deferred-walking mode is proved once at the exact successful parking
adoption. Because `RequiresWalkingPathToTarget` is transient external planning
state, the later transfer endpoint validates the durable adopted transaction,
the same created occupant and intact vehicle membership, and the absence of a
replacement pedestrian path rather than incorrectly requiring that expired
TM:PE flag again.

Detailed parking-decision, routing, occupancy, animation, visual, placement and
persistence diagnostics are off by default. Enable `Advanced logs` in UPG's
Diagnostics options only while investigating a problem; garage behavior is
unchanged by the logging setting.

For building-attached garages, `Parking Management` uses the same native title
strip drag handle as Real Office Supplies and saves its offset from the live
vanilla host dialogue. Opening car-park x-ray captures and restores that host dialogue's
exact runtime panel subtype after Transport mode changes, and returns input to
the default selection tool so other buildings remain clickable. The entire
title strip is draggable, and the saved offset follows the host dialogue every
time that dialogue actually moves while remaining untouched during a drag; a
stationary host causes no competing panel-position writes.

## Compatibility And Limits

- Ploppable RICO Revisited and Realistic Population preview lifecycles are isolated from runtime entrance registration.
- Public Transport category registration avoids duplicate generated categories.
- The compact `P` tab binds only to the real Public Transport panel and leaves ongoing layout control to toolbar-layout mods.
- UPG remains enabled when the last loaded city still contains a garage and explains the safe `NUKE`, new-save, full-exit and disable sequence.
- No fallback may unload occupants while their car is in the carriageway.
- Placement and connectors remain conservative where native road/tunnel geometry cannot be proven safe.

## Source

This repository is the clean public source snapshot for Underground Parking
Garage v2.3.0. Build references target the standard macOS Cities: Skylines
managed assemblies and Harmony Workshop dependency.

## Copyright and intellectual property

Copyright © 2026 ScratchyBald. All rights reserved.

This repository is published for source transparency and reference only. No
licence is granted to copy, modify, compile, distribute, repackage, republish,
or incorporate its code or documentation into another project without prior
written permission, except as permitted by applicable law and GitHub's Terms of
Service.

**Underground Parking Garage** and its associated original branding identify a
ScratchyBald release. They may not be used in a way that falsely suggests
authorship, endorsement, or affiliation. Original concepts and functionality
are claimed only to the extent protected by applicable law.

Cities: Skylines and related marks are the property of their respective owners.
This independent community modification is not affiliated with or endorsed by
Colossal Order or Paradox Interactive.
