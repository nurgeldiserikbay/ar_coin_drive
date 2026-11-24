using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Two-player link over the local network (same Wi-Fi or a phone hotspot), with no server.
/// The host owns the game simulation; the client sends its controls and draws what the host
/// reports. Everything is polled on the main thread, so no threading is involved.
/// Discovery: the joining phone sends a small "hello" to every address on its /24 subnet
/// (plus a broadcast); the host answers. Unicast works even where Android filters broadcasts.
/// </summary>
public sealed class LanSession : IDisposable
{
    public enum SessionRole { Host, Client }

    public const int GamePort = 47777;
    private const byte ProtocolVersion = 1;
    private const byte Hello = (byte)'H';
    private const byte Welcome = (byte)'W';
    private const byte InputPacket = (byte)'I';
    private const byte StatePacket = (byte)'S';
    private const byte Bye = (byte)'X';
    private const float TimeoutSeconds = 4f;

    private readonly UdpClient _socket;
    private readonly List<IPEndPoint> _scanTargets = new List<IPEndPoint>();
    private readonly byte[] _inputBuffer = new byte[10];
    private IPEndPoint _peer;
    private float _lastHeard;
    private float _nextScan;
    private float _nextBroadcast;
    private int _scanCursor;
    private byte[] _latestState;
    private bool _disposed;

    public SessionRole Role { get; }
    public bool Connected { get; private set; }
    public bool LostConnection { get; private set; }
    public string LocalAddress { get; }
    public string PeerAddress => _peer != null ? _peer.Address.ToString() : "";
    public float RemoteDrive { get; private set; }
    public float RemoteSteer { get; private set; }
    public bool HasNewState => _latestState != null;

    private LanSession(SessionRole role)
    {
        Role = role;
        var locals = FindLocalAddresses();
        LocalAddress = locals.Count > 0 ? locals[0].ToString() : "unknown";
        _socket = role == SessionRole.Host ? new UdpClient(GamePort) : new UdpClient(0);
        _socket.EnableBroadcast = true;
        if (role == SessionRole.Client)
            BuildScanTargets();
    }

    public static LanSession Host() => new LanSession(SessionRole.Host);

    public static LanSession Join() => new LanSession(SessionRole.Client);

    public string Status
    {
        get
        {
            if (LostConnection) return "connection lost";
            if (Connected) return (Role == SessionRole.Host ? "hosting, player joined from " : "joined host ") + PeerAddress;
            return Role == SessionRole.Host
                ? "waiting for a player on " + LocalAddress
                : "searching the local network from " + LocalAddress;
        }
    }

