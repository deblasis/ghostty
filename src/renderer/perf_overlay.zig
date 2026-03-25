const std = @import("std");
const Allocator = std.mem.Allocator;
const datastruct = @import("../datastruct/main.zig");
const z2d = @import("z2d");
const Image = @import("image.zig").Image;

const PerfOverlay = @This();

/// Number of frame time samples to keep (5 minutes at 120fps).
const buffer_capacity = 36_000;

/// The number of downsampled buckets for visualization.
const bucket_count = 100;

/// Overlay dimensions in pixels.
const overlay_width: i32 = 220;
const overlay_height: i32 = 110;
const graph_height: i32 = 50;
const overlay_margin: i32 = 8;
const bar_width: i32 = 2;

pub const FrameTimeRing = datastruct.CircBuf(f32, 0.0);

enabled: bool = false,
frame_times: FrameTimeRing,
last_instant: ?std.time.Instant = null,
instant_failed: bool = false,

/// Pre-allocated z2d surface, created on first use, reused across frames.
overlay_surface: ?z2d.Surface = null,

pub const Stats = struct {
    fps: f32 = 0,
    frame_time_ms: f32 = 0,
    min_ms: f32 = 0,
    max_ms: f32 = 0,
    avg_ms: f32 = 0,
    buckets: [bucket_count]f32 = [_]f32{0} ** bucket_count,
    bucket_count_valid: usize = 0,
};

pub fn init(alloc: Allocator) Allocator.Error!PerfOverlay {
    return .{
        .frame_times = try FrameTimeRing.init(alloc, buffer_capacity),
    };
}

pub fn deinit(self: *PerfOverlay, alloc: Allocator) void {
    if (self.overlay_surface) |*sfc| sfc.deinit(alloc);
    self.frame_times.deinit(alloc);
}

pub fn toggle(self: *PerfOverlay) void {
    self.enabled = !self.enabled;
}

pub fn recordFrameTime(self: *PerfOverlay, now: std.time.Instant) void {
    if (self.last_instant) |last| {
        const elapsed_ns = now.since(last);
        const elapsed_s: f32 = @as(f32, @floatFromInt(elapsed_ns)) / std.time.ns_per_s;

        self.frame_times.append(elapsed_s) catch {
            self.frame_times.deleteOldest(1);
            self.frame_times.append(elapsed_s) catch unreachable;
        };
    }
    self.last_instant = now;
}

pub fn computeStats(self: *const PerfOverlay) Stats {
    const count = self.frame_times.len();
    if (count == 0) return .{};

    // Compute min, max, sum over all samples.
    var it = self.frame_times.iterator(.forward);
    var min_s: f32 = std.math.floatMax(f32);
    var max_s: f32 = 0;
    var sum_s: f32 = 0;

    while (it.next()) |v| {
        const t = v.*;
        if (t < min_s) min_s = t;
        if (t > max_s) max_s = t;
        sum_s += t;
    }

    const avg_s = sum_s / @as(f32, @floatFromInt(count));

    // Downsample into buckets.
    var stats = Stats{
        .fps = if (avg_s > 0) 1.0 / avg_s else 0,
        .frame_time_ms = avg_s * 1000.0,
        .min_ms = min_s * 1000.0,
        .max_ms = max_s * 1000.0,
        .avg_ms = avg_s * 1000.0,
    };

    const num_buckets: usize = @min(count, bucket_count);
    stats.bucket_count_valid = num_buckets;

    const samples_per_bucket_f: f32 = @as(f32, @floatFromInt(count)) / @as(f32, @floatFromInt(num_buckets));

    var bucket_idx: usize = 0;
    var sample_idx: usize = 0;
    var bucket_sum: f32 = 0;
    var bucket_samples: usize = 0;

    var it2 = self.frame_times.iterator(.forward);
    while (it2.next()) |v| {
        bucket_sum += v.*;
        bucket_samples += 1;
        sample_idx += 1;

        // Check if we've crossed into the next bucket.
        const next_boundary: usize = @intFromFloat(samples_per_bucket_f * @as(f32, @floatFromInt(bucket_idx + 1)));
        if (sample_idx >= next_boundary or sample_idx == count) {
            if (bucket_samples > 0 and bucket_idx < num_buckets) {
                stats.buckets[bucket_idx] = bucket_sum / @as(f32, @floatFromInt(bucket_samples));
                bucket_idx += 1;
            }
            bucket_sum = 0;
            bucket_samples = 0;
        }
    }

    return stats;
}

/// Draw a filled rectangle using line segments (z2d has no rectangle primitive).
fn drawRect(ctx: *z2d.Context, x: f64, y: f64, w: f64, h: f64) !void {
    try ctx.moveTo(x, y);
    try ctx.lineTo(x + w, y);
    try ctx.lineTo(x + w, y + h);
    try ctx.lineTo(x, y + h);
    try ctx.closePath();
}

