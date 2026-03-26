// Scene manager for the spike demo.
//
// Manages scene rotation: which scene is active, auto-advance timer,
// key-press-to-skip. The splash waits for a keypress; other scenes
// auto-advance after a few seconds; the finale stays put.

const std = @import("std");
const CellGrid = @import("../renderer/directx11/cell_grid.zig").CellGrid;
const bitmap_font = @import("bitmap_font.zig");

const splash = @import("scenes/splash.zig");
const pixel_ghost = @import("scenes/pixel_ghost.zig");
const color_palette = @import("scenes/color_palette.zig");
const text_render = @import("scenes/text_render.zig");
const matrix_rain = @import("scenes/matrix_rain.zig");
const dpi_metrics = @import("scenes/dpi_metrics.zig");
const gradient = @import("scenes/gradient.zig");
const cursor_styles = @import("scenes/cursor_styles.zig");
const honest_work = @import("scenes/honest_work.zig");

const RenderFn = *const fn (*CellGrid, f32) void;

const Scene = struct {
    render: RenderFn,
    name: []const u8,
};

const scenes = [_]Scene{
    .{ .render = splash.render, .name = "splash" },
    .{ .render = pixel_ghost.render, .name = "pixel_ghost" },
    .{ .render = color_palette.render, .name = "color_palette" },
    .{ .render = text_render.render, .name = "text_render" },
    .{ .render = matrix_rain.render, .name = "matrix_rain" },
    .{ .render = dpi_metrics.render, .name = "dpi_metrics" },
    .{ .render = gradient.render, .name = "gradient" },
    .{ .render = cursor_styles.render, .name = "cursor_styles" },
    .{ .render = honest_work.render, .name = "honest_work" },
};

pub const Demo = struct {
    current_scene: usize = 0,
    scene_start_time: f64 = 0,
    total_time: f64 = 0,
    auto_advance_secs: f64 = 5.0,

    pub fn advance(self: *Demo) void {
        if (self.current_scene < scenes.len - 1) {
            self.current_scene += 1;
            self.scene_start_time = self.total_time;
        }
    }

    pub fn update(self: *Demo, dt: f64) void {
        self.total_time += dt;

        // Auto-advance (splash waits for keypress, finale stays).
        if (self.current_scene > 0 and self.current_scene < scenes.len - 1) {
            if (self.total_time - self.scene_start_time >= self.auto_advance_secs) {
                self.advance();
            }
        }
    }

    pub fn render(self: *Demo, grid: *CellGrid) void {
        const scene_time: f32 = @floatCast(self.total_time - self.scene_start_time);
        scenes[self.current_scene].render(grid, scene_time);

        // Draw progress counter on demo scenes (not splash or finale).
        if (self.current_scene > 0 and self.current_scene < scenes.len - 1) {
            self.renderCounter(grid);
        }
    }

    fn renderCounter(self: *Demo, grid: *CellGrid) void {
        // "N/M" in top-right corner — inverted colors.
        const display_num = self.current_scene; // 1-based (splash is 0)
        const total = scenes.len - 2; // exclude splash and finale

        const counter_bg = [4]f32{ 0.85, 0.85, 0.85, 1.0 };
        const counter_fg = [4]f32{ 0.1, 0.1, 0.1, 1.0 };

        if (grid.cols < 4) return;
        const col = grid.cols - 4;

        grid.setCell(col, 0, .{ .bg_color = counter_bg, .fg_color = counter_fg, .glyph_index = '0' + @as(u32, @intCast(display_num)) });
        grid.setCell(col + 1, 0, .{ .bg_color = counter_bg, .fg_color = counter_fg, .glyph_index = '/' });
        grid.setCell(col + 2, 0, .{ .bg_color = counter_bg, .fg_color = counter_fg, .glyph_index = '0' + @as(u32, @intCast(total)) });
        grid.setCell(col + 3, 0, .{ .bg_color = counter_bg, .fg_color = counter_fg, .glyph_index = 0 });
    }
};
