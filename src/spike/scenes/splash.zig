// Splash screen: renders the "internal memo" as pixelated bitmap font text.
//
// Each character is expanded to a 6x8 block of cells (5x7 glyph + 1px spacing).
// Lit pixels get the foreground color; unlit pixels get the background color.

const bitmap_font = @import("../bitmap_font.zig");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const bg = [4]f32{ 0.06, 0.06, 0.06, 1.0 }; // near-black
const fg = [4]f32{ 0.85, 0.75, 0.45, 1.0 }; // warm amber
const dim = [4]f32{ 0.40, 0.35, 0.20, 1.0 }; // dimmer amber for headers
const cta = [4]f32{ 1.0, 0.95, 0.7, 1.0 }; // bright warm white for CTA
const cta_bg = [4]f32{ 0.30, 0.25, 0.10, 1.0 }; // subtle highlight behind CTA

const memo_lines = [_][]const u8{
    "INTERNAL MEMORANDUM",
    "",
    "TO:     Whoever is reading this",
    "FROM:   A bunch of Zig code pretending",
    "        to be a DLL",
    "DATE:   2026-03-26",
    "RE:     Does this thing even work?",
    "",
    "CONFIDENTIALITY NOTICE:",
    "This demo contains privileged and",
    "confidential rendering. Any unauthorized",
    "viewing may result in raised expectations.",
    "",
    "Do not distribute. Do not screenshot.",
    "Definitely do not post on social media.",
    "",
    "       [PRESS ANY KEY TO PROCEED]",
};

pub fn render(grid: *CellGrid, _time: f32) void {
    _ = _time;
    grid.clear(bg);

    // Center the text block vertically and horizontally.
    const max_line_len = comptime blk: {
        var max: usize = 0;
        for (memo_lines) |line| {
            if (line.len > max) max = line.len;
        }
        break :blk max;
    };

    const text_w = max_line_len * bitmap_font.char_w;
    const text_h = memo_lines.len * bitmap_font.char_h;

    const start_col: u32 = if (grid.cols > text_w)
        @intCast((grid.cols - text_w) / 2)
    else
        0;
    const start_row: u32 = if (grid.rows > text_h)
        @intCast((grid.rows - text_h) / 2)
    else
        0;

    // Paint CTA background bar across the full grid width first.
    const cta_line_idx = memo_lines.len - 1;
    const cta_row_base = start_row + @as(u32, @intCast(cta_line_idx)) * bitmap_font.char_h;
    for (0..grid.cols) |c| {
        for (0..bitmap_font.char_h) |py| {
            const r = cta_row_base + @as(u32, @intCast(py));
            if (r < grid.rows) {
                grid.setCell(@intCast(c), r, .{
                    .bg_color = cta_bg,
                    .fg_color = cta,
                    .glyph_index = 0,
                });
            }
        }
    }

    for (memo_lines, 0..) |line, line_idx| {
        const row_base = start_row + @as(u32, @intCast(line_idx)) * bitmap_font.char_h;

        const is_cta = line_idx == cta_line_idx;
        // First line and "CONFIDENTIALITY" get dim color, CTA gets bright, rest amber.
        const line_fg = if (is_cta) cta else if (line_idx == 0 or line_idx == 8) dim else fg;

        for (line, 0..) |char, char_idx| {
            const col_base = start_col + @as(u32, @intCast(char_idx)) * bitmap_font.char_w;
            const glyph = bitmap_font.getGlyph(char);

            // Expand glyph into cells.
            for (0..bitmap_font.glyph_height) |py| {
                for (0..bitmap_font.glyph_width) |px| {
                    const cell_col = col_base + @as(u32, @intCast(px));
                    const cell_row = row_base + @as(u32, @intCast(py));
                    if (cell_col >= grid.cols or cell_row >= grid.rows) continue;

                    if (bitmap_font.isPixelSet(glyph, @intCast(px), @intCast(py))) {
                        grid.setCell(cell_col, cell_row, .{
                            .bg_color = line_fg,
                            .fg_color = line_fg,
                            .glyph_index = 0,
                        });
                    }
                }
            }
        }
    }
}
