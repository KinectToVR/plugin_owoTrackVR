using Amethyst.Plugins.Contract;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace plugin_OwoTrack;

internal static class Discovery
{
    public static int DataPort { get; set; }
    private static int InfoPort => 35903;

    private static UdpClient Client { get; set; }
    private static bool IsRunning { get; set; } = true;
    public static IAmethystHost Host { get; set; }

    public static bool Restart(int dataPort, IAmethystHost host)
    {
        DataPort = dataPort;
        Host = host;

        Stop();
        var result = Start();

        if (!result)
            Host?.Log("Failed to restart discovery service.");

        return result;
    }

    private static bool Start()
    {
        try
        {
            Client = new UdpClient(InfoPort);
            IsRunning = true;

            Task.Run(async () =>
            {
                while (IsRunning)
                    try
                    {
                        if (!await ReceiveDiscoveryMessage())
                            await Task.Delay(100);
                    }
                    catch (SocketException ex)
                    {
                        Host?.Log($"Socket exception: {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Host?.Log($"Unexpected exception: {ex.Message}");
                        break;
                    }
            });

            return true;
        }
        catch (Exception ex)
        {
            Host?.Log($"Error starting discovery: {ex.Message}");
            Stop();
        }

        return false;
    }

    private static async Task<bool> ReceiveDiscoveryMessage()
    {
        if (Client.Available is 0)
            return false;

        var remoteEp = new IPEndPoint(IPAddress.Any, 0);
        var response = Client.Receive(ref remoteEp);
        var received = Encoding.UTF8.GetString(response);

        if (received.TrimEnd('\0', '\r', '\n') != "DISCOVERY") return true;

        var sendBytes = Encoding.UTF8.GetBytes($"{DataPort}:Default\n");
        await Client.SendAsync(sendBytes, sendBytes.Length, remoteEp);

        return true;
    }

    public static void Stop()
    {
        try
        {
            IsRunning = false;
            Client?.Close();
            Client = null;
        }
        catch (Exception ex)
        {
            Host?.Log($"Error stopping discovery: {ex.Message}");
        }
    }
}

internal class Handler(IAmethystHost host)
{
    public enum ConnectionStatus
    {
        ConnectionDead = 0x00010001, // No connection
        NoData = 0x00010002, // No data received
        InitFailed = 0x00010003, // Init failed
        PortsTaken = 0x00010004, // Ports taken
        NotStarted = 0x00010005, // Disconnected (initial)
        Ok = 0
    }

    public int Port { get; init; } = 6969;
    public IAmethystHost Host { get; set; } = host;
    private DeviceServer Server { get; set; }
    public List<string> Addresses { get; private set; }

