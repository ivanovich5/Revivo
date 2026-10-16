using Microsoft.Win32.SafeHandles;
using Rescate.Core;
using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Rescate.Acquire;

public sealed class PhysicalSource : IReadOnlySource
{
    private readonly SafeFileHandle handle;
    private readonly AlignedBuffer aligned = new(ImageAcquirer.BlockSize);
    public long Length { get; }
    public int SectorSize { get; }
    public string Identity { get; }
    public bool CanResume => false; // A blank/generic USB serial must never silently resume another stick.

    public PhysicalSource(int disk, long expectedLength, string expectedProduct,bool unbuffered=false)
    {
        if (disk < 0 || disk > 256 || expectedLength <= 0 || expectedLength > 2L * 1024 * 1024 * 1024 * 1024 || string.IsNullOrWhiteSpace(expectedProduct))
            throw new IOException("Selección de dispositivo no válida.");
        // FileAccess.Read becomes GENERIC_READ. No physical write handle is opened.
        handle = File.OpenHandle(@"\\.\PhysicalDrive" + disk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.Asynchronous | (unbuffered?(FileOptions)0x20000000:FileOptions.None));
        try
        {
            using var queryHandle = File.OpenHandle(@"\\.\PhysicalDrive" + disk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var descriptor = Query(queryHandle, 0x002D1400, new byte[12], 4096);
            if (BitConverter.ToInt32(descriptor, 28) != 7) throw new IOException("El origen no es un dispositivo USB. Operación bloqueada.");
            var product = Field(descriptor, 16);
            var serial = Field(descriptor, 24);
            var vendor = Field(descriptor, 12);
            var requestedProduct=expectedProduct.Replace(" USB Device","",StringComparison.OrdinalIgnoreCase).Trim();
            if (requestedProduct.Length==0 || !$"{vendor} {product}".Contains(requestedProduct, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Cambió el modelo del origen: {vendor} {product}.");
            var geometry = Query(queryHandle, 0x00070000, null, 24);
            SectorSize = BitConverter.ToInt32(geometry, 20);
            Length = BitConverter.ToInt64(Query(queryHandle, 0x0007405C, null, 8), 0);
            if (Length != expectedLength || SectorSize is not (512 or 4096))
                throw new IOException("Cambió la capacidad o la geometría del dispositivo. No se leerá.");
            Identity = $"PhysicalDrive{disk}|USB|{vendor}|{product}|{serial}|{Length}|{SectorSize}";
        }
        catch { handle.Dispose(); ((IDisposable)aligned).Dispose(); throw; }
    }

    public async ValueTask<int> Read(long offset, Memory<byte> buffer, CancellationToken token)
    {
        if (offset < 0 || offset % SectorSize != 0 || buffer.Length % SectorSize != 0 || buffer.Length > ImageAcquirer.BlockSize || offset > Length-buffer.Length)
            throw new IOException("Lectura fuera de límites o sin alineación.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var memory = aligned.Memory[..buffer.Length];
            int count = await RandomAccess.ReadAsync(handle, memory, offset, timeout.Token);
            memory[..count].CopyTo(buffer);
            return count;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("El dispositivo no respondió en 15 segundos. Copia detenida; no se reintentó."); }
    }

    private static byte[] Query(SafeFileHandle queryHandle, uint code, byte[]? input, int size)
    {
        var data = new byte[size];
        if (!DeviceIoControl(queryHandle, code, input, input?.Length ?? 0, data, size, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (returned < Math.Min(size,32)) throw new IOException("Respuesta de dispositivo incompleta.");
        return data;
    }
    private static string Field(byte[] data, int offsetPosition)
    {
        var offset = BitConverter.ToInt32(data,offsetPosition);
        if (offset <= 0 || offset >= data.Length) return "";
        int end = Array.IndexOf(data,(byte)0,offset); if(end<0) end=data.Length;
        return Encoding.ASCII.GetString(data,offset,end-offset).Trim();
    }
    public void Dispose() { handle.Dispose(); ((IDisposable)aligned).Dispose(); }

    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool DeviceIoControl(SafeFileHandle file,uint code,byte[]? input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);
}

internal sealed unsafe class AlignedBuffer : MemoryManager<byte>
{
    private byte* data;
    private readonly int length;
    public AlignedBuffer(int size) { length=size; data=(byte*)NativeMemory.AlignedAlloc((nuint)size,4096); if(data is null) throw new OutOfMemoryException(); }
    public override Span<byte> GetSpan() => data is null ? throw new ObjectDisposedException(nameof(AlignedBuffer)) : new(data,length);
    public override MemoryHandle Pin(int elementIndex=0)
    { if(data is null || (uint)elementIndex>(uint)length) throw new ArgumentOutOfRangeException(nameof(elementIndex)); return new MemoryHandle(data+elementIndex); }
    public override void Unpin() { }
    protected override void Dispose(bool disposing) { if(data is not null) { NativeMemory.AlignedFree(data); data=null; } }
}
