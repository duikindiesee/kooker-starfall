# Exploring the CityLife island

The player starts at an island vista. Click **Automatic tour** in the lower-right corner for a roughly 30-second overview, flight and walking route, ending with a close ground-detail view. It saves four images and performance evidence and leaves the player open. No keyboard shortcut is needed. Use the buttons along the bottom or these controls to explore yourself:

| Control | Behaviour |
| --- | --- |
| WASD | Move relative to the current view; walking stays on the terrain |
| Hold right mouse | Look; release returns the pointer for buttons |
| Shift | Faster flight or a running pace |
| Q / E | Descend / ascend in flight |
| Mouse wheel | Adjust flight speed, or zoom while orbiting |
| F | Switch between walking and flight |
| O | Orbit the terrain ahead; WASD pans the focus |
| Home | Whole-island vista |
| 1 / 2 / 3 | Landing coast / highlands / neighbourhood reserve |
| 4 | Close view of an existing dry grass tuft near the landing |
| N | Day / night lighting |
| H | Hide / show the overlay |
| F12 | Save a player screenshot |
| F9 | Record a manual exploration benchmark |
| Shift + F9 | Run the repeatable overview/flight/walking route in the visible player |
| Escape | Release the pointer |

Walking uses 1.85 m eye height, 6 m/s movement and 14 m/s running. It stops at water and steep slopes. Switching to walking from water or steep terrain returns to the landing point. Flight keeps the camera above the terrain and allows exploration over the surrounding sea. The reserve view identifies a relatively flat area for future work; it does not establish ownership, plots, roads or a building permission.

Screenshots and benchmark JSON go into `Application.persistentDataPath/Evidence`. The player log prints each absolute output path with `CITYLIFE_SCREENSHOT` or `CITYLIFE_BENCHMARK`.

## Repeatable player evidence

Start an isolated copy of the built Windows player with `-citylifeSmoke -citylifeEvidence <absolute-directory>`. This waits five seconds for warmup, then records eight seconds each of overview, flight and walking through the same view and movement methods used by the controls. A final unmeasured close view frames an existing grass tuft. Its URP `SingleCameraRequest` renders the scene into a 1600 × 900 GPU render texture every `LateUpdate`, after the island renderer prepares current terrain and flora visibility. The camera's normal rendering path is disabled to avoid drawing the scene twice. Each phase reads an image from that texture; it does not capture the desktop. These offscreen scene images exclude the HUD and do not validate native window presentation.

The smoke process does not poll keyboard/mouse controls or change cursor lock/visibility. It writes JSON and exits with code 0 when the evidence checks pass, 3 if an evidence check fails, or 4 if the render texture/request is unsupported. Run without `-batchmode` and without `-nographics`, since the route requires a functioning graphics device and end-of-frame image readback. A blank image fails validation. A user-triggered **Automatic tour** or Shift+F9 continues to use the normal player framebuffer and leaves the application open.

The JSON identifies `renderPath` and `captureScope`, measured frame times and per-phase mean, median, p95, p99 and worst frame time; render resolution, GPU, graphics API and frame pacing configuration; camera positions and distance travelled; terrain penetration and blocked walking steps; streaming counters; and image paths. For an offscreen run it also records total/per-phase submitted render frames. The raw frame sample array is retained in chronological order. The pass flag checks sufficient samples, actual movement during flight/walking, camera floor safety, nonuniform image content, per-phase offscreen render coverage and absence of observed runtime errors. Black frames fail even if a PNG was written. It does **not** assert visual quality, GPU headroom, native input coverage or completed gameplay. Images and real input still need review, and offscreen timings must not be presented as verified desktop-player performance.

Automated rendering does not verify desktop focus or native input. `CITYLIFE_INPUT` log records distinguish keyboard, mouse and GUI events received by the player from the automatic route. Discrete movement start/stop events include the camera pose and travelled distance. These complement captured images; they do not replace permitted inspection of the real window. Escape releases the pointer during manual exploration. Desktop interaction and visible-player tests are separate from isolated smoke execution.

F9 instead records 24 seconds of normal manual exploration after five seconds of warmup. The overlay's live mean and p95 are a rolling sample of up to 360 frames after startup warmup, not an instantaneous FPS claim. Benchmarks include frame pacing limits and all active local workloads; compare equivalent resolution and conditions.
