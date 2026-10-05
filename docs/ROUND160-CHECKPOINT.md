# Round 160 verified candidate checkpoint

Source: `dff467c971da5a289f3aab1e7ce2ed0b80c0687d`.
Player: `KookerStarfallIntegrated-0.0.10-canyon.1-20260914-204508`.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Compiled integrated automated checks | Passed | `evidence/local/combined/runtime-27/runtime/integrated-runtime.json`; three deliveries, seven events, no failed checks | Physical user input and play acceptance remain separate |
| Real scene reflection | Visibly present | Runtime 27 `01h-scene-reflection-on.png` and `01i-offshore-islands-sea-vista.png` show reflected canyon, giant and islands | Water colour, caustic scale and depth transition remain unlike reference |
| Full build identity | Matched | `tools/check-integrated-build.ps1` succeeded against round 160 manifest and runtime 27 launch receipt | Does not approve release |
| Local package | Bytes verified | 187 entries; 319144822 bytes; ZIP checksum below | No services/model weights; not final accepted package |

Full player content SHA256:
`7161e4ab7f299c900e348e4ed17f149047e73c36812b9f0a01c28955ec28a1f7`

ZIP SHA256:
`877ecbbf85bdaec091b03c555446d9c10cc7d512c075f61a8a44c33cd836cf8a`

ZIP location: `Builds/Download/KookerStarfallIntegrated-0.0.10-canyon.1-20260914-204508-Windows.zip`.
Its included README is `docs/RELEASE-CANDIDATE-ROUND160.txt`.

## Independent visual review

The sea view now has recognizable mirrored silhouettes; the earlier broad
radial/vertical artefacts are no longer the dominant defect. The transition
from cyan foreground to deep navy remains conspicuously abrupt. Islands are
visible but block-like. The shallow view still has pale green water, oversized
loop caustics and overly geometric bed rocks. Do not assign a panorama pass
from reflection success. Preserve this candidate while making controlled
comparisons, and rerun relevant gameplay checks after scene changes.

The 7.3/10 visual goal, user acceptance, protected review and narrated walkthrough
remain unfinished. This record is a recovery checkpoint, not a release receipt.
