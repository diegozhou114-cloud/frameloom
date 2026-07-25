using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Native;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class Kcp2kImpairmentTests
    {
        [Test]
        public async Task NativeKcp_FragmentedMessage_RoundTrips()
        {
            var impairment = new DatagramImpairment(
                seed: 42,
                lossPercent: 20,
                duplicatePercent: 10,
                reorderWindow: 4,
                latencyMilliseconds: 150);
            using var pair = await Kcp2kTestPair.ConnectAsync(
                impairment, TestCredentials.Valid);
            byte[] payload = Enumerable.Range(0, 8192)
                .Select(index => (byte)(index % 251)).ToArray();

            await pair.Client.SendAsync(payload, CancellationToken.None);
            byte[] received = await pair.Server.ReceiveAsync(TimeSpan.FromSeconds(10));

            Assert.That(received, Is.EqualTo(payload));
            Assert.That(pair.Impairment.DroppedDatagrams, Is.GreaterThan(0));
            Assert.That(pair.Impairment.ReorderedDatagrams, Is.GreaterThan(0));
            Assert.That(pair.Impairment.DuplicatedDatagrams, Is.GreaterThan(0));
            ImpairmentEvidence.Record(nameof(NativeKcp_FragmentedMessage_RoundTrips), impairment);
        }

        [Test]
        public async Task NativeKcp_GappedAck_DoesNotDropMissingSegment()
        {
            var impairment = DatagramImpairment.DropOnce(
                datagramOrdinal: 2, reorderWindow: 3);
            using var pair = await Kcp2kTestPair.ConnectAsync(
                impairment, TestCredentials.Valid);
            byte[] payload = Enumerable.Range(0, 12288)
                .Select(index => (byte)(index % 251)).ToArray();

            await pair.Client.SendAsync(payload, CancellationToken.None);
            byte[] received = await pair.Server.ReceiveAsync(TimeSpan.FromSeconds(10));

            Assert.That(received, Is.EqualTo(payload));
            Assert.That(pair.Impairment.DroppedDatagrams, Is.EqualTo(1));
            Assert.That(pair.Impairment.RetransmittedMissingDatagrams,
                Is.GreaterThanOrEqualTo(1));
            ImpairmentEvidence.Record(nameof(NativeKcp_GappedAck_DoesNotDropMissingSegment), impairment);
        }
    }

    internal sealed class Kcp2kTestPair : IDisposable
    {
        private readonly DatagramProxy _proxy;

        public IRealtimeChannel Client { get; }
        public Kcp2kTestServer Server { get; }
        public DatagramImpairment Impairment { get; }

        private Kcp2kTestPair(
            IRealtimeChannel client,
            Kcp2kTestServer server,
            DatagramProxy proxy,
            DatagramImpairment impairment)
        {
            Client = client;
            Server = server;
            _proxy = proxy;
            Impairment = impairment;
        }

        public static async Task<Kcp2kTestPair> ConnectAsync(
            DatagramImpairment impairment,
            RealtimeCredentials credentials)
        {
            if (impairment == null) throw new ArgumentNullException(nameof(impairment));
            var server = new Kcp2kTestServer(credentials);
            server.Start();
            var proxy = new DatagramProxy(server.Endpoint, impairment);
            proxy.Start();
            IRealtimeChannel client = new Kcp2kRealtimeChannel();
            var pair = new Kcp2kTestPair(client, server, proxy, impairment);

            try
            {
                var endpoint = new RealtimeEndpoint(
                    RealtimeTransport.NativeKcp,
                    new Uri($"kcp://127.0.0.1:{proxy.Port}"));
                Task connect = client.ConnectAsync(
                    endpoint, credentials, CancellationToken.None);
                if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(10))) != connect)
                    throw new TimeoutException("Timed out completing the authenticated KCP handshake.");
                await connect;
                if (client.State != RealtimeChannelState.Connected)
                    throw new InvalidOperationException("The KCP channel did not reach Connected state.");
                impairment.BeginPayload();
                return pair;
            }
            catch
            {
                pair.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            _proxy.Dispose();
            Server.Dispose();
        }

    }

    internal sealed class Kcp2kTestServer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<byte[]> _messages = new Queue<byte[]>();
        private readonly SemaphoreSlim _messageSignal = new SemaphoreSlim(0);
        private readonly RealtimeCredentials _credentials;
        private readonly SequenceReplayWindow _networkReplay = new SequenceReplayWindow();
        private readonly CancellationTokenSource _tickCancellation =
            new CancellationTokenSource();
        private readonly kcp2k.KcpServer _server;
        private Task _tickTask;
        private bool _networkAuthenticated;
        private ulong _sendSequence;
        private int _connectionId;

        public IPEndPoint Endpoint
        {
            get
            {
                lock (_gate)
                {
                    var endpoint = (IPEndPoint)_server.LocalEndPoint;
                    return new IPEndPoint(IPAddress.Loopback, endpoint.Port);
                }
            }
        }
        public Kcp2kTestServer(RealtimeCredentials credentials)
        {
            _credentials = CloneCredentials(credentials);
            var config = new kcp2k.KcpConfig(
                DualMode: false,
                Interval: 1,
                FastResend: 2,
                CongestionWindow: false,
                SendWindowSize: 256,
                ReceiveWindowSize: 256,
                Timeout: 10000);
            _server = new kcp2k.KcpServer(
                connectionId => _connectionId = connectionId,
                OnData,
                connectionId => { },
                (connectionId, code, message) => { },
                config);
        }

        public void Start()
        {
            lock (_gate) _server.Start(0);
            _tickTask = Task.Run(TickLoopAsync);
        }

        public byte[] EncodeApplicationPacket(
            RealtimeCredentials credentials, ulong sequence, byte[] payload)
        {
            return AuthenticatedPacketCodec.Encode(
                credentials.SessionId, sequence, payload, credentials.MacKey);
        }

        public void SendApplicationPacket(byte[] packet)
        {
            lock (_gate)
            {
                _server.Send(
                    _connectionId, new ArraySegment<byte>(packet),
                    kcp2k.KcpChannel.Reliable);
            }
        }

        public async Task<byte[]> ReceiveAsync(TimeSpan timeout)
        {
            if (!await _messageSignal.WaitAsync(timeout))
                throw new TimeoutException("Timed out waiting for a complete KCP message.");
            lock (_gate) return _messages.Dequeue();
        }

        private async Task TickLoopAsync()
        {
            while (!_tickCancellation.IsCancellationRequested)
            {
                lock (_gate)
                {
                    _server.TickIncoming();
                    _server.TickOutgoing();
                }
                try
                {
                    await Task.Delay(1, _tickCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void OnData(
            int connectionId, ArraySegment<byte> segment, kcp2k.KcpChannel channel)
        {
            byte[] packet = Copy(segment);
            ProtocolError error = AuthenticatedPacketCodec.TryDecode(
                packet, _credentials.SessionId, _credentials.MacKey,
                _networkReplay, out AuthenticatedPacket decoded);
            if (error != ProtocolError.None) return;

            if (!_networkAuthenticated)
            {
                DecodeResult hello = RealtimeCodec.Decode(decoded.Payload);
                if (hello.Error != ProtocolError.None ||
                    hello.Kind != RealtimeMessageKind.AuthHello ||
                    hello.AuthHello.SessionId != _credentials.SessionId ||
                    !hello.AuthHello.OpaqueTicket.SequenceEqual(_credentials.OpaqueTicket) ||
                    _credentials.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    return;

                _networkAuthenticated = true;
                byte[] accepted = RealtimeCodec.Encode(new AuthAccepted
                {
                    SessionId = _credentials.SessionId,
                    LastAuthorityTick = -1,
                    TimelineRevision = 0
                });
                byte[] authenticated = AuthenticatedPacketCodec.Encode(
                    _credentials.SessionId, ++_sendSequence, accepted, _credentials.MacKey);
                lock (_gate)
                {
                    _server.Send(connectionId, new ArraySegment<byte>(authenticated),
                        kcp2k.KcpChannel.Reliable);
                }
                return;
            }

            lock (_gate) _messages.Enqueue(decoded.Payload);
            _messageSignal.Release();
        }

        public void Dispose()
        {
            _tickCancellation.Cancel();
            try
            {
                _tickTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }
            lock (_gate) _server.Stop();
            _tickCancellation.Dispose();
            _messageSignal.Dispose();
        }

        private static byte[] Copy(ArraySegment<byte> segment)
        {
            var copy = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, copy, 0, segment.Count);
            return copy;
        }

        private static RealtimeCredentials CloneCredentials(RealtimeCredentials credentials)
        {
            return new RealtimeCredentials
            {
                SessionId = credentials.SessionId,
                OpaqueTicket = (byte[])credentials.OpaqueTicket.Clone(),
                ResumeToken = (byte[])credentials.ResumeToken.Clone(),
                MacKey = (byte[])credentials.MacKey.Clone(),
                ExpiresAtUnixSeconds = credentials.ExpiresAtUnixSeconds
            };
        }
    }

    internal sealed class DatagramProxy : IDisposable
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _serverEndpoint;
        private readonly DatagramImpairment _impairment;
        private readonly CancellationTokenSource _cancellation =
            new CancellationTokenSource();
        private Task _pumpTask;

        public int Port => ((IPEndPoint)_socket.LocalEndPoint).Port;
        public IPEndPoint ClientEndpoint { get; private set; }

        public DatagramProxy(
            IPEndPoint serverEndpoint, DatagramImpairment impairment)
        {
            _serverEndpoint = serverEndpoint ??
                throw new ArgumentNullException(nameof(serverEndpoint));
            _impairment = impairment ?? throw new ArgumentNullException(nameof(impairment));
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Blocking = false;
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        }

        public void Start()
        {
            _pumpTask = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            var receiveBuffer = new byte[2048];
            while (!_cancellation.IsCancellationRequested)
            {
                ReceiveAvailable(receiveBuffer);
                foreach (ScheduledDatagram datagram in _impairment.TakeDue())
                    _socket.SendTo(datagram.Payload, _serverEndpoint);
                try
                {
                    await Task.Delay(1, _cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void ReceiveAvailable(byte[] receiveBuffer)
        {
            while (_socket.Poll(0, SelectMode.SelectRead))
            {
                EndPoint source = new IPEndPoint(IPAddress.Any, 0);
                int count;
                try
                {
                    count = _socket.ReceiveFrom(receiveBuffer, ref source);
                }
                catch (SocketException exception) when (
                    exception.SocketErrorCode == SocketError.WouldBlock)
                {
                    return;
                }

                var payload = new byte[count];
                Buffer.BlockCopy(receiveBuffer, 0, payload, 0, count);
                var sourceEndpoint = (IPEndPoint)source;
                bool fromServer = sourceEndpoint.Port == _serverEndpoint.Port &&
                    (_serverEndpoint.Address.Equals(IPAddress.Any) ||
                     sourceEndpoint.Address.Equals(_serverEndpoint.Address));
                if (fromServer)
                {
                    if (ClientEndpoint != null) _socket.SendTo(payload, ClientEndpoint);
                }
                else
                {
                    ClientEndpoint = sourceEndpoint;
                    _impairment.Enqueue(payload);
                }
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            try
            {
                _pumpTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }
            _socket.Dispose();
            _cancellation.Dispose();
        }
    }

    internal sealed class DatagramImpairment
    {
        private readonly object _gate = new object();
        private readonly int _seed;
        private readonly int _lossPercent;
        private readonly int _duplicatePercent;
        private readonly int _reorderWindow;
        private readonly int _latencyMilliseconds;
        private readonly int _dropOnceOrdinal;
        private readonly List<ScheduledDatagram> _scheduled =
            new List<ScheduledDatagram>();
        private readonly HashSet<uint> _droppedSequences = new HashSet<uint>();
        private readonly HashSet<uint> _seenSequences = new HashSet<uint>();
        private bool _active;
        private int _datagramOrdinal;

        public static DatagramImpairment None { get; } =
            new DatagramImpairment(0, 0, 0, 0, 0);

        public int DroppedDatagrams { get; private set; }
        public int DuplicatedDatagrams { get; private set; }
        public int ReorderedDatagrams { get; private set; }
        public int RetransmittedMissingDatagrams { get; private set; }

        public DatagramImpairment(
            int seed,
            int lossPercent,
            int duplicatePercent,
            int reorderWindow,
            int latencyMilliseconds)
            : this(seed, lossPercent, duplicatePercent, reorderWindow,
                latencyMilliseconds, 0)
        {
        }

        private DatagramImpairment(
            int seed,
            int lossPercent,
            int duplicatePercent,
            int reorderWindow,
            int latencyMilliseconds,
            int dropOnceOrdinal)
        {
            _seed = seed;
            _lossPercent = lossPercent;
            _duplicatePercent = duplicatePercent;
            _reorderWindow = reorderWindow;
            _latencyMilliseconds = latencyMilliseconds;
            _dropOnceOrdinal = dropOnceOrdinal;
        }

        public static DatagramImpairment DropOnce(
            int datagramOrdinal, int reorderWindow)
        {
            if (datagramOrdinal < 1)
                throw new ArgumentOutOfRangeException(nameof(datagramOrdinal));
            return new DatagramImpairment(
                0, 0, 0, reorderWindow, 0, datagramOrdinal);
        }

        public void BeginPayload()
        {
            lock (_gate)
            {
                _active = true;
                _datagramOrdinal = 0;
                DroppedDatagrams = 0;
                DuplicatedDatagrams = 0;
                ReorderedDatagrams = 0;
                RetransmittedMissingDatagrams = 0;
                _droppedSequences.Clear();
                _seenSequences.Clear();
            }
        }

        public void Enqueue(byte[] payload)
        {
            lock (_gate)
            {
                if (!_active || !TryReadPushSequence(payload, out uint sequence))
                {
                    Schedule(payload, DateTime.UtcNow);
                    return;
                }

                bool retransmission = !_seenSequences.Add(sequence);
                if (retransmission && _droppedSequences.Contains(sequence))
                    RetransmittedMissingDatagrams++;
                if (!retransmission) _datagramOrdinal++;

                bool drop = !retransmission &&
                    (_datagramOrdinal == _dropOnceOrdinal ||
                     DeterministicPercent(_datagramOrdinal, 37) < _lossPercent);
                if (drop)
                {
                    DroppedDatagrams++;
                    _droppedSequences.Add(sequence);
                    return;
                }

                int reorderDelay = _reorderWindow > 1
                    ? ((_reorderWindow - (_datagramOrdinal % _reorderWindow)) %
                       _reorderWindow) * 5
                    : 0;
                if (reorderDelay > 0 && !retransmission) ReorderedDatagrams++;
                DateTime delivery = DateTime.UtcNow.AddMilliseconds(
                    _latencyMilliseconds + reorderDelay);
                Schedule(payload, delivery);

                if (!retransmission &&
                    DeterministicPercent(_datagramOrdinal, 53) < _duplicatePercent)
                {
                    DuplicatedDatagrams++;
                    Schedule((byte[])payload.Clone(), delivery.AddMilliseconds(1));
                }
            }
        }

        public ScheduledDatagram[] TakeDue()
        {
            lock (_gate)
            {
                DateTime now = DateTime.UtcNow;
                ScheduledDatagram[] due = _scheduled
                    .Where(item => item.DeliveryUtc <= now)
                    .OrderBy(item => item.DeliveryUtc)
                    .ToArray();
                if (due.Length > 0)
                    _scheduled.RemoveAll(item => item.DeliveryUtc <= now);
                return due;
            }
        }

        private int DeterministicPercent(int ordinal, int multiplier)
        {
            return Math.Abs(unchecked(_seed + ordinal * multiplier)) % 100;
        }

        private void Schedule(byte[] payload, DateTime deliveryUtc)
        {
            _scheduled.Add(new ScheduledDatagram(payload, deliveryUtc));
        }

        private static bool TryReadPushSequence(byte[] payload, out uint sequence)
        {
            sequence = 0;
            const int kcpOffset = 5;
            const int commandOffset = kcpOffset + 4;
            const int sequenceOffset = kcpOffset + 12;
            if (payload == null || payload.Length < kcpOffset + kcp2k.Kcp.OVERHEAD)
                return false;
            if (payload[0] != (byte)kcp2k.KcpChannel.Reliable ||
                payload[commandOffset] != kcp2k.Kcp.CMD_PUSH)
                return false;
            sequence = (uint)(payload[sequenceOffset] |
                (payload[sequenceOffset + 1] << 8) |
                (payload[sequenceOffset + 2] << 16) |
                (payload[sequenceOffset + 3] << 24));
            return true;
        }
    }

    internal readonly struct ScheduledDatagram
    {
        public byte[] Payload { get; }
        public DateTime DeliveryUtc { get; }

        public ScheduledDatagram(byte[] payload, DateTime deliveryUtc)
        {
            Payload = payload;
            DeliveryUtc = deliveryUtc;
        }
    }

    internal static class ImpairmentEvidence
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, DatagramImpairment> Results =
            new Dictionary<string, DatagramImpairment>();

        public static void Record(string testName, DatagramImpairment impairment)
        {
            string path = Environment.GetEnvironmentVariable(
                "ZWEB_NATIVE_IMPAIRMENT_EVIDENCE");
            if (string.IsNullOrWhiteSpace(path))
                path = "/tmp/zweb-network-server-evidence/network/task-06/native-impairment.json";

            lock (Gate)
            {
                Results[testName] = impairment;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string[] entries = Results.OrderBy(pair => pair.Key)
                    .Select(pair =>
                        "  {\"test\":\"" + pair.Key + "\",\"dropped\":" +
                        pair.Value.DroppedDatagrams + ",\"duplicated\":" +
                        pair.Value.DuplicatedDatagrams + ",\"reordered\":" +
                        pair.Value.ReorderedDatagrams + ",\"retransmittedMissing\":" +
                        pair.Value.RetransmittedMissingDatagrams + "}")
                    .ToArray();
                File.WriteAllText(path, "[\n" + string.Join(",\n", entries) + "\n]\n");
            }
        }
    }

    internal static class TestCredentials
    {
        private static readonly Guid ValidSession =
            new Guid("7f670efe-5a69-4b00-9ccb-8a56ae224203");

        public static RealtimeCredentials Valid => Create(ValidSession, 4102444800L);
        public static RealtimeCredentials Expired => Create(ValidSession, 1);
        public static RealtimeCredentials Foreign => Create(
            new Guid("97d6dc30-a9c1-42bf-a7b4-7c07844e8d5a"), 4102444800L);

        private static RealtimeCredentials Create(Guid sessionId, long expiresAt)
        {
            return new RealtimeCredentials
            {
                SessionId = sessionId,
                OpaqueTicket = new byte[] { 3, 1, 4, 1, 5, 9 },
                ResumeToken = new byte[] { 2, 6, 5, 3, 5 },
                MacKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                ExpiresAtUnixSeconds = expiresAt
            };
        }
    }
}