    /// <summary>Call once per frame: reads every waiting packet and keeps discovery going.</summary>
    public void Poll(float now)
    {
        if (_disposed) return;
        try
        {
            while (_socket.Available > 0)
            {
                IPEndPoint from = null;
                var data = _socket.Receive(ref from);
                Handle(data, from, now);
            }
        }
        catch (SocketException)
        {
            // An unreachable scan target can surface as an ICMP error on some platforms; ignore it.
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (Role == SessionRole.Client && !Connected)
            ContinueDiscovery(now);

        if (Connected && now - _lastHeard > TimeoutSeconds)
        {
            Connected = false;
            LostConnection = true;
        }
    }

    private void Handle(byte[] data, IPEndPoint from, float now)
    {
        if (data.Length < 2) return;
        var type = data[0];

        if (Role == SessionRole.Host)
        {
            if (type == Hello && data[1] == ProtocolVersion && (!Connected || from.Equals(_peer)))
            {
                _peer = from;
                Connected = true;
                _lastHeard = now;
                Send(new[] { Welcome, ProtocolVersion });
                return;
            }

            if (!Connected || !from.Equals(_peer)) return;
            _lastHeard = now;
            if (type == InputPacket && data.Length >= 9)
            {
                RemoteDrive = BitConverter.ToSingle(data, 1);
                RemoteSteer = BitConverter.ToSingle(data, 5);
            }
            else if (type == Bye)
            {
                Connected = false;
                LostConnection = true;
            }

            return;
        }

        if (type == Welcome && data[1] == ProtocolVersion && !Connected)
        {
            _peer = from;
            Connected = true;
            _lastHeard = now;
            return;
        }

        if (!Connected || !from.Equals(_peer)) return;
        _lastHeard = now;
        if (type == StatePacket)
            _latestState = data;
        else if (type == Bye)
        {
            Connected = false;
            LostConnection = true;
        }
    }

    public byte[] TakeState()
    {
        var state = _latestState;
        _latestState = null;
        return state;
    }

    public void SendInput(float drive, float steer)
    {
        if (!Connected) return;
        _inputBuffer[0] = InputPacket;
        Buffer.BlockCopy(BitConverter.GetBytes(drive), 0, _inputBuffer, 1, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(steer), 0, _inputBuffer, 5, 4);
        Send(_inputBuffer);
    }

    /// <summary>State packets must start with 'S'; the caller builds the payload.</summary>
    public void SendState(byte[] packet, int length)
    {
        if (!Connected) return;
        packet[0] = StatePacket;
        Send(packet, length);
    }

    private void Send(byte[] data, int length = -1)
    {
        if (_peer == null || _disposed) return;
        try
        {
            _socket.Send(data, length < 0 ? data.Length : length, _peer);
        }
        catch (SocketException)
        {
            // The timeout above reports a dead link; a single failed send is not fatal.
        }
    }

    private void ContinueDiscovery(float now)
    {
        var hello = new[] { Hello, ProtocolVersion };
        if (now >= _nextBroadcast)
        {
            _nextBroadcast = now + 0.5f;
            TrySend(hello, new IPEndPoint(IPAddress.Broadcast, GamePort));
        }

        if (now < _nextScan || _scanTargets.Count == 0) return;
        _nextScan = now + 0.02f;
        // About 50 addresses per 0.1 s: a full /24 sweep takes roughly half a second.
        for (var i = 0; i < 10; i++)
        {
            TrySend(hello, _scanTargets[_scanCursor]);
            _scanCursor = (_scanCursor + 1) % _scanTargets.Count;
        }
    }

    private void TrySend(byte[] data, IPEndPoint target)
    {
        try
        {
            _socket.Send(data, data.Length, target);
        }
        catch (SocketException)
        {
        }
    }

    private void BuildScanTargets()
    {
        var seen = new HashSet<string>();
        var locals = FindLocalAddresses();
        foreach (var local in locals)
        {
            var bytes = local.GetAddressBytes();
            for (var host = 1; host < 255; host++)
            {
                if (host == bytes[3]) continue;
                var target = new IPAddress(new[] { bytes[0], bytes[1], bytes[2], (byte)host });
                if (seen.Add(target.ToString()))
                    _scanTargets.Add(new IPEndPoint(target, GamePort));
            }
        }

        // Common phone-hotspot gateways, in case the local address could not be read.
        foreach (var gateway in new[] { "192.168.43.1", "192.168.49.1", "172.20.10.1", "192.168.137.1" })
        {
            if (seen.Add(gateway))
                _scanTargets.Add(new IPEndPoint(IPAddress.Parse(gateway), GamePort));
        }
    }

    private static List<IPAddress> FindLocalAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up
                    || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address.Address))
                        result.Add(address.Address);
                }
            }
        }
        catch (Exception)
        {
            // Some Android builds restrict interface enumeration; fall through to the route trick.
        }

        if (result.Count > 0) return result;

        try
        {
            // Connecting a UDP socket sends nothing but reveals which local address has a route.
            using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe.Connect("192.168.43.1", 65530);
                if (probe.LocalEndPoint is IPEndPoint endPoint && !IPAddress.Any.Equals(endPoint.Address))
                    result.Add(endPoint.Address);
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    public void Dispose()
    {
        if (Connected)
        {
            var bye = new[] { Bye, ProtocolVersion };
            for (var i = 0; i < 3; i++) Send(bye);
        }

        Connected = false;
        _disposed = true;
        _socket.Close();
    }
}
