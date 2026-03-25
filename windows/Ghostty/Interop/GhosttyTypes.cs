using System.Runtime.InteropServices;

namespace Ghostty.Interop;

// ghostty_platform_e
public enum GhosttyPlatform : int
{
    Invalid = 0,
    MacOS = 1,
    iOS = 2,
    Windows = 3,
}

// ghostty_clipboard_e
public enum GhosttyClipboard : int
{
    Standard = 0,
    Selection = 1,
}

// ghostty_clipboard_request_e
public enum GhosttyClipboardRequest : int
{
    Paste = 0,
    Osc52Read = 1,
    Osc52Write = 2,
}

// ghostty_target_tag_e
public enum GhosttyTargetTag : int
{
    App = 0,
    Surface = 1,
}

// ghostty_action_tag_e
public enum GhosttyActionTag : int
{
    Quit = 0,
    NewWindow = 1,
    NewTab = 2,
    CloseTab = 3,
    NewSplit = 4,
    CloseAllWindows = 5,
    ToggleMaximize = 6,
    ToggleFullscreen = 7,
    ToggleTabOverview = 8,
    ToggleWindowDecorations = 9,
    ToggleQuickTerminal = 10,
    ToggleCommandPalette = 11,
    ToggleVisibility = 12,
    ToggleBackgroundOpacity = 13,
    MoveTab = 14,
    GotoTab = 15,
    GotoSplit = 16,
    GotoWindow = 17,
    ResizeSplit = 18,
    EqualizeSplits = 19,
    ToggleSplitZoom = 20,
    PresentTerminal = 21,
    SizeLimit = 22,
    ResetWindowSize = 23,
    InitialSize = 24,
    CellSize = 25,
    Scrollbar = 26,
    Render = 27,
    Inspector = 28,
    ShowGtkInspector = 29,
    RenderInspector = 30,
    DesktopNotification = 31,
    SetTitle = 32,
    SetTabTitle = 33,
    PromptTitle = 34,
    Pwd = 35,
    MouseShape = 36,
    MouseVisibility = 37,
    MouseOverLink = 38,
    RendererHealth = 39,
    OpenConfig = 40,
    QuitTimer = 41,
    FloatWindow = 42,
    SecureInput = 43,
    KeySequence = 44,
    KeyTable = 45,
    ColorChange = 46,
    ReloadConfig = 47,
    ConfigChange = 48,
    CloseWindow = 49,
    RingBell = 50,
    Undo = 51,
    Redo = 52,
    CheckForUpdates = 53,
    OpenUrl = 54,
    ShowChildExited = 55,
    ProgressReport = 56,
    ShowOnScreenKeyboard = 57,
    CommandFinished = 58,
    StartSearch = 59,
    EndSearch = 60,
    SearchTotal = 61,
    SearchSelected = 62,
    Readonly = 63,
    CopyTitleToClipboard = 64,
}

// ghostty_build_mode_e
public enum GhosttyBuildMode : int
{
    Debug = 0,
    ReleaseSafe = 1,
    ReleaseFast = 2,
    ReleaseSmall = 3,
}

// ghostty_info_s
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyInfo
{
    public GhosttyBuildMode BuildMode;
    public nint Version;
    public nuint VersionLen;
}

// ghostty_platform_windows_s
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyPlatformWindows
{
    public nint Hwnd;
}

// ghostty_platform_u — only the Windows member is needed on this platform
[StructLayout(LayoutKind.Explicit)]
public struct GhosttyPlatformUnion
{
    [FieldOffset(0)]
    public GhosttyPlatformWindows Windows;
}

// ghostty_target_s
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyTarget
{
    public GhosttyTargetTag Tag;
    public nint Surface; // ghostty_target_u (union with single pointer member)
}

// ghostty_action_s — matches the C struct layout exactly.
// The union payload (24 bytes) is the size of the largest member
// (ghostty_action_scrollbar_s: 3 x uint64).
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GhosttyAction
{
    [FieldOffset(0)]
    public GhosttyActionTag Tag;

    // Union payload at offset 8 (aligned to 8 bytes).
    // Individual action payloads will be read via Unsafe.As when needed.
}

// ghostty_runtime_config_s
[StructLayout(LayoutKind.Sequential)]
public struct GhosttyRuntimeConfig
{
    public nint Userdata;

    public bool SupportsSelectionClipboard;

    public nint WakeupCb;
    public nint ActionCb;
    public nint ReadClipboardCb;
    public nint ConfirmReadClipboardCb;
    public nint WriteClipboardCb;
    public nint CloseSurfaceCb;
}
