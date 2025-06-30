using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Chronofoil.Utility;

public class ProgressReportingStream : Stream
{
    private readonly Stream _innerStream;
    private readonly ProgressHolder _progress;
    private long _totalBytesRead;

    public ProgressReportingStream(Stream innerStream, ProgressHolder progress)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));

        if (!_innerStream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(innerStream));

        _totalBytesRead = 0;
        ReportProgress();
    }
    
    private void ReportProgress()
    {
        _progress.Set(_totalBytesRead, Length);
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _innerStream.Length;
    
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _totalBytesRead += bytesRead;
            ReportProgress();
        }
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
        {
            _totalBytesRead += bytesRead;
            ReportProgress();
        }
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
        {
            _totalBytesRead += bytesRead;
            ReportProgress();
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _innerStream.Seek(offset, origin);
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }
}