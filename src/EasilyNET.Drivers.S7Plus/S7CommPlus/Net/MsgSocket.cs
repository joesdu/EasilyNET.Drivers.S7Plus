// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Net.Sockets;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

// 纯异步 ISO-on-TCP socket：所有 I/O 走 Socket 的 *Async API，无忙等待、无阻塞调用。
// 不实现 IDisposable：底层 Socket 的关闭是同步原语，统一通过 Close() 释放。
internal sealed class MsgSocket
{
    private Socket? TCPSocket;
    public int LastError;

    public MsgSocket()
    {
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

    public async ValueTask<int> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (ConnectTimeout > 0)
            {
                cts.CancelAfter(ConnectTimeout);
            }
            await TCPSocket!.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastError = S7Consts.errTCPConnectionTimeout;
            Close();
        }
        catch (OperationCanceledException)
        {
            // 调用方取消
            Close();
            throw;
        }
        catch
        {
            LastError = S7Consts.errTCPConnectionFailed;
            Close();
        }
        return LastError;
    }

    /// <summary>
    ///     异步精确读取 <paramref name="size" /> 个字节到 <paramref name="buffer" /> 的 <paramref name="start" /> 处。
    ///     <paramref name="applyReadTimeout" /> 为 <see langword="true" /> 时套用 <see cref="ReadTimeout" />；
    ///     为 <see langword="false" /> 时仅受 <paramref name="cancellationToken" /> 约束（用于空闲等待下一帧首字节）。
    /// </summary>
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int start, int size, bool applyReadTimeout, CancellationToken cancellationToken = default)
    {
        LastError = 0;
        var sock = TCPSocket;
        if (sock is null)
        {
            return LastError = S7Consts.errTCPNotConnected;
        }
        CancellationTokenSource? cts = null;
        try
        {
            var token = cancellationToken;
            if (applyReadTimeout && ReadTimeout > 0)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ReadTimeout);
                token = cts.Token;
            }
            var read = 0;
            while (read < size)
            {
                var n = await sock.ReceiveAsync(buffer.AsMemory(start + read, size - read), SocketFlags.None, token).ConfigureAwait(false);
                if (n == 0)
                {
                    // 对端关闭连接
                    LastError = S7Consts.errTCPDataReceive;
                    Close();
                    break;
                }
                read += n;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 读超时：socket 仍可能可用，但本帧已残缺；上层据此触发重连
            LastError = S7Consts.errTCPDataReceive;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            LastError = S7Consts.errTCPDataReceive;
            Close();
        }
        finally
        {
            cts?.Dispose();
        }
        return LastError;
    }

    public async ValueTask<int> SendAsync(byte[] buffer, int size, CancellationToken cancellationToken = default)
    {
        LastError = 0;
        var sock = TCPSocket;
        if (sock is null)
        {
            return LastError = S7Consts.errTCPNotConnected;
        }
        try
        {
            var sent = 0;
            while (sent < size)
            {
                var n = await sock.SendAsync(buffer.AsMemory(sent, size - sent), SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                {
                    LastError = S7Consts.errTCPDataSend;
                    Close();
                    break;
                }
                sent += n;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
}
