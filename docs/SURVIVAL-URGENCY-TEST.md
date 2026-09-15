# Ordinary survival urgency: test interpretation

Source inspection on 15 September distinguishes missing outcome evidence from
failure under urgent hunger. `FoodState` starts at energy 6500 and hydration
5500. Each ordinary FoodModel second spends 2 energy while idle or 5 while
active, and 4 or 6 water respectively. FixedStep advances one such second per
50 fixed steps; wall-clock duration alone is not proof of simulated duration.

Assuming 329 simulated seconds, no food/drink and continuously active movement,
energy cannot fall below 4855 and water cannot fall below 3526. This is an
arithmetic bound from current rules, not a recovered run13 state observation.
The raw run13 decision records lack those state fields; its last persisted
snapshot was stale. Do not call that run proof of ignoring imminent starvation.
It did fail to demonstrate the requested gather/eat/drink progression.

| Observation | Earliest continuously active time from fresh state | Interpretation |
|---|---|---|
| Water below 2000 | 584 simulated seconds | A longer ordinary run is needed to reach this illustrative low-water level |
| Energy below 2000 | 901 simulated seconds | Five minutes cannot test this illustrative low-energy level |
| Death | Separate prolonged deficits and health rules | Do not conflate these levels with death thresholds or accelerated diagnostics |

Next ordinary test should retain simulation time, energy, water, stomach,
inventory, observed-knowledge flags and offered actions at request issuance.
Bind the model request hash and admitted action to those observations; admission
may occur later, so do not substitute later state for the supplied context.
Use a sufficiently long bounded ordinary run and preserve periodic/quit scoped
checkpoints. A model can still behave poorly without urgent needs, but report
that as repetitive exploration, not unobserved starvation.

If an authorized low-health hint is used, label the intervention and retain
assisted and independent outcomes separately. Do not force a meal, disclose
undiscovered resource locations, pregrant food benefits, or shorten physiology
just to make the ordinary test pass. The final milestone still requires real
food and water outcomes, scoped reload, readable footage and visual acceptance.
