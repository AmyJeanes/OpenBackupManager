# Agent Guidance

OpenBackupManager is a cross-platform backup and two-way sync app. Our own sync engine plans every change; rclone, run as a supervised `rclone rcd`, only moves the bytes.

## How we build

- **Small, reviewable steps.** Work in steps agreed up front. A step is one focused change, such as a feature slice, a refactor or a set of tests, small enough to read in one sitting. Each step is one commit, reviewed as a diff with its tests passing before it's committed.
- **Pushes, tags and releases need explicit approval** from a maintainer.
- **Explore freely, commit carefully.** Prototype and test locally as much as needed, then refine into a small, clean change. Throwaway experiments go in `spike/`, which is git-ignored; whatever survives is rebuilt as a proper step.
- **Green at every step.** Build, tests and CI pass after every commit.
- **No big drops.** No large scaffolds or generated code without review.

## Real data stays safe

- Development builds sync only to an `OpenBackupManager-dev/` root or a test account. Never write to real synced folders; measurements on real folders only list them.
- Nothing private goes in the repo: no secrets, tokens, personal paths or real file names. Commit only summarised findings from experiments.

## Design docs

Each area of the app gets `docs/design/<area>.md` when it's implemented: how it works, why (decisions, rejected alternatives, evidence) and accepted limits that shouldn't be "fixed". Read the relevant doc before changing an area, and update it in the same step as the code.

## Build and test

```sh
dotnet build OpenBackupManager.slnx
dotnet test OpenBackupManager.slnx
dotnet format whitespace OpenBackupManager.slnx
dotnet format style OpenBackupManager.slnx --severity info
dotnet format analyzers OpenBackupManager.slnx
```

- Run `git config core.hooksPath .githooks` once per clone. The pre-commit hook then formats staged C# files.
- Style is auto-fixed, so style rules are suggestions. Rules that can't be auto-fixed, such as naming, are warnings, and warnings are errors.
- Turn a noisy analyzer rule down in `.editorconfig`, with a comment saying why.
- Package versions live in `Directory.Packages.props`.
