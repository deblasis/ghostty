const std = @import("std");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

/// Render the standard xterm 256-color palette as a grid of colored cells,
/// with a gentle wave highlight animation.
pub fn render(grid: *CellGrid, time: f32) void {
    // Dark background.
    grid.clear(.{ 0.04, 0.04, 0.04, 1.0 });

    // Palette layout (in palette-cell units):
    //   Row 0:     colors 0-7   (standard)
    //   Row 1:     colors 8-15  (bright)
    //   Row 2:     (spacer)
    //   Rows 3-8:  6x6x6 cube, 6 rows of 36 columns
    //   Row 9:     (spacer)
    //   Row 10:    colors 232-255 grayscale (24 columns)
    const palette_cols: u32 = 36;
    const palette_rows: u32 = 11;

    // Compute cell block size so the palette fits centered in the grid.
    const bw = if (grid.cols >= palette_cols) grid.cols / palette_cols else 1;
    const bh = if (grid.rows >= palette_rows) grid.rows / palette_rows else 1;
    // Pick the smaller to keep blocks square-ish but cap to fit.
    const block = @min(bw, bh);

    // Centering offsets in grid cells.
    const total_w = palette_cols * block;
    const total_h = palette_rows * block;
    const ox = if (grid.cols > total_w) (grid.cols - total_w) / 2 else 0;
    const oy = if (grid.rows > total_h) (grid.rows - total_h) / 2 else 0;

    // --- Standard colors (0-7) ---
    for (0..8) |i| {
        const rgb = colorToRgb(@intCast(i));
        drawBlock(grid, ox + @as(u32, @intCast(i)) * block, oy, block, block, rgb, time, @intCast(i));
    }

    // --- Bright colors (8-15) ---
    for (0..8) |i| {
        const rgb = colorToRgb(@as(u8, @intCast(i)) + 8);
        drawBlock(grid, ox + @as(u32, @intCast(i)) * block, oy + block, block, block, rgb, time, @as(u32, @intCast(i)) + 8);
    }

    // --- 6x6x6 color cube (16-231) ---
    // Laid out as 6 rows x 36 columns.
    for (0..6) |cube_row| {
        for (0..36) |cube_col| {
            const idx: u8 = @intCast(16 + cube_row * 36 + cube_col);
            const rgb = colorToRgb(idx);
            const gx = ox + @as(u32, @intCast(cube_col)) * block;
            const gy = oy + (@as(u32, @intCast(cube_row)) + 3) * block;
            drawBlock(grid, gx, gy, block, block, rgb, time, @as(u32, idx));
        }
    }

    // --- Grayscale ramp (232-255) ---
    for (0..24) |i| {
        const idx: u8 = @intCast(232 + i);
        const rgb = colorToRgb(idx);
        const gx = ox + @as(u32, @intCast(i)) * block;
        const gy = oy + 10 * block;
        drawBlock(grid, gx, gy, block, block, rgb, time, @as(u32, idx));
    }
}

/// Draw a rectangular block of grid cells with the given color and a subtle
/// wave highlight based on time and palette index.
fn drawBlock(
    grid: *CellGrid,
    x: u32,
    y: u32,
    w: u32,
    h: u32,
    base_rgb: [3]f32,
    time: f32,
    idx: u32,
) void {
    // Subtle wave highlight: a slow sine wave that sweeps across palette indices.
    const phase = @as(f32, @floatFromInt(idx)) * 0.05 + time * 0.8;
    const wave = (std.math.sin(phase) + 1.0) * 0.5; // 0..1
    const highlight = wave * 0.12; // gentle brightness boost

    const r = clampf(base_rgb[0] + highlight);
    const g = clampf(base_rgb[1] + highlight);
    const b = clampf(base_rgb[2] + highlight);

    const cell = CellInstance{
        .bg_color = .{ r, g, b, 1.0 },
        .fg_color = .{ 1, 1, 1, 1 },
        .glyph_index = 0,
    };

    for (0..h) |dy| {
        for (0..w) |dx| {
            grid.setCell(x + @as(u32, @intCast(dx)), y + @as(u32, @intCast(dy)), cell);
        }
    }
}

/// Convert xterm 256-color index to linear RGB (0..1).
fn colorToRgb(idx: u8) [3]f32 {
    if (idx < 16) {
        return standardColor(idx);
    } else if (idx < 232) {
        // 6x6x6 color cube.
        const ci = idx - 16;
        const ri = ci / 36;
        const gi = (ci % 36) / 6;
        const bi = ci % 6;
        return .{
            cubeComponent(ri),
            cubeComponent(gi),
            cubeComponent(bi),
        };
    } else {
        // Grayscale ramp: 8 + 10*i for i in 0..23.
        const v: f32 = @as(f32, @floatFromInt(@as(u32, 8) + @as(u32, 10) * @as(u32, idx - 232))) / 255.0;
        return .{ v, v, v };
    }
}

/// Map a cube component index (0-5) to a linear RGB value.
fn cubeComponent(c: u8) f32 {
    if (c == 0) return 0.0;
    return @as(f32, @floatFromInt(@as(u32, 55) + @as(u32, 40) * @as(u32, c))) / 255.0;
}

/// Standard + bright xterm colors.
fn standardColor(idx: u8) [3]f32 {
    const table = [16][3]u8{
        .{ 0, 0, 0 }, // 0  black
        .{ 205, 0, 0 }, // 1  red
        .{ 0, 205, 0 }, // 2  green
        .{ 205, 205, 0 }, // 3  yellow
        .{ 0, 0, 238 }, // 4  blue
        .{ 205, 0, 205 }, // 5  magenta
        .{ 0, 205, 205 }, // 6  cyan
        .{ 229, 229, 229 }, // 7  white
        .{ 127, 127, 127 }, // 8  bright black
        .{ 255, 0, 0 }, // 9  bright red
        .{ 0, 255, 0 }, // 10 bright green
        .{ 255, 255, 0 }, // 11 bright yellow
        .{ 92, 92, 255 }, // 12 bright blue
        .{ 255, 0, 255 }, // 13 bright magenta
        .{ 0, 255, 255 }, // 14 bright cyan
        .{ 255, 255, 255 }, // 15 bright white
    };
    const c = table[idx];
    return .{
        @as(f32, @floatFromInt(c[0])) / 255.0,
        @as(f32, @floatFromInt(c[1])) / 255.0,
        @as(f32, @floatFromInt(c[2])) / 255.0,
    };
}

/// Clamp a float to 0..1.
fn clampf(v: f32) f32 {
    return @min(1.0, @max(0.0, v));
}
