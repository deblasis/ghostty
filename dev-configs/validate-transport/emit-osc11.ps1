# Emit a raw OSC 11 query to stdout, then exit. Consumed by the
# pwsh-* smoke fixtures in this directory.
[Console]::Out.Write([char]0x1B + ']11;?' + [char]0x1B + '\')
