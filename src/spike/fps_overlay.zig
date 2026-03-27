// Simple FPS counter overlay for the spike demo.
//
// Tracks frame times and draws "FPS: XX | XX.Xms" in the bottom-left corner
// using the bitmap font. Designed for video recordings.

const std = @import("std");
const bitmap_font = @import("bitmap_font.zig");
const CellGrid = @import("../renderer/directx11/cell_grid.zig").CellGrid;

const sample_count = 120; // ~2 seconds at 60fps

/// Colors
const overlay_bg = [4]f32{ 0.06, 0.06, 0.06, 1.0 };
const overlay_fg = [4]f32{ 0.0, 0.9, 0.3, 1.0 }; // green
const overlay_fg_warn = [4]f32{ 1.0, 1.0, 0.3, 1.0 }; // yellow
const overlay_fg_bad = [4]f32{ 1.0, 0.3, 0.3, 1.0 }; // red

samples: [sample_count]f64 = [_]f64{0} ** sample_count,
write_idx: usize = 0,
filled: bool = false,

pub fn recordFrame(self: *@This(), dt: f64) void {
    self.samples[self.write_idx] = dt;
    self.write_idx += 1;
    if (self.write_idx >= sample_count) {
        self.write_idx = 0;
        self.filled = true;
    }
}

fn avgFrameTime(self: *const @This()) f64 {
    const count = if (self.filled) sample_count else self.write_idx;
    if (count == 0) return 0;
    var sum: f64 = 0;
    for (self.samples[0..count]) |s| sum += s;
    return sum / @as(f64, @floatFromInt(count));
}

pub fn render(self: *const @This(), grid: *CellGrid) void {
    const count = if (self.filled) sample_count else self.write_idx;
    if (count < 2) return; // need at least a couple frames

    const avg = self.avgFrameTime();
    if (avg <= 0.0) return;

    const fps = @min(1.0 / avg, 999.0);
    const ms = @min(avg * 1000.0, 99.9);

    // Format "FPS: XXX | XX.Xms"
    var buf: [32]u8 = undefined;
    const text = formatFps(&buf, fps, ms);

    // Pick color based on frame time.
    const fg = if (ms < 12.0) overlay_fg else if (ms < 20.0) overlay_fg_warn else overlay_fg_bad;

    // Draw in the top-right corner.
    const text_width = @as(u32, @intCast(text.len)) * bitmap_font.char_w;
    if (grid.cols < text_width + 2 or grid.rows < bitmap_font.glyph_height + 2) return;
    const col_base = grid.cols - text_width - 2;
    const row_base: u32 = 2;

    drawString(grid, text, col_base, row_base, fg, overlay_bg);
}

fn formatFps(buf: *[32]u8, fps: f64, ms: f64) []const u8 {
    const fps_int: u32 = @intFromFloat(@min(fps, 999.0));
    const ms_int: u32 = @intFromFloat(@min(ms, 99.0));
    const ms_frac: u32 = @intFromFloat((@min(ms, 99.99) - @as(f64, @floatFromInt(ms_int))) * 10.0);

    var i: usize = 0;

    // "FPS: "
    for ("FPS: ") |c| {
        buf[i] = c;
        i += 1;
    }

    // FPS number (up to 3 digits)
    if (fps_int >= 100) {
        buf[i] = '0' + @as(u8, @intCast(fps_int / 100));
        i += 1;
    }
    if (fps_int >= 10) {
        buf[i] = '0' + @as(u8, @intCast((fps_int / 10) % 10));
        i += 1;
    }
    buf[i] = '0' + @as(u8, @intCast(fps_int % 10));
    i += 1;

    // " | "
    for (" | ") |c| {
        buf[i] = c;
        i += 1;
    }

    // Frame time: XX.Xms
    if (ms_int >= 10) {
        buf[i] = '0' + @as(u8, @intCast(ms_int / 10));
        i += 1;
    }
    buf[i] = '0' + @as(u8, @intCast(ms_int % 10));
    i += 1;
    buf[i] = '.';
    i += 1;
    buf[i] = '0' + @as(u8, @intCast(ms_frac % 10));
    i += 1;
    for ("ms") |c| {
        buf[i] = c;
        i += 1;
    }

    return buf[0..i];
}

fn drawString(
    grid: *CellGrid,
    text: []const u8,
    col_base: u32,
    row_base: u32,
    fg_color: [4]f32,
    bg_color: [4]f32,
) void {
    for (text, 0..) |char, char_idx| {
        const col = col_base + @as(u32, @intCast(char_idx)) * bitmap_font.char_w;
        const glyph = bitmap_font.getGlyph(char);

        for (0..bitmap_font.glyph_height) |py| {
            for (0..bitmap_font.glyph_width) |px| {
                const cell_col = col + @as(u32, @intCast(px));
                const cell_row = row_base + @as(u32, @intCast(py));
                if (cell_col >= grid.cols or cell_row >= grid.rows) continue;

                if (bitmap_font.isPixelSet(glyph, @intCast(px), @intCast(py))) {
                    grid.setCell(cell_col, cell_row, .{
                        .bg_color = fg_color,
                        .fg_color = fg_color,
                        .glyph_index = 0,
                    });
                } else {
                    grid.setCell(cell_col, cell_row, .{
                        .bg_color = bg_color,
                        .fg_color = bg_color,
                        .glyph_index = 0,
                    });
                }
            }
        }
    }
}
