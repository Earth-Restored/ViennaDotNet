using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static ViennaDotNet.Common.Utils.Java;

namespace ViennaDotNet.ObjectStore.Server
{
    public class NetworkServer
    {
        private readonly Server server;
        private readonly TcpListener serverSocket;

        public NetworkServer(Server server, int port)
        {
            this.server = server;
            serverSocket = new TcpListener(IPAddress.Loopback, port);
            //serverSocket.Server.ReceiveTimeout = Timeout;
            //serverSocket.Server.SendTimeout = Timeout;
            serverSocket.Start();
            Log.Information($"Created server on port {port}");
        }

        public void run()
        {
            for (; ; )
            {
                try
                {
                    Socket socket = serverSocket.AcceptSocket();
                    Log.Information($"Connection from {socket.RemoteEndPoint}");
                    Connection connection = new Connection(this, socket);
                    new Thread(connection.run).Start();
                }
                catch (IOException exception)
                {
                    Log.Warning($"Exception while accepting connection: {exception}");
                }
            }
        }

        private sealed class Connection
        {
            private readonly NetworkServer networkServer;

            private readonly Socket socket;

            public Connection(NetworkServer networkServer, Socket socket)
            {
                this.networkServer = networkServer;
                this.socket = socket;
            }

            public void run()
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
                        int readLength = socket.Receive(readBuffer);
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
                                        if (!handleBinaryData(lastCommand, byteArrayOutputStream.ToArray()))
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
                                            binaryReadLength = this.handleCommand(lastCommand);
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
                        else if (readLength == -1)
                            close = true;
                        else
                            throw new InvalidOperationException();
                    }
                }
                catch (SocketException exception)
                {
                    Log.Warning($"Exception while reading socket: {exception}");
                }
                Log.Information("Connection closed");
            }

            private void sendMessage(string message)
            {
                try
                {
                    socket.Send(Encoding.ASCII.GetBytes(message + "\n"));
                }
                catch (IOException exception)
                {
                    Log.Warning($"Exception while sending: {exception}");
                    try
                    {
                        socket.Close();
                    }
                    catch (IOException exception1)
                    {
                        Log.Warning($"Exception while closing socket: {exception1}");
                    }
                }
            }

            private void sendData(byte[] data)
            {
                try
                {
                    socket.Send(data);
                }
                catch (IOException exception)
                {
                    Log.Warning($"Exception while sending: {exception}");
                    try
                    {
                        socket.Close();
                    }
                    catch (IOException exception1)
                    {
                        Log.Warning($"Exception while closing socket: {exception1}");
                    }
                }
            }

            private int handleCommand(string command)
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
                            else
                                return length;
                        }
                    case "GET":
                        {
                            string id = parts[1];
                            if (!validateObjectId(id))
                            {
                                return -1;
                            }
                            byte[]? data = networkServer.server.load(id);
                            if (data != null)
                            {
                                sendMessage("OK " + data.Length.ToString());
                                sendData(data);
                            }
                            else
                                sendMessage("ERR");

                            return 0;
                        }
                    case "DEL":
                        {
                            string id = parts[1];
                            if (!validateObjectId(id))
                                return -1;

                            if (networkServer.server.delete(id))
                                sendMessage("OK");
                            else
                                sendMessage("ERR");

                            return 0;
                        }
                    default:
                        return -1;
                }
            }

            private bool handleBinaryData(string? command, byte[] data)
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
                            string? id = networkServer.server.store(data);
                            if (id != null)
                                sendMessage("OK " + id);
                            else
                                sendMessage("ERR");

                            return true;
                        }
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        private static bool validateObjectId(string id)
        {
            if (!Regex.IsMatch(id, "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))
                return false;

            return true;
        }
    }
}
