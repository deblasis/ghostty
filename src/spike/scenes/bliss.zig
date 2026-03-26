// Bliss scene: renders the classic Windows XP wallpaper from an embedded
// raw RGBA image, held static.

const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;
const CellInstance = @import("../../renderer/directx11/cell_grid.zig").CellInstance;

const img_data = @embedFile("../assets/bliss.bin");
const img_w: u32 = 622;
const img_h: u32 = 500;

pub fn render(grid: *CellGrid, time: f32) void {
    _ = time;
    const cols = grid.cols;
    const rows = grid.rows;

    for (0..rows) |r| {
        const row: u32 = @intCast(r);
        for (0..cols) |c| {
            const col: u32 = @intCast(c);

            const img_col = col * img_w / cols;
            const img_row = row * img_h / rows;
            const idx = (img_row * img_w + img_col) * 4;

            if (idx + 3 >= img_data.len) continue;

            const bg = [4]f32{
                @as(f32, @floatFromInt(img_data[idx + 0])) / 255.0,
                @as(f32, @floatFromInt(img_data[idx + 1])) / 255.0,
                @as(f32, @floatFromInt(img_data[idx + 2])) / 255.0,
                @as(f32, @floatFromInt(img_data[idx + 3])) / 255.0,
            };

            grid.setCell(col, row, CellInstance{
                .bg_color = bg,
                .fg_color = .{ 0, 0, 0, 0 },
                .glyph_index = 0,
            });
        }
    }
}
