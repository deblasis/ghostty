// Spike demo C exports for the SwapChainPanel DX11 proof-of-concept.
//
// These symbols are compiled into ghostty.dll on Windows and called from
// the C# WinUI 3 host. They are not part of the stable libghostty API and
// must be removed before upstreaming.

const std = @import("std");
const builtin = @import("builtin");
const log = std.log.scoped(.spike);

const Device = @import("../renderer/directx11/device.zig").Device;
const Pipeline = @import("../renderer/directx11/pipeline.zig").Pipeline;
const Constants = @import("../renderer/directx11/pipeline.zig").Constants;
const CellGrid = @import("../renderer/directx11/cell_grid.zig").CellGrid;
const Demo = @import("demo.zig").Demo;
const FpsOverlay = @import("fps_overlay.zig");

// Render-thread target: ~60 fps.
const frame_ns: u64 = 16_666_667;

// Cell size in pixels for the grid.
// Small enough that bitmap font text (6 cells/char) fits at 960px width (~53 chars).
const cell_px: f32 = 3.0;

// Global spike state. There is exactly one DX11 device per process in this
// spike, so a file-level singleton is fine.
var g_device: ?Device = null;
var g_pipeline: ?Pipeline = null;
var g_grid: ?CellGrid = null;
var g_running: std.atomic.Value(bool) = std.atomic.Value(bool).init(false);
var g_thread: ?std.Thread = null;
// Only accessed from the render thread (single-writer).
var g_width: u32 = 960;
var g_height: u32 = 640;
var g_demo: Demo = .{};
var g_fps: FpsOverlay = .{};

// Atomic signaling for resize/DPI from UI thread to render thread.
var g_resize_width: std.atomic.Value(u32) = std.atomic.Value(u32).init(0);
var g_resize_height: std.atomic.Value(u32) = std.atomic.Value(u32).init(0);
var g_resize_pending: std.atomic.Value(bool) = std.atomic.Value(bool).init(false);
var g_dpi_scale: std.atomic.Value(u32) = std.atomic.Value(u32).init(0); // f32 stored as u32 bits
var g_dpi_pending: std.atomic.Value(bool) = std.atomic.Value(bool).init(false);


/// ghostty_spike_init — create the D3D11 device and start the render thread.
///
/// panel_native must be a pointer to an ISwapChainPanelNative obtained via
/// ISwapChainPanelNative::QueryInterface from the XAML SwapChainPanel.
pub export fn ghostty_spike_init(
    panel_native: *anyopaque,
    width: u32,
    height: u32,
    scale: f32,
) callconv(.c) bool {
    if (g_device != null) {
        log.warn("ghostty_spike_init called while already initialized; ignoring", .{});
        return false;
    }

    g_width = width;
    g_height = height;

    g_device = Device.init(panel_native, width, height, scale) catch |err| {
        log.err("Device.init failed: {}", .{err});
        return false;
    };

    // Create the rendering pipeline (shaders, input layout, constant buffer).
    g_pipeline = Pipeline.init(g_device.?.device) catch |err| {
        log.err("Pipeline.init failed: {}", .{err});
        g_device.?.deinit();
        g_device = null;
        return false;
    };

    // Create the cell grid sized to fill the window.
    const cols: u32 = @max(1, @as(u32, @intFromFloat(@as(f32, @floatFromInt(width)) / cell_px)));
    const rows: u32 = @max(1, @as(u32, @intFromFloat(@as(f32, @floatFromInt(height)) / cell_px)));
    g_grid = CellGrid.init(std.heap.page_allocator, g_device.?.device, cols, rows) catch |err| {
        log.err("CellGrid.init failed: {}", .{err});
        g_pipeline.?.deinit();
        g_pipeline = null;
        g_device.?.deinit();
        g_device = null;
        return false;
    };

    log.info("spike initialised: {}x{} @{d:.2}x, grid={}x{}", .{ width, height, scale, cols, rows });

    // Spawn the render thread.
    g_running.store(true, .release);
    g_thread = std.Thread.spawn(.{}, renderLoop, .{}) catch |err| {
        log.err("failed to spawn render thread: {}", .{err});
        g_running.store(false, .release);
        g_grid.?.deinit();
        g_grid = null;
        g_pipeline.?.deinit();
        g_pipeline = null;
        g_device.?.deinit();
        g_device = null;
        return false;
    };

    return true;
}

/// ghostty_spike_shutdown — stop the render thread and release the device.
pub export fn ghostty_spike_shutdown() callconv(.c) void {
    g_running.store(false, .release);

    if (g_thread) |t| {
        t.join();
        g_thread = null;
    }

    if (g_grid) |*grid| {
        grid.deinit();
        g_grid = null;
    }

    if (g_pipeline) |*pipe| {
        pipe.deinit();
        g_pipeline = null;
    }

    if (g_device) |*dev| {
        dev.deinit();
        g_device = null;
    }

    log.info("spike shut down", .{});
}

