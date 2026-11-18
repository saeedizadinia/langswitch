using LangSwitch.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LangSwitch.Editing;

internal sealed class ClipboardCopy(nint window)
{
    private const int SnapshotLimit = 16 * 1024 * 1024;
    private const int RestoreAttempts = 8;
    private const int CopyTimeoutMilliseconds = 325;
    private const int RetryDelayMilliseconds = 15;
    private const int ClipboardAllocationPadding = 32;
    private static readonly HashSet<uint> MemoryFormats = new()
    {
        1, 7, 8, Native.ClipboardUnicodeText, 15, 16, 17, // Text, OEM text, DIB, file drop, locale, DIB V5
        Native.RegisterClipboardFormat("HTML Format"), Native.RegisterClipboardFormat("Rich Text Format"),
        Native.RegisterClipboardFormat("UniformResourceLocatorW"), Native.RegisterClipboardFormat("UniformResourceLocator"),
        Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        Native.RegisterClipboardFormat("CanIncludeInClipboardHistory"), Native.RegisterClipboardFormat("CanUploadToCloudClipboard")
    };

    public async Task<string?> ReadAsync(DesktopTarget target, Func<bool> targetIsCurrent, Func<bool> inputIsCurrent)
    {
        using var snapshot = await Task.Run(TakeSnapshot);
        if (snapshot is null || !targetIsCurrent() || Native.ModifierDown() || Native.GetClipboardSequenceNumber() != snapshot.Sequence)
            return null;

        if (!Native.SendCopy())
            return null;
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < CopyTimeoutMilliseconds && targetIsCurrent())
        {
            var copy = await Task.Run(() => ReadOwnedCopy(target, snapshot.Sequence, inputIsCurrent));
            if (copy is not null)
            {
                // Restore under the clipboard lock, even if focus changed after reading the copy.
                for (int attempt = 0; attempt < RestoreAttempts; attempt++)
                {
                    if (await Task.Run(() => Restore(snapshot, copy)))
                        break;
                    await Task.Delay(RetryDelayMilliseconds);
                }
                return targetIsCurrent() ? copy.Text : null;
            }
            await Task.Delay(RetryDelayMilliseconds);
        }
        return null;
    }

    private Snapshot? TakeSnapshot()
    {
        if (!Native.OpenClipboard(window))
            return null;

        var snapshot = new Snapshot();
        bool complete = false;
        try
        {
            var formats = new List<uint>();
            uint format = 0;
            while (true)
            {
                // A zero result can mean either the end of the list or an error.
                Native.SetLastError(0);
                format = Native.EnumClipboardFormats(format);
                if (format == 0)
                {
                    if (Marshal.GetLastWin32Error() != 0)
                        return null;
                    break;
                }
                if (!MemoryFormats.Contains(format))
                    return null;
                formats.Add(format);
            }

            int remaining = SnapshotLimit;
            foreach (uint item in formats)
            {
                byte[]? bytes = ReadMemory(item, remaining);
                if (bytes is null || !snapshot.TryAdd(item, bytes))
                    return null;
                remaining -= bytes.Length;
            }

            snapshot.Sequence = Native.GetClipboardSequenceNumber();
            complete = true;
            return snapshot;
        }
        finally
        {
            Native.CloseClipboard();
            if (!complete)
                snapshot.Dispose();
        }
    }

    private Copy? ReadOwnedCopy(DesktopTarget target, uint originalSequence, Func<bool> inputIsCurrent)
    {
        if (!inputIsCurrent() || Native.GetClipboardSequenceNumber() == originalSequence || !Native.OpenClipboard(window))
            return null;

        try
        {
            nint owner = Native.GetClipboardOwner();
            if (!target.OwnsWindow(owner) || Native.GetClipboardSequenceNumber() == originalSequence || !inputIsCurrent())
                return null;

            byte[]? bytes = ReadMemory(Native.ClipboardUnicodeText, (EditorProbe.TextLimit + ClipboardAllocationPadding) * sizeof(char));
            string? text = null;
            if (bytes is not null)
            {
                var chars = new char[bytes.Length / sizeof(char)];
                Buffer.BlockCopy(bytes, 0, chars, 0, chars.Length * sizeof(char));
                int end = Array.IndexOf(chars, '\0');
                if (end is > 0 and <= EditorProbe.TextLimit)
                    text = new string(chars, 0, end);
            }
            return new Copy(Native.GetClipboardSequenceNumber(), owner, text);
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    private bool Restore(Snapshot snapshot, Copy copy)
    {
        if (!Native.OpenClipboard(window))
            return false;

        try
        {
            if (Native.GetClipboardSequenceNumber() != copy.Sequence || Native.GetClipboardOwner() != copy.Owner)
                return true;
            if (!Native.EmptyClipboard())
                return false;
            snapshot.RestoreFormats();
            return true;
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    private static byte[]? ReadMemory(uint format, int limit)
    {
        nint handle = Native.GetClipboardData(format);
        nuint size = handle == 0 ? 0 : Native.GlobalSize(handle);
        if (size == 0 || size > (nuint)limit)
            return null;

        nint data = Native.GlobalLock(handle);
        if (data == 0)
            return null;

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Native.GlobalUnlock(handle);
        }
    }

    private sealed record Copy(uint Sequence, nint Owner, string? Text);

    private sealed class Snapshot : IDisposable
    {
        private readonly List<(uint Format, nint Handle)> formats = [];
        public uint Sequence { get; set; }

        public bool TryAdd(uint format, byte[] bytes)
        {
            nint handle = Native.GlobalAlloc(2, (nuint)bytes.Length); // GMEM_MOVEABLE
            if (handle == 0)
                return false;

            nint data = Native.GlobalLock(handle);
            if (data == 0)
            {
                Native.GlobalFree(handle);
                return false;
            }
            Marshal.Copy(bytes, 0, data, bytes.Length);
            Native.GlobalUnlock(handle);
            formats.Add((format, handle));
            return true;
        }

        public void RestoreFormats()
        {
            for (int index = 0; index < formats.Count; index++)
            {
                var (format, handle) = formats[index];
                if (Native.SetClipboardData(format, handle) != 0)
                    formats[index] = (format, 0); // Windows now owns the allocation.
            }
        }

        public void Dispose()
        {
            foreach (var (_, handle) in formats)
            {
                if (handle != 0)
                    Native.GlobalFree(handle);
            }
        }
    }
}