    public uint ConnectionRetries { get; set; }
    public bool IsInitialized { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.NotStarted;
    public bool CalibratingForward { get; set; }
    public bool CalibratingDown { get; set; }
    public Quaternion GlobalRotation { get; set; }
    public Quaternion LocalRotation { get; set; }
    public EventHandler<string> StatusChanged { get; set; }

    public Handler Next()
    {
        return new Handler(Host)
        {
            Port = Port + 1
        };
    }

    public void OnLoad()
    {
        try
        {
            Addresses = NetworkInformation.GetHostNames().Where(x =>
                x.IPInformation?.NetworkAdapter?.NetworkAdapterId ==
                NetworkInformation.GetInternetConnectionProfile().NetworkAdapter.NetworkAdapterId &&
                x.Type is HostNameType.Ipv4).Select(x => x.CanonicalName).ToList();
        }
        catch (Exception ex)
        {
            Host?.Log($"Failed to get host names: {ex.Message}");
            Addresses = [];
        }

        if (Addresses.Count is 0)
            Addresses.Add("127.0.0.1");
    }

    public void Update()
    {
        if (!IsInitialized || Server is null) return;

        try
        {
            Server.Tick();
        }
        catch (Exception ex)
        {
            Host?.Log($"Error during update: {ex.Message}");
        }

        if (!Server.IsDataAvailable)
        {
            if (ConnectionRetries >= 100) // >1s timeout
            {
                ConnectionRetries = 0;
                var previousResult = Status;

                Status = Server.IsConnectionAlive
                    ? ConnectionStatus.Ok // NoData
                    : ConnectionStatus.ConnectionDead;

                if (previousResult != Status)
                    StatusChanged?.Invoke(this, "STATUS ERROR");
            }
            else
            {
                ConnectionRetries++;
            }
        }
        else
        {
            var previousResult = Status;
            Status = ConnectionStatus.Ok; // All fine now!

            if (previousResult != Status)
                StatusChanged?.Invoke(this, "STATUS OK");
        }
    }

    public void Signal()
    {
        if (!IsInitialized || Status is not ConnectionStatus.Ok) return;
        Server?.Signal(0.7f, 100f, 0.5f);
    }

    public ConnectionStatus Initialize()
    {
        // Optionally initialize the server
        // (Warning: this can be done only once)
        if (Status is ConnectionStatus.NotStarted)
            try
            {
                Server = new DeviceServer(Host)
                {
                    Port = Port
                };

                if (!Discovery.Restart(Port, Host))
                    Host.Log("Failed to start discovery service.");

                if (!Server.StartListening())
                    return Status = ConnectionStatus.PortsTaken;

                Status = ConnectionStatus.ConnectionDead;
            }
            catch (Exception ex)
            {
                Host?.Log("Failed to set up the handler!");
                Host?.Log(ex);
            }

        if (Status is ConnectionStatus.InitFailed
            or ConnectionStatus.PortsTaken) return Status;

        // Mark the device as initialized
        IsInitialized = true;
        CalibratingForward = false;
        CalibratingDown = false;
        return ConnectionStatus.Ok;
    }

    public void Shutdown()
    {
        IsInitialized = false;
    }

    public (Vector3 Position, Quaternion Orientation) CalculatePose(
        (Vector3 Position, Quaternion Orientation, float Yaw) headsetPose,
        Vector3 globalOffset, Vector3 deviceOffset, Vector3 trackerOffset)
    {
        if (!IsInitialized || Status is not ConnectionStatus.Ok || Server is null)
            return (Vector3.Zero, Quaternion.Identity);

        /* Prepare for the position calculations */

        (Vector3 Position, Quaternion Orientation) pose = (headsetPose.Position, Quaternion.Identity);
        var offsetBasis = new Basis(headsetPose.Orientation);

        var remoteQuaternion = Quaternion.CreateFromAxisAngle(
            new Vector3(1, 0, 0), -MathF.PI / 2f) * Server.Orientation;

        if (CalibratingForward)
        {
            GlobalRotation = Quaternion.CreateFromYawPitchRoll(
                remoteQuaternion.GetYaw() - offsetBasis.Quaternion.GetYaw(new Vector3(0, 0, -1)), 0, 0); // YXZ

            globalOffset = Vector3.Normalize(offsetBasis.XForm(new Vector3(
                0, 0, -1)) * new Vector3(1, 0, 1)) + new Vector3(0, 0.2f, 0);

            deviceOffset = Vector3.Zero;
            trackerOffset = Vector3.Zero;
        }

        remoteQuaternion = GlobalRotation * remoteQuaternion;

        if (CalibratingDown)
            LocalRotation = Quaternion.Inverse(remoteQuaternion) * Quaternion
                .CreateFromAxisAngle(new Vector3(0, 1, 0), -headsetPose.Yaw);

        remoteQuaternion *= LocalRotation;
        pose.Orientation = remoteQuaternion;

        var finalBasis = new Basis(remoteQuaternion);
        pose.Position.X += globalOffset.X + offsetBasis.XForm(deviceOffset).X + finalBasis.XForm(trackerOffset).X;
        pose.Position.Y += globalOffset.Y + offsetBasis.XForm(deviceOffset).Y + finalBasis.XForm(trackerOffset).Y;
        pose.Position.Z += globalOffset.Z + offsetBasis.XForm(deviceOffset).Z + finalBasis.XForm(trackerOffset).Z;

        return pose;
    }
}

public static class Extensions
{
    public static float GetYaw(this Quaternion q, Vector3? forward = null)
    {
        // xform to get front vector (up points front)
        var frontRelative = new Basis(q).XForm(forward ?? new Vector3(0, 1, 0));

        // flatten to XZ for yaw
        frontRelative = Vector3.Normalize(frontRelative * new Vector3(1, 0, 1));

        // get angle and convert to offset
        var angle = frontRelative.AngleTo(new Vector3(0, 0, 1));
        return -angle * float.Sign(frontRelative.X);
    }

