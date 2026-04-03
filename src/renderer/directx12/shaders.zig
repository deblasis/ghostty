//! Shader management for DX12.
//!
//! Loads DXIL bytecode via @embedFile and defines input element descriptions
//! for all 5 pipelines. GPU data structs are imported from gpu_data.zig.
const std = @import("std");
const builtin = @import("builtin");

const d3d12 = @import("d3d12.zig");
const gpu_data = @import("gpu_data.zig");
const Pipeline = @import("Pipeline.zig");

pub const Uniforms = gpu_data.Uniforms;
pub const CellText = gpu_data.CellText;
pub const CellBg = gpu_data.CellBg;
pub const Image = gpu_data.Image;
pub const BgImage = gpu_data.BgImage;

/// Embedded shader bytecode -- compiled at build time by HlslStep.zig.
/// On non-Windows these are empty slices so the module still compiles.
const shader_bytecode = if (builtin.os.tag == .windows) struct {
    const bg_color_vs = @embedFile("ghostty_hlsl_bg_color_vs");
    const bg_color_ps = @embedFile("ghostty_hlsl_bg_color_ps");
    const cell_bg_ps = @embedFile("ghostty_hlsl_cell_bg_ps");
    const cell_text_vs = @embedFile("ghostty_hlsl_cell_text_vs");
    const cell_text_ps = @embedFile("ghostty_hlsl_cell_text_ps");
    const image_vs = @embedFile("ghostty_hlsl_image_vs");
    const image_ps = @embedFile("ghostty_hlsl_image_ps");
    const bg_image_vs = @embedFile("ghostty_hlsl_bg_image_vs");
    const bg_image_ps = @embedFile("ghostty_hlsl_bg_image_ps");
} else struct {
    const bg_color_vs: []const u8 = &.{};
    const bg_color_ps: []const u8 = &.{};
    const cell_bg_ps: []const u8 = &.{};
    const cell_text_vs: []const u8 = &.{};
    const cell_text_ps: []const u8 = &.{};
    const image_vs: []const u8 = &.{};
    const image_ps: []const u8 = &.{};
    const bg_image_vs: []const u8 = &.{};
    const bg_image_ps: []const u8 = &.{};
};

// --- Input element descriptions for instanced pipelines ---
const PER_INSTANCE = d3d12.D3D12_INPUT_CLASSIFICATION.PER_INSTANCE_DATA;

