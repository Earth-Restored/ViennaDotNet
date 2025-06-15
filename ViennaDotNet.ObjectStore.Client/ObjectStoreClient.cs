using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace ViennaDotNet.ObjectStore.Client;

public class ObjectStoreClient
{
    public static ObjectStoreClient create(string connectionString)
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

    private readonly Socket socket;
    private readonly BlockingCollection<object> outgoingMessageQueue = [];
    private readonly Thread outgoingThread;
    private readonly Thread incomingThread;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    private bool closed = false;

    private Command? currentCommand = null;
    private LinkedList<Command> queuedCommands = new();

    private ObjectStoreClient(Socket socket)
    {
        this.socket = socket;

        outgoingThread = new Thread(() =>
        {
            try
            {
                for (; ; )
                {
                    object message = outgoingMessageQueue.Take();
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
                    closed = true;
            }

            initiateClose();
        });

        incomingThread = new Thread(() =>
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
                        if (closed)
                            break;

                    int readLength = socket.Receive(readBuffer);
                    if (readLength > 0)
                    {
                        int startOffset = 0;
                        while (startOffset < readLength)
                        {
                            lock (_lock)
                                if (closed)
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
                                    if (!handleBinaryData(lastMessage, byteArrayOutputStream.ToArray()))
                                    {
                                        initiateClose();
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
                                        binaryReadLength = handleMessage(lastMessage);
                                        if (binaryReadLength == -1)
                                        {
                                            initiateClose();
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
                        initiateClose();
                    else
                        throw new InvalidOperationException();

                    Thread.Sleep(0);
                }
            }

            catch (SocketException)
            {
                lock (_lock)
                    closed = true;
            }

            initiateClose();

            lock (_lock)
            {
                if (currentCommand != null)
                {
                    currentCommand.completableFuture.TrySetResult(currentCommand.type == Command.Type.DELETE ? false : null);
                    currentCommand = null;
                }

                foreach (var command in queuedCommands)
                {
                    command.completableFuture.TrySetResult(command.type == Command.Type.DELETE ? false : null);
                }

                queuedCommands.Clear();
            }
        });

        outgoingThread.Start();
        incomingThread.Start();
    }

    public void close()
    {
        initiateClose();

        for (; ; )
        {
            try
            {
                incomingThread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }

        for (; ; )
        {
            try
            {
                outgoingThread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }
    }

    private void initiateClose()
    {
        lock (_lock)
            closed = true;

        try
        {
            socket.Shutdown(SocketShutdown.Both);
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
            socket.Close();
        }

        outgoingThread.Interrupt();
    }

    public TaskCompletionSource<object?> store(byte[] data)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        queueCommand(new Command(Command.Type.STORE, data, completableFuture));
        return completableFuture;
    }

    public TaskCompletionSource<object?> get(string id)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        queueCommand(new Command(Command.Type.GET, id, completableFuture));
        return completableFuture;
    }

    public TaskCompletionSource<object?> delete(string id)
    {
        TaskCompletionSource<object?> completableFuture = new TaskCompletionSource<object?>();
        queueCommand(new Command(Command.Type.DELETE, id, completableFuture));
        return completableFuture;
    }

    private void queueCommand(Command command)
    {
        lock (_lock)
        {
            if (closed)
                command.completableFuture.TrySetResult(command.type == Command.Type.DELETE ? false : null);
            else
            {
                queuedCommands.AddLast(command);
                if (currentCommand == null)
                    sendNextCommand();
            }
        }
    }

    private void sendNextCommand()
    {
        lock (_lock)
        {
            currentCommand = null;

            if (closed)
                return;

            if (queuedCommands.Count != 0)
            {
                currentCommand = queuedCommands.First!.Value;
                queuedCommands.RemoveFirst();
                switch (currentCommand.type)
                {
                    case Command.Type.STORE:
                        {
                            sendMessage("STORE " + ((byte[])currentCommand.data).Length + "\n");
                            sendMessage(currentCommand.data);
                            break;
                        }
                    case Command.Type.GET:
                        {
                            sendMessage("GET " + ((string)currentCommand.data) + "\n");
                            break;
                        }
                    case Command.Type.DELETE:
                        {
                            sendMessage("DEL " + ((string)currentCommand.data) + "\n");
                            break;
                        }
                }
            }
        }
    }

    private int handleMessage(string message)
    {
        lock (_lock)
        {
            if (closed)
                return -1;

            if (currentCommand == null)
                return -1;

            string[] parts = message.Split(' ', 2);
            switch (currentCommand.type)
            {
                case Command.Type.STORE:
                    {
                        if (parts[0] == "OK")
                        {
                            if (parts.Length != 2)
                                return -1;

                            currentCommand.completableFuture.TrySetResult(parts[1]);
                            sendNextCommand();
                            return 0;
                        }
                        else if (parts[0] == "ERR")
                        {
                            currentCommand.completableFuture.TrySetResult(null);
                            sendNextCommand();
                            return 0;
                        }
                        else
                            return -1;
                    }
                case Command.Type.GET:
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
                                    currentCommand.completableFuture.TrySetResult(Array.Empty<byte>());
                                    sendNextCommand();
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
                            currentCommand.completableFuture.TrySetResult(null);
                            sendNextCommand();
                            return 0;
                        }
                        else
                            return -1;
                    }
                case Command.Type.DELETE:
                    {
                        if (parts[0] == "OK")
                        {
                            currentCommand.completableFuture.TrySetResult(true);
                            sendNextCommand();
                            return 0;
                        }
                        else if (parts[0] == "ERR")
                        {
                            currentCommand.completableFuture.TrySetResult(false);
                            sendNextCommand();
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

    private bool handleBinaryData(string message, byte[] data)
    {
        lock (_lock)
        {
            if (closed)
                return false;

            if (currentCommand == null)
                throw new InvalidOperationException();

            string[] parts = message.Split(' ', 2);
            if (parts.Length != 2)
                throw new InvalidOperationException();

            switch (currentCommand.type)
            {
                case Command.Type.GET:
                    {
                        if (parts[0] == "OK")
                        {
                            currentCommand.completableFuture.TrySetResult(data);
                            sendNextCommand();
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

    private void sendMessage(object message)
    {
        lock (_lock)
            if (closed)
                throw new InvalidOperationException();

        for (; ; )
        {
            try
            {
                outgoingMessageQueue.Add(message);
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
        public readonly Type type;
        public readonly object data;
        public readonly TaskCompletionSource<object?> completableFuture;

        public enum Type
        {
            STORE,
            GET,
            DELETE
        }

        public Command(Type type, object data, TaskCompletionSource<object?> completableFuture)
        {
            this.type = type;
            this.data = data;
            this.completableFuture = completableFuture;
        }
    }
}
