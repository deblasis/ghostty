const CellGrid = @import("../../renderer/directx11/cell_grid.zig").CellGrid;

pub fn render(grid: *CellGrid, _time: f32) void {
    _ = _time;
    grid.clear(.{ 0.3, 0.3, 0.3, 1.0 }); // gray
}
