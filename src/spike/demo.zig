// Scene manager for the spike demo.
//
// Manages scene rotation: which scene is active, auto-advance timer,
// key-press-to-skip. The splash waits for a keypress; the ghost finale
// loops until a key press restarts the whole demo.

const std = @import("std");
const CellGrid = @import("../renderer/directx11/cell_grid.zig").CellGrid;
const bitmap_font = @import("bitmap_font.zig");

const splash = @import("scenes/splash.zig");
const color_palette = @import("scenes/color_palette.zig");
const text_render = @import("scenes/text_render.zig");
const matrix_rain = @import("scenes/matrix_rain.zig");
const dpi_metrics = @import("scenes/dpi_metrics.zig");
const gradient = @import("scenes/gradient.zig");
const cursor_styles = @import("scenes/cursor_styles.zig");
const bliss = @import("scenes/bliss.zig");
const honest_work = @import("scenes/honest_work.zig");
const pixel_ghost = @import("scenes/pixel_ghost.zig");

const RenderFn = *const fn (*CellGrid, f32) void;

const Scene = struct {
    render: RenderFn,
    name: []const u8,
    duration_secs: f64, // 0 = wait for key press
};

// Scene order: splash → demo scenes → honest_work → ghost (looping finale).
const scenes = [_]Scene{
    .{ .render = splash.render, .name = "splash", .duration_secs = 0 },
    .{ .render = color_palette.render, .name = "color_palette", .duration_secs = 5.0 },
    .{ .render = text_render.render, .name = "text_render", .duration_secs = 5.0 },
    .{ .render = matrix_rain.render, .name = "matrix_rain", .duration_secs = 5.0 },
    .{ .render = dpi_metrics.render, .name = "dpi_metrics", .duration_secs = 10.0 },
    .{ .render = gradient.render, .name = "gradient", .duration_secs = 5.0 },
    .{ .render = cursor_styles.render, .name = "cursor_styles", .duration_secs = 5.0 },
    .{ .render = bliss.render, .name = "bliss", .duration_secs = 5.0 },
    .{ .render = honest_work.render, .name = "honest_work", .duration_secs = 6.0 },
    .{ .render = pixel_ghost.render, .name = "ghostty", .duration_secs = 0 }, // loops forever
};

// Indices for special scenes.
const splash_idx = 0;
const ghost_idx = scenes.len - 1;

pub const Demo = struct {
    current_scene: usize = 0,
    scene_start_time: f64 = 0,
    total_time: f64 = 0,

    /// Advance to next scene, or restart from splash if on ghost finale.
    pub fn advance(self: *Demo) void {
        if (self.current_scene == ghost_idx) {
            // On the ghost finale — restart the whole demo.
            self.current_scene = 0;
        } else {
            self.current_scene += 1;
        }
        self.scene_start_time = self.total_time;
    }

    pub fn update(self: *Demo, dt: f64) void {
        self.total_time += dt;

        const scene = scenes[self.current_scene];

        // Auto-advance for scenes with a duration (not splash or ghost).
        if (scene.duration_secs > 0) {
            if (self.total_time - self.scene_start_time >= scene.duration_secs) {
                self.advance();
            }
        }
    }

    pub fn render(self: *Demo, grid: *CellGrid) void {
        const scene_time: f32 = @floatCast(self.total_time - self.scene_start_time);
        scenes[self.current_scene].render(grid, scene_time);

        // Draw progress counter on auto-advance scenes (not splash or ghost).
        if (self.current_scene > splash_idx and self.current_scene < ghost_idx) {
            self.renderCounter(grid);
        }
    }

    fn renderCounter(self: *Demo, grid: *CellGrid) void {
        // "N/M" in top-right corner — inverted colors.
        const display_num = self.current_scene;
        const total = ghost_idx - 1; // exclude splash and ghost

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
