# Contributing

Thanks for your interest. OpenBackupManager is early and has one maintainer, so please open an issue before starting anything bigger than a small fix, so we can agree the approach first.

OpenBackupManager is a cross-platform backup and two-way sync app. Our own sync engine plans every change; rclone, run as a supervised `rclone rcd`, only moves the bytes.

## Making a change

- **One focused change per pull request**, small enough to review in one sitting: a feature slice, a fix, a refactor or a set of tests. No large scaffolds or dumps of generated code.
- **Green before review.** Build, tests and format checks pass. CI runs them on every pull request.
- **Check the Windows app by hand.** Changes to its windows or tray also need the [checks by hand](docs/design/windows-app.md#checking-by-hand), since CI can't see display scaling or effects.
- **Update the design doc.** Each area of the app has `docs/design/<area>.md` once it's implemented: how it works, why (decisions, rejected alternatives, evidence) and accepted limits that shouldn't be "fixed". Read it before changing that area, and update it in the same pull request. List new docs in [docs/design/README.md](docs/design/README.md).
- **Update the user guide.** If a change affects what people see or do, update its page in [`docs/guide/`](docs/guide/README.md) in the same pull request.

## Real data stays safe

- Never point a development build at folders or accounts you care about. Use a test account or a throwaway folder.
- Nothing private goes in the repo: no secrets, tokens, personal paths or real file names.

## Build and test

```sh
dotnet build OpenBackupManager.slnx
dotnet test OpenBackupManager.slnx
dotnet format whitespace OpenBackupManager.slnx
dotnet format style OpenBackupManager.slnx --severity info
dotnet format analyzers OpenBackupManager.slnx
```

- On macOS and Linux, the solution leaves out the Windows-only projects, which have `.Windows` in their names, because WinUI only builds on Windows.
- The first build downloads rclone and checks its signature, so it needs internet access. After that it's cached; see [Bundled rclone](docs/design/rclone-bundling.md).
- Run `git config core.hooksPath .githooks` once per clone. The hooks then format staged C# files and turn AI tools' `Co-authored-by` trailers into `Assisted-by`.
- Style is auto-fixed, so style rules are suggestions. Rules that can't be auto-fixed, such as naming, are warnings, and warnings are errors.
- Fix warnings rather than suppress them. When suppressing is right, add a comment saying why: a `#pragma` for a one-off case, or `.editorconfig` for a rule that doesn't fit the project.
- Log messages, error messages and code comments don't end with a full stop. A longer one can have full stops between its sentences, just not after the last.
- Package versions live in `Directory.Packages.props`.

## Commits

Mark AI-assisted commits with an `Assisted-by: <tool> (<model>)` trailer, such as `Assisted-by: Claude Code (Claude Opus 5.5)`. AI tools aren't authors, so don't add a `Co-Authored-By` trailer for them.

## How this project uses AI

- **What AI does here:** drafts code, tests and docs for a step the maintainer has agreed, researches options and reviews diffs.
- **What it doesn't do:** decide scope or design, decide anything about what gets deleted or overwritten or which way a change syncs, approve its own work, or push, tag or release.
- **What every change passes:** the maintainer reads the whole diff before it's committed, the build treats warnings as errors, formatting and tests are checked, and CI runs on Windows, Linux and macOS.
- **How to check:** AI-assisted commits carry an `Assisted-by` trailer, and each area's design doc in `docs/design/` records its decisions and the alternatives that were rejected.
- **Limits:** AI-assisted code can have subtle bugs, and so can hand-written code. Review and tests are what we rely on, not the tool.

The rules below apply to the maintainer as well.

## AI-assisted contributions

AI-assisted contributions are welcome if you understand them, can defend them and take responsibility for them.

- **Read every line.** Before opening a pull request, build it, run the tests, and make sure you can answer review questions without going back to the tool.
- **Disclose it.** The pull request template asks which tools you used and what they did. Disclosure helps review and isn't held against you; hiding it is what gets pull requests closed.
- **Write to people yourself.** Pull request descriptions, issues and replies to review should be in your own words. Translation and grammar help are fine. If you quote AI output, mark it as such.
- **No autonomous agents.** Don't let an agent open pull requests or issues, or post comments, on its own.
- **Keep the tests honest.** Don't weaken, skip or delete tests to make them pass.
- **Data safety gets extra care.** Changes to what gets deleted, overwritten or synced in which direction, to how rclone is called, or to credential handling need an agreed issue first.
- **Reports found with AI** must be reproduced by you and written up in your own words. See [SECURITY.md](SECURITY.md) for vulnerabilities.

Pull requests that don't follow these rules may be closed without a detailed review. Repeated or deceptive ones lead to a block.

## Licence

By contributing, you confirm you have the right to submit your contribution under the [MIT licence](LICENSE), whether or not AI tools were used.

## Conduct

Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md).
