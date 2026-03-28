using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace ViennaDotNet.ObjectStore.Client;

public class ObjectStoreClient : IDisposable
{
    public static ObjectStoreClient Create(string connectionString)
    {
        string[] parts = connectionString.Split(':', 2);
        string host = parts[0];
        int port;
        try
        {
            port = parts.Length > 1 ? int.Parse(parts[1]) : 5396;
        }
        catch (FormatException)
        {
            throw new ArgumentException($"Invalid port number \"{parts[1]}\"");
        }

        if (port <= 0 || port > 65535)
            throw new ArgumentException("Port number out of range");

        Socket socket;
        try
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(host, port);
        }
        catch (SocketException ex)
        {
            throw new ConnectException($"Could not create socket: {ex}");
        }

        return new ObjectStoreClient(socket);
    }

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
    private readonly BlockingCollection<object> _outgoingMessageQueue = [];
    private readonly Thread _outgoingThread;
    private readonly Thread _incomingThread;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    private bool _closed = false;

    private Command? _currentCommand = null;
    private readonly LinkedList<Command> _queuedCommands = new();

    private ObjectStoreClient(Socket socket)
    {
        _socket = socket;

        _outgoingThread = new Thread(() =>
        {
            try
            {
                for (; ; )
                {
                    object message = _outgoingMessageQueue.Take();
                    if (message is string command)
                        socket.Send(Encoding.ASCII.GetBytes(command));
                    else if (message is byte[] data)
                        socket.Send(data);
                    else
                        throw new InvalidOperationException();

                    Thread.Sleep(0);
                }
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
            catch (SocketException)
            {
                lock (_lock)
                    _closed = true;
            }

            InitiateClose();
        });

        _incomingThread = new Thread(() =>
        {
            try
            {
                byte[] readBuffer = new byte[65536];
                MemoryStream byteArrayOutputStream = new MemoryStream(128);
                string? lastMessage = null;
                int binaryReadLength = 0;
                for (; ; )
                {
                    lock (_lock)
                        if (_closed)
                            break;

                    int readLength = socket.Receive(readBuffer);
                    if (readLength > 0)
                    {
                        int startOffset = 0;
                        while (startOffset < readLength)
                        {
                            lock (_lock)
                                if (_closed)
                                    break;

                            if (binaryReadLength > 0)
                            {
                                if (startOffset + binaryReadLength > readLength)
                                {
                                    byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                                    binaryReadLength -= readLength - startOffset;
                                    startOffset += readLength - startOffset;
                                }
                                else
                                {
                                    byteArrayOutputStream.Write(readBuffer, startOffset, binaryReadLength);
                                    if (!HandleBinaryData(lastMessage, byteArrayOutputStream.ToArray()))
                                    {
                                        InitiateClose();
                                        break;
                                    }

                                    lastMessage = null;
                                    byteArrayOutputStream = new MemoryStream(128);
                                    startOffset += binaryReadLength;
                                    binaryReadLength = 0;
                                }
                            }
                            else
                            {
                                for (int offset = startOffset; offset < readLength; offset++)
                                {
                                    if (readBuffer[offset] == '\n')
                                    {
                                        byteArrayOutputStream.Write(readBuffer, startOffset, offset - startOffset);
                                        lastMessage = Encoding.ASCII.GetString(byteArrayOutputStream.ToArray());
                                        binaryReadLength = HandleMessage(lastMessage);
                                        if (binaryReadLength == -1)
                                        {
                                            InitiateClose();
                                            break;
                                        }

                                        byteArrayOutputStream = new MemoryStream(128);
                                        startOffset = offset + 1;
                                        break;
                                    }
                                    else if (offset == readLength - 1)
                                    {
                                        byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                                        startOffset = readLength;
                                    }
                                }
                            }
                        }
                    }
                    else if (readLength == 0)
                        InitiateClose();
                    else
                        throw new InvalidOperationException();

                    Thread.Sleep(0);
                }
            }

            catch (SocketException)
            {
                lock (_lock)
                    _closed = true;
            }

            InitiateClose();

            lock (_lock)
            {
                if (_currentCommand is not null)
                {
                    _currentCommand.CompletableFuture.TrySetResult(_currentCommand.Type == Command.TypeE.DELETE ? false : null);
                    _currentCommand = null;
                }

                foreach (var command in _queuedCommands)
                {
                    command.CompletableFuture.TrySetResult(command.Type == Command.TypeE.DELETE ? false : null);
                }

                _queuedCommands.Clear();
            }
        });

        _outgoingThread.Start();
        _incomingThread.Start();
    }

    public void Close()
    {
        InitiateClose();

        while (true)
        {
            try
            {
                _incomingThread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }

        while (true)
        {
            try
            {
                _outgoingThread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }
    }

    public void Dispose()
        => Close();

    private void InitiateClose()
    {
        lock (_lock)
            _closed = true;

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // empty
        }
        catch (ObjectDisposedException)
        {
            // empty
        }
        finally
        {
            _socket.Close();
        }

        _outgoingThread.Interrupt();
    }

    public TaskCompletionSource<object?> Store(byte[] data)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        QueueCommand(new Command(Command.TypeE.STORE, data, completableFuture));
        return completableFuture;
    }

    public TaskCompletionSource<object?> Get(string id)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        QueueCommand(new Command(Command.TypeE.GET, id, completableFuture));
        return completableFuture;
    }

    public TaskCompletionSource<object?> Delete(string id)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        QueueCommand(new Command(Command.TypeE.DELETE, id, completableFuture));
        return completableFuture;
    }

    private void QueueCommand(Command command)
    {
        lock (_lock)
        {
            if (_closed)
                command.CompletableFuture.TrySetResult(command.Type == Command.TypeE.DELETE ? false : null);
            else
            {
                _queuedCommands.AddLast(command);
                if (_currentCommand is null)
                    SendNextCommand();
            }
        }
    }

    private void SendNextCommand()
    {
        lock (_lock)
        {
            _currentCommand = null;

            if (_closed)
                return;

            if (_queuedCommands.Count != 0)
            {
                _currentCommand = _queuedCommands.First!.Value;
                _queuedCommands.RemoveFirst();
                switch (_currentCommand.Type)
                {
                    case Command.TypeE.STORE:
                        {
                            SendMessage("STORE " + ((byte[])_currentCommand.Data).Length + "\n");
                            SendMessage(_currentCommand.Data);
                            break;
                        }
                    case Command.TypeE.GET:
                        {
                            SendMessage("GET " + ((string)_currentCommand.Data) + "\n");
                            break;
                        }
                    case Command.TypeE.DELETE:
                        {
                            SendMessage("DEL " + ((string)_currentCommand.Data) + "\n");
                            break;
                        }
                }
            }
        }
    }

    private int HandleMessage(string message)
    {
        lock (_lock)
        {
            if (_closed)
                return -1;

            if (_currentCommand is null)
                return -1;

            string[] parts = message.Split(' ', 2);
            switch (_currentCommand.Type)
            {
                case Command.TypeE.STORE:
                    {
                        if (parts[0] == "OK")
                        {
                            if (parts.Length != 2)
                                return -1;

                            _currentCommand.CompletableFuture.TrySetResult(parts[1]);
                            SendNextCommand();
                            return 0;
                        }
                        else if (parts[0] == "ERR")
                        {
                            _currentCommand.CompletableFuture.TrySetResult(null);
                            SendNextCommand();
                            return 0;
                        }
                        else
                            return -1;
                    }
                case Command.TypeE.GET:
                    {
                        if (parts[0] == "OK")
                        {
                            if (parts.Length != 2)
                                return -1;

                            try
                            {
                                int length = int.Parse(parts[1]);
                                if (length < 0)
                                    return -1;

                                if (length == 0)
                                {
                                    _currentCommand.CompletableFuture.TrySetResult(Array.Empty<byte>());
                                    SendNextCommand();
                                }

                                return length;
                            }
                            catch (FormatException)
                            {
                                return -1;
                            }
                        }
                        else if (parts[0] == "ERR")
                        {
                            _currentCommand.CompletableFuture.TrySetResult(null);
                            SendNextCommand();
                            return 0;
                        }
                        else
                            return -1;
                    }
                case Command.TypeE.DELETE:
                    {
                        if (parts[0] == "OK")
                        {
                            _currentCommand.CompletableFuture.TrySetResult(true);
                            SendNextCommand();
                            return 0;
                        }
                        else if (parts[0] == "ERR")
                        {
                            _currentCommand.CompletableFuture.TrySetResult(false);
                            SendNextCommand();
                            return 0;
                        }
                        else
                            return -1;
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    private bool HandleBinaryData(string message, byte[] data)
    {
        lock (_lock)
        {
            if (_closed)
                return false;

            if (_currentCommand is null)
                throw new InvalidOperationException();

            string[] parts = message.Split(' ', 2);
            if (parts.Length != 2)
                throw new InvalidOperationException();

            switch (_currentCommand.Type)
            {
                case Command.TypeE.GET:
                    {
                        if (parts[0] == "OK")
                        {
                            _currentCommand.CompletableFuture.TrySetResult(data);
                            SendNextCommand();
                            return true;
                        }
                        else
                            throw new InvalidOperationException();
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    private void SendMessage(object message)
    {
        lock (_lock)
            if (_closed)
                throw new InvalidOperationException();

        while (true)
        {
            try
            {
                _outgoingMessageQueue.Add(message);
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }
    }

    private sealed class Command
    {
        public readonly TypeE Type;
        public readonly object Data;
        public readonly TaskCompletionSource<object?> CompletableFuture;

        public enum TypeE
        {
            STORE,
            GET,
            DELETE,
            UPDATE,
        }

        public Command(TypeE type, object data, TaskCompletionSource<object?> completableFuture)
        {
            Type = type;
            Data = data;
            CompletableFuture = completableFuture;
        }
    }
}
