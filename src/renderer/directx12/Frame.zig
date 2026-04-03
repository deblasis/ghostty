//! DX12 per-frame command recording context.
//!
//! Each in-flight frame (triple buffered) owns a command allocator and
//! a graphics command list. The allocator backs the command list's memory
//! and must not be reset until the GPU has finished executing its commands.
//!
//! Lifecycle per frame:
//!   1. Wait for this frame's fence value (GPU done with previous use)
//!   2. reset() -- resets allocator + command list for new recording
//!   3. renderPass() -- returns a RenderPass for draw command recording
//!   4. complete() -- closes the command list, reports health
const Frame = @This();

const std = @import("std");

const com = @import("com.zig");
const d3d12 = @import("d3d12.zig");

const DirectX12 = @import("../DirectX12.zig");
const Renderer = @import("../generic.zig").Renderer(DirectX12);
const RenderPass = @import("RenderPass.zig");
const Target = @import("Target.zig");
const Health = @import("../../renderer.zig").Health;

const HRESULT = com.HRESULT;
const FAILED = com.FAILED;

const log = std.log.scoped(.directx12);

// --- State ---

command_allocator: *d3d12.ID3D12CommandAllocator,
command_list: *d3d12.ID3D12GraphicsCommandList,
fence_value: u64,

renderer: *Renderer,
target: *Target,

// --- Creation / teardown ---

pub fn init(device: *d3d12.ID3D12Device) !Frame {
    // -- Command allocator --
    var allocator: ?*d3d12.ID3D12CommandAllocator = null;
    {
        const hr = device.CreateCommandAllocator(
            .DIRECT,
            &d3d12.IID_ID3D12CommandAllocator,
            @ptrCast(&allocator),
        );
        if (FAILED(hr)) {
            log.err("CreateCommandAllocator failed: 0x{x}", .{@as(u32, @bitCast(hr))});
            return error.CommandAllocatorCreationFailed;
        }
    }
    errdefer _ = allocator.?.Release();

    // -- Graphics command list --
    // Created in a closed state so the first reset() opens it cleanly.
    var command_list: ?*d3d12.ID3D12GraphicsCommandList = null;
    {
        const hr = device.CreateCommandList(
            0,
            .DIRECT,
            allocator.?,
            null,
            &d3d12.IID_ID3D12GraphicsCommandList,
            @ptrCast(&command_list),
        );
        if (FAILED(hr)) {
            log.err("CreateCommandList failed: 0x{x}", .{@as(u32, @bitCast(hr))});
            return error.CommandListCreationFailed;
        }
    }
    errdefer _ = command_list.?.Release();

    // Close immediately -- reset() will reopen when the frame is first used.
    {
        const hr = command_list.?.Close();
        if (FAILED(hr)) {
            log.err("initial command list Close failed: 0x{x}", .{@as(u32, @bitCast(hr))});
            return error.CommandListCloseFailed;
        }
    }

    return .{
        .command_allocator = allocator.?,
        .command_list = command_list.?,
        .fence_value = 0,
        .renderer = undefined,
        .target = undefined,
    };
}

pub fn deinit(self: *Frame) void {
    _ = self.command_list.Release();
    _ = self.command_allocator.Release();
}

// --- Per-frame operations ---

/// Reset the allocator and command list for a new frame.
/// The caller must ensure the GPU has finished with this frame's
/// previous commands (by waiting on fence_value) before calling.
pub fn reset(self: *Frame) !void {
    {
        const hr = self.command_allocator.Reset();
        if (FAILED(hr)) {
            log.err("ID3D12CommandAllocator.Reset failed: 0x{x}", .{@as(u32, @bitCast(hr))});
            return error.CommandAllocatorResetFailed;
        }
    }
    {
        const hr = self.command_list.Reset(self.command_allocator, null);
        if (FAILED(hr)) {
            log.err("ID3D12GraphicsCommandList.Reset failed: 0x{x}", .{@as(u32, @bitCast(hr))});
            return error.CommandListResetFailed;
        }
    }
}

/// Begin a render pass for this frame. The returned RenderPass is used
/// by GenericRenderer to issue draw commands.
pub fn renderPass(
    self: *Frame,
    attachments: []const RenderPass.Options.Attachment,
) RenderPass {
    return RenderPass.begin(.{
        .command_list = self.command_list,
        .attachments = attachments,
    });
}

/// Close the command list and report frame health.
/// If sync is true the caller will block until the GPU finishes.
pub fn complete(self: *Frame, sync: bool) void {
    _ = sync;

    const hr = self.command_list.Close();
    const health: Health = if (FAILED(hr)) blk: {
        log.err("command list Close failed: 0x{x}", .{@as(u32, @bitCast(hr))});
        break :blk .unhealthy;
    } else .healthy;

    self.renderer.frameCompleted(health);
}

// --- Tests ---

test "Frame struct fields" {
    try std.testing.expect(@hasField(Frame, "command_allocator"));
    try std.testing.expect(@hasField(Frame, "command_list"));
    try std.testing.expect(@hasField(Frame, "fence_value"));
    try std.testing.expect(@hasField(Frame, "renderer"));
    try std.testing.expect(@hasField(Frame, "target"));
}

test "Frame has required methods" {
    // Compile-time check that the public API exists.
    try std.testing.expect(@TypeOf(Frame.init) != void);
    try std.testing.expect(@TypeOf(Frame.deinit) != void);
    try std.testing.expect(@TypeOf(Frame.reset) != void);
    try std.testing.expect(@TypeOf(Frame.renderPass) != void);
    try std.testing.expect(@TypeOf(Frame.complete) != void);
}
