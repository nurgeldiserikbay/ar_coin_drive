using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Up to four players over the local network (same Wi-Fi or a phone hotspot), with no server.
/// The host owns the game simulation; each guest sends its controls and draws what the host
/// reports. Everything is polled on the main thread, so no threading is involved.
///
/// Discovery: a guest probes every address on its /24 subnet (plus a broadcast) and lists the
/// games that answer; tapping one joins it. Unicast works even where Android filters broadcasts.
/// Both sides send a keep-alive twice a second, so a player can still be scanning the floor.
/// </summary>
public sealed class LanSession : IDisposable
{
    public enum SessionRole { Host, Client }

    public const int GamePort = 47777;
    public const int MaxPlayers = 4;
    private const byte Version = 3;
    private const byte Probe = (byte)'P';
    private const byte Offer = (byte)'O';
    private const byte JoinRequest = (byte)'J';
    private const byte Welcome = (byte)'W';
    private const byte Full = (byte)'F';
    private const byte InputPacket = (byte)'I';
    private const byte StatePacket = (byte)'S';
    private const byte KeepAlive = (byte)'K';
    private const byte Bye = (byte)'X';
    private const float TimeoutSeconds = 4f;

    public sealed class Peer
    {
        public IPEndPoint EndPoint;
        public int Slot;
        public string Name;
        public int Color;
        public int CarType;
        public float LastHeard;
        public float Drive;
        public float Steer;
    }

    public sealed class HostOffer
    {
        public IPEndPoint EndPoint;
        public string Name;
        public int Players;
        public float LastSeen;
    }

    private readonly UdpClient _socket;
    private readonly List<IPEndPoint> _scanTargets = new List<IPEndPoint>();
    private readonly byte[] _inputBuffer = new byte[10];
    private readonly string _name;
    private readonly int _color;
    private readonly int _carType;
    private IPEndPoint _host;
    private float _lastHeard;
    private float _nextScan;
    private float _nextBroadcast;
    private float _nextKeepAlive;
    private float _nextJoinAttempt;
    private int _scanCursor;
    private byte[] _latestState;
    private bool _disposed;

    public SessionRole Role { get; }
    public string LocalAddress { get; }
    public List<Peer> Peers { get; } = new List<Peer>();
    public List<HostOffer> Offers { get; } = new List<HostOffer>();
    public readonly Queue<int> Joined = new Queue<int>();
    public readonly Queue<int> Left = new Queue<int>();

    /// <summary>Guest: joined a host. Host: at least one guest connected.</summary>
    public bool Connected => Role == SessionRole.Host ? Peers.Count > 0 : Slot > 0;
    public bool LostConnection { get; private set; }
    public bool RejectedFull { get; private set; }
    public HostOffer Joining { get; private set; }
    public int Slot { get; private set; }
    public int PlayerCount => Role == SessionRole.Host ? Peers.Count + 1 : 0;

    private LanSession(SessionRole role, string name, int color, int carType)
    {
        Role = role;
        _name = GameSettings.Clean(name);
        _color = color;
        _carType = carType;
        var locals = FindLocalAddresses();
        LocalAddress = locals.Count > 0 ? locals[0].ToString() : "unknown";
        _socket = role == SessionRole.Host ? new UdpClient(GamePort) : new UdpClient(0);
        _socket.EnableBroadcast = true;
        if (role == SessionRole.Client) BuildScanTargets();
    }

    public static LanSession Host(string name, int color, int carType = 0) => new LanSession(SessionRole.Host, name, color, carType);
    public static LanSession Browse(string name, int color, int carType = 0) => new LanSession(SessionRole.Client, name, color, carType);

    /// <summary>Also probe a specific address (manual address entry, tests).</summary>
    public void AddProbeTarget(IPEndPoint target) => _scanTargets.Insert(0, target);

    public string Status
    {
        get
        {
            if (LostConnection) return "connection lost";
            if (Role == SessionRole.Host)
                return Peers.Count == 0 ? "waiting for players on " + LocalAddress : PlayerCount + " players";
            if (Slot > 0) return "joined " + (Joining != null ? Joining.Name : "host");
            if (Joining != null) return "joining " + Joining.Name + "...";
            return "searching the local network from " + LocalAddress;
        }
    }

