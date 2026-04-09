using System;
using System.Collections.Generic;
using Ghostty.Core.JumpList;
using Ghostty.Interop;

namespace Ghostty.JumpList;

/// <summary>
/// Real implementation of <see cref="ICustomDestinationListFacade"/>.
/// Creates the underlying <see cref="ShellInterop.ICustomDestinationList"/>
/// COM object via <see cref="ShellInterop.CoCreateInstance"/> on
/// construction and forwards every facade call to it.
///
/// One instance per process is typical; <see cref="JumpListBuilder"/>
/// calls <see cref="BeginList"/> and <see cref="Commit"/> in pairs
/// to replace the list wholesale.
/// </summary>
internal sealed class CustomDestinationListFacade : ICustomDestinationListFacade
{
    private readonly ShellInterop.ICustomDestinationList _list;
    // AddUserTasks on ICustomDestinationList is a one-shot call per
    // BeginList/Commit cycle — it creates the Tasks slot in the shell
    // store and a second invocation returns 0x800700B7 ALREADY_EXISTS.
    // So we buffer every AddTask call and flush a single AddUserTasks
    // in Commit.
    private readonly List<(string exe, string args, string title)> _pendingTasks = new();

    public CustomDestinationListFacade()
    {
        var clsid = ShellInterop.CLSID_DestinationList;
        var iid = ShellInterop.IID_ICustomDestinationList;
        ShellInterop.CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            ShellInterop.CLSCTX_INPROC_SERVER,
            ref iid,
            out var obj);
        _list = (ShellInterop.ICustomDestinationList)obj;
    }

    public void SetAppId(string appId) => _list.SetAppID(appId);

    public uint BeginList()
    {
        var iid = ShellInterop.IID_IObjectArray;
        _list.BeginList(out var maxSlots, ref iid, out _);
        return maxSlots;
    }

    public void AddTask(string exePath, string arguments, string title)
        => _pendingTasks.Add((exePath, arguments, title));

    public void AddCategory(string categoryName, IReadOnlyList<(string exePath, string args, string title)> entries)
    {
        var collection = CreateCollection();
        foreach (var e in entries)
        {
            var link = CreateShellLink(e.exePath, e.args, e.title);
            collection.AddObject(link);
        }
        var array = (ShellInterop.IObjectArray)collection;
        _list.AppendCategory(categoryName, array);
    }

    public void Commit()
    {
        if (_pendingTasks.Count > 0)
        {
            var collection = CreateCollection();
            foreach (var t in _pendingTasks)
            {
                var link = CreateShellLink(t.exe, t.args, t.title);
                collection.AddObject(link);
            }
            _list.AddUserTasks((ShellInterop.IObjectArray)collection);
            _pendingTasks.Clear();
        }
        _list.CommitList();
    }

    private static ShellInterop.IShellLinkW CreateShellLink(string exePath, string arguments, string title)
    {
        var clsid = ShellInterop.CLSID_ShellLink;
        var iid = ShellInterop.IID_IShellLinkW;
        ShellInterop.CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            ShellInterop.CLSCTX_INPROC_SERVER,
            ref iid,
            out var obj);
        var link = (ShellInterop.IShellLinkW)obj;
        link.SetPath(exePath);
        link.SetArguments(arguments);
        link.SetDescription(title);
        // Title shown in the jump list comes from System.Title, not
        // the description. Set it via the IPropertyStore side of the
        // same object.
        ShellInterop.SetShellLinkTitle(link, title);
        return link;
    }

    private static ShellInterop.IObjectCollection CreateCollection()
    {
        var clsid = ShellInterop.CLSID_EnumerableObjectCollection;
        var iid = ShellInterop.IID_IObjectCollection;
        ShellInterop.CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            ShellInterop.CLSCTX_INPROC_SERVER,
            ref iid,
            out var obj);
        return (ShellInterop.IObjectCollection)obj;
    }
}