/// Render the performance overlay to an image.
/// Returns null if there is no data to display.
pub fn renderOverlay(self: *PerfOverlay, alloc: Allocator) !?Image.Pending {
    const stats = self.computeStats();
    if (stats.bucket_count_valid == 0) return null;

    // Lazy-init the surface on first use, reuse afterward.
    if (self.overlay_surface == null) {
        self.overlay_surface = z2d.Surface.initPixel(
            .{ .rgba = .{ .r = 0, .g = 0, .b = 0, .a = 0 } },
            alloc,
            overlay_width,
            overlay_height,
        );
    }

    var sfc = &self.overlay_surface.?;

    // Clear to fully transparent.
    sfc.paintPixel(.{ .rgba = .{ .r = 0, .g = 0, .b = 0, .a = 0 } });

    var ctx: z2d.Context = .init(alloc, sfc);
    defer ctx.deinit();

    ctx.setAntiAliasingMode(.none);

    // Semi-transparent dark background.
    const bg_color: z2d.Pixel = .{ .rgba = .{ .r = 20, .g = 20, .b = 20, .a = 200 } };
    try drawRect(&ctx, 0, 0, @floatFromInt(overlay_width), @floatFromInt(overlay_height));
    ctx.setSourceToPixel(bg_color);
    try ctx.fill();

    // Draw bars for each valid bucket.
    const margin_f: f64 = @floatFromInt(overlay_margin);
    const graph_h: f64 = @floatFromInt(graph_height);
    const bw: f64 = @floatFromInt(bar_width);
    // Bars start at the bottom of the graph area.
    const graph_bottom: f64 = @as(f64, @floatFromInt(overlay_height)) - margin_f;

    // Cap at 33ms (~30fps) for full bar height.
    const max_ms_cap: f64 = 33.0;

    for (0..stats.bucket_count_valid) |i| {
        const ms: f64 = @floatCast(stats.buckets[i] * 1000.0);
        const ratio = @min(ms / max_ms_cap, 1.0);
        const bar_h = ratio * graph_h;
        const x = margin_f + @as(f64, @floatFromInt(i)) * bw;
        const y = graph_bottom - bar_h;

        // Color based on frame time thresholds.
        const bar_color: z2d.Pixel = if (ms < 12.0)
            .{ .rgba = .{ .r = 80, .g = 220, .b = 80, .a = 255 } } // green
        else if (ms < 20.0)
            .{ .rgba = .{ .r = 220, .g = 220, .b = 40, .a = 255 } } // yellow
        else
            .{ .rgba = .{ .r = 220, .g = 40, .b = 40, .a = 255 } }; // red

        try drawRect(&ctx, x, y, bw, bar_h);
        ctx.setSourceToPixel(bar_color);
        try ctx.fill();
    }

    return .{
        .width = @intCast(sfc.getWidth()),
        .height = @intCast(sfc.getHeight()),
        .pixel_format = .rgba,
        .data = @ptrCast(sfc.image_surface_rgba.buf.ptr),
    };
}

test "empty buffer stats" {
    const alloc = std.testing.allocator;
    var overlay = try PerfOverlay.init(alloc);
    defer overlay.deinit(alloc);

    const stats = overlay.computeStats();
    try std.testing.expectEqual(@as(f32, 0), stats.fps);
    try std.testing.expectEqual(@as(f32, 0), stats.frame_time_ms);
    try std.testing.expectEqual(@as(f32, 0), stats.min_ms);
    try std.testing.expectEqual(@as(f32, 0), stats.max_ms);
    try std.testing.expectEqual(@as(f32, 0), stats.avg_ms);
    try std.testing.expectEqual(@as(usize, 0), stats.bucket_count_valid);
}

test "toggle" {
    const alloc = std.testing.allocator;
    var overlay = try PerfOverlay.init(alloc);
    defer overlay.deinit(alloc);

    try std.testing.expect(!overlay.enabled);
    overlay.toggle();
    try std.testing.expect(overlay.enabled);
    overlay.toggle();
    try std.testing.expect(!overlay.enabled);
}

test "record frame times and stats" {
    const alloc = std.testing.allocator;
    var overlay = try PerfOverlay.init(alloc);
    defer overlay.deinit(alloc);

    // Simulate 10 frames at ~16.67ms (60 FPS) by manually inserting
    // frame times into the ring buffer, since std.time.Instant is
    // not easily mockable.
    const frame_time_s: f32 = 1.0 / 60.0; // ~0.01667s
    for (0..10) |_| {
        try overlay.frame_times.append(frame_time_s);
    }

    const stats = overlay.computeStats();

    // FPS should be approximately 60.
    try std.testing.expect(stats.fps > 59.0 and stats.fps < 61.0);

    // Frame time should be approximately 16.67ms.
    try std.testing.expect(stats.frame_time_ms > 16.0 and stats.frame_time_ms < 17.0);

    // Min and max should equal the frame time since all samples are identical.
    try std.testing.expect(stats.min_ms > 16.0 and stats.min_ms < 17.0);
    try std.testing.expect(stats.max_ms > 16.0 and stats.max_ms < 17.0);

    // With 10 samples and 100 buckets, we should get 10 valid buckets.
    try std.testing.expectEqual(@as(usize, 10), stats.bucket_count_valid);
}

test "buffer overflow wraps" {
    const alloc = std.testing.allocator;

    // Use a small ring buffer to test overflow behavior.
    var ring = try FrameTimeRing.init(alloc, 4);
    defer ring.deinit(alloc);

    try ring.append(1.0);
    try ring.append(2.0);
    try ring.append(3.0);
    try ring.append(4.0);

    // Buffer is full; simulate the overflow pattern used by recordFrameTime.
    ring.deleteOldest(1);
    try ring.append(5.0);

    // Should contain [2.0, 3.0, 4.0, 5.0].
    try std.testing.expectEqual(@as(usize, 4), ring.len());

    // Oldest should be 2.0.
    try std.testing.expectEqual(@as(f32, 2.0), ring.first().?.*);

    // Newest should be 5.0.
    try std.testing.expectEqual(@as(f32, 5.0), ring.last().?.*);
}
