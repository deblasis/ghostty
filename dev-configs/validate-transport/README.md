# validate-transport fixtures

Config fixtures for the ConPTY-mode smoke test. Consumed by
`just validate-transport-smoke ROW` and the sibling assertion script
at `scripts/validate-transport-assert.ps1`.

Each fixture pins a `conpty-mode` value and a one-shot `command`
whose only job is to emit a known OSC 11 query (or to sanity-spawn,
for cmd) and then exit. `quit-after-last-window-closed = true` plus
the default `wait-after-command = false` brings the app down
automatically, and `confirm-close-surface = false` skips the exit
confirmation prompt.

The pwsh fixtures invoke `emit-osc11.ps1` via `-File`. Keeping the
emission logic in a standalone script avoids cmd metacharacters in
the fixture's `command` value (ghostty wraps commands containing
`&`, `|`, `(`, `)`, `%`, `!` with `cmd /c`, which mangles the
PowerShell escapes).

Never edit these to chase a test failure. If an expected verdict
changes, update the assertion table in
`scripts/validate-transport-assert.ps1` in the same commit.
