// Cursor styles scene: demonstrates block, bar, and underline cursors
// with blinking animation using the bitmap font for labels.

const bitmap_font = @import("../bitmap_font.zig");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const bg = [4]f32{ 0.04, 0.04, 0.08, 1.0 }; // #0a0a14
const amber = [4]f32{ 0.85, 0.75, 0.45, 1.0 }; // warm amber for labels
const text_fg = [4]f32{ 0.78, 0.78, 0.78, 1.0 }; // light gray for sample text
const cursor_bright = [4]f32{ 0.95, 0.92, 0.80, 1.0 }; // bright cursor color
const cursor_dark = [4]f32{ 0.04, 0.04, 0.08, 1.0 }; // dark fg when block-inverted

const sample_text = "ghostty $ ";
const cursor_pos = sample_text.len - 1; // cursor on the trailing space

const Section = struct {
    label: []const u8,
    cursor_type: enum { block, bar, underline },
};

const sections = [_]Section{
    .{ .label = "Block Cursor:", .cursor_type = .block },
    .{ .label = "Bar Cursor:", .cursor_type = .bar },
    .{ .label = "Underline Cursor:", .cursor_type = .underline },
};

pub fn render(grid: *CellGrid, time: f32) void {
    grid.clear(bg);

    const blink_on = @mod(time, 1.06) < 0.53;

    // Calculate vertical centering. Each section is 2 lines (label + sample),
    // with 1 blank line between sections = 3 * 2 + 2 gaps = 8 lines total.
    const section_count = sections.len;
    const lines_per_section = 2; // label line + sample line
    const gap_lines = 1;
    const total_lines = section_count * lines_per_section + (section_count - 1) * gap_lines;
    const total_h: u32 = @intCast(total_lines * @as(usize, bitmap_font.char_h));

    const start_row: u32 = if (grid.rows > total_h)
        @intCast((grid.rows - total_h) / 2)
    else
        0;

    // Horizontal: find the widest content to center.
    const max_label_len = comptime blk: {
        var max: usize = 0;
        for (sections) |s| {
            if (s.label.len > max) max = s.label.len;
        }
        break :blk max;
    };
    const content_w: u32 = @intCast(@max(max_label_len, sample_text.len) * @as(usize, bitmap_font.char_w));
    const start_col: u32 = if (grid.cols > content_w)
        @intCast((grid.cols - content_w) / 2)
    else
        0;

    for (sections, 0..) |section, section_idx| {
        const line_offset: u32 = @intCast(section_idx * (lines_per_section + gap_lines));

        // -- Render the label --
        const label_row = start_row + line_offset * bitmap_font.char_h;
        renderText(grid, start_col, label_row, section.label, amber);

        // -- Render the sample text --
        const sample_row = label_row + bitmap_font.char_h;
        renderText(grid, start_col, sample_row, sample_text, text_fg);

        // -- Render the cursor on top of the sample text --
        if (blink_on) {
            const cursor_col = start_col + @as(u32, @intCast(cursor_pos)) * bitmap_font.char_w;
            renderCursor(grid, cursor_col, sample_row, section.cursor_type);
        }
    }
}

fn renderCursor(grid: *CellGrid, col: u32, row: u32, cursor_type: anytype) void {
    switch (cursor_type) {
        .block => {
            // Fill the entire character cell with bright bg, dark fg (inverted).
            for (0..bitmap_font.char_h) |py| {
                for (0..bitmap_font.char_w) |px| {
                    const c = col + @as(u32, @intCast(px));
                    const r = row + @as(u32, @intCast(py));
                    if (c < grid.cols and r < grid.rows) {
                        grid.setCell(c, r, .{
                            .bg_color = cursor_bright,
                            .fg_color = cursor_dark,
                            .glyph_index = 0,
                        });
                    }
                }
            }
        },
        .bar => {
            // Thin vertical bar: 1 column on the left edge of the cursor cell.
            for (0..bitmap_font.char_h) |py| {
                const c = col;
                const r = row + @as(u32, @intCast(py));
                if (c < grid.cols and r < grid.rows) {
                    grid.setCell(c, r, .{
                        .bg_color = cursor_bright,
                        .fg_color = cursor_bright,
                        .glyph_index = 0,
                    });
                }
                // Second column for visibility.
                if (col + 1 < grid.cols and r < grid.rows) {
                    grid.setCell(col + 1, r, .{
                        .bg_color = cursor_bright,
                        .fg_color = cursor_bright,
                        .glyph_index = 0,
                    });
                }
            }
        },
        .underline => {
            // Bottom 2 rows of the character cell.
            const underline_start = bitmap_font.char_h - 2;
            for (underline_start..bitmap_font.char_h) |py| {
                for (0..bitmap_font.char_w) |px| {
                    const c = col + @as(u32, @intCast(px));
                    const r = row + @as(u32, @intCast(py));
                    if (c < grid.cols and r < grid.rows) {
                        grid.setCell(c, r, .{
                            .bg_color = cursor_bright,
                            .fg_color = cursor_bright,
                            .glyph_index = 0,
                        });
                    }
                }
            }
        },
    }
}

fn renderText(grid: *CellGrid, start_col: u32, start_row: u32, text: []const u8, color: [4]f32) void {
    for (text, 0..) |char, char_idx| {
        const col_base = start_col + @as(u32, @intCast(char_idx)) * bitmap_font.char_w;
        const glyph = bitmap_font.getGlyph(char);

        for (0..bitmap_font.glyph_height) |py| {
            for (0..bitmap_font.glyph_width) |px| {
                const cell_col = col_base + @as(u32, @intCast(px));
                const cell_row = start_row + @as(u32, @intCast(py));
                if (cell_col >= grid.cols or cell_row >= grid.rows) continue;

                if (bitmap_font.isPixelSet(glyph, @intCast(px), @intCast(py))) {
                    grid.setCell(cell_col, cell_row, .{
                        .bg_color = color,
                        .fg_color = color,
                        .glyph_index = 0,
                    });
                }
            }
        }
    }
}
