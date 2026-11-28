# LC Mothballing (Decommission / Recommission) — Design

## Intent

Players often build a launch complex for one program and then have no use for it for years. Examples: the Saturn V-class LC after crewed lunar landings, until a crewed Mars program needs it again; or an early small LC that later becomes useful for cheap LEO launches. Today the only options are paying full upkeep indefinitely or dismantling the LC and rebuilding from scratch later.

Mothballing adds a middle path: decommission the LC to shed upkeep, accept that its engineering competence and physical state degrade over time, and later recommission it through a construction project that is always cheaper than building new.

## Goals

- Player can decommission (mothball) a pad LC and later recommission it.
- Upkeep of a mothballed LC falls off quickly (not instantly) toward 0.
- Efficiency degrades more slowly, on a separate curve. Reactivation cost mirrors the upkeep curve (rises as upkeep falls), so players can read one from the other (user decision, 2026-11-27).
- Reactivation is a construction project (funds + build time), like building or modifying an LC.
- Reactivation cost grows with time mothballed, from near 0 to an asymptote of pad cost + resource-handling cost. The VAB/dimension cost is never charged again, so reactivation is always cheaper than a new LC of the same stats.

## Non-goals

- Partial mothballing (some pads only).
- Modifying an LC while mothballed. Recommission first, then modify.
- Retroactive changes to existing LCs in saves beyond defaulting to "not mothballed".

## Anti-goals

- Efficiency must never drop to 0 by itself. It decays toward the current floor (`LCEfficiency.MinEfficiency`, which is tech-dependent) and never below it.
- Mothballing must never degrade an active LC. If the mothballed LC shares an `LCEfficiency` pool with any non-mothballed LC, that pool does not decay.
- Reactivation must never cost more than building a new LC with the same stats.

## Behaviour

### Decommissioning