    public static float AngleTo(this Vector3 v, Vector3 u)
    {
        return float.Atan2(Vector3.Cross(v, u).Length(), Vector3.Dot(v, u));
    }
}

public class Basis
{
    public Basis(Quaternion q)
    {
        Quaternion = q;

        var s = 2f / q.LengthSquared();
        float xs = q.X * s, ys = q.Y * s, zs = q.Z * s;
        float wx = q.W * xs, wy = q.W * ys, wz = q.W * zs;
        float xx = q.X * xs, xy = q.X * ys, xz = q.X * zs;
        float yy = q.Y * ys, yz = q.Y * zs, zz = q.Z * zs;

        Set(1f - (yy + zz), xy - wz, xz + wy,
            xy + wz, 1f - (xx + zz), yz - wx,
            xz - wy, yz + wx, 1f - (xx + yy));
    }

    private void Set(
        float xx, float xy, float xz,
        float yx, float yy, float yz,
        float zx, float zy, float zz)
    {
        Elements[0].X = xx;
        Elements[0].Y = xy;
        Elements[0].Z = xz;
        Elements[1].X = yx;
        Elements[1].Y = yy;
        Elements[1].Z = yz;
        Elements[2].X = zx;
        Elements[2].Y = zy;
        Elements[2].Z = zz;
    }

    public Vector3 XForm(Vector3 v)
    {
        return new Vector3(
            Vector3.Dot(Elements[0], v),
            Vector3.Dot(Elements[1], v),
            Vector3.Dot(Elements[2], v));
    }

    public Vector3[] Elements { get; } = new Vector3[3];
    public Quaternion Quaternion { get; }
}

internal class DeviceServer(IAmethystHost host)
{
    public enum MessageType
    {
        Heartbeat,
        Rotation,
        Gyro,
        Handshake,
        Accelerometer
    }

    public int Port { get; init; }
    public Quaternion Orientation { get; set; }
    public Vector3 Gyroscope { get; set; }
    public Vector3 Acceleration { get; set; }
    public IAmethystHost Host { get; set; } = host;

    public bool IsConnectionAlive
    {
        get
        {
            if (Socket is null || LastConnection == default || !_isConnectionAlive)
            {
                _isConnectionAlive = false;
                return _isConnectionAlive;
            }

            // ReSharper disable once InvertIf
            if (DateTime.Now - LastConnection > TimeSpan.FromSeconds(5))
            {
                _isConnectionAlive = false;
                Host?.Log("Connection timed out.");
                
                // Flush buffer when connection times out to prevent stale data
                FlushBuffer();
            }

            return _isConnectionAlive;
        }
        set => _isConnectionAlive = value;
    }

    public bool IsDataAvailable
    {
        get
        {
            if (!_isDataAvailable) return _isDataAvailable;
            _isDataAvailable = false;
            return true;
        }
        set => _isDataAvailable = value;
    }

    public bool StartListening()
    {
        try
        {
            Socket = new UdpClient(Port);

            // Make the socket non-blocking to prevent packet buffering delays
            Socket.Client.ReceiveTimeout = 0;
            Socket.Client.Blocking = false;

            // Flush any existing packets in the buffer
            FlushBuffer();

            return true;
        }
        catch (Exception ex)
        {
            Host?.Log($"Failed to start listening on port {Port}: {ex.Message}");
        }

        return false;
    }

    private void FlushBuffer()
    {
        if (Socket is null) return;

        try
        {
            // Flush any remaining packets in the socket buffer
            var flushedCount = 0;
            while (Socket.Available > 0 && flushedCount < 100) // Prevent infinite loop
            {
                var tempClient = new IPEndPoint(IPAddress.Any, 0);
                Socket.Receive(ref tempClient);
                flushedCount++;
            }

            if (flushedCount > 0)
            {
                Host?.Log($"Flushed {flushedCount} old packets from buffer");
            }
        }
        catch (Exception ex)
        {
            Host?.Log($"Error flushing buffer: {ex.Message}");
        }
    }

    public void Tick()
    {
        SendHeartbeat();
        
        // Process all available packets in one go to prevent buffering delays
        var packetsProcessed = 0;
        const int maxPacketsPerTick = 50; // Prevent infinite loops
        
        while (packetsProcessed < maxPacketsPerTick && ReadData())
        {
            packetsProcessed++;
        }
        
        // If we processed the maximum number of packets, log a warning
        if (packetsProcessed >= maxPacketsPerTick)
        {
            Host?.Log($"Warning: Processed {maxPacketsPerTick} packets in one tick. Consider checking packet rate.");
        }
    }

    public void Signal(float duration, float frequency, float amplitude)
    {
        SendBytes(new ByteBuffer()
            .PutInt(2)
            .PutFloat(duration)
            .PutFloat(frequency)
            .PutFloat(amplitude));
    }

