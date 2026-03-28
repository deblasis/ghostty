//! Render target for DX11.
//! TODO: Implement with ID3D11Texture2D + ID3D11RenderTargetView.

/// Options for initializing a target.
pub const Options = struct {
    width: usize,
    height: usize,
};

pub fn deinit(self: *@This()) void {
    _ = self;
    @panic("TODO: DX11 Target.deinit");
}
