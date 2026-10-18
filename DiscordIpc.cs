using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace YuzuPresence
{
    /// <summary>Minimal Discord Rich Presence client over the local IPC pipe.</summary>
    public class DiscordIpc
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly object sync = new object();
        private NamedPipeClientStream pipe;
        private string connectedId;

        public string LastError { get; private set; }

        public bool Connect(string clientId)
        {
            lock (sync) { return ConnectLocked(clientId); }
        }

        public void Close()
        {
            lock (sync) { CloseLocked(); }
        }

        /// <summary>Sets the activity, or clears it when activity is null.</summary>
        public bool SetActivity(string clientId, Dictionary<string, object> activity)
        {
            lock (sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (!ConnectLocked(clientId)) return false;
                        var args = new Dictionary<string, object>();
                        args["pid"] = Process.GetCurrentProcess().Id;
                        if (activity != null) args["activity"] = activity;
                        var cmd = new Dictionary<string, object>();
                        cmd["cmd"] = "SET_ACTIVITY";
                        cmd["args"] = args;
                        cmd["nonce"] = Guid.NewGuid().ToString();
                        WritePacket(1, Serialization.ToJson(cmd));
                        int op;
                        ReadPacket(out op);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        logger.Warn(ex, "Discord IPC call failed");
                        CloseLocked();
                    }
                }
                return false;
            }
        }

        /// <summary>Clears the status and drops the connection (Discord also removes a status when its connection closes).</summary>
        public void Clear(string clientId)
        {
            lock (sync)
            {
                if (pipe != null && pipe.IsConnected)
                {
                    try
                    {
                        var args = new Dictionary<string, object>();
                        args["pid"] = Process.GetCurrentProcess().Id;
                        var cmd = new Dictionary<string, object>();
                        cmd["cmd"] = "SET_ACTIVITY";
                        cmd["args"] = args;
                        cmd["nonce"] = Guid.NewGuid().ToString();
                        WritePacket(1, Serialization.ToJson(cmd));
                        int op;
                        ReadPacket(out op);
                    }
                    catch (Exception ex) { logger.Warn(ex, "Clearing Discord presence failed"); }
                }
                CloseLocked();
            }
        }

        private bool ConnectLocked(string clientId)
        {
            if (pipe != null && pipe.IsConnected && connectedId == clientId) return true;
            CloseLocked();
            LastError = null;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                LastError = "No Discord Application ID set.";
                return false;
            }
            for (int i = 0; i <= 9; i++)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(500);
                    pipe = p;
                    var hello = new Dictionary<string, object>();
                    hello["v"] = 1;
                    hello["client_id"] = clientId.Trim();
                    WritePacket(0, Serialization.ToJson(hello));
                    int op;
                    var reply = ReadPacket(out op);
                    if (op == 1 && reply.Contains("READY"))
                    {
                        connectedId = clientId;
                        return true;
                    }
                    LastError = "Discord rejected the Application ID: " + reply;
                    CloseLocked();
                    return false;
                }
                catch (Exception)
                {
                    CloseLocked();
                    if (p != null) p.Dispose();
                }
            }
            LastError = "Could not reach Discord. Is the desktop app running?";
            return false;
        }

        private void CloseLocked()
        {
            if (pipe != null)
            {
                try { pipe.Dispose(); } catch { }
            }
            pipe = null;
            connectedId = null;
        }

        private void WritePacket(int opcode, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var buf = new byte[8 + payload.Length];
            BitConverter.GetBytes(opcode).CopyTo(buf, 0);
            BitConverter.GetBytes(payload.Length).CopyTo(buf, 4);
            payload.CopyTo(buf, 8);
            pipe.Write(buf, 0, buf.Length);
            pipe.Flush();
        }

        private byte[] ReadExact(int count)
        {
            var buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                var task = pipe.ReadAsync(buf, off, count - off);
                if (!task.Wait(3000)) throw new TimeoutException("Discord did not answer in time.");
                if (task.Result <= 0) throw new IOException("Discord closed the connection.");
                off += task.Result;
            }
            return buf;
        }

        private string ReadPacket(out int opcode)
        {
            var header = ReadExact(8);
            opcode = BitConverter.ToInt32(header, 0);
            int len = BitConverter.ToInt32(header, 4);
            if (len <= 0) return "";
            return Encoding.UTF8.GetString(ReadExact(len));
        }
    }
}
