using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LumosPresenter.WebHost.Api;

/// <summary>One entry in the picker's file-type dropdown: a label and bare extensions (".jpg").</summary>
internal sealed record FileFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>
/// Opens the operating system's own file dialog on the machine running the server. The console
/// is a browser page, and a browser file input hands over bytes and a bare name but never a
/// location — so the dialog has to be shown by the server, which then gets real paths back.
/// Only meaningful when the operator is sitting at this machine; the caller checks that.
///
/// Windows uses the shell's IFileOpenDialog, macOS asks AppleScript's <c>choose file</c>, and
/// Linux tries zenity then kdialog.
/// </summary>
internal static class NativeFilePicker
{
    /// <summary>
    /// Shows the dialog and waits for it to close. Returns the chosen absolute paths (empty when
    /// the operator cancels), or null when this machine has no dialog the server can show.
    /// </summary>
    public static Task<IReadOnlyList<string>?> PickFilesAsync(
        string title, IReadOnlyList<FileFilter> filters, int defaultFilter)
    {
        if (OperatingSystem.IsWindows())
        {
            return Windows.PickAsync(title, filters, defaultFilter);
        }
        if (OperatingSystem.IsMacOS())
        {
            return MacPickAsync(title, filters[defaultFilter]);
        }
        return LinuxPickAsync(title, filters, defaultFilter);
    }

    private static async Task<IReadOnlyList<string>?> MacPickAsync(string title, FileFilter filter)
    {
        // `activate` brings osascript's dialog in front of the browser; without it the dialog
        // can open behind the console window and the click appears to do nothing.
        var types = string.Join(", ", filter.Extensions.Select(e => $"\"{e.TrimStart('.')}\""));
        string[] script =
        [
            "activate",
            $"set picked to choose file with prompt \"{title}\" of type {{{types}}} with multiple selections allowed",
            "set out to \"\"",
            "repeat with f in picked",
            "set out to out & POSIX path of f & linefeed",
            "end repeat",
            "return out",
        ];
        var args = script.SelectMany(line => new[] { "-e", line }).ToArray();
        var result = await RunAsync("osascript", args);
        if (result is null) return null;
        // -128 is AppleScript's "User canceled".
        if (result.ExitCode != 0)
        {
            return result.Error.Contains("-128")
                ? []
                : throw new InvalidOperationException($"The file dialog failed: {result.Error.Trim()}");
        }
        return SplitLines(result.Output);
    }

    private static async Task<IReadOnlyList<string>?> LinuxPickAsync(
        string title, IReadOnlyList<FileFilter> filters, int defaultFilter)
    {
        // zenity lists filters in the order given and selects the first, so lead with the default.
        var ordered = filters.Skip(defaultFilter).Concat(filters.Take(defaultFilter)).ToList();

        var zenityArgs = new List<string> { "--file-selection", "--multiple", "--separator=\n", $"--title={title}" };
        zenityArgs.AddRange(ordered.Select(f =>
            $"--file-filter={f.Name} | {string.Join(' ', f.Extensions.Select(e => "*" + e))}"));
        var result = await RunAsync("zenity", zenityArgs);

        if (result is null)
        {
            var kdialogFilter = string.Join('\n', ordered.Select(f =>
                $"{string.Join(' ', f.Extensions.Select(e => "*" + e))}|{f.Name}"));
            result = await RunAsync("kdialog",
                ["--title", title, "--getopenfilename", "--multiple", "--separate-output",
                 Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), kdialogFilter]);
        }