    // ------------------------------------------------------------------ polling

    /// <summary>Call once per frame: reads every waiting packet, keeps discovery and links alive.</summary>
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

        if (Role == SessionRole.Host)
        {
            for (var i = Peers.Count - 1; i >= 0; i--)
            {
                if (now - Peers[i].LastHeard <= TimeoutSeconds) continue;
                Left.Enqueue(Peers[i].Slot);
                Peers.RemoveAt(i);
            }
        }
        else
        {
            if (Slot == 0)
            {
                ContinueDiscovery(now);
                if (Joining != null && now >= _nextJoinAttempt)
                {
                    _nextJoinAttempt = now + 0.4f;
                    TrySend(JoinPacket(), Joining.EndPoint);
                }
            }
            else if (now - _lastHeard > TimeoutSeconds)
            {
                Slot = 0;
                LostConnection = true;
            }

            Offers.RemoveAll(offer => now - offer.LastSeen > 3f);
        }

        if (now >= _nextKeepAlive)
        {
            _nextKeepAlive = now + 0.5f;
            var ping = new[] { KeepAlive, Version, (byte)Slot };
            if (Role == SessionRole.Host) foreach (var peer in Peers) TrySend(ping, peer.EndPoint);
            else if (Slot > 0) TrySend(ping, _host);
        }
    }

    private void Handle(byte[] data, IPEndPoint from, float now)
    {
        if (data.Length < 2) return;
        var type = data[0];
        if (Role == SessionRole.Host) HandleAsHost(type, data, from, now);
        else HandleAsGuest(type, data, from, now);
    }

    private void HandleAsHost(byte type, byte[] data, IPEndPoint from, float now)
    {
        if (type == Probe && data[1] == Version)
        {
            var name = Encoding.UTF8.GetBytes(_name);
            var packet = new byte[5 + name.Length];
            packet[0] = Offer;
            packet[1] = Version;
            packet[2] = (byte)PlayerCount;
            packet[3] = MaxPlayers;
            packet[4] = (byte)name.Length;
            Buffer.BlockCopy(name, 0, packet, 5, name.Length);
            TrySend(packet, from);
            return;
        }

        var peer = Peers.Find(p => p.EndPoint.Equals(from));
        if (type == JoinRequest && data[1] == Version && data.Length >= 5)
        {
            if (peer == null)
            {
                if (Peers.Count >= MaxPlayers - 1)
                {
                    TrySend(new[] { Full, Version }, from);
                    return;
                }

                var nameLength = Mathf.Min(data[4], data.Length - 5);
                peer = new Peer
                {
                    EndPoint = from,
                    Slot = FreeSlot(),
                    Color = data[2],
                    CarType = data[3],
                    Name = GameSettings.Clean(Encoding.UTF8.GetString(data, 5, nameLength))
                };
                Peers.Add(peer);
                Joined.Enqueue(peer.Slot);
            }

            peer.LastHeard = now;
            TrySend(new[] { Welcome, Version, (byte)peer.Slot }, from);
            return;
        }

        if (peer == null) return;
        peer.LastHeard = now;
        if (type == InputPacket && data.Length >= 10)
        {
            peer.Drive = Mathf.Clamp(BitConverter.ToSingle(data, 2), -1f, 1f);
            peer.Steer = Mathf.Clamp(BitConverter.ToSingle(data, 6), -1f, 1f);
        }
        else if (type == Bye)
        {
            Left.Enqueue(peer.Slot);
            Peers.Remove(peer);
        }
    }

    private void HandleAsGuest(byte type, byte[] data, IPEndPoint from, float now)
    {
        if (type == Offer && data[1] == Version && data.Length >= 5)
        {
            var nameLength = Mathf.Min(data[4], data.Length - 5);
            var offer = Offers.Find(o => o.EndPoint.Equals(from));
            if (offer == null)
            {
                offer = new HostOffer { EndPoint = from };
                Offers.Add(offer);
            }

            offer.Name = GameSettings.Clean(Encoding.UTF8.GetString(data, 5, nameLength));
            offer.Players = data[2];
            offer.LastSeen = now;
            return;
        }

        if (Joining == null || !from.Address.Equals(Joining.EndPoint.Address)) return;
        if (type == Welcome && data[1] == Version && data.Length >= 3)
        {
            if (Slot == 0)
            {
                _host = from;
                Slot = data[2];
            }

            _lastHeard = now;
            return;
        }

        if (type == Full)
        {
            RejectedFull = true;
            Joining = null;
            return;
        }

        if (Slot == 0) return;
        _lastHeard = now;
        if (type == StatePacket) _latestState = data;
        else if (type == Bye)
        {
            Slot = 0;
            LostConnection = true;
        }
    }

    private int FreeSlot()
    {
        for (var slot = 1; slot < MaxPlayers; slot++)
            if (!Peers.Exists(p => p.Slot == slot)) return slot;
        return MaxPlayers - 1;
    }

    // ------------------------------------------------------------------ guest API

    public void Join(HostOffer offer)
    {
        if (Role != SessionRole.Client || Slot > 0) return;
        Joining = offer;
        RejectedFull = false;
        _nextJoinAttempt = 0f;
    }

    public byte[] TakeState()
    {
        var state = _latestState;
        _latestState = null;
        return state;
    }

    public void SendInput(float drive, float steer)
    {
        if (Slot == 0 || _host == null) return;
        _inputBuffer[0] = InputPacket;
        _inputBuffer[1] = (byte)Slot;
        Buffer.BlockCopy(BitConverter.GetBytes(drive), 0, _inputBuffer, 2, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(steer), 0, _inputBuffer, 6, 4);
        TrySend(_inputBuffer, _host);
    }

    private byte[] JoinPacket()
    {
        var name = Encoding.UTF8.GetBytes(_name);
        var packet = new byte[5 + name.Length];
        packet[0] = JoinRequest;
        packet[1] = Version;
        packet[2] = (byte)_color;
        packet[3] = (byte)_carType;
        packet[4] = (byte)name.Length;
        Buffer.BlockCopy(name, 0, packet, 5, name.Length);
        return packet;
    }

    // ------------------------------------------------------------------ host API

    public Peer FindPeer(int slot) => Peers.Find(p => p.Slot == slot);

    /// <summary>State packets must start with 'S'; the caller builds the payload.</summary>
    public void SendState(byte[] packet, int length)
    {
        if (Role != SessionRole.Host) return;
        packet[0] = StatePacket;
        foreach (var peer in Peers)
        {
            try
            {
                _socket.Send(packet, length, peer.EndPoint);
            }
            catch (SocketException)
            {
            }
        }
    }

    // ------------------------------------------------------------------ discovery

    private void ContinueDiscovery(float now)
    {
        var probe = new[] { Probe, Version };
        if (now >= _nextBroadcast)
        {
            _nextBroadcast = now + 0.5f;
            TrySend(probe, new IPEndPoint(IPAddress.Broadcast, GamePort));
        }

        if (now < _nextScan || _scanTargets.Count == 0) return;
        _nextScan = now + 0.02f;
        // About 50 addresses per 0.1 s: a full /24 sweep takes roughly half a second.
        for (var i = 0; i < 10; i++)
        {
            TrySend(probe, _scanTargets[_scanCursor]);
            _scanCursor = (_scanCursor + 1) % _scanTargets.Count;
        }
    }

    private void TrySend(byte[] data, IPEndPoint target)
    {
        if (target == null || _disposed) return;
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
        foreach (var local in FindLocalAddresses())
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
        if (_disposed) return;
        var bye = new[] { Bye, Version, (byte)Slot };
        for (var i = 0; i < 3; i++)
        {
            if (Role == SessionRole.Host) foreach (var peer in Peers) TrySend(bye, peer.EndPoint);
            else if (Slot > 0) TrySend(bye, _host);
        }

        _disposed = true;
        _socket.Close();
    }
}
