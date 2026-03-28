//! Render pass for DX11 draw steps.
//!
//! Stores the device context and issues immediate-mode draw commands.
//! Unlike Metal (which records into a command buffer), DX11 commands
//! execute immediately when called on the device context.
const std = @import("std");
const d3d11 = @import("d3d11.zig");
const Pipeline = @import("Pipeline.zig");
const Sampler = @import("Sampler.zig");
const Target = @import("Target.zig");
const Texture = @import("Texture.zig");
const bufferpkg = @import("buffer.zig");
const RawBuffer = bufferpkg.RawBuffer;

const log = std.log.scoped(.directx11);

/// Options for beginning a render pass.
pub const Options = struct {
    attachments: []const Attachment,

    pub const Attachment = struct {
        target: union(enum) {
            texture: Texture,
            target: Target,
        },
        clear_color: ?[4]f64 = null,
    };
};

/// A single step in a render pass.
pub const Step = struct {
    pipeline: Pipeline,
    uniforms: ?RawBuffer = null,
    buffers: []const ?RawBuffer = &.{},
    textures: []const ?Texture = &.{},
    samplers: []const ?Sampler = &.{},
    draw: Draw,

    pub const Draw = struct {
        type: DrawType,
        vertex_count: usize,
        instance_count: usize = 1,
    };

    pub const DrawType = enum {
        triangle,
        triangle_strip,
    };
};

/// The device context for issuing draw commands.
context: ?*d3d11.ID3D11DeviceContext,

/// The device for creating render target views from textures.
device: ?*d3d11.ID3D11Device,

pub fn begin(
    context: ?*d3d11.ID3D11DeviceContext,
    device: ?*d3d11.ID3D11Device,
    opts: Options,
) @This() {
    // Clearing and render target binding is handled by Frame.renderPass()
    // via device.clearRenderTarget(), which sets the viewport, binds the
    // swap chain's RTV, and clears it. Per-attachment RTV creation from
    // Texture/Target is a future task (Target doesn't hold an
    // ID3D11Texture2D yet).
    _ = opts;

    return .{ .context = context, .device = device };
}

pub fn step(self: *@This(), s: Step) void {
    const ctx = self.context orelse return;

    // Skip if the pipeline has no shaders loaded yet.
    if (s.pipeline.vertex_shader == null and s.pipeline.pixel_shader == null) return;

    // Skip zero-instance draws.
    if (s.draw.instance_count == 0) return;

    // Bind pipeline state: shaders and input layout.
    ctx.VSSetShader(s.pipeline.vertex_shader);
    ctx.PSSetShader(s.pipeline.pixel_shader);
    if (s.pipeline.input_layout) |layout| {
        ctx.IASetInputLayout(layout);
    }

    // Set primitive topology.
    ctx.IASetPrimitiveTopology(switch (s.draw.type) {
        .triangle => .TRIANGLELIST,
        .triangle_strip => .TRIANGLESTRIP,
    });

    // Bind vertex buffers.
    // Metal convention: index 0 is the vertex buffer, index 1+ is additional data.
    // DX11: all go through IASetVertexBuffers at their respective slots.
    for (s.buffers, 0..) |buf_opt, i| {
        if (buf_opt) |buf| {
            // Stride of 0 lets the shader use SV_VertexID for procedural geometry.
            // When real vertex data is bound, the stride will come from the pipeline
            // (future work when HLSL shaders define their input layouts).
            ctx.IASetVertexBuffers(
                @intCast(i),
                &.{@as(?*d3d11.ID3D11Buffer, buf)},
                &.{@as(u32, 0)},
                &.{@as(u32, 0)},
            );
        }
    }

    // Bind uniforms as constant buffer at slot 0 for both VS and PS.
    if (s.uniforms) |buf| {
        ctx.VSSetConstantBuffers(0, &.{@as(?*d3d11.ID3D11Buffer, buf)});
        ctx.PSSetConstantBuffers(0, &.{@as(?*d3d11.ID3D11Buffer, buf)});
    }

    // Bind textures as shader resource views for both VS and PS.
    for (s.textures, 0..) |tex_opt, i| {
        if (tex_opt) |tex| {
            const srv = @as(?*d3d11.ID3D11ShaderResourceView, tex.srv);
            ctx.VSSetShaderResources(@intCast(i), &.{srv});
            ctx.PSSetShaderResources(@intCast(i), &.{srv});
        }
    }

    // Bind samplers for the pixel shader.
    for (s.samplers, 0..) |samp_opt, i| {
        if (samp_opt) |samp| {
            ctx.PSSetSamplers(@intCast(i), &.{@as(?*d3d11.ID3D11SamplerState, samp.sampler)});
        }
    }

    // Draw.
    ctx.DrawInstanced(
        @intCast(s.draw.vertex_count),
        @intCast(s.draw.instance_count),
        0,
        0,
    );
}

pub fn complete(self: *const @This()) void {
    // DX11 immediate mode: commands already executed on the context.
    // Nothing to finalize (unlike Metal which calls endEncoding).
    _ = self;
}
