CITYLIFE ISLAND - FOUNDATION PREVIEW 0.1.0

Extract the whole archive into a folder, then open CityLife.exe.
Keep CityLife_Data, MonoBleedingEdge and the DLLs beside it.
Unity does not need to be installed to play this Windows build.

This preview is a new 4.096km-wide island using seed 4242. It is an offline
terrain explorer. Roads, plots, houses, Kooker HQ, bots and multiplayer
remain later milestones. It does not import or replace browser saves.

CONTROLS
WASD move. Hold right mouse to look, release to use the pointer.
Escape releases the pointer. Shift moves faster. Q/E changes flight height.
Scroll changes flight speed or orbit zoom. F switches walk/fly. O orbits.
Home shows the whole island. 1/2/3 visit coast/highlands/open reserve.
4 shows an existing dry grass tuft close up at eye height.
N changes day/night. H hides the HUD. F12 saves a screenshot.
Automatic tour runs roughly 30 seconds and leaves the player open.
F9 measures manual exploration; Shift+F9 runs the same automatic tour.

EVIDENCE STATUS
The world/save tests passed. An earlier native player rendered the island
and responded to its Night button. This candidate passed an offscreen
rendered flight/walking route. Native keyboard/mouse controls, candidate
HUD presentation and native frame timing still need interactive acceptance.
Detailed reports and the visual catalogue are in the source repository.

Screenshots and benchmarks save beneath the user's LocalLow/CityLife/
CityLife Island/Evidence folder. Local world data is kept separately in
Worlds/<world-definition-fingerprint>.

Source: https://github.com/duikindiesee/citylife-unity
