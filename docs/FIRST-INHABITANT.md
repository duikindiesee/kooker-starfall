# First inhabitant: free Quaternius Standard

This is a separate Starfall character courtyard, version 0.0.3-preview.1. One adult male walks around a solid obstacle, takes a crystal and places it on another plinth. It uses authored goals, collision queries and bounded grid search. It does not learn. The original worlds, R06/R19 tree previews and released ZIPs remain separate.

The executable starts a short scripted expedition. WASD takes manual control; hold the right mouse button to turn the following camera; R pauses/resumes an unfinished expedition; Escape stops the expedition and releases the pointer. The pointer starts free. Close the window to exit. The completed expedition stays completed until the preview is restarted.

## Verified free contents

Downloaded directly through the publishers' free Standard download flow on 12 September 2026. Both archives contain explicit CC0 notices. No paid tier, subscription or Source blend files were acquired.

| Source | Actual contents | Selection and limitation |
|---|---|---|
| [Universal Base Characters](https://quaternius.itch.io/universal-base-characters) | Adult Superhero male and female; hairstyles and textures | Selected Superhero male, 8,477 vertices including eyes and eyebrows. Regular and Teen bodies are absent from Standard. |
| [Universal Animation Library](https://quaternius.itch.io/universal-animation-library) | 42 action clips plus A_TPose; Unity FBX and GLB; root-motion and no-root-motion variants | Selected the no-root-motion FBX. All 43 Unity takes import as humanoid, 30fps. Only idle, walk and Interact are exercised here. |

Walking, crouching, sitting, swimming and several gestures are included. Crawling is absent. Hit_Head is a reaction clip, not independent head aiming. Arm gestures are full-body clips, not an independent arm controller. Animation data does not provide ragdoll physics, bot learning or a gameplay controller.

The private local library preserves the original archives and notices. [The consumer revision record](CHARACTER-ASSET-REVISION.json) pins the exact library commit, import bundle and selected file hashes. Binary uploads are pending quota review; remote retrieval is not claimed.

## Implementation and verification

The body and animation FBX each use a valid Humanoid avatar. Feet-based Y rooting and a 180-degree visual orientation correction align the imported character with the controller. Original vendor model and texture bytes are unchanged. A CharacterController supplies collision and gravity. The camera follows the character and uses a sphere cast to shorten its distance before an obstacle.

The roamer follows two authored goals on a finite one-metre grid. Real capsule queries exclude occupied cells and blocked edges. Reaching each stand within the permitted distance triggers the free Interact clip. The actual crystal changes parent from its source to the mapped right hand, then to the destination socket. Observation/action/outcome entries expose this sequence on the preview HUD. Stalls stop movement after two seconds. There is no external service, saved-world access, neural policy or learned preference.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Body and clips import | Verified in Unity 6000.6.0f1 | Valid human avatars; 43 humanoid takes at 30fps | Remaining clips have not been visually exercised |
| Visible walking and collision | Automated standalone proof passed | Actual rendered frames, deformed vertex grounding, pose changes, displacement, wall contacts and gravity in the runtime report | Native keyboard/mouse acceptance |
| Scripted collect and deliver | Automated standalone proof passed | Two collision-aware routes; crystal source/hand/destination state; inspectable journal | Other layouts and free exploration |
| Existing releases and worlds | Kept separate | New checkout and unique build directory; release hash comparison recorded with final evidence | No new acceptance claim for earlier builds |
| Learning, Stone Age appearance and broader world | Not implemented here | Explicit scope boundary | Future milestones |

The offscreen player test fixes simulation delta to 1/30 second for repeatability. Its wall-clock duration is not an FPS benchmark. Engine warnings about stripped, unused depth-of-field/Panini effects and Forward+ GPU resident drawing do not establish a runtime error or performance acceptance.

## One focused critique

The gain is a visible, retargeted adult with functioning locomotion and an observable completed task. Grounding is measured on the deformed body, not inferred from its capsule. Imported materials and skinning remain readable without proprietary shaders.

The current Superhero proportions are very muscular; baldness and briefs do not establish the intended Stone Age identity. The stage is a plain testing courtyard, and its lighting/shadows need an art pass when the inhabitant enters the actual landscape. The carry gesture has no hand IK or finger grip matching, and grid turns are mechanical. These are recorded limitations, not reasons for another open-ended polish loop.

The next bounded step is native control review, then a small Blender derivative with human proportions/hair and fitted hide clothing on the same skeleton. A later local behavior layer can expose sensed object, motivation, selected action and outcome. Genuine adaptation would require retained memory and a repeatable before/after result against a fixed-policy baseline. Shelter, social learning, multiple inhabitants, advanced physics and the living sea remain separate work.

## Rebuild without touching earlier releases

On Windows with pinned Unity 6000.6.0f1, commit the reviewed checkout and run:

~~~powershell
.\tools\build-character.ps1 -Smoke
~~~

The script creates a unique player directory and retained local evidence, restores project rendering/player settings, and runs only an opt-in hidden smoke player. It does not launch a visible window. Fresh imports can be configured with CityLife.World.Editor.CharacterAssetImport.Run; the checked-in metadata already contains the selected settings.
