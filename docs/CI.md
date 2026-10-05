# CI, public repository and delivery safeguards

## The existing Kooker workflow reference

The spoken workflow reference was resolved from CityLife's actual repository, not from a guessed repository name. A fresh fetch on 2026-09-09 left browser `main` at `b71307023aea600da27aa3eada6cdcce7ba758ed`. Its [Docker workflow](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/.github/workflows/docker.yml) calls **`duikindiesee/kooker-workflows`** for `node-version-bump.yml`, `ghcr-retention.yml` and `gitops-sync.yml`.

Authenticated read-only discovery confirmed that [kooker-workflows](https://github.com/duikindiesee/kooker-workflows/tree/cb96b1ec13872ea96265e737d3e4f0e874b02d5c) is public, with `main` at `cb96b1ec13872ea96265e737d3e4f0e874b02d5c`. The inspected catalogue contains Maven/Android/Node versioning, container retention, GitOps, Java baseline, database and related service checks. It has no Unity editor/player build workflow. The Node version-bump and container/GitOps deployment jobs do not apply to this first Unity island PR. They are not copied with inherited credentials or write permissions.

The browser's [secret-scan workflow](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/.github/workflows/secret-scan.yml) supplies the applicable security pattern: pinned open-source Gitleaks CLI, verified release checksum, full reachable history, fully redacted logs, no findings artifact, and read-only Actions permissions. Its hardware WebGL runner is a separate browser lane; it is not assumed to be a licensed Unity runner.

## Checks for main and explicit dispatch

`CI` runs on GitHub-hosted Ubuntu 24.04 for pull requests to `main`, pushes to `main`, and explicit dispatch. It does not use a private runner, repository credentials or a Unity licence.

The Starfall WIP is stacked on `codex/island-foundation`, so its pull request does **not** automatically trigger either workflow. For each published Starfall commit, dispatch both `ci.yml` and `secret-scan.yml` with `--ref codex/kokerboom-cosmic-desert`, then verify every run's `head_sha` equals that published commit and all jobs succeed. Repeat after every later push; previous foundation or Starfall results do not cover a new head. Expanding automatic pull-request coverage to all base branches remains an explicit automation improvement. Existing action pins, jobs, schedules and read-only permissions are retained.

| Check | What it establishes | What it does not establish |
| --- | --- | --- |
| Unity project and asset safeguards | Pinned editor/package settings; lockfile agreement; complete and unique `.meta` GUID inventory; scene inclusion; world/oracle identity; public-file boundary; imported art hashes, licence and notices | C# compilation, shader compilation, player performance or visual quality |
| Original CityLife source oracle | Actual TypeScript execution at the pinned source commit; exact equality of every recorded number, settings value and complete field hash | C# execution or identical production/player saves |
| PowerShell parsing | All checked-in build/package/check scripts parse through PowerShell's AST parser | Execution of the licensed build or player |
| Actionlint | GitHub workflow syntax and expressions, with a checksum-verified pinned executable | Branch-protection configuration or future external service availability |
| Package-guard canaries | Rejects archive checksum changes, path traversal, private state, Unity backup folders and duplicate Windows paths | A real player build; all fixture archives are temporary and explicitly synthetic |

Actions are pinned to full commits. Workflow tokens have only `contents: read`, checkout credentials are not persisted, executions have timeouts and superseded checks cancel. There is no `pull_request_target` code execution, deployment, automatic merge, or Unity secret exposed to fork code.

The source oracle is executed with the source lockfile and lifecycle scripts disabled. Windows source checkout hashes in the original reference manifest use CRLF. Linux uses LF. `check-oracle.py` permits only those explicit line-ending representations of the **same pinned Git blobs**, then compares the remaining JSON exactly. Numerical values and complete field hashes are never normalized or tolerated away.

## Full-history secret scan

`Secret scan` runs on PRs and `main`, manual dispatch and weekly. It requires `fetch-depth: 0`, then runs Gitleaks 8.30.1 with `git --log-opts=--all`. It also scans the present publishable file inventory, including untracked files during local pre-commit use. Every detected value is redacted. No findings report is uploaded. The Gitleaks CLI does not require the paid organisation key used by the separate Gitleaks Action.

The pinned release SHA-256 values are:

- Linux x64 archive: `551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb`.
- Windows x64 archive: `d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e`.

The script also creates an ephemeral synthetic credential and requires the scanner to reject it. The value is generated only in a temporary directory, never committed, never reported unredacted and removed afterward. Inline `gitleaks:allow` comments cannot suppress the scan. There is no broad project allowlist and no copied browser-history exception. A future false positive needs a reviewed exact fingerprint; a real exposure requires credential invalidation/rotation before repository cleanup. Deleting its current file alone is insufficient.

Local command:

```powershell
./tools/check-secrets.ps1
```

To scan the actual distributable, after packaging:

```powershell
./tools/check-secrets.ps1 -DistributionArchive Builds/Download/CityLife-Island-0.1.0-Windows.zip
```

The second command additionally validates the final ZIP against its packaging manifest and safely extracts only byte-verified entries into a private temporary directory. Absolute/escaping/duplicate/symlink entries are rejected; nothing is launched. It scans that directory with recursive archive detection disabled and removes it afterward. This checks what will ship, rather than Unity's excluded backup directory. Run the independent `check-package.py` first as a second integrity check. Gitleaks is a preventive detector, not proof that arbitrary binary content contains no secret.

The first real distribution scan found three generic-key matches in bundled Mono `machine.config` files and a scanner file-type problem: Gitleaks mistakes plain-text `Compat.browser` XML for Brotli. The matches are the `strongNames/pubTokenMapping` **public** key identifier records, not account credentials. Each complete file was byte-identical to its installed Unity 6000.6.0f1 vendor copy; the structure is also documented in [Mono's public configuration source](https://github.com/mono/mono/blob/main/data/net_2_0/machine.config). The scan now verifies these whole-file hashes before creating exactly three temporary `file:generic-api-key:line` fingerprints:

| Distribution path | Exact line | Required complete-file SHA-256 |
| --- | --- | --- |
| `MonoBleedingEdge/etc/mono/2.0/machine.config` | 214 | `e91782a27fa39fc6c1d6ee8b08529f5d35052310d0006034b878eb04b8f2af30` |
| `MonoBleedingEdge/etc/mono/4.0/machine.config` | 231 | `e60aec2c5115d65b3acb3c55ea21576dbd770f579166c017125571e46ae560ed` |
| `MonoBleedingEdge/etc/mono/4.5/machine.config` | 234 | `ee950004b576fb28dc85f4b0435ed04bf96612de2e8b53be84d07afe85a0de6c` |

No rule or directory is allowlisted, and these distribution-only exceptions never affect repository/history scans. Modified vendor bytes fail before an exception is created; a canary exercises that refusal. The three `Compat.browser` files are separately read as UTF-8 text and scanned through Gitleaks stdin, so disabling recursive detection does not leave the known XML files unread. Other findings remain blocking. The initial failed scan is not represented as a pass.

## Imported art and packaging

Every committed texture, model, audio file or font under `Assets/` must have a matching `provenance.json` record. Records include public source/download/licence URLs, publisher/authors, exact local path, SHA-256, byte size, modifications and a committed licence notice. This first milestone admits only explicitly verified `CC0-1.0` imports. Other terms require a deliberate policy/code review, not changing a string to make CI green. Binary art with no record, changed bytes, a missing notice or an escaping path fails the check. Procedural C# geometry is source code; milestone screenshots outside `Assets/` are reviewed evidence, not imported art. Catalogue preview images inside `Assets/` still require provenance even when no model is imported.

`tools/package.ps1` packages the actual build, includes portable controls and third-party notices, excludes Unity's `BackUpThisFolder_ButDontShipItWithYourGame` directory, and verifies the ZIP against the original distributable files before writing an entry-by-entry manifest. The independent Python guard validates the archive hash and every entry hash without extracting or launching the game:

```powershell
python tools/check-project.py --self-test
./tools/check-powershell.ps1
python tools/check-package.py --self-test
python tools/check-package.py --archive Builds/Download/CityLife-Island-0.1.0-Windows.zip
```

Use an available Python 3.11+ executable in place of `python` where necessary. Raw local logs, editor caches, signing/licence containers, private databases, player saves and runtime archives do not belong in public commits or the player ZIP. Do not add broad scanner exceptions for these files.

## The genuine Unity CI boundary

Read-only GitHub checks on 2026-09-09 returned **zero registered runners** for `citylife-unity` and **no repository-level `UNITY_*` secrets**. This does not establish the availability or policy of organisation-level credentials. The developer machine's licensed editor is local; its account, licence files and password have not been exported.

Unity editor/player builds require an appropriately activated editor. [GameCI's activation documentation](https://game.ci/docs/github/activation/) describes distinct personal/professional/license-server setups. No license server address, serial, reusable license file, account credential or runner authorization has been invented for this repository. Consequently the hosted green checks are explicitly **static, provenance and TypeScript-oracle checks**, not a fake green Unity build job.

The existing licensed local command remains required for a release candidate:

```powershell
./tools/build.ps1 -BuildName Candidate
./tools/package.ps1 -BuildName Candidate
```

`CityLifeBuild.BuildWindows` runs `IslandValidation.Run` before building. Reviewable evidence must record the exact commit, Unity version, validation result, package checksum and actual player captures/measurements. Source files or a successful ZIP scan cannot replace user-visible validation. Full hosted Unity execution can be added after the owner provides an authorized licence/runner arrangement; untrusted fork code must never receive those credentials or execute on the personal workstation automatically.

Maintainers should require `Gitleaks full-history scan`, `Unity project and asset safeguards` and `Original CityLife source oracle` before merging. This PR provides the workflows and review template; it does not claim that GitHub branch protection has already been configured. The PR template separately requires honest licensed-build and real-player evidence.

## Validation record for the first CI slice

| Claim | Status | Evidence | Remaining gap |
| --- | --- | --- | --- |
| Existing Kooker reference resolved | Verified | Fresh source fetch plus authenticated workflow listing at the commits above | No Unity reusable workflow exists in the inspected catalogue |
| Local Gitleaks history and publishable-file checks | Passed during implementation | Pinned 8.30.1 CLI; original single initialization commit and then-current publishable files; synthetic canary rejected | Rerun after final implementation commit and against final distributable |
| Source-oracle comparator | Passed locally | `check-oracle.py --self-test`; exact values and same-blob line-ending provenance, altered-height/hash canaries rejected | Hosted PR execution |
| Workflow syntax and PowerShell parsing | Passed locally | Checksum-verified Actionlint 1.7.12; PowerShell AST parser | Hosted PR execution |
| Project/art metadata | Passed locally | `check-project.py --self-test`: 142 then-publishable files, 43 unique Unity GUIDs, 3 CC0 records; invalid-art/private-path canaries rejected | Rerun after final staging; hosted PR execution |
| Final distribution ZIP | Passed locally | Independent integrity and distribution scans: 186 entries; SHA-256 `f77dd9b6d1150ccd27705a19906c9d1e8f6e7fdcf114a902968a9994e3b7b2e9`; sanitized [repository-checks.json](../evidence/verified/repository-checks.json) | A repack changes the checksum and requires both checks again |
| Hosted Unity editor/player CI | Not configured | No authorized runner/license arrangement verified | Owner-authorized Unity CI setup; no credentials requested or exported by this slice |
