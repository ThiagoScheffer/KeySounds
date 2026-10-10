# Documentation

Start with the [project README](../README.md) for features, installation, development commands, and an engineering walkthrough.

## Current design and user guidance

| Document | Purpose |
| --- | --- |
| [Current state and continuation](../.sdlc/PROJECT-STATE.md) | Implemented phases, latest checks, remaining acceptance work, and next build slice. |
| [System design / SDD](../.sdlc/design/SYSTEM-DESIGN.md) | Architecture implemented in the current source, runtime flows, contracts, persistence, and trade-offs. |
| [Sound-pack format](soundpack-format.md) | Local manifest structure and validation rules. |
| [Application icon](application-icon.md) | Source artwork, Windows icon sizes, integration, and generation prompt. |
| [Custom sound-pack walkthrough](audiopack1-md.md) | Manual folder/manifest/WAV creation and profile assignment examples. |
| [Privacy](privacy.md) | Local data handling and diagnostic report contents. |
| [Troubleshooting](troubleshooting.md) | Input, audio, profile, and startup troubleshooting. |
| [Packaging](packaging.md) | Portable builds, automated releases, upgrade, startup, and uninstall behavior. |
| [Release checklist](release-checklist.md) | Automated and manual Windows release verification. |
| [Release measurements](release-measurements.md) | Recorded measurements from earlier release candidates; remeasure new builds. |
| [Repository readiness audit](repository-audit.md) | Repository gaps addressed by this cleanup and remaining verification limits. |

## Historical proposals and phase records

These documents record development intent and reviews at particular points in time. They can contain proposed architecture or future requirements; use the current SDD and source for the implemented behavior.

- [Initial architecture proposal](mainprojdesign.md)
- [Phase 1](phase1.md) and [fixes](phase1-fixes.md)
- [Phase 2](phase2.md), [review](phase2-review.md), and [2B](phase2b.md), [2C](phase2c.md), [2D](phase2d.md), [2E](phase2e.md), [2F](phase2f.md)
- [Phase 3](phase3.md) and [review](phase3-review.md)
- [Legacy phase4 file](phase4.md), whose contents describe Phase 2D audio-device selection
