const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const img_data = @embedFile("../assets/honest_work.bin");
const img_w: u32 = 800;
const img_h: u32 = 600;

fn hash(x: u32) u32 {
    var h = x;
    h = (h ^ (h >> 16)) *% 0x45d9f3b;
    h = (h ^ (h >> 16)) *% 0x45d9f3b;
    h = h ^ (h >> 16);
    return h;
}

/// Sample the embedded image at the given grid position, returning RGBA as 0.0-1.0.
fn sampleImage(col: u32, row: u32, cols: u32, rows: u32) [4]f32 {
    const img_col = col * img_w / cols;
    const img_row = row * img_h / rows;
    const idx = (img_row * img_w + img_col) * 4;
    return .{
        @as(f32, @floatFromInt(img_data[idx + 0])) / 255.0,
        @as(f32, @floatFromInt(img_data[idx + 1])) / 255.0,
        @as(f32, @floatFromInt(img_data[idx + 2])) / 255.0,
        @as(f32, @floatFromInt(img_data[idx + 3])) / 255.0,
    };
}

/// Generate a noise color from a deterministic hash seed.
fn noiseColor(seed: u32) [4]f32 {
    const h0 = hash(seed);
    const h1 = hash(seed +% 1);
    const h2 = hash(seed +% 2);
    return .{
        @as(f32, @floatFromInt(h0 & 0xFF)) / 255.0,
        @as(f32, @floatFromInt(h1 & 0xFF)) / 255.0,
        @as(f32, @floatFromInt(h2 & 0xFF)) / 255.0,
        1.0,
    };
}

pub fn render(grid: *CellGrid, time: f32) void {
    const cols = grid.cols;
    const rows = grid.rows;

    // Frame seed derived from time so noise changes each frame.
    const frame_seed: u32 = @intFromFloat(time * 60.0);

    for (0..rows) |r| {
        const row: u32 = @intCast(r);
        for (0..cols) |c| {
            const col: u32 = @intCast(c);

            const img_color = sampleImage(col, row, cols, rows);
            const seed = col ^ (row *% 997) ^ frame_seed;
            const noise = noiseColor(seed);

            var bg: [4]f32 = undefined;

            if (time < 1.5) {
                // Phase 1: static noise
                bg = noise;
            } else if (time < 3.5) {
                // Phase 2: wave resolve left-to-right
                const wave_pos = (time - 1.5) / 2.0 * @as(f32, @floatFromInt(cols + 20));
                const col_f: f32 = @floatFromInt(col);
                const dist = col_f - wave_pos;

                if (dist < -10.0) {
                    // Fully resolved
                    bg = img_color;
                } else if (dist > 0.0) {
                    // Ahead of wave: noise
                    bg = noise;
                } else {
                    // Within the 10-column transition band: mix
                    const t = (dist + 10.0) / 10.0; // 1.0 at resolved edge, 0.0 at noise edge
                    bg = .{
                        img_color[0] * t + noise[0] * (1.0 - t),
                        img_color[1] * t + noise[1] * (1.0 - t),
                        img_color[2] * t + noise[2] * (1.0 - t),
                        1.0,
                    };
                }
            } else {
                // Phase 3: final image held static
                bg = img_color;
            }

            grid.setCell(col, row, CellInstance{
                .bg_color = bg,
                .fg_color = .{ 0, 0, 0, 0 },
                .glyph_index = 0,
            });
        }
    }
}