    #region UDPDeviceQuatServer

    private uint _heartbeatAcc;

    private UdpClient Socket { get; set; }
    private DateTime LastConnection { get; set; }
    private IPEndPoint Client => _client;

    private void SendBytes(ByteBuffer bytes)
    {
        try
        {
            Socket.Send(bytes.ToArray(), Client);
        }
        catch (SocketException ex)
        {
            Host?.Log($"Socket exception while sending data: {ex.Message}");
            IsConnectionAlive = false;
        }
        catch (Exception ex)
        {
            Host?.Log($"Unexpected exception while sending data: {ex.Message}");
            IsConnectionAlive = false;
        }
    }

    private void SendHeartbeat()
    {
        _heartbeatAcc += 1;

        if (_heartbeatAcc <= 200) return;
        _heartbeatAcc = 0;

        if (!IsConnectionAlive)
            return;

        SendBytes(new ByteBuffer().PutInt(1).PutInt(0));
    }

    private bool ReadData()
    {
        if (Socket is null) return false;

        try
        {
            // Check if data is available without blocking
            if (Socket.Available == 0)
                return false;

            var buffer = Socket.Receive(ref _client);
            if (buffer.Length < sizeof(uint))
            {
                Host?.Log("Received packet too small.");
                return false;
            }

            var messageType = (MessageType)BinaryPrimitives
                .ReadUInt32BigEndian(buffer.AsSpan(0, 4));

            IsConnectionAlive = true;
            LastConnection = DateTime.Now;

            switch (messageType)
            {
                case MessageType.Heartbeat:
                    break;
                case MessageType.Handshake:
                    byte[] buffHello = [.. " Hey OVR =D 5"u8.ToArray()];
                    buffHello[0] = 3;
                    Socket.Send(buffHello, Client);
                    break;
                case MessageType.Rotation:
                case MessageType.Gyro:
                case MessageType.Accelerometer:
                    HandlePacket(buffer, messageType);
                    break;
            }

            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
        {
            // No data available, this is normal for non-blocking sockets
            return false;
        }
        catch (SocketException ex)
        {
            Host?.Log($"Socket exception: {ex.Message}");
            IsConnectionAlive = false;
            return false;
        }
        catch (Exception ex)
        {
            Host?.Log($"Unexpected exception: {ex.Message}");
            IsConnectionAlive = false;
            return false;
        }
    }

    #endregion

    #region NetworkedDeviceQuatServer

    private ulong _packetId;
    private bool _isDataAvailable;
    private IPEndPoint _client;
    private bool _isConnectionAlive;

    private bool ReceivePacketId(ulong id)
    {
        if (id <= _packetId && id >= 5) return false;

        _packetId = id;
        return true;
    }

    private void HandlePacket(byte[] packet, MessageType type)
    {
        try
        {
            var offset = sizeof(uint); // Skip message_header_type_t (4 bytes)

            // Read message_id_t (8 bytes unsigned long long)
            var id = BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(offset));
            offset += sizeof(ulong);

            if (!ReceivePacketId(id))
                return;

            switch (type)
            {
                case MessageType.Rotation:
                    Orientation = new Quaternion(
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset)),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + sizeof(float))),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + 2 * sizeof(float))),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + 3 * sizeof(float)))
                    );
                    break;
                case MessageType.Gyro:
                    Gyroscope = new Vector3(
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset)),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + sizeof(float))),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + 2 * sizeof(float)))
                    );
                    break;
                case MessageType.Accelerometer:
                    Acceleration = new Vector3(
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset)),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + sizeof(float))),
                        BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset + 2 * sizeof(float)))
                    );
                    break;
                case MessageType.Heartbeat:
                case MessageType.Handshake:
                default:
                    break;
            }

            IsDataAvailable = true;
        }
        catch (Exception ex)
        {
            Host?.Log($"Error handling packet: {ex.Message}");
        }
    }

    #endregion
}

internal class ByteBuffer
{
    private readonly List<byte> _buffer = [];

    public ByteBuffer PutFloat(float value)
    {
        _buffer.AddRange(BitConverter.GetBytes(value));
        return this;
    }

    public ByteBuffer PutInt(int value)
    {
        _buffer.AddRange(BitConverter.GetBytes(value));
        return this;
    }

    public byte[] ToArray()
    {
        return _buffer.ToArray();
    }
}