# First compiled refuge candidate — failed acceptance, preserved

Built source be6b6a7375434fa5e76d5ef4e22511a113d08b8b, version 0.0.8-refuge.1. These images come from the actual interactive Windows player, not concept art or an editor render. The hidden-launch run rendered black and its images are not used here.

30 of 36 scripted checks passed. Entry/exit routes, wall and roof collisions, measured floor and ingress clearance, proximity rejection, rest/sleep/wake and finite storage passed. The clearance grid sampled too close to an intentionally blocked hearth stone; geometry was rejected, causing the dependent fire/zone/warming checks to fail closed. V2 expands the designated obstacle exclusion by the capsule radius and adds explicit blocked-collider diagnostics and editor preflight. A new player is required to accept the correction.

Visual review rejected the overly smooth/shiny rock primitives and narrow field of view. V2 uses faceted stone, procedural rock shading and a 65-degree player view. These are pending changes, not proof of improvement yet.

Recorded p95 frame time was 67.67ms at 1280x720 during this short scripted run. This fails a 33.3ms target if applied; no sustained or uncontented performance acceptance is claimed. The hidden run's 17.2ms excluded actual visible rendering and must not be presented as gameplay performance.
