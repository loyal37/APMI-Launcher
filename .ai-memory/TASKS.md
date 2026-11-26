# APMI Launcher tasks

## Next

- [ ] Integrate the verified AP extractor and exporter into LoyalTools when the user requests the add-on work. The integration plan is in the APMI Package workspace.
- [ ] Verify cross-component bone mapping from same-frame AP palettes before implementing merged-skeleton export.

Latest scope on 2026-11-26: publish Launcher v0.2.0 and submit APMI Package source. Continue using the user's EFMI template structure and LoyalTools resource names.

## Pending verification

- [ ] Extend AP Core verification beyond one live unchanged component to edited meshes, more characters, motion and LOD transitions. [Known issues](KNOWN_ISSUES.md#limit-001--azur-promilia-model-compatibility)

## Completed

- [x] T-001 · 2026-11-26 · Add live MCP capture and the persisted injection switch. Build launcher 0.2.0, package the capture backend, and verify the UI, published worker, MCP protocol, process isolation, restart and real AP capture with a character visible. [Current state](CURRENT_STATE.md)
- [x] T-001b · 2026-11-26 · Expand to 34 shared tools. Verify reload, mod toggle, mesh export, recording/replay, texture fingerprinting and whole-buffer dump parity; fix module-readiness and included GUI notification lookup. Keep publication local.
- [x] T-003 · 2026-11-26 · Develop, verify and deploy APMI Core 0.2.0 at the original runtime path. Follow `main.ini` / `API.ini` conventions and verify live replacement draws on the current character.
- [x] T-002 · 2026-11-26 · Extract 15 components from the supplied dump and verify Blender import/export. Extract the live current-character component through MCP and verify its unchanged round trip.
- [x] T-004 · 2026-11-26 · Present the LoyalTools integration plan and update it for the verified Core API. The add-on source has only been read.
- [x] T-005 · 2026-11-26 · Deploy Core 0.2.1 and verify the ordinary EFMI template on the live current character. Confirm F11 and reloading after test mod removal.
- [x] T-006 · 2026-11-26 · Deploy Core 0.2.2 with the requested EFMI entry points and LoyalTools resource names. Core manages skinning for the exported mod. Confirm five live replacements, previous poses, F11 and cleanup.

- [x] 2026-11-26 · Initialize the seven-file project memory and its root entry points using the APMI source and existing evidence. [Session log](SESSION_LOG.md)
- [x] 2026-11-24 · Package and verify the v0.1.1 folder-based release with empty first-run paths. [Current state](CURRENT_STATE.md)
- [x] 2026-11-24 · Apply the whole-buffer hash workaround and verify the previously stalled login-screen dump. [Known issues](KNOWN_ISSUES.md#issue-001--xxmi-133-frame-analysis-stall)
- [x] 2026-11-24 · Verify package selection, worker behavior, game initialization and the in-game overlay. [README verification](../README.md#verification)
