// Animated Ghostty ghost — plays the 3D rotating ghost from ghostty.org.
//
// 235 frames of 100x41 ASCII art, pre-converted to a binary format.
// Each byte: bits 0-2 = brightness (0-5), bit 7 = bold flag.
// Bold characters render in brand blue (#3551F3), non-bold in gray (#c3c3c4).
// Plays at ~30 fps, loops continuously.

const std = @import("std");
const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const frame_data = @embedFile("../assets/ghost_frames.bin");
const frame_cols: u32 = 100;
const frame_rows: u32 = 41;
const frame_size: u32 = frame_cols * frame_rows; // 4100 bytes per frame
const frame_count: u32 = 235;

// Starting frame (the website starts at frame 16).
const start_frame: u32 = 16;

// Target FPS for frame stepping (website uses ~32 fps).
const target_fps: f32 = 32.0;

// Ghostty official colors.
const bg_color = [4]f32{ 0.059, 0.059, 0.067, 1.0 }; // #0F0F11

// Brightness LUTs — 6 levels each for blue (bold) and gray (non-bold).
const blue_lut = [6][4]f32{
    bg_color, // 0 = background
    .{ 0.08, 0.10, 0.30, 1.0 }, // 1 = very dim blue
    .{ 0.11, 0.16, 0.50, 1.0 }, // 2 = dim blue
    .{ 0.15, 0.24, 0.72, 1.0 }, // 3 = medium blue
    .{ 0.18, 0.28, 0.85, 1.0 }, // 4 = bright blue
    .{ 0.208, 0.318, 0.953, 1.0 }, // 5 = full brand #3551F3
};

const gray_lut = [6][4]f32{
    bg_color, // 0 = background
    .{ 0.15, 0.15, 0.16, 1.0 }, // 1 = very dim gray
    .{ 0.28, 0.28, 0.29, 1.0 }, // 2 = dim gray
    .{ 0.45, 0.45, 0.46, 1.0 }, // 3 = medium gray
    .{ 0.60, 0.60, 0.61, 1.0 }, // 4 = bright gray
    .{ 0.765, 0.765, 0.769, 1.0 }, // 5 = full #c3c3c4
};

pub fn render(grid: *CellGrid, time: f32) void {
    grid.clear(bg_color);

    // Pick the current frame based on time.
    const raw_frame: u32 = @intFromFloat(@mod(time * target_fps, @as(f32, @floatFromInt(frame_count))));
    const frame_idx = (raw_frame + start_frame) % frame_count;
    const frame_offset = frame_idx * frame_size;

    // Scale: each ASCII char maps to a block of cells.
    // Choose scale so the 100x41 frame fills the grid nicely.
    const scale_x: u32 = @max(1, grid.cols / frame_cols);
    const scale_y: u32 = @max(1, grid.rows / frame_rows);
    const scale: u32 = @min(scale_x, scale_y);

    // Center the frame on the grid.
    const render_w = frame_cols * scale;
    const render_h = frame_rows * scale;
    const ox: u32 = if (grid.cols > render_w) (grid.cols - render_w) / 2 else 0;
    const oy: u32 = if (grid.rows > render_h) (grid.rows - render_h) / 2 else 0;

    for (0..frame_rows) |fr| {
        const row: u32 = @intCast(fr);
        for (0..frame_cols) |fc| {
            const col: u32 = @intCast(fc);
            const byte_idx = frame_offset + row * frame_cols + col;
            if (byte_idx >= frame_data.len) continue;

            const cell_byte = frame_data[byte_idx];
            const brightness: u3 = @intCast(cell_byte & 0x07);
            const is_bold = (cell_byte & 0x80) != 0;

            // Skip background cells.
            if (brightness == 0) continue;

            // Clamp to LUT range.
            const level: usize = @min(5, @as(usize, brightness));
            const color = if (is_bold) blue_lut[level] else gray_lut[level];

            // Fill the scale x scale block.
            const base_x = ox + col * scale;
            const base_y = oy + row * scale;
            for (0..scale) |dy| {
                for (0..scale) |dx| {
                    const cx = base_x + @as(u32, @intCast(dx));
                    const cy = base_y + @as(u32, @intCast(dy));
                    grid.setCell(cx, cy, .{
                        .bg_color = color,
                        .fg_color = color,
                        .glyph_index = 0,
                    });
                }
            }
        }
    }
}