/// Input layout for CellText instances (32 bytes per instance).
const cell_text_input_elements = [_]d3d12.D3D12_INPUT_ELEMENT_DESC{
    .{ // glyph_pos: [2]u32
        .SemanticName = "GLYPH_POS",
        .SemanticIndex = 0,
        .Format = .R32G32_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 0,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // glyph_size: [2]u32
        .SemanticName = "GLYPH_SIZE",
        .SemanticIndex = 0,
        .Format = .R32G32_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 8,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // bearings: [2]i16
        .SemanticName = "BEARINGS",
        .SemanticIndex = 0,
        .Format = .R16G16_SINT,
        .InputSlot = 0,
        .AlignedByteOffset = 16,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // grid_pos: [2]u16
        .SemanticName = "GRID_POS",
        .SemanticIndex = 0,
        .Format = .R16G16_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 20,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // color: [4]u8
        .SemanticName = "COLOR",
        .SemanticIndex = 0,
        .Format = .R8G8B8A8_UNORM,
        .InputSlot = 0,
        .AlignedByteOffset = 24,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // atlas: Atlas (u8)
        .SemanticName = "ATLAS",
        .SemanticIndex = 0,
        .Format = .R8_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 28,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // bools: packed struct(u8)
        .SemanticName = "BOOLS",
        .SemanticIndex = 0,
        .Format = .R8_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 29,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
};

/// Input layout for Image instances (40 bytes per instance).
const image_input_elements = [_]d3d12.D3D12_INPUT_ELEMENT_DESC{
    .{ // grid_pos: [2]f32
        .SemanticName = "GRID_POS",
        .SemanticIndex = 0,
        .Format = .R32G32_FLOAT,
        .InputSlot = 0,
        .AlignedByteOffset = 0,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // cell_offset: [2]f32
        .SemanticName = "CELL_OFFSET",
        .SemanticIndex = 0,
        .Format = .R32G32_FLOAT,
        .InputSlot = 0,
        .AlignedByteOffset = 8,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // source_rect: [4]f32
        .SemanticName = "SOURCE_RECT",
        .SemanticIndex = 0,
        .Format = .R32G32B32A32_FLOAT,
        .InputSlot = 0,
        .AlignedByteOffset = 16,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // dest_size: [2]f32
        .SemanticName = "DEST_SIZE",
        .SemanticIndex = 0,
        .Format = .R32G32_FLOAT,
        .InputSlot = 0,
        .AlignedByteOffset = 32,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
};

/// Input layout for BgImage instances (8 bytes per instance).
const bg_image_input_elements = [_]d3d12.D3D12_INPUT_ELEMENT_DESC{
    .{ // opacity: f32
        .SemanticName = "OPACITY",
        .SemanticIndex = 0,
        .Format = .R32_FLOAT,
        .InputSlot = 0,
        .AlignedByteOffset = 0,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
    .{ // info: packed struct(u8)
        .SemanticName = "INFO",
        .SemanticIndex = 0,
        .Format = .R8_UINT,
        .InputSlot = 0,
        .AlignedByteOffset = 4,
        .InputSlotClass = PER_INSTANCE,
        .InstanceDataStepRate = 1,
    },
};

// Verify input layouts match GPU data struct sizes and offsets.
comptime {
    std.debug.assert(@sizeOf(CellText) == 32);
    std.debug.assert(@offsetOf(CellText, "bools") == 29);
    std.debug.assert(@sizeOf(Image) == 40);
    std.debug.assert(@offsetOf(Image, "dest_size") == 32);
    std.debug.assert(@sizeOf(BgImage) == 8);
    std.debug.assert(@offsetOf(BgImage, "info") == 4);
}

/// Shader management for DX12.
pub const Shaders = struct {
    /// Shared root signature owned by this Shaders instance.
    /// All pipelines reference it but only Shaders releases it.
    root_signature: ?*d3d12.ID3D12RootSignature = null,
    pipelines: Pipelines = .{},
    post_pipelines: []const Pipeline = &.{},
    defunct: bool = false,

    pub const Pipelines = struct {
        bg_color: Pipeline = .{},
        cell_bg: Pipeline = .{},
        cell_text: Pipeline = .{},
        image: Pipeline = .{},
        bg_image: Pipeline = .{},
    };

    pub const InitError = error{
        RootSignatureSerializeFailed,
        RootSignatureCreationFailed,
        PipelineStateCreationFailed,
    };

    /// Initialize all 5 pipelines from embedded DXIL bytecode.
    /// On non-Windows, returns default-initialized (empty) pipelines.
    pub fn init(device: ?*d3d12.ID3D12Device) InitError!Shaders {
        const dev = device orelse return .{};

        // All pipelines share a single root signature.
        const root_sig = Pipeline.createRootSignature(dev) catch |err| return err;

        return .{
            .pipelines = .{
                // bg_color: full-screen VS, no input layout, no blending.
                .bg_color = try Pipeline.init(.{
                    .device = dev,
                    .root_signature = root_sig,
                    .vs_bytecode = shader_bytecode.bg_color_vs,
                    .ps_bytecode = shader_bytecode.bg_color_ps,
                }),
                // cell_bg: same full-screen VS, blended.
                .cell_bg = try Pipeline.init(.{
                    .device = dev,
                    .root_signature = root_sig,
                    .vs_bytecode = shader_bytecode.bg_color_vs,
                    .ps_bytecode = shader_bytecode.cell_bg_ps,
                    .blend = .premultiplied_alpha,
                }),
                // cell_text: instanced with CellText input layout, blended.
                .cell_text = try Pipeline.init(.{
                    .device = dev,
                    .root_signature = root_sig,
                    .vs_bytecode = shader_bytecode.cell_text_vs,
                    .ps_bytecode = shader_bytecode.cell_text_ps,
                    .input_layout = &cell_text_input_elements,
                    .blend = .premultiplied_alpha,
                }),
                // image: instanced with Image input layout, blended.
                .image = try Pipeline.init(.{
                    .device = dev,
                    .root_signature = root_sig,
                    .vs_bytecode = shader_bytecode.image_vs,
                    .ps_bytecode = shader_bytecode.image_ps,
                    .input_layout = &image_input_elements,
                    .blend = .premultiplied_alpha,
                }),
                // bg_image: instanced with BgImage input layout, blended.
                .bg_image = try Pipeline.init(.{
                    .device = dev,
                    .root_signature = root_sig,
                    .vs_bytecode = shader_bytecode.bg_image_vs,
                    .ps_bytecode = shader_bytecode.bg_image_ps,
                    .input_layout = &bg_image_input_elements,
                    .blend = .premultiplied_alpha,
                }),
            },
        };
    }

    pub fn deinit(self: *Shaders, alloc: std.mem.Allocator) void {
        for (self.post_pipelines) |p| {
            p.deinit();
        }
        if (self.post_pipelines.len > 0) {
            alloc.free(self.post_pipelines);
        }
        self.post_pipelines = &.{};

        // Release the shared root signature via bg_color (which owns it).
        // Other pipelines just reference it, so only release PSOs for them.
        if (self.pipelines.bg_color.root_signature) |rs| _ = rs.Release();

        self.pipelines.bg_color.deinit();
        self.pipelines.cell_bg.deinit();
        self.pipelines.cell_text.deinit();
        self.pipelines.image.deinit();
        self.pipelines.bg_image.deinit();
        self.pipelines = .{};

        if (self.root_signature) |rs| _ = rs.Release();
        self.root_signature = null;
    }
};

// --- Tests ---

test "shader bytecode fields exist" {
    // Verify all expected bytecode fields are present.
    _ = shader_bytecode.bg_color_vs;
    _ = shader_bytecode.bg_color_ps;
    _ = shader_bytecode.cell_bg_ps;
    _ = shader_bytecode.cell_text_vs;
    _ = shader_bytecode.cell_text_ps;
    _ = shader_bytecode.image_vs;
    _ = shader_bytecode.image_ps;
    _ = shader_bytecode.bg_image_vs;
    _ = shader_bytecode.bg_image_ps;
}

test "cell_text_input_elements count matches CellText fields" {
    // 7 elements: glyph_pos, glyph_size, bearings, grid_pos, color, atlas, bools
    try std.testing.expectEqual(@as(usize, 7), cell_text_input_elements.len);
}

test "image_input_elements count matches Image fields" {
    // 4 elements: grid_pos, cell_offset, source_rect, dest_size
    try std.testing.expectEqual(@as(usize, 4), image_input_elements.len);
}

test "bg_image_input_elements count matches BgImage fields" {
    // 2 elements: opacity, info
    try std.testing.expectEqual(@as(usize, 2), bg_image_input_elements.len);
}

test "cell_text byte offsets match struct layout" {
    // Last element (bools) at offset 29 matches @offsetOf(CellText, "bools")
    try std.testing.expectEqual(@as(u32, 29), cell_text_input_elements[6].AlignedByteOffset);
    try std.testing.expectEqual(@as(u32, 28), cell_text_input_elements[5].AlignedByteOffset);
    try std.testing.expectEqual(@as(u32, 24), cell_text_input_elements[4].AlignedByteOffset);
}

test "image byte offsets match struct layout" {
    try std.testing.expectEqual(@as(u32, 32), image_input_elements[3].AlignedByteOffset);
    try std.testing.expectEqual(@as(u32, 16), image_input_elements[2].AlignedByteOffset);
}

test "bg_image byte offsets match struct layout" {
    try std.testing.expectEqual(@as(u32, 4), bg_image_input_elements[1].AlignedByteOffset);
    try std.testing.expectEqual(@as(u32, 0), bg_image_input_elements[0].AlignedByteOffset);
}

test "Shaders struct fields" {
    try std.testing.expect(@hasField(Shaders, "pipelines"));
    try std.testing.expect(@hasField(Shaders, "post_pipelines"));
    try std.testing.expect(@hasField(Shaders, "defunct"));
}

test "Shaders default is empty" {
    const s: Shaders = .{};
    try std.testing.expect(s.pipelines.bg_color.pso == null);
    try std.testing.expect(s.pipelines.cell_text.pso == null);
    try std.testing.expect(s.post_pipelines.len == 0);
    try std.testing.expect(!s.defunct);
}

test "Shaders.init returns default on null device" {
    const s = try Shaders.init(null);
    try std.testing.expect(s.pipelines.bg_color.pso == null);
    try std.testing.expect(s.pipelines.cell_text.pso == null);
}

test "Pipelines has all 5 fields" {
    try std.testing.expect(@hasField(Shaders.Pipelines, "bg_color"));
    try std.testing.expect(@hasField(Shaders.Pipelines, "cell_bg"));
    try std.testing.expect(@hasField(Shaders.Pipelines, "cell_text"));
    try std.testing.expect(@hasField(Shaders.Pipelines, "image"));
    try std.testing.expect(@hasField(Shaders.Pipelines, "bg_image"));
}

test "all input elements use PER_INSTANCE_DATA" {
    for (&cell_text_input_elements) |elem| {
        try std.testing.expectEqual(PER_INSTANCE, elem.InputSlotClass);
    }
    for (&image_input_elements) |elem| {
        try std.testing.expectEqual(PER_INSTANCE, elem.InputSlotClass);
    }
    for (&bg_image_input_elements) |elem| {
        try std.testing.expectEqual(PER_INSTANCE, elem.InputSlotClass);
    }
}
