﻿using Npgsql.FrontendMessages;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Npgsql
{
    /// <summary>
    /// An interface to remotely control the seekable stream for an opened large object on a PostgreSQL server.
    /// Note that the OpenRead/OpenReadWrite method as well as all operations performed on this stream must be wrapped inside a database transaction.
    /// </summary>
    public class NpgsqlLargeObjectStream : Stream
    {
        NpgsqlLargeObjectManager _manager;
        uint _oid;
        int _fd;
        long _pos;
        bool _writeable;
        bool _disposed;

        private NpgsqlLargeObjectStream() { }

        internal NpgsqlLargeObjectStream(NpgsqlLargeObjectManager manager, uint oid, int fd, bool writeable)
        {
            _manager = manager;
            _oid = oid;
            _fd = fd;
            _pos = 0;
            _writeable = writeable;
        }

        void CheckDisposed()
        {
            if (_disposed)
                throw new InvalidOperationException("Object disposed");
        }

        /// <summary>
        /// Reads up to <i>count</i> bytes from the large object into the buffer.
        /// A return value of 0 indicates end of file.
        /// </summary>
        /// <param name="buffer">The buffer where read data should be stored.</param>
        /// <param name="offset">The offset in the buffer where the first byte should be stored.</param>
        /// <param name="count">The maximum number of bytes that should be read.</param>
        /// <returns>How many bytes actually read, or 0 if end of file was already reached.</returns>
        [GenerateAsync]
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset or count for this buffer");
            Contract.EndContractBlock();

            CheckDisposed();

            count = Math.Min(count, _manager.MaxTransferBlockSize);

            _manager._connection.CheckConnectionReady();
            using (_manager._connection.Connector.BlockNotifications())
            {
                var bytesRead = _manager.ExecuteFunction(NpgsqlLargeObjectManager.Function.loread, -1, NpgsqlLargeObjectManager.GetInt32(_fd), NpgsqlLargeObjectManager.GetInt32(count));
                _pos += bytesRead;
                _manager._connection.Connector.Buffer.ReadBytes(buffer, offset, bytesRead, true);
                _manager.EatReadyForQuery();
                return bytesRead;
            }
        }

        /// <summary>
        /// Reads <i>count</i> bytes from the large object. The only case when fewer bytes are read is when end of stream is reached.
        /// </summary>
        /// <param name="buffer">The buffer where read data should be stored.</param>
        /// <param name="offset">The offset in the buffer where the first byte should be stored.</param>
        /// <param name="count">The maximum number of bytes that should be read.</param>
        /// <returns>How many bytes actually read, or 0 if end of file was already reached.</returns>
        [GenerateAsync]
        public int ReadAll(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset or count for this buffer");
            Contract.EndContractBlock();

            CheckDisposed();

            int chunkCount = Math.Min(count, _manager.MaxTransferBlockSize);
            int read = 0;

            _manager._connection.CheckConnectionReady();
            using (_manager._connection.Connector.BlockNotifications())
            {
                while (read < count)
                {
                    var bytesRead = _manager.ExecuteFunction(NpgsqlLargeObjectManager.Function.loread, -1, NpgsqlLargeObjectManager.GetInt32(_fd), NpgsqlLargeObjectManager.GetInt32(chunkCount));
                    _pos += bytesRead;
                    _manager._connection.Connector.Buffer.ReadBytes(buffer, offset + read, bytesRead, true);
                    read += bytesRead;
                    _manager.EatReadyForQuery();
                    if (bytesRead < chunkCount)
                    {
                        return bytesRead;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Writes <i>count</i> bytes to the large object.
        /// </summary>
        /// <param name="buffer">The buffer to write data from.</param>
        /// <param name="offset">The offset in the buffer at which to begin copying bytes.</param>
        /// <param name="count">The number of bytes to write.</param>
        [GenerateAsync]
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset or count for this buffer");
            Contract.EndContractBlock();

            CheckDisposed();

            if (!_writeable)
                throw new NotSupportedException("Write cannot be called on a stream opened with no write permissions");

            int totalWritten = 0;

            while (totalWritten < count)
            {
                var chunkSize = Math.Min(count - totalWritten, _manager.MaxTransferBlockSize);
                var bytesWritten = _manager.ExecuteFunctionInt32(NpgsqlLargeObjectManager.Function.lowrite, NpgsqlLargeObjectManager.GetInt32(_fd), new FastpathMessage.ByteArraySlice(buffer, offset + totalWritten, chunkSize));
                totalWritten += bytesWritten;

                if (bytesWritten != chunkSize)
                    throw PGUtil.ThrowIfReached();

                _pos += bytesWritten;
            }
        }

        /// <summary>
        /// CanTimeout always returns false.
        /// </summary>
        public override bool CanTimeout
        {
            get
            {
                return false; // TODO
            }
        }

        /// <summary>
        /// CanRead always returns true, unless the stream has been closed.
        /// </summary>
        public override bool CanRead
        {
            get { return true && !_disposed; }
        }

        /// <summary>
        /// CanWrite returns true if the stream was opened with write permissions, and the stream has not been closed.
        /// </summary>
        public override bool CanWrite
        {
            get { return _writeable && !_disposed; }
        }

        /// <summary>
        /// CanSeek always returns true, unless the stream has been closed.
        /// </summary>
        public override bool CanSeek
        {
            get { return true && !_disposed; }
        }

        /// <summary>
        /// Returns the current position in the stream. Getting the current position does not need a round-trip to the server, however setting the current position does.
        /// </summary>
        public override long Position
        {
            get
            {
                CheckDisposed();
                return _pos;
            }
            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Gets the length of the large object. This internally seeks to the end of the stream to retrieve the length, and then back again.
        /// </summary>
        public override long Length
        {
            get { return GetLengthInternal(); }
        }

        // TODO: uncomment this
        /*public Task<long> GetLengthAsync()
        {
            return GetLengthInternalAsync();
        }*/

        [GenerateAsync]
        long GetLengthInternal()
        {
            CheckDisposed();
            long old = _pos;
            long retval = Seek(0, SeekOrigin.End);
            if (retval != old)
                Seek(old, SeekOrigin.Begin);
            return retval;
        }

        /// <summary>
        /// Seeks in the stream to the specified position. This requires a round-trip to the backend.
        /// </summary>
        /// <param name="offset">A byte offset relative to the <i>origin</i> parameter.</param>
        /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns></returns>
        [GenerateAsync]
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin < SeekOrigin.Begin || origin > SeekOrigin.End)
                throw new ArgumentException("Invalid origin");
            if (!_manager.Has64BitSupport && offset != (long)(int)offset)
                throw new ArgumentOutOfRangeException("offset", "offset must fit in 32 bits for PostgreSQL versions older than 9.3");
            Contract.EndContractBlock();

            CheckDisposed();

            if (_manager.Has64BitSupport)
                return _pos = _manager.ExecuteFunctionInt64(NpgsqlLargeObjectManager.Function.lo_lseek64,
                    NpgsqlLargeObjectManager.GetInt32(_fd),
                    NpgsqlLargeObjectManager.GetInt64(offset),
                    NpgsqlLargeObjectManager.GetInt32((int)origin));

            else
                return _pos = _manager.ExecuteFunctionInt32(NpgsqlLargeObjectManager.Function.lo_lseek,
                    NpgsqlLargeObjectManager.GetInt32(_fd),
                    NpgsqlLargeObjectManager.GetInt32((int)offset),
                    NpgsqlLargeObjectManager.GetInt32((int)origin));
        }

        /// <summary>
        /// Does nothing.
        /// </summary>
        [GenerateAsync]
        public override void Flush()
        {
        }

        /// <summary>
        /// Truncates or enlarges the large object to the given size. If enlarging, the large object is extended with null bytes.
        /// For PostgreSQL versions earlier than 9.3, the value must fit in an Int32.
        /// </summary>
        /// <param name="value">Number of bytes to either truncate or enlarge the large object.</param>
        [GenerateAsync]
        public override void SetLength(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException("value");
            if (!_manager.Has64BitSupport && value != (long)(int)value)
                throw new ArgumentOutOfRangeException("value", "offset must fit in 32 bits for PostgreSQL versions older than 9.3");
            Contract.EndContractBlock();

            CheckDisposed();

            if (!_writeable)
                throw new NotSupportedException("SetLength cannot be called on a stream opened with no write permissions");

            if (_manager.Has64BitSupport)
                _manager.ExecuteFunctionInt32(NpgsqlLargeObjectManager.Function.lo_truncate64,
                    NpgsqlLargeObjectManager.GetInt32(_fd),
                    NpgsqlLargeObjectManager.GetInt64(value));

            else
                _manager.ExecuteFunctionInt32(NpgsqlLargeObjectManager.Function.lo_truncate,
                    NpgsqlLargeObjectManager.GetInt32(_fd),
                    NpgsqlLargeObjectManager.GetInt32((int)value));
        }

        /// <summary>
        /// Releases resources at the backend allocated for this stream.
        /// </summary>
        [GenerateAsync]
        public override void Close()
        {
            if (!_disposed)
            {
                _manager.ExecuteFunctionInt32(NpgsqlLargeObjectManager.Function.lo_close, NpgsqlLargeObjectManager.GetInt32(_fd));
                _disposed = true;
            }
        }

        /// <summary>
        /// Releases resources at the backend allocated for this stream, iff disposing is true.
        /// </summary>
        /// <param name="disposing">Whether to release resources allocated at the backend.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();
            }
        }
    }
}