/// ghostty_spike_resize — signal the render thread to resize.
///
/// Called from the UI thread; the render thread picks it up next frame.
pub export fn ghostty_spike_resize(width: u32, height: u32) callconv(.c) void {
    g_resize_width.store(width, .release);
    g_resize_height.store(height, .release);
    g_resize_pending.store(true, .release);
}

/// ghostty_spike_key_press — forward a key event to the spike renderer.
pub export fn ghostty_spike_key_press(virtual_key: u32) callconv(.c) void {
    _ = virtual_key;
    g_demo.advance();
}

/// ghostty_spike_dpi_changed — signal the render thread of a DPI change.
///
/// Called from the UI thread when the window moves between monitors.
pub export fn ghostty_spike_dpi_changed(scale: f32) callconv(.c) void {
    g_dpi_scale.store(@bitCast(scale), .release);
    g_dpi_pending.store(true, .release);
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

fn renderLoop() void {
    log.debug("render thread started", .{});

    var timer = std.time.Timer.start() catch {
        log.err("failed to start timer", .{});
        return;
    };
    var prev_ns: u64 = 0;

    while (g_running.load(.acquire)) {
        const dev = &(g_device orelse break);
        const pipe = &(g_pipeline orelse break);
        var grid = &(g_grid orelse break);

        // Handle pending resize from UI thread.
        if (g_resize_pending.load(.acquire)) {
            g_resize_pending.store(false, .release);
            const new_w = g_resize_width.load(.acquire);
            const new_h = g_resize_height.load(.acquire);
            if (new_w > 0 and new_h > 0 and (new_w != g_width or new_h != g_height)) {
                dev.resize(new_w, new_h) catch |err| {
                    log.err("device resize failed: {}; continuing with old size", .{err});
                };
                g_width = new_w;
                g_height = new_h;

                // Recreate cell grid with new dimensions.
                const new_cols: u32 = @max(1, @as(u32, @intFromFloat(@as(f32, @floatFromInt(new_w)) / cell_px)));
                const new_rows: u32 = @max(1, @as(u32, @intFromFloat(@as(f32, @floatFromInt(new_h)) / cell_px)));
                if (new_cols != grid.cols or new_rows != grid.rows) {
                    grid.deinit();
                    g_grid = CellGrid.init(std.heap.page_allocator, dev.device, new_cols, new_rows) catch |err| {
                        log.err("CellGrid.init on resize failed: {}; stopping", .{err});
                        break;
                    };
                    grid = &(g_grid orelse break);
                    log.info("resized grid to {}x{} ({}x{} px)", .{ new_cols, new_rows, new_w, new_h });
                }
            }
        }

        // Handle pending DPI change.
        if (g_dpi_pending.load(.acquire)) {
            g_dpi_pending.store(false, .release);
            const scale_bits = g_dpi_scale.load(.acquire);
            const scale: f32 = @bitCast(scale_bits);
            log.info("DPI scale changed to {d:.2}", .{scale});
            // For this spike, DPI change just triggers a resize with scaled dimensions.
            // The swap chain already handles composition scaling via XAML.
        }

        const elapsed_ns = timer.read();
        const dt_ns = elapsed_ns - prev_ns;
        prev_ns = elapsed_ns;
        const dt: f64 = @as(f64, @floatFromInt(dt_ns)) / 1_000_000_000.0;
        const time: f32 = @as(f32, @floatFromInt(elapsed_ns)) / 1_000_000_000.0;

        // Update and render the current demo scene.
        g_demo.update(dt);
        g_demo.render(grid);

        // FPS overlay on top of the scene.
        g_fps.recordFrame(dt);
        g_fps.render(grid);

        // Clear the render target to black.
        dev.clearRenderTarget(.{ 0, 0, 0, 1 });

        // Update pipeline constants.
        pipe.updateConstants(dev.context, .{
            .grid_size = .{ @floatFromInt(grid.cols), @floatFromInt(grid.rows) },
            .cell_size_px = .{ cell_px, cell_px },
            .viewport_size = .{ @floatFromInt(g_width), @floatFromInt(g_height) },
            .time = time,
        });

        // Bind pipeline, upload cells, draw.
        pipe.bind(dev.context);
        grid.upload(dev.context);
        grid.draw(dev.context);

        dev.present() catch |err| {
            log.err("present failed: {}; stopping render thread", .{err});
            break;
        };

        // Vsync in Present(1,0) paces us at ~60fps; no manual sleep needed.
    }

    log.debug("render thread stopped", .{});
}

