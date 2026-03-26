const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

/// Simple deterministic hash for pseudo-random values seeded by column index.
fn hash(x: u32) u32 {
    var h = x;
    h = (h ^ (h >> 16)) *% 0x45d9f3b;
    h = (h ^ (h >> 16)) *% 0x45d9f3b;
    h = h ^ (h >> 16);
    return h;
}

/// Near-black background with a faint green tint.
const bg = [4]f32{ 0.008, 0.031, 0.008, 1.0 }; // ~#020802

pub fn render(grid: *CellGrid, time: f32) void {
    grid.clear(bg);

    const cols = grid.cols;
    const rows = grid.rows;

    var col: u32 = 0;
    while (col < cols) : (col += 1) {
        const col_hash = hash(col);

        // ~70% of columns are active (requirement: 60-80%).
        if (col_hash % 10 < 3) continue;

        // Per-column parameters derived from column hash.
        const trail_len: u32 = 8 + (hash(col_hash) % 13); // 8..20
        const speed: f32 = 3.0 + @as(f32, @floatFromInt(hash(col_hash +% 7) % 80)) / 10.0; // 3.0..11.0 cells/sec
        const phase: f32 = @as(f32, @floatFromInt(hash(col_hash +% 13) % 1000)) / 1000.0 * 50.0; // offset so columns don't start in sync

        // A second, slower stream for some columns to add density.
        const has_second = (hash(col_hash +% 31) % 3) == 0;
        const speed2: f32 = 2.0 + @as(f32, @floatFromInt(hash(col_hash +% 41) % 60)) / 10.0;
        const phase2: f32 = @as(f32, @floatFromInt(hash(col_hash +% 53) % 1000)) / 1000.0 * 80.0;
        const trail_len2: u32 = 8 + (hash(col_hash +% 61) % 13);

        // Draw the primary stream.
        drawStream(grid, col, rows, time, speed, phase, trail_len);

        // Draw the optional second stream.
        if (has_second) {
            drawStream(grid, col, rows, time, speed2, phase2, trail_len2);
        }
    }
}

fn drawStream(
    grid: *CellGrid,
    col: u32,
    rows: u32,
    time: f32,
    speed: f32,
    phase: f32,
    trail_len: u32,
) void {
    // Current head position (wraps around rows + trail_len so drops re-enter smoothly).
    const cycle: u32 = rows + trail_len;
    const raw_pos: f32 = (time + phase) * speed;
    // Convert to integer position within the cycle.
    const pos_in_cycle: u32 = @as(u32, @intFromFloat(@mod(raw_pos, @as(f32, @floatFromInt(cycle)))));

    // The head row (may be off-screen while the trail is still visible).
    const head: i32 = @as(i32, @intCast(pos_in_cycle)) - @as(i32, @intCast(trail_len));

    var i: u32 = 0;
    while (i <= trail_len) : (i += 1) {
        const row_i: i32 = head + @as(i32, @intCast(i));
        if (row_i < 0 or row_i >= @as(i32, @intCast(rows))) continue;

        const row: u32 = @intCast(row_i);

        // i == trail_len is the head, i == 0 is the dimmest tail.
        const t: f32 = @as(f32, @floatFromInt(i)) / @as(f32, @floatFromInt(trail_len));

        // Fade: t=1.0 is the head (brightest), t=0.0 is the tail (dimmest).
        // Use a quadratic falloff for a smoother look.
        const fade = t * t;

        // Head cell: bright green-white; trail: fading green.
        var cell: CellInstance = undefined;
        cell.glyph_index = 0;

        if (i == trail_len) {
            // Drop head: bright green-white.
            cell.fg_color = .{ 0.67, 1.0, 0.67, 1.0 }; // #AAFFAA
            cell.bg_color = .{ 0.15, 0.4, 0.15, 1.0 };
        } else {
            // Trail: fading green on near-black.
            const g = 0.15 + 0.65 * fade; // green: 0.15 .. 0.80
            const r = 0.02 * fade;
            const b = 0.02 * fade;
            cell.fg_color = .{ r, g, b, 1.0 };
            // Background gets a subtle green glow near the head.
            const bg_g = 0.031 + 0.08 * fade;
            cell.bg_color = .{ bg[0], bg_g, bg[2], 1.0 };
        }

        grid.setCell(col, row, cell);
    }
}
