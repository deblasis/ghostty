// Spike demo C exports for the SwapChainPanel DX11 proof-of-concept.
//
// These symbols are compiled into ghostty.dll on Windows and called from
// the C# WinUI 3 host. They are not part of the stable libghostty API and
// must be removed before upstreaming.

const std = @import("std");
const builtin = @import("builtin");
const log = std.log.scoped(.spike);

const Device = @import("../renderer/directx11/device.zig").Device;

// Cornflower blue — the classic Direct3D "proof of life" clear colour.
const cornflower_blue = [4]f32{ 0.39, 0.58, 0.93, 1.0 };

// Render-thread target: ~60 fps.
const frame_ns: u64 = 16_666_667;

// Global spike state. There is exactly one DX11 device per process in this
// spike, so a file-level singleton is fine.
var g_device: ?Device = null;
var g_running: std.atomic.Value(bool) = std.atomic.Value(bool).init(false);
var g_thread: ?std.Thread = null;

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

    g_device = Device.init(panel_native, width, height, scale) catch |err| {
        log.err("Device.init failed: {}", .{err});
        return false;
    };

    // Proof-of-life: clear to cornflower blue before the render thread starts
    // so the panel shows colour immediately on init.
    g_device.?.clearRenderTarget(cornflower_blue);
    g_device.?.present() catch |err| {
        log.warn("initial present failed: {}", .{err});
    };

    // Spawn the render thread.
    g_running.store(true, .release);
    g_thread = std.Thread.spawn(.{}, renderLoop, .{}) catch |err| {
        log.err("failed to spawn render thread: {}", .{err});
        g_running.store(false, .release);
        g_device.?.deinit();
        g_device = null;
        return false;
    };

    log.info("spike initialised ({}x{} @{d:.2}x)", .{ width, height, scale });
    return true;
}

/// ghostty_spike_shutdown — stop the render thread and release the device.
pub export fn ghostty_spike_shutdown() callconv(.c) void {
    g_running.store(false, .release);

    if (g_thread) |t| {
        t.join();
        g_thread = null;
    }

    if (g_device) |*dev| {
        dev.deinit();
        g_device = null;
    }

    log.info("spike shut down", .{});
}

/// ghostty_spike_resize — update the swap chain for the new size.
///
/// TODO: implement after the render pipeline is stabilised.
pub export fn ghostty_spike_resize(width: u32, height: u32) callconv(.c) void {
    _ = width;
    _ = height;
    // TODO: call Device.resize and recreate RTV
}

/// ghostty_spike_key_press — forward a key event to the spike renderer.
///
/// TODO: implement once the scene framework is in place.
pub export fn ghostty_spike_key_press() callconv(.c) void {
    // TODO: handle key input
}

/// ghostty_spike_dpi_changed — notify the spike renderer of a DPI change.
///
/// TODO: implement alongside ghostty_spike_resize.
pub export fn ghostty_spike_dpi_changed(scale: f32) callconv(.c) void {
    _ = scale;
    // TODO: update scale factor and resize swap chain
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

fn renderLoop() void {
    log.debug("render thread started", .{});

    while (g_running.load(.acquire)) {
        const dev = &(g_device orelse break);

        dev.clearRenderTarget(cornflower_blue);
        dev.present() catch |err| {
            log.err("present failed: {}; stopping render thread", .{err});
            break;
        };

        std.Thread.sleep(frame_ns);
    }

    log.debug("render thread stopped", .{});
}
