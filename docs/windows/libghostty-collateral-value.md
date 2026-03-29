# libghostty Collateral Value Catalog

A living catalog of what I learn about libghostty through the Windows port.
Items are added when I hit real friction, not speculatively.

Companion to issue #26 (north stars).

## How to read this

**Status tags:**
- `already-visible` - I can see it from architecture review today
- `discovered:[date]` - I actually hit it (updated when it happens)
- `deliberate-tradeoff` - I considered this but it is a deliberate upstream
  design choice, not a gap. Documented for my own understanding.

---

## Things I got wrong (deliberate tradeoffs, not gaps)

### GW-1: Renderer contract is not "Metal-biased"

I initially thought the GraphicsAPI compile-time interface had Metal
fingerprints (`custom_shader_y_is_down`, texture format enums, the
target/render-pass/step hierarchy) and that the abstraction should be more
"backend-neutral." On reflection: this IS the abstraction working correctly.
Each backend provides its own types and values for a well-defined contract.
OpenGL does not "adapt around" Metal assumptions; it implements the same
interface with its own semantics. DX11 does the same. The contract is
deliberately not lowest-common-denominator.

Status: `deliberate-tradeoff`

### GW-2: Library initialization - fix was the fix, no new API needed

I initially considered proposing a `ghostty_init()` / `ghostty_deinit()` pair.
On reflection: the DllMain issue was a Zig compiler platform bug (Zig's start.zig
does not initialize the MSVC CRT for DLLs), not a libghostty design gap. The
specific fix (calling `__vcrt_initialize` / `__acrt_initialize` from DllMain) was
correct and has been upstreamed. Adding init/deinit ceremony to work around an
upstream compiler issue would be solving the wrong problem.

Status: `deliberate-tradeoff`

### EF-3: Font system extraction - not my problem to solve

I considered whether the font stack could be extracted as a standalone component.
On reflection: the font system is coupled to the renderer because rendering glyphs
IS a renderer responsibility. Extracting it into a standalone "give me an atlas
texture" API would be a massive new surface area for unclear benefit, and I have
no concrete need for it.

Status: `deliberate-tradeoff`

### VT-1: Parser architecture - deliberate throughput choice

I noted that some consumers want mid-stream parser hooks. On reflection:
libghostty-vt's batch-tokenize-then-process model is a deliberate architectural
choice. The SIMD VT parser is one of Ghostty's core performance advantages.
Making it hookable would trade a proven strength for use cases that are not mine.
libghostty is a terminal emulation library optimized for terminal emulators.

Status: `deliberate-tradeoff`

### VT-2: Scrollback ownership - libghostty owns terminal state

I considered whether scrollback should have push/pop/clear callbacks for
consumers that want to manage their own. On reflection: libghostty owning terminal
state (including scrollback) is the product definition, not a limitation.
The render state API already provides read access. Proposing ownership transfer
callbacks would change what libghostty IS, not improve what it does.

Status: `deliberate-tradeoff`

---

## Concrete findings

### GW-3: Allocator ownership across the C boundary

The CRT heap mismatch (Zig's libc heap vs MSVC CRT heap) is documented in
`include/ghostty/vt/allocator.h`. `ghostty_free()` exists as the correct pattern.
Every allocation that crosses the C API boundary needs clear ownership semantics.
As I build P/Invoke bindings, I will track which allocations need explicit free
calls and whether the current documentation is sufficient.

Status: `already-visible`

### GW-4: Build system portability

My work already contributed Windows build fixes upstream: CMake support, import
library generation, path handling, CRT linking. Every build fix I make for Windows
also flushes out implicit Unix assumptions.

Status: `already-visible`

### GW-5: Default color configuration

libghostty had no sensible default colors out of the box, resulting in
black-on-black text. Fixed upstream. This is a "first five minutes" issue: the
library was extracted from Ghostty where the app always sets colors, so the
standalone case was never tested. I will watch for similar missing-default
patterns as I integrate.

Status: `already-visible` (fixed)

### VT-5: Effect/capability consistency

libghostty advertises terminal capabilities (like in-band size reports) even when
the host has not wired up the corresponding effect callback (`write_pty`). This
means the library can claim to support something the host cannot deliver. I will
likely hit variants of this as I incrementally wire up the C# app shell.

Status: `already-visible`

---

## The C API lag pattern

The Zig API is always ahead of the C API because the Ghostty app uses Zig
directly. The C API gets extended when someone needs it. My P/Invoke work will
generate a steady stream of "I need this in the C API" moments. I will track
these and propose batched C API extensions rather than one-off requests.

---

## Items to add when I actually hit them

This section is intentionally empty. Items move here from my notes when I have
concrete evidence, not speculation. The rules: hit it, document it, propose a fix.
