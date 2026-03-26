// DPI/resize metrics scene: displays grid dimensions and frame timing
// using the bitmap font renderer.

const bitmap_font = @import("../bitmap_font.zig");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const bg = [4]f32{ 0.063, 0.063, 0.082, 1.0 }; // #101015 dark gray
const box_bg = [4]f32{ 0.045, 0.045, 0.065, 1.0 }; // slightly darker for box
const amber = [4]f32{ 0.85, 0.75, 0.45, 1.0 }; // header color
const green = [4]f32{ 0.35, 0.85, 0.45, 1.0 }; // label color
const white = [4]f32{ 0.95, 0.95, 0.95, 1.0 }; // value color

/// Format a u32 into a decimal string in a stack buffer.
/// Returns the slice of buf that contains the formatted digits.
fn formatU32(buf: *[10]u8, value: u32) []const u8 {
    if (value == 0) {
        buf[9] = '0';
        return buf[9..10];
    }
    var v = value;
    var i: usize = 10;
    while (v > 0) {
        i -= 1;
        buf[i] = @intCast('0' + (v % 10));
        v /= 10;
    }
    return buf[i..10];
}

/// Format time (seconds) as "N.Ns" into a stack buffer.
/// Returns the slice containing the formatted string.
fn formatTime(buf: *[16]u8, time: f32) []const u8 {
    // Clamp to reasonable range to avoid overflow.
    const clamped: f32 = if (time < 0.0) 0.0 else if (time > 99999.0) 99999.0 else time;

    // Multiply by 10, round, then split into integer and decimal parts.
    const scaled: u32 = @intFromFloat(clamped * 10.0 + 0.5);
    const integer_part = scaled / 10;
    const decimal_part = scaled % 10;

    // Build from the right: 's', decimal digit, '.', then integer digits.
    var i: usize = 16;

    i -= 1;
    buf[i] = 's';
    i -= 1;
    buf[i] = @intCast('0' + decimal_part);
    i -= 1;
    buf[i] = '.';

    if (integer_part == 0) {
        i -= 1;
        buf[i] = '0';
    } else {
        var v = integer_part;
        while (v > 0) {
            i -= 1;
            buf[i] = @intCast('0' + (v % 10));
            v /= 10;
        }
    }

    return buf[i..16];
}

/// Render a string at (col_base, row_base) in cell coordinates,
/// expanding each character into bitmap font cells.
fn drawText(grid: *CellGrid, col_base: u32, row_base: u32, text: []const u8, color: [4]f32) void {
    for (text, 0..) |char, char_idx| {
        const col_off = col_base + @as(u32, @intCast(char_idx)) * bitmap_font.char_w;
        const glyph = bitmap_font.getGlyph(char);

        for (0..bitmap_font.glyph_height) |py| {
            for (0..bitmap_font.glyph_width) |px| {
                const cell_col = col_off + @as(u32, @intCast(px));
                const cell_row = row_base + @as(u32, @intCast(py));
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

/// Fill a rectangular region with a background color.
fn fillRect(grid: *CellGrid, col: u32, row: u32, w: u32, h: u32, color: [4]f32) void {
    var r: u32 = row;
    while (r < row + h and r < grid.rows) : (r += 1) {
        var c: u32 = col;
        while (c < col + w and c < grid.cols) : (c += 1) {
            grid.setCell(c, r, .{
                .bg_color = color,
                .fg_color = color,
                .glyph_index = 0,
            });
        }
    }
}

pub fn render(grid: *CellGrid, time: f32) void {
    grid.clear(bg);

    // Lines to display (label, value pairs rendered separately).
    const header = "GRID METRICS";

    // Format numeric values.
    var cols_buf: [10]u8 = undefined;
    const cols_str = formatU32(&cols_buf, grid.cols);

    var rows_buf: [10]u8 = undefined;
    const rows_str = formatU32(&rows_buf, grid.rows);

    var cells_buf: [10]u8 = undefined;
    const cells_str = formatU32(&cells_buf, grid.cols * grid.rows);

    var time_buf: [16]u8 = undefined;
    const time_str = formatTime(&time_buf, time);

    // Layout: header + blank line + 4 data lines = 6 lines total.
    const line_count = 6;
    const label_prefix = "Cells: "; // longest label for width calc
    _ = label_prefix;

    // Widest line determines the text block width (in characters).
    // "Cells: NNNNN" could be up to 12 chars, header is 12.
    // Use a generous fixed width for the content area.
    const content_chars = 18; // enough for "Cells: " + 5-digit number
    const header_chars = header.len;

    // Use whichever is wider.
    const block_chars = if (content_chars > header_chars) content_chars else header_chars;

    const text_w: u32 = @intCast(block_chars * bitmap_font.char_w);
    const text_h: u32 = @intCast(line_count * bitmap_font.char_h);

    // Padding around text for the box (in cells).
    const pad_x: u32 = bitmap_font.char_w * 2;
    const pad_y: u32 = bitmap_font.char_h;

    const box_w = text_w + pad_x * 2;
    const box_h = text_h + pad_y * 2;

    // Center the box in the grid.
    const box_col: u32 = if (grid.cols > box_w)
        @intCast((grid.cols - box_w) / 2)
    else
        0;
    const box_row: u32 = if (grid.rows > box_h)
        @intCast((grid.rows - box_h) / 2)
    else
        0;

    // Draw the background box.
    fillRect(grid, box_col, box_row, box_w, box_h, box_bg);

    // Text origin inside the box.
    const text_col = box_col + pad_x;
    const text_row = box_row + pad_y;

    // Line 0: Header (centered within text block).
    const header_col = text_col + @as(u32, @intCast((block_chars - header_chars) / 2)) * bitmap_font.char_w;
    drawText(grid, header_col, text_row, header, amber);

    // Line 1: blank (skip)

    // Line 2: Cols
    const data_row_base = text_row + 2 * bitmap_font.char_h;
    const label_col = text_col;
    const value_col = text_col + 7 * bitmap_font.char_w; // after "Cells: " (7 chars)

    drawText(grid, label_col, data_row_base, "Cols:  ", green);
    drawText(grid, value_col, data_row_base, cols_str, white);

    // Line 3: Rows
    const row3 = data_row_base + bitmap_font.char_h;
    drawText(grid, label_col, row3, "Rows:  ", green);
    drawText(grid, value_col, row3, rows_str, white);

    // Line 4: Cells
    const row4 = row3 + bitmap_font.char_h;
    drawText(grid, label_col, row4, "Cells: ", green);
    drawText(grid, value_col, row4, cells_str, white);

    // Line 5: Time
    const row5 = row4 + bitmap_font.char_h;
    drawText(grid, label_col, row5, "Time:  ", green);
    drawText(grid, value_col, row5, time_str, white);
}
