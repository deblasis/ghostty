const std = @import("std");
const math = std.math;
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;

/// Early sunrise gradient — animated with multiple color stops and gentle wave.
pub fn render(grid: *CellGrid, time: f32) void {
    const rows_f: f32 = @floatFromInt(grid.rows);
    const cols_f: f32 = @floatFromInt(grid.cols);

    // Animation: the gradient origin shifts upward over time, looping smoothly.
    // A full cycle takes about 20 seconds worth of time units.
    const shift = 0.15 * math.sin(time * 0.3);

    for (0..grid.rows) |row| {
        const row_f: f32 = @floatFromInt(row);

        for (0..grid.cols) |col| {
            const col_f: f32 = @floatFromInt(col);

            // Horizontal wave: gentle undulation across columns.
            const wave = 0.02 * math.sin(col_f * 0.15 + time * 1.2);

            // Normalized vertical position with animation shift and wave.
            const t_raw = row_f / rows_f + shift + wave;
            // Wrap into [0, 1) for smooth looping.
            const t = t_raw - @floor(t_raw);

            const color = sampleGradient(t);

            // Subtle brightness variation across columns for depth.
            const col_norm = col_f / cols_f;
            const brightness = 1.0 - 0.03 * math.sin(col_norm * math.pi);

            grid.setCell(@intCast(col), @intCast(row), .{
                .bg_color = .{ color[0] * brightness, color[1] * brightness, color[2] * brightness, 1.0 },
                .fg_color = .{ 1, 1, 1, 1 },
                .glyph_index = 0,
            });
        }
    }
}

/// Color stops for an early sunrise palette (top-to-bottom, no purple).
const Color = [3]f32;

const stops = [_]struct { pos: f32, color: Color }{
    .{ .pos = 0.00, .color = hexToLinear(0x0B, 0x10, 0x26) }, // deep navy/dark blue
    .{ .pos = 0.20, .color = hexToLinear(0x1A, 0x30, 0x40) }, // dark teal
    .{ .pos = 0.40, .color = hexToLinear(0xE8, 0x95, 0x6A) }, // warm peach
    .{ .pos = 0.55, .color = hexToLinear(0xF5, 0xA6, 0x23) }, // bright orange
    .{ .pos = 0.75, .color = hexToLinear(0xF5, 0xD7, 0x6E) }, // golden yellow
    .{ .pos = 1.00, .color = hexToLinear(0xFF, 0xF4, 0xD6) }, // pale sky/cream
};

fn sampleGradient(t: f32) Color {
    // Clamp to valid range.
    const tc = @max(0.0, @min(1.0, t));

    // Find the two stops we sit between.
    var i: usize = 0;
    while (i < stops.len - 1) : (i += 1) {
        if (tc <= stops[i + 1].pos) break;
    }

    const s0 = stops[i];
    const s1 = stops[i + 1];

    // Lerp factor within this segment.
    const segment_len = s1.pos - s0.pos;
    const f = if (segment_len > 0.0) (tc - s0.pos) / segment_len else 0.0;

    return .{
        lerpf(s0.color[0], s1.color[0], f),
        lerpf(s0.color[1], s1.color[1], f),
        lerpf(s0.color[2], s1.color[2], f),
    };
}

fn lerpf(a: f32, b: f32, t: f32) f32 {
    return a + (b - a) * t;
}

fn hexToLinear(r: u8, g: u8, b: u8) Color {
    return .{
        @as(f32, @floatFromInt(r)) / 255.0,
        @as(f32, @floatFromInt(g)) / 255.0,
        @as(f32, @floatFromInt(b)) / 255.0,
    };
}
