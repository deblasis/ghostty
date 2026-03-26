const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;

/// Early sunrise gradient — warm oranges and pinks.
pub fn render(grid: *CellGrid, _time: f32) void {
    _ = _time;
    for (0..grid.rows) |row| {
        const t: f32 = @as(f32, @floatFromInt(row)) / @as(f32, @floatFromInt(grid.rows));
        const r = 0.15 + 0.7 * t;
        const g = 0.05 + 0.35 * t;
        const b = 0.1 + 0.15 * t;
        for (0..grid.cols) |col| {
            grid.setCell(@intCast(col), @intCast(row), .{
                .bg_color = .{ r, g, b, 1.0 },
                .fg_color = .{ 1, 1, 1, 1 },
                .glyph_index = 0,
            });
        }
    }
}
