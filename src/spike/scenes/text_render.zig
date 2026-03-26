// Text rendering showcase: displays multiple lines of bitmap font text
// with different color schemes to demonstrate the rendering pipeline.

const std = @import("std");
const bitmap_font = @import("../bitmap_font.zig");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const bg = [4]f32{ 0.031, 0.031, 0.031, 1.0 }; // #080808

// Color palette
const white = [4]f32{ 1.0, 1.0, 1.0, 1.0 };
const black = [4]f32{ 0.0, 0.0, 0.0, 1.0 };
const bright_cyan = [4]f32{ 0.0, 0.9, 1.0, 1.0 };
const dark_bg = [4]f32{ 0.05, 0.08, 0.12, 1.0 };
const red = [4]f32{ 1.0, 0.3, 0.3, 1.0 };
const green = [4]f32{ 0.3, 1.0, 0.3, 1.0 };
const blue = [4]f32{ 0.4, 0.5, 1.0, 1.0 };
const yellow = [4]f32{ 1.0, 1.0, 0.3, 1.0 };
const dim_white = [4]f32{ 0.5, 0.5, 0.5, 1.0 };
const bold_white = [4]f32{ 1.0, 1.0, 1.0, 1.0 };
const amber = [4]f32{ 0.85, 0.65, 0.2, 1.0 };

/// Render a string at (col_base, row_base) in grid cells, expanding each
/// character through the bitmap font. Lit pixels get fg_color, unlit get bg_color.
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

const Line = struct {
    text: []const u8,
    fg: [4]f32,
    bg_color: [4]f32,
};

// The multi-color line is handled specially, so we use a sentinel.
const color_line_text = "Bold  Dim  Red  Green  Blue  Yellow";

const lines = [_]Line{
    .{ .text = "Hello from Ghostty on Windows!", .fg = white, .bg_color = black },
    .{ .text = "DirectX 11 - SwapChainPanel - Zig", .fg = bright_cyan, .bg_color = dark_bg },
    .{ .text = color_line_text, .fg = white, .bg_color = bg }, // placeholder, drawn specially
    .{ .text = "The quick brown fox jumps over", .fg = amber, .bg_color = bg },
    .{ .text = "the lazy dog  0123456789", .fg = amber, .bg_color = bg },
};

/// Draw the multi-colored "Bold  Dim  Red  Green  Blue  Yellow" line.
/// Each word is rendered in its named color.
fn drawColorLine(grid: *CellGrid, col_base: u32, row_base: u32) void {
    const Word = struct { text: []const u8, color: [4]f32 };
    const words = [_]Word{
        .{ .text = "Bold", .color = bold_white },
        .{ .text = "  ", .color = bg },
        .{ .text = "Dim", .color = dim_white },
        .{ .text = "  ", .color = bg },
        .{ .text = "Red", .color = red },
        .{ .text = "  ", .color = bg },
        .{ .text = "Green", .color = green },
        .{ .text = "  ", .color = bg },
        .{ .text = "Blue", .color = blue },
        .{ .text = "  ", .color = bg },
        .{ .text = "Yellow", .color = yellow },
    };

    var offset: u32 = 0;
    for (words) |word| {
        drawString(grid, word.text, col_base + offset * bitmap_font.char_w, row_base, word.color, bg);
        offset += @intCast(word.text.len);
    }
}

pub fn render(grid: *CellGrid, time: f32) void {
    _ = time;
    grid.clear(bg);

    // Find the widest line for centering.
    const max_line_len = comptime blk: {
        var max: usize = 0;
        for (lines) |line| {
            if (line.text.len > max) max = line.text.len;
        }
        break :blk max;
    };

    const text_w = max_line_len * bitmap_font.char_w;
    const text_h = lines.len * bitmap_font.char_h;

    const start_col: u32 = if (grid.cols > text_w)
        @intCast((grid.cols - text_w) / 2)
    else
        0;
    const start_row: u32 = if (grid.rows > text_h)
        @intCast((grid.rows - text_h) / 2)
    else
        0;

    for (lines, 0..) |line, line_idx| {
        const row_base = start_row + @as(u32, @intCast(line_idx)) * bitmap_font.char_h;

        // Center each line individually within the max-width block.
        const line_w = line.text.len * bitmap_font.char_w;
        const line_col: u32 = if (text_w > line_w)
            start_col + @as(u32, @intCast((text_w - line_w) / 2))
        else
            start_col;

        if (std.mem.eql(u8, line.text, color_line_text)) {
            drawColorLine(grid, line_col, row_base);
        } else {
            drawString(grid, line.text, line_col, row_base, line.fg, line.bg_color);
        }
    }
}
