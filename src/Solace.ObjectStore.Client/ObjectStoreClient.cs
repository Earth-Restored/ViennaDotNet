using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace ViennaDotNet.ObjectStore.Client;

public class ObjectStoreClient : IAsyncDisposable
{
    public class ConnectException : ObjectStoreClientException
    {
        public ConnectException(string? message)
            : base(message)
        {
        }

        public ConnectException(string? message, Exception? cause)
            : base(message, cause)
        {
        }
    }

    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly Channel<Command> _commandQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    public static async Task<ObjectStoreClient> ConnectAsync(string connectionString)
    {
        string[] parts = connectionString.Split(':', 2);
        string host = parts[0];
        if (!int.TryParse(parts.Length > 1 ? parts[1] : "5396", out int port) || port is <= 0 or > 65535)
        {
            throw new ArgumentException($"Invalid port number in connection string.");
        }

        Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(host, port);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new ConnectException($"Could not create socket: {ex.Message}", ex);
        }

        return new ObjectStoreClient(socket);
    }

    private ObjectStoreClient(Socket socket)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);

        _commandQueue = Channel.CreateUnbounded<Command>();

        _processingTask = Task.Run(ProcessConnectionAsync);
    }

    public async Task<string?> StoreAsync(ReadOnlyMemory<byte> data)
    {
        var result = await EnqueueCommand(CommandType.Store, data);
        return (string?)result;
    }

    public async Task<byte[]?> GetAsync(string id)
    {
        var result = await EnqueueCommand(CommandType.Get, id);
        return (byte[]?)result;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await EnqueueCommand(CommandType.Delete, id);
        return (bool)result!;
    }

    private Task<object?> EnqueueCommand(CommandType type, object data)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_commandQueue.Writer.TryWrite(new Command(type, data, tcs)))
        {
            tcs.SetException(new ObjectDisposedException(nameof(ObjectStoreClient)));
        }

        return tcs.Task;
    }

    private async Task ProcessConnectionAsync()
    {
        var reader = PipeReader.Create(_stream);
        var writer = PipeWriter.Create(_stream);

        Command? activeCommand = null;

        try
        {
            await foreach (var command in _commandQueue.Reader.ReadAllAsync(_cts.Token))
            {
                activeCommand = command;
                await WriteCommandAsync(writer, command);
                await ReadResponseAsync(reader, command);
                activeCommand = null;
            }
        }
        catch (Exception ex)
        {
            activeCommand?.Tcs.TrySetException(ex);
            FaultPendingCommands(ex);
        }
        finally
        {
            await reader.CompleteAsync();
            await writer.CompleteAsync();
            _socket.Close();
        }
    }

    private async Task WriteCommandAsync(PipeWriter writer, Command command)
    {
        switch (command.Type)
        {
            case CommandType.Store:
                var memory = (ReadOnlyMemory<byte>)command.Data;
                var header = Encoding.ASCII.GetBytes($"STORE {memory.Length}\n");

                writer.Write(header);
                writer.Write(memory.Span);

                await writer.FlushAsync(_cts.Token);
                break;
            case CommandType.Get:
                await writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {(string)command.Data}\n"), _cts.Token);
                break;
            case CommandType.Delete:
                await writer.WriteAsync(Encoding.ASCII.GetBytes($"DEL {(string)command.Data}\n"), _cts.Token);
                break;
        }
    }

    private async Task ReadResponseAsync(PipeReader reader, Command command)
    {
        Range[] partsArray = ArrayPool<Range>.Shared.Rent(2);
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync(_cts.Token);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (TryReadMessage(ref buffer, out ReadOnlySequence<byte> line))
                {
                    var message = Encoding.ASCII.GetString(line).AsSpan().Trim('\r');
                    var parts = partsArray.AsSpan(0, 2);
                    var partsLength = message.Split(parts, ' ');
                    var partsLocal = parts[..partsLength];

                    reader.AdvanceTo(buffer.Start, result.Buffer.End);

                    if (message[partsLocal[0]] is "ERR")
                    {
                        command.Tcs.TrySetResult(command.Type is CommandType.Delete ? false : null);
                        return;
                    }

                    if (message[partsLocal[0]] is "OK")
                    {
                        if (command.Type is CommandType.Delete)
                        {
                            command.Tcs.TrySetResult(true);
                            return;
                        }

                        if (command.Type is CommandType.Store)
                        {
                            command.Tcs.TrySetResult(partsLocal.Length > 1 ? message[partsLocal[1]].ToString() : null);
                            return;
                        }

                        if (command.Type is CommandType.Get && partsLocal.Length is 2 && int.TryParse(message[partsLocal[1]], out int length))
                        {
                            await ReadBinaryPayloadAsync(reader, length, command);
                            return;
                        }
                    }

                    throw new InvalidOperationException("Invalid server response format.");
                }

                reader.AdvanceTo(buffer.Start, result.Buffer.End);

                if (result.IsCompleted)
                {
                    throw new EndOfStreamException("Server closed the connection.");
                }
            }
        }
        finally
        {
            ArrayPool<Range>.Shared.Return(partsArray);
        }
    }

    private async Task ReadBinaryPayloadAsync(PipeReader reader, int length, Command command)
    {
        if (length is 0)
        {
            command.Tcs.TrySetResult(Array.Empty<byte>());
            return;
        }

        while (true)
        {
            ReadResult result = await reader.ReadAsync(_cts.Token);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.Length >= length)
            {
                byte[] data = buffer.Slice(0, length).ToArray();
                command.Tcs.TrySetResult(data);

                reader.AdvanceTo(buffer.GetPosition(length));
                return;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Incomplete binary payload received.");
            }
        }
    }

    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private void FaultPendingCommands(Exception ex)
    {
        _commandQueue.Writer.TryComplete();
        while (_commandQueue.Reader.TryRead(out var cmd))
        {
            cmd.Tcs.TrySetException(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _commandQueue.Writer.TryComplete();

        try
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        _stream.Dispose();
        _socket.Dispose();
    }

    private enum CommandType
    {
        Store,
        Get,
        Delete,
    }

    private readonly record struct Command(CommandType Type, object Data, TaskCompletionSource<object?> Tcs);
}
