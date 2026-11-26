# APMI Launcher working agreement

- At the start of a resumed session, read [Current state](.ai-memory/CURRENT_STATE.md) and [Tasks](.ai-memory/TASKS.md). Use the [Project overview](.ai-memory/PROJECT_OVERVIEW.md) to find code and the other memory documents.
- At the end of substantive work, refresh the current state and tasks and add a dated entry to the [Session log](.ai-memory/SESSION_LOG.md). Keep the current state focused on the latest baseline. Update [Technical decisions](.ai-memory/DECISIONS.md) or [Known issues](.ai-memory/KNOWN_ISSUES.md) when the work changes them.
- Record the version, scenario, result and evidence for checks that were performed. Name the specific result observed, such as worker initialization, fixture rendering, an in-game overlay or a completed frame dump. Date earlier results when citing them.
- Use private package copies for worker tests. Launch preparation edits `d3dx.ini` in the selected package and archives its previous runtime log. Record diagnostic paths relative to the repository and keep generated output and machine-specific settings in the existing ignored directories.
- Exercise worker changes through the published executable with the integration suite. Check the WebView2 window for UI or native picker changes. Build and test entry points are in the [README](README.md).