        if (result is null) return null;
        // Both tools exit 1 on cancel.
        if (result.ExitCode == 1) return [];
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"The file dialog failed: {result.Error.Trim()}");
        }
        return SplitLines(result.Output);
    }

    private static List<string> SplitLines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    /// <summary>Runs a dialog helper to completion; null when the program is not installed.</summary>
    private static async Task<ProcessResult?> RunAsync(string program, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{program} did not start.");
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await output, await error);
        }
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        public static Task<IReadOnlyList<string>?> PickAsync(
            string title, IReadOnlyList<FileFilter> filters, int defaultFilter)
        {
            // The shell dialog is COM and needs a single-threaded apartment, which no thread-pool
            // thread is — so it gets a thread of its own for as long as it is open.
            var done = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    done.SetResult(Show(title, filters, defaultFilter));
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "Native file picker",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }

        // Keeps "last folder used" separate from any other dialog this process might show.
        private static readonly Guid ClientGuid = new("5b0f6d2e-8c4a-4f57-9a55-6c1f3c2a7e10");

        private static List<string> Show(string title, IReadOnlyList<FileFilter> filters, int defaultFilter)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
            var owner = IntPtr.Zero;
            var browser = GetForegroundWindow();
            try
            {
                dialog.SetTitle(title);
                dialog.SetOptions(FosAllowMultiSelect | FosForceFileSystem | FosPathMustExist | FosFileMustExist);
                var specs = filters
                    .Select(f => new FilterSpec
                    {
                        Name = f.Name,
                        Spec = string.Join(';', f.Extensions.Select(e => "*" + e)),
                    })
                    .ToArray();
                dialog.SetFileTypes((uint)specs.Length, specs);
                dialog.SetFileTypeIndex((uint)defaultFilter + 1); // 1-based
                var clientGuid = ClientGuid;
                dialog.SetClientGuid(ref clientGuid);

                owner = CreateOwner(browser);
                var hr = dialog.Show(owner);
                if (hr == ErrorCancelled) return [];
                Marshal.ThrowExceptionForHR(hr);

                dialog.GetResults(out var results);
                try
                {
                    results.GetCount(out var count);
                    var paths = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        results.GetItemAt(i, out var item);
                        try
                        {
                            item.GetDisplayName(SigdnFileSysPath, out var name);
                            try
                            {
                                if (Marshal.PtrToStringUni(name) is { } path) paths.Add(path);
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(name);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                    return paths;
                }
                finally
                {
                    Marshal.ReleaseComObject(results);
                }
            }
            finally
            {
                if (owner != IntPtr.Zero)
                {
                    DestroyWindow(owner);
                    // Hand focus back to the console so the operator lands where they clicked.
                    if (browser != IntPtr.Zero) BringToFront(browser);
                }
                Marshal.ReleaseComObject(dialog);
            }
        }

        /// <summary>
        /// An invisible, topmost window centred on the browser for the dialog to be modal to.
        /// The server is a background process, and Windows will not let one of those pop a
        /// window over the app the operator is using — without a foreground owner the dialog
        /// opens behind the browser and only flashes on the taskbar.
        /// </summary>
        private static IntPtr CreateOwner(IntPtr browser)
        {
            int x = 0, y = 0;
            if (browser != IntPtr.Zero && GetWindowRect(browser, out var rect))
            {
                x = (rect.Left + rect.Right) / 2;
                y = (rect.Top + rect.Bottom) / 2;
            }
            var owner = CreateWindowExW(
                WsExToolWindow | WsExTopmost, "STATIC", "", WsPopup,
                x, y, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (owner == IntPtr.Zero) return IntPtr.Zero;
            ShowWindow(owner, SwShowNormal);
            BringToFront(owner);
            return owner;
        }

        /// <summary>
        /// SetForegroundWindow only succeeds for the thread that owns the foreground; sharing
        /// that thread's input state for the call is the documented way around the lock.
        /// </summary>
        private static void BringToFront(IntPtr window)
        {
            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var thisThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != thisThread
                && AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        private const int ErrorCancelled = unchecked((int)0x800704C7);
        private const uint SigdnFileSysPath = 0x80058000;
        private const uint FosForceFileSystem = 0x40;
        private const uint FosAllowMultiSelect = 0x200;
        private const uint FosPathMustExist = 0x800;
        private const uint FosFileMustExist = 0x1000;
        private const uint WsPopup = 0x80000000;
        private const uint WsExToolWindow = 0x80;
        private const uint WsExTopmost = 0x8;
        private const int SwShowNormal = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(
            uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCoClass;

        // Vtable order matters: IModalWindow, then IFileDialog, then IFileOpenDialog. Methods
        // this class never calls are declared only to hold their slot.
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid riid, out IntPtr result);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr result);
            void GetAttributes(int attribFlags, uint mask, out uint attribs);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr enumerator);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, out IntPtr name);
            void GetAttributes(uint mask, out uint attribs);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
