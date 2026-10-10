# Repository readiness audit

Reviewed 10 October 2026. This pass covers repository hygiene, documented architecture, build/test commands, and release automation; it does not replace a full application security or hardware audit.

## Findings and changes

| Initial state | Resolution |
| --- | --- |
| No CI/CD workflow | Windows Release build and xUnit checks for PRs/main; portable GitHub Releases on version tags. |
| No `.gitignore`; 954 generated files/logs tracked | Ignore .NET/IDE/test/publish/log output and remove generated directories and logs from commit history. |
| An informative but untracked README | Preserve and refine the draft; add usage, CI/CD, current design links, contribution guidance, and license. |
| No license file despite open-source intent | Add MIT with the repository author's name. |
| Earlier design proposes separate projects | Add an SDD matching the actual app/test projects and mark the initial proposal historical. |
| Phase documents mixed with current guidance | Add a documentation index identifying current instructions and historical records. |
| Publish script could continue after a native-command failure | Check the publish exit code and executable, reject test assemblies, validate the deletion target, and package license/privacy notices. |
| Privacy wording overstated arbitrary error-message sanitization | Document its actual username replacement and length limit. |

The initial 11-commit Git object store occupied approximately 251 MiB. Tracked output included root build directories, the test project's build directories, a portable runtime distribution, and six logs. Source audio assets and manifests remain versioned intentionally.

## Verification evidence

- Gitleaks 8.30.1 scanned all 11 pre-cleanup commits, the working tree, staged changes, and the rewritten 12-commit `main` history with redacted reporting; no credential matches were reported. Binary output is not evidence that no secrets could ever exist; scan results apply to the tool's detectable content and rules.
- Package restore initially failed in the restricted environment. With authorized network access, the existing Release test suite completed: **97 passed, 0 failed, 0 skipped**.
- A fresh source-only checkout also restored, built, and passed all **97 tests**, confirming that the app does not depend on the removed tracked build output. The explicit Release build had zero warnings and errors.
- Actionlint 1.7.12 accepted the workflow. The PowerShell publish script passed syntax validation and published successfully. The portable ZIP contained 232 entries, including the executable, MIT license, and privacy notice, with no test assemblies; its size was 74,387,019 bytes.
- Local documentation links resolve. Ignore rules cover generated output, logs, test results, local settings, and signing keys while retaining source and audio assets.
- The history filter removed exactly **954 generated/log paths** and changed no remaining source files. Hosted workflow execution and a future tagged release provide the final integration checks for GitHub automation.

The rewrite preserves an external recovery backup. Replacing public `main` changes commit IDs; existing clones must resync or re-clone and should not merge old history into the cleaned branch.

## Remaining implementation and release gaps

- No installer, release signing, or validated single-file package.
- Windows is the supported runtime platform; Avalonia UI portability alone does not supply input/audio adapters for other platforms.
- Real audio latency, keyboard/device compatibility, tray behavior, startup registration, migration upgrades, and shutdown stress require the manual [release checklist](release-checklist.md).
- Directory separation is within one application assembly. It does not enforce dependency direction with separate projects.
- Error-report message sanitation is limited; review exported diagnostics before publication.

These are documented limits and future work, not claims of completed implementation.
