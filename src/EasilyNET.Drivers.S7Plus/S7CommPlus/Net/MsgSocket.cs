// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Net.Sockets;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

// 
internal sealed class MsgSocket : IDisposable
{
    private Socket? TCPSocket;
    public int LastError;

    public MsgSocket()
    {
    }

    ~MsgSocket()
    {
        Close();
    }

    public void Close()
    {
        TCPSocket?.Dispose();
        TCPSocket = null;
    }

    private void CreateSocket()
    {
        TCPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
    }

    public int Connect(string Host, int Port)
    {
        LastError = 0;
        if (Connected)
        {
            return LastError;
        }

        // 释放可能残留的上一次失败连接的 socket，避免句柄泄漏
        Close();

        try
        {
            CreateSocket();
            // 同步 Socket.Connect 会忽略 ConnectTimeout（不可达主机时阻塞约 21 秒），
            // 这里用带取消令牌的异步连接来真正实施连接超时。
            using var cts = new CancellationTokenSource(ConnectTimeout > 0 ? ConnectTimeout : Timeout.Infinite);
            TCPSocket!.ConnectAsync(Host, Port, cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            LastError = S7Consts.errTCPConnectionTimeout;
            Close();
        }
        catch
        {
            LastError = S7Consts.errTCPConnectionFailed;
            Close();
        }
        return LastError;
    }

    private int WaitForData(int Size, int Timeout)
    {
        var Expired = false;
        int SizeAvail;
        var Elapsed = Environment.TickCount;
        LastError = 0;
        try
        {
            SizeAvail = TCPSocket?.Available ?? 0;
            while ((SizeAvail < Size) && (!Expired))
            {
                Thread.Sleep(2);
                SizeAvail = TCPSocket?.Available ?? 0;
                Expired = Environment.TickCount - Elapsed > Timeout;
                // If timeout we clean the buffer
                if (Expired && (SizeAvail > 0))
                {
                    try
                    {
                        var Flush = new byte[SizeAvail];
                        TCPSocket?.Receive(Flush, 0, SizeAvail, SocketFlags.None);
                    }
                    catch { }
                }
            }
        }
        catch
        {
            LastError = S7Consts.errTCPDataReceive;
        }
        if (Expired)
        {
            LastError = S7Consts.errTCPDataReceive;
        }
        return LastError;
    }

    public int Receive(byte[] Buffer, int Start, int Size)
    {

        var BytesRead = 0;
        LastError = WaitForData(Size, ReadTimeout);
        if (LastError == 0)
        {
            try
            {
                BytesRead = TCPSocket?.Receive(Buffer, Start, Size, SocketFlags.None) ?? 0;
            }
            catch
            {
                LastError = S7Consts.errTCPDataReceive;
            }
            if (BytesRead == 0) // Connection Reset by the peer
            {
                LastError = S7Consts.errTCPDataReceive;
                Close();
            }
        }
        return LastError;
    }

    public int Send(byte[] Buffer, int Size)
    {
        LastError = 0;
        try
        {
            var BytesSent = TCPSocket?.Send(Buffer, Size, SocketFlags.None) ?? 0;
        }
        catch
        {
            LastError = S7Consts.errTCPDataSend;
            Close();
        }
        return LastError;
    }

    public bool Connected => (TCPSocket != null) && TCPSocket.Connected;

    public int ReadTimeout { get; set; } = 2000;

    public int WriteTimeout { get; set; } = 2000;
    public int ConnectTimeout { get; set; } = 1000;

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}
