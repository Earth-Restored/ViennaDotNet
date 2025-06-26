using Serilog;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ViennaDotNet.ObjectStore.Server;

public sealed partial class NetworkServer
{
    private readonly Server _server;
    private readonly TcpListener _serverSocket;

    public NetworkServer(Server server, int port)
    {
        _server = server;
        _serverSocket = new TcpListener(IPAddress.Loopback, port);
        _serverSocket.Start();
        Log.Information($"Created server on port {port}");
    }

    public void Run()
    {
        while (true)
        {
            try
            {
                Socket socket = _serverSocket.AcceptSocket();
                Log.Information($"Connection from {socket.RemoteEndPoint}");
                Connection connection = new Connection(this, socket);
                new Thread(connection.Run).Start();
            }
            catch (SocketException ex)
            {
                Log.Warning($"Exception while accepting connection: {ex}");
            }
        }
    }

    private sealed class Connection
    {
        private readonly NetworkServer _networkServer;

        private readonly Socket _socket;

        public Connection(NetworkServer networkServer, Socket socket)
        {
            _networkServer = networkServer;
            _socket = socket;
        }

        public void Run()
        {
            try
            {
                byte[] readBuffer = new byte[65536];
                MemoryStream byteArrayOutputStream = new MemoryStream(128);
                bool close = false;
                string? lastCommand = null;
                int binaryReadLength = 0;
                while (!close)
                {
                    int readLength = _socket.Receive(readBuffer);
                    if (readLength > 0)
                    {
                        int startOffset = 0;
                        while (startOffset < readLength && !close)
                        {
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
                                    if (!HandleBinaryData(lastCommand, byteArrayOutputStream.ToArray()))
                                    {
                                        close = true;
                                        break;
                                    }

                                    lastCommand = null;
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
                                        lastCommand = Encoding.ASCII.GetString(byteArrayOutputStream.ToArray());
                                        binaryReadLength = HandleCommand(lastCommand);
                                        if (binaryReadLength == -1)
                                        {
                                            close = true;
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
                        close = true;
                    else
                        throw new InvalidOperationException();
                }
            }
            catch (SocketException ex)
            {
                Log.Warning($"Exception while reading socket: {ex}");
            }

            Log.Information("Connection closed");
        }

        private void SendMessage(string message)
        {
            try
            {
                _socket.Send(Encoding.ASCII.GetBytes(message + "\n"));
            }
            catch (SocketException ex)
            {
                Log.Warning($"Exception while sending: {ex}");
                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException shutdownEx)
                {
                    Log.Warning($"Exception while shutting down socket: {shutdownEx}");
                }
                finally
                {
                    _socket.Close();
                }
            }
        }

        private void SendData(byte[] data)
        {
            try
            {
                _socket.Send(data);
            }
            catch (SocketException ex)
            {
                Log.Warning($"Exception while sending: {ex}");
                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException shutdownEx)
                {
                    Log.Warning($"Exception while shutting down socket: {shutdownEx}");
                }
                finally
                {
                    _socket.Close();
                }
            }
        }

        private int HandleCommand(string command)
        {
            string[] parts = command.Split(' ', 2);
            if (parts.Length != 2)
                return -1;

            switch (parts[0])
            {
                case "STORE":
                    {
                        if (!int.TryParse(parts[1], out int length) || length < 0)
                            return -1;

                        if (length == 0)
                        {
                            string? id = _networkServer._server.Store([]);
                            if (id is not null)
                                SendMessage("OK " + id);
                            else
                                SendMessage("ERR");
                        }

                        return length;
                    }
                case "GET":
                    {
                        string id = parts[1];
                        if (!ValidateObjectId(id))
                            return -1;

                        byte[]? data = _networkServer._server.Load(id);
                        if (data is not null)
                        {
                            SendMessage("OK " + data.Length.ToString());
                            SendData(data);
                        }
                        else
                            SendMessage("ERR");

                        return 0;
                    }
                case "DEL":
                    {
                        string id = parts[1];
                        if (!ValidateObjectId(id))
                            return -1;

                        if (_networkServer._server.Delete(id))
                            SendMessage("OK");
                        else
                            SendMessage("ERR");

                        return 0;
                    }
                default:
                    return -1;
            }
        }

        private bool HandleBinaryData(string? command, byte[] data)
        {
            if (command is null)
                throw new InvalidOperationException();

            string[] parts = command.Split(' ', 2);
            if (parts.Length != 2)
                throw new InvalidOperationException();

            switch (parts[0])
            {
                case "STORE":
                    {
                        string? id = _networkServer._server.Store(data);
                        if (id is not null)
                            SendMessage("OK " + id);
                        else
                            SendMessage("ERR");

                        return true;
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    private static bool ValidateObjectId(string id)
    {
        if (!GetRegex1().IsMatch(id))
            return false;

        return true;
    }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex GetRegex1();
}