- Available for operational pad LCs and the hangar (user decision, 2026-11-28). The hangar has no efficiency pool, so only upkeep and reactivation cost apply; its reactivation cost is GSE (resources) only and may be 0, in which case it recommissions instantly.
- Blocked while any vessel is rolling out, on the pad, or rolling back, and while the LC has anything in progress: vessels in the integration queue (`BuildList`), LC construction/modify, pad construction, reconditioning, airlaunch prep, vessel repairs.
- Allowed with vessels in storage (`Warehouse`). They stay there and cannot be launched or edited until the LC is recommissioned. (Note: this differs from `CanModifyButton`, which requires an empty warehouse.)
- All of the LC's engineers return to the KSC's unassigned pool.
- Instant and free. *(Builder's call: confirmation dialog, consistent with Dismantle.)*
- Record the UT of mothballing on the LC (persisted).
- A mothballed LC behaves as non-operational for selection, editor, building and launching. Implementation choice is left to the builder: either reuse `IsOperational = false` plus a new `IsMothballed` flag, or a state enum. Note that `MaintenanceHandler.LCUpkeep` and several UI paths currently assume "non-operational = under construction".

### The two curves

Let `t` = the LC's accumulated dormant time (days). It is time spent mothballed, excluding time paused by a running reactivation project (see Recommissioning). Both curves are Weibull-shaped.

1. **Upkeep curve (fast):**
   `upkeep(t) = fullUpkeep * exp(-(t / λu)^ku)`
   `fullUpkeep` is the LC's normal upkeep (the existing `LCUpkeep` formula for its stats and pad count).

2. **Degradation curve (slow):**
   `D(t) = 1 - exp(-(t / λd)^kd)`, ranging 0 to 1.
   - Efficiency: `eff(t) = floor + (eff0 - floor) * (1 - D(t))`, where `eff0` is the pool's efficiency at the moment decay started and `floor = LCEfficiency.MinEfficiency` (re-read live, so tech upgrades raise it).
   - Reactivation cost: `cost(t) = (padCost * (1 + (n - 1) * AdditionalPadCostMult) + resCost) * lerp(minFrac, 1, 1 - upkeepFactor(t))`, i.e. it follows the fast upkeep curve, not D, using `LCData.GetCostStats` outputs for the LC's current stats. Here `n` is the number of operational pads at mothballing. `costVAB` is excluded.
   - All pads are reactivated together, as the user decided. Extra pads are priced the same way new-LC construction prices them (`AdditionalPadCostMult`, currently 0.5). That keeps reactivation at or below the cost of building the same LC and its pads new. *(Pad pricing is my call; "all pads" is the user's.)*

Parameters `λu, ku, λd, kd, minFrac` live in `SpaceCenterSettings` (`GameData/RP-1/SpaceCenterSettings.cfg`) so they can be tuned without recompiling. Starting values (my call, to be tuned in playtesting):
- `λu = 90` days, `ku = 1.5`: upkeep is roughly halved after 2.5 months and nearly gone after 6.
- `λd = 1826` days (5 years), `kd = 2`: little loss in the first year, about 63% degraded at 5 years.
- `minFrac = 0.1`.

### Efficiency pools

Efficiency lives on `LCEfficiency` pools shared by LCs with similar stats (see `LCEfficiency.cs`, `GetOrCreateEfficiencyForLC`, `Formula.GetLCCloseness`).

- A pool decays only while every LC in it is mothballed with no reactivation project running.
- Each pool persists its own `eff0` and an accumulated decay time `tp`. `D(tp)` drives its efficiency.
- When a pool goes from "has an active member" to "all mothballed", snapshot `eff0` = current efficiency and set `tp` = 0.
- Starting a reactivation on any member pauses `tp`. Cancelling it resumes `tp` from the paused value, and `eff0` is unchanged. So start-then-cancel cannot reset decay. *(My call.)*
- When a reactivation completes, or a non-mothballed LC joins the pool, the pool stops decaying and keeps its current (decayed) value. Its `eff0`/`tp` are discarded.
- Pools in the all-mothballed state do not receive distributed efficiency gains from other pools (`ReceiveDistributedEfficiency`), so the snapshot formula stays valid. *(My call.)*
- How decay is applied (on each `SpaceCenterManagement` update tick, or lazily when efficiency is read) is the builder's call. Either way, save/load must give the same result as continuous play.

### Recommissioning

- Player starts it from the LC list on a mothballed LC.
- Creates an `LCConstructionProject`-style project (a new flag such as `isReactivation`, or a subclass) with cost from the formula above at the moment the project starts. BP/time derive from cost the same way new-LC construction does.
- While the reactivation project runs, the LC stays non-operational and its dormant clock `t` is paused, as is the pool's `tp` (see above). *(My call: no further decay once work starts.)*
- Upkeep during reactivation ramps linearly from the frozen mothballed value up to full upkeep as the project progresses: `lerp(upkeep(t_frozen), fullUpkeep, progress / BP)`. This is a new branch in `MaintenanceHandler.LCUpkeep(LaunchComplex)`, distinct from the new-build ramp and from `isModify`'s factor 1. *(My call.)*
- On completion: all pads and the LC become operational, the mothball state and dormant clock are cleared, and efficiency stays at its decayed value and then grows normally once engineers are assigned. Engineers are not auto-reassigned.
- On cancel: the LC returns to mothballed, and `t` resumes from the paused value. Refund behaviour follows existing LC construction cancellation.
- Career log: fire events for mothball and recommission, and record them in `CareerLog` alongside the existing LC construction/dismantle events.

## UI

- Add "Mothball" and "Recommission" buttons in the LC section of `GUI_BuildList.cs`, next to Modify/Dismantle, with tooltips explaining why the button is disabled when blocked.
- For a mothballed LC show: time mothballed, current upkeep, current efficiency, and the projected reactivation cost.
- Layout details are the builder's call. Match the existing IMGUI style.

## Constraints

- KSP 1.12 / Unity IMGUI, existing RP-1 code conventions, `[Persistent]` ConfigNode persistence.
- Saves without the new fields must load as "not mothballed". Bump `SpaceCenterManagement.VERSION` only if needed.
- Upkeep totals in `MaintenanceHandler` must use the curved upkeep for mothballed LCs.
- Forward budget projections (the maintenance UI, the extrapolation around `SpaceCenterManagement` ~line 1000 using `UpkeepPerDayForDisplay`, and the Programs/budget projections) must model the future decay, as the user decided. Evaluate (integrate) the upkeep curve over the projected period, not today's rate held constant. This needs a time parameter on the affected projection paths. The builder should find every caller that extrapolates upkeep. Mothballed LCs with a running reactivation should project the ramp to full upkeep.

## Left to the builder

- State representation (flag vs enum) and where the decay is evaluated.
- Exact UI layout and wording.
- Whether reactivation is a flag on `LCConstructionProject` or a new subclass.
- Tests/self-checks: at minimum, unit-checkable pure functions for both curves and the reactivation cost, and asserts for the "never below floor" and "never more than new" invariants.
