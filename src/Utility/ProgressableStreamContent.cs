using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Chronofoil.Utility;

// From https://stackoverflow.com/a/41392145/4213397
internal class ProgressableStreamContent : HttpContent
{
    private const int DefaultBufferSize = 5 * 4096;
    private readonly HttpContent _content;
    private readonly int _bufferSize;
    private readonly Action<long, long> _progress;

    public ProgressableStreamContent(HttpContent content, Action<long, long> progress) : this(content,
        DefaultBufferSize, progress)
    {
    }

    public ProgressableStreamContent(HttpContent content, int bufferSize, Action<long, long> progress)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        this._content = content;
        this._bufferSize = bufferSize;
        this._progress = progress;

        foreach (var h in content.Headers)
        {
            Headers.Add(h.Key, h.Value);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return Task.Run(async () =>
        {
            var buffer = new byte[_bufferSize];
            TryComputeLength(out var size);
            var uploaded = 0;

            await using (var inputStream = await _content.ReadAsStreamAsync())
            {
                while (true)
                {
                    var length = await inputStream.ReadAsync(buffer);
                    if (length <= 0) break;

                    uploaded += length;
                    _progress?.Invoke(uploaded, size);

                    //System.Diagnostics.Debug.WriteLine($"Bytes sent {uploaded} of {size}");

                    await stream.WriteAsync(buffer.AsMemory(0, length));
                    await stream.FlushAsync();
                }
            }

            await stream.FlushAsync();
        });
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Headers.ContentLength.GetValueOrDefault();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
        }

        base.Dispose(disposing);
    }
}