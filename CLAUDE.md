# SFT Windows — Release Guidelines

## Incremental Releases
This project follows an incremental release model to ensure steady progress.

- **Versioning**: Always increment the version in `main.py` for every change.
- **Git Tags**: Every push to `main` must be tagged with the corresponding version (e.g., `v1.2.1`).
- **Automation**: New tags trigger the build workflow to produce a fresh `.exe`.

## UI Standards
- **Theme**: Dark Mode.
- **Visuals**: Glassmorphism with semi-transparent containers and blurring.
- **Components**: Rounded icons and clear visual feedback for sync states.
