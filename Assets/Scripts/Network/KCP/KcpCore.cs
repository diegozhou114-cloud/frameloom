using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Network
{
    // ============================================================
    // KCP — 基于 UDP 的可靠传输协议
    //
    // 算法参考：
    // - skywind3000/kcp (C 语言原始实现)
    // - RFC 6298: Computing TCP's Retransmission Timer (Jacobson/Karels)
    // - RFC 5681: TCP Congestion Control (Slow Start / Congestion Avoidance)
    //
    // 核心机制：
    // 1. ARQ (Automatic Repeat reQuest) — 超时重传
    // 2. 快速重传 — 收到 skip 次重复 ACK 后不等超时直接重传
    // 3. 流量控制 — 滑动窗口（发送/接收窗口）
    // 4. 拥塞控制 — 慢启动 + 拥塞避免
    // 5. RTO 计算 — Jacobson/Karels 算法
    // ============================================================

    #region 常量与数据结构

    /// <summary>KCP 命令</summary>
    internal static class KcpCmd
    {
        public const byte Push = 81;  // 数据推送
        public const byte Ack  = 82;  // 确认
        public const byte WAsk = 83;  // 窗口探测（询问对端窗口大小）
        public const byte Wins = 84;  // 窗口通知（告知对端本端窗口大小）
    }

    /// <summary>KCP 段（Segment）</summary>
    internal class KcpSegment
    {
        public uint Conv;          // 会话 ID
        public byte Cmd;           // 命令：Push/Ack/WAsk/Wins
        public byte Frg;           // 分片序号（倒序：0=最后一片）
        public ushort Wnd;         // 窗口大小
        public uint Ts;            // 时间戳
        public uint Sn;            // 序列号
        public uint Una;           // 未确认序列号（累积 ACK）
        public uint ResendTs;      // 下次重传时间戳
        public uint Rto;           // 重传超时
        public uint FastAck;       // 快速重传 ACK 计数
        public uint Xmit;          // 已传输次数
        public byte[] Data;        // 数据负载

        public KcpSegment(int dataSize)
        {
            Data = new byte[dataSize];
        }
    }

    #endregion

    #region KCP 控制块

    /// <summary>
    /// KCP 核心控制块 — 每个连接一个实例。
    /// 
    /// 用法：
    /// <code>
    ///   var kcp = new KcpCore(conv, (buf, len) => udp.Send(buf, len));
    ///   kcp.NoDelay(1, 10, 2, 1);       // 快速模式
    ///   kcp.WndSize(128, 128);          // 窗口大小
    ///   kcp.Send(data);                  // 发送数据
    ///   kcp.Update(DateTime.Now.Millisecond); // 每 10~100ms 调用
    ///   var recv = kcp.Recv();           // 接收数据
    ///   kcp.Input(receivedUdpData);       // 喂入收到的 UDP 数据
    /// </code>
    /// </summary>
    public class KcpCore
    {
        // 基于 skywind3000/kcp 的 IKCPCB 结构

        /// <summary>默认内部 flush 间隔 (ms)</summary>
        public const int IKCP_INTERVAL = 10;

        private readonly uint _conv;          // 会话 ID（双方一致）
        private uint _mtu = 1400;              // 最大传输单元（含 KCP 头 24 字节）
        private uint _mss = 1376;              // 最大段大小 = MTU - KCP_OVERHEAD(24)
        private int _state = 0;               // 0=未建立, -1=已释放

        // 发送端状态
        private uint _sndUna;                  // 最早未确认的序列号（对端已连续收到的）
        private uint _sndNxt;                  // 下一个要发送的序列号
        private uint _rcvNxt;                  // 期望接收的下一个序列号

        // RTT 估算 — Jacobson/Karels 算法 (RFC 6298)
        // SRTT = (1-α)*SRTT + α*RTT,  α=1/8
        // RTTVAR = (1-β)*RTTVAR + β*|SRTT-RTT|, β=1/4
        // RTO = SRTT + max(G, 4*RTTVAR)
        private uint _rxSrtt;                  // 平滑 RTT (×1000)
        private uint _rxRttval;                // RTT 变化量 (×1000)
        private uint _rxRto = 200;             // 重传超时 (ms)，初始 200ms
        private uint _rxMinrto = 100;          // 最小 RTO (ms)

        // 窗口与拥塞控制 — RFC 5681
        private uint _sndWnd = 32;             // 发送窗口（段数）
        private uint _rcvWnd = 128;            // 接收窗口（段数）
        private uint _rmtWnd = 128;            // 远端窗口
        private uint _cwnd = 0;               // 拥塞窗口
        private uint _ssthresh = 0;           // 慢启动阈值
        private uint _incr = 0;               // 拥塞窗口增量（字节计数）

        // 探测
        private uint _probe = 0;              // 探测标志：1=需要发送窗口探测
        private uint _probeWait = 0;           // 探测等待计时器

        // 时间
        private uint _current = 0;             // 当前时间 (ms)
        private uint _interval = 100;          // flush 间隔 (ms)

        /// <summary>内部 flush 间隔 (ms)，最小 10</summary>
        public uint Interval
        {
            get => _interval;
            set => _interval = Math.Max(value, 10);
        }

        private uint _tsFlush = 0;             // 下次 flush 时间
        private uint _nextUpdate = 0;          // 下次 update 需要的时间

        // 模式开关
        private int _nodelay = 0;              // 0=正常, 1=无延迟
        private int _updated = 0;             // 是否调用过 update
        private int _tsProbe = 0;              // 时间戳探测
        private uint _probeAddr = 0;           // 探测地址

        // 快速重传
        private int _fastResend = 0;           // 触发快速重传的重复 ACK 数
        private int _fastLimit = 5;            // 快速重传最大次数
        private int _noCwnd = 0;              // 是否禁用拥塞控制
        private int _stream = 0;              // 流模式（合并相邻数据包）

        // 当前最小 RTO
        private uint _currentRto;

        // 回调 — 将 KCP 输出写入底层 UDP
        private readonly Action<byte[], int> _output;

        // 队列
        private readonly LinkedList<KcpSegment> _sndQueue = new LinkedList<KcpSegment>();
        private readonly LinkedList<KcpSegment> _rcvQueue = new LinkedList<KcpSegment>();
        private readonly LinkedList<KcpSegment> _sndBuf = new LinkedList<KcpSegment>();
        private readonly LinkedList<KcpSegment> _rcvBuf = new LinkedList<KcpSegment>();

        // ACK 列表
        private readonly List<(uint sn, uint ts)> _ackList = new List<(uint, uint)>();

        // 缓冲区
        private byte[] _buffer;

        // 统计数据
        private uint _rxCount = 0;
        private uint _rxMaxTry = 0;

        private const int KcpOverhead = 24;    // KCP 头部大小

        #endregion

        #region 构造

        /// <param name="conv">会话 ID（双方必须一致）</param>
        /// <param name="output">输出回调 — KCP 将编码好的数据通过此函数发送到 UDP</param>
        public KcpCore(uint conv, Action<byte[], int> output)
        {
            _conv = conv;
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _buffer = new byte[(_mtu + KcpOverhead) * 3];
        }

        #endregion

        #region 公共 API

        /// <summary>发送数据（流模式自动分片）</summary>
        /// <returns>0=成功, 负值=失败</returns>
        public int Send(byte[] data)
        {
            return Send(data, 0, data.Length);
        }

        /// <summary>发送数据</summary>
        public int Send(byte[] data, int offset, int length)
        {
            if (length <= 0) return 0;

            int count;
            if (_stream != 0)
            {
                // 流模式：合并到最后一个未满的段
                count = SendStream(data, offset, length);
            }
            else
            {
                // 消息模式：按 MSS 分片
                count = SendMessage(data, offset, length);
            }
            return count;
        }

        /// <summary>窥探下一完整消息大小（不消费）</summary>
        /// <returns>消息字节数，无完整消息返回 0</returns>
        public int PeekSize()
        {
            if (_rcvQueue.Count == 0) return 0;

            var peek = _rcvQueue.First.Value;
            if (peek.Frg != 0) return 0; // 首包不完整

            int totalLen = 0;
            foreach (var seg in _rcvQueue)
            {
                totalLen += seg.Data.Length;
                if (seg.Frg == 0) break;
            }
            return totalLen;
        }

        /// <summary>接收数据到缓冲区（标准 KCP Recv 重载）</summary>
        /// <returns>实际接收字节数，无数据返回 -1</returns>
        public int Recv(byte[] buffer)
        {
            var data = Recv();
            if (data == null) return -1;
            int len = Math.Min(data.Length, buffer.Length);
            Buffer.BlockCopy(data, 0, buffer, 0, len);
            return len;
        }

        /// <summary>接收数据</summary>
        /// <returns>完整消息的数据，null=没有新消息</returns>
        public byte[] Recv()
        {
            if (_rcvQueue.Count == 0) return null;

            // 检查队首是否完整（frg=0 表示最后一片）
            var peek = _rcvQueue.First.Value;
            if (peek.Frg != 0)
            {
                // 不完整，等待更多片段
                return null;
            }

            // 计算消息总长度
            int totalLen = 0;
            foreach (var seg in _rcvQueue)
            {
                totalLen += seg.Data.Length;
                if (seg.Frg == 0) break;
            }

            var result = new byte[totalLen];
            int destOffset = 0;
            var consumed = new List<KcpSegment>();

            foreach (var seg in _rcvQueue)
            {
                Buffer.BlockCopy(seg.Data, 0, result, destOffset, seg.Data.Length);
                destOffset += seg.Data.Length;
                consumed.Add(seg);

                if (seg.Frg == 0) break;
            }

            foreach (var seg in consumed)
            {
                _rcvQueue.Remove(seg);
            }

            return result;
        }

        /// <summary>喂入从 UDP 收到的原始数据</summary>
        /// <returns>0=成功</returns>
        public int Input(byte[] data)
        {
            return Input(data, 0, data.Length);
        }

        /// <summary>喂入从 UDP 收到的原始数据</summary>
        public int Input(byte[] data, int offset, int length)
        {
            if (data == null || length < KcpOverhead) return -1;

            uint oldUna = _sndUna;
            int maxAck = 0;
            int flag = 0;
            uint receivedUna = _sndUna;

            // 解析数据包（可能包含多个 KCP 段）
            int pos = offset;
            while (pos < offset + length)
            {
                int newPos = ParseKcpPacket(data, pos, offset + length, ref flag, ref receivedUna, ref maxAck);
                if (newPos < 0) break;
                pos = newPos;
            }

            // 处理累积 ACK (una) — 更新对端已连续收到的序列号
            if (flag != 0)
            {
                _sndUna = receivedUna;
                ParseUnalignedAck(maxAck);
            }

            if (oldUna != _sndUna)
            {
                // 更新发送缓冲区
                UpdateSndBuf();
            }

            return 0;
        }

        /// <returns>New position after consuming this packet, or -1 if we should break the loop</returns>
        private int ParseKcpPacket(byte[] data, int pos, int endPos,
            ref int flag, ref uint receivedUna, ref int maxAck)
        {
            if (endPos - pos < KcpOverhead) return -1;

            uint segConv = ReadU32(data, pos);
            if (segConv != _conv) return pos + KcpOverhead;

            byte cmd = data[pos + 4];
            byte frg = data[pos + 5];
            ushort wnd = ReadU16(data, pos + 6);
            uint ts = ReadU32(data, pos + 8);
            uint sn = ReadU32(data, pos + 12);
            uint una = ReadU32(data, pos + 16);
            int dataLen = (int)ReadU32(data, pos + 20);

            pos += KcpOverhead;
            if (pos + dataLen > endPos) return -1;

            flag = 1; // 标记有有效数据包
            if (una > receivedUna) receivedUna = una;

            // 处理各类命令
            switch (cmd)
            {
                case KcpCmd.Push:
                    ParseData(sn, frg, ts, data, pos, dataLen);
                    break;
                case KcpCmd.Ack:
                    ParseAck(sn, ts);
                    break;
                case KcpCmd.WAsk:
                    ParseWAsk();
                    break;
                case KcpCmd.Wins:
                    ParseWins(wnd);
                    break;
            }

            if (sn > maxAck) maxAck = (int)sn;

            return pos + dataLen;
        }

        /// <summary>定时更新 — 每 10~100ms 调用一次</summary>
        /// <param name="currentMs">当前时间 (ms)，通常用 DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond</param>
        public void Update(uint currentMs)
        {
            _current = currentMs;

            if (_updated == 0)
            {
                _updated = 1;
                _tsFlush = _current;
            }

            int slap = (int)(_current - _tsFlush);
            if (slap >= 10000 || slap < -10000)
            {
                _tsFlush = _current;
                slap = 0;
            }

            if (slap >= 0)
            {
                _tsFlush += _interval;
                if (_current - _tsFlush >= 0)
                {
                    _tsFlush = _current + _interval;
                }
                Flush();
            }
        }

        /// <summary>检查 Update 的间隔 — 返回距离下次 Update 还需要多少 ms</summary>
        public uint Check(uint currentMs)
        {
            if (_updated == 0) return 0;

            uint tsFlush = _tsFlush;
            int tmFlush = (int)(tsFlush - currentMs);

            if (tmFlush >= 10000 || tmFlush < -10000)
            {
                tsFlush = currentMs;
                tmFlush = 0;
            }

            if (tmFlush < 0) return 0;

            // 检查重传定时器
            uint tmPacket = 0xFFFFFFFF;
            uint minimal = 0;

            if (_nodelay != 0)
            {
                minimal = _rxMinrto;
            }
            else
            {
                minimal = _interval;
            }

            foreach (var seg in _sndBuf)
            {
                int diff = (int)(seg.ResendTs - currentMs);
                if (diff <= 0)
                    return 0;
                if ((uint)diff < tmPacket)
                    tmPacket = (uint)diff;
            }

            uint tmEvent = (uint)tmFlush;
            if (tmPacket < tmEvent) tmEvent = tmPacket;

            tmEvent = Math.Max(tmEvent, minimal);
            return tmEvent < _interval ? tmEvent : _interval;
        }

        /// <summary>设置 MTU（含 KCP 头 24 字节）</summary>
        public int SetMtu(int mtu)
        {
            if (mtu < 50 || mtu < KcpOverhead) return -1;
            _mtu = (uint)mtu;
            _mss = (uint)mtu - KcpOverhead;
            _buffer = new byte[(_mtu + KcpOverhead) * 3];
            return 0;
        }

        /// <summary>设置窗口大小</summary>
        public int WndSize(int sndWnd, int rcvWnd)
        {
            if (sndWnd > 0) _sndWnd = (uint)sndWnd;
            if (rcvWnd > 0) _rcvWnd = (uint)Math.Max(rcvWnd, 16);
            return 0;
        }

        /// <summary>无延迟模式（SetNoDelay 别名）</summary>
        public int SetNoDelay(int nodelay, int interval, int resend, int nc)
            => NoDelay(nodelay, interval, resend, nc);

        /// <summary>设置窗口大小（SetWindowSize 别名）</summary>
        public int SetWindowSize(int sndWnd, int rcvWnd)
            => WndSize(sndWnd, rcvWnd);

        /// <summary>获取待发送队列大小</summary>
        public int WaitSnd => _sndBuf.Count + _sndQueue.Count;

        /// <summary>无延迟模式</summary>
        /// <param name="nodelay">0=禁用, 1=启用</param>
        /// <param name="interval">内部 flush 间隔 (ms)</param>
        /// <param name="resend">快速重传阈值</param>
        /// <param name="nc">是否禁用拥塞控制 (0=启用, 1=禁用)</param>
        public int NoDelay(int nodelay, int interval, int resend, int nc)
        {
            _nodelay = nodelay;
            if (nodelay != 0)
            {
                _rxMinrto = 10;
            }
            else
            {
                _rxMinrto = 100;
            }

            if (interval >= 0)
            {
                _interval = (uint)Math.Max(interval, 10);
                _currentRto = _interval;
            }

            if (resend >= 0) _fastResend = resend;
            if (nc >= 0) _noCwnd = nc;

            return 0;
        }

        /// <summary>释放 KCP</summary>
        public void Release()
        {
            _state = -1;
            ClearList(_sndBuf);
            ClearList(_rcvBuf);
            ClearList(_sndQueue);
            ClearList(_rcvQueue);
            _ackList.Clear();
            _buffer = null;
        }

        #endregion

        #region 内部 — 解析接收数据

        private void ParseData(uint sn, byte frg, uint ts, byte[] data, int offset, int len)
        {
            if (sn < _rcvNxt + _rcvWnd)
            {
                // 生成 ACK
                _ackList.Add((sn, ts));

                if (sn >= _rcvNxt)
                {
                    // 按序列号插入 rcvBuf
                    InsertSorted(_rcvBuf, sn, frg, ts, data, offset, len);
                }
                else
                {
                    // 重复包，丢弃
                }
            }
            else
            {
                // 超出窗口，丢弃
            }

            // 移动数据到 rcvQueue
            MoveRcvData();
        }

        private void ParseAck(uint sn, uint ts)
        {
            if (sn >= _sndNxt || sn < _sndUna)
            {
                // 无效或重复 ACK
                UpdateAck((int)(_current - ts));
                return;
            }

            // 遍历发送缓冲区，找到对应段并标记 ACK
            var node = _sndBuf.First;
            while (node != null)
            {
                var seg = node.Value;
                if (sn == seg.Sn)
                {
                    _sndBuf.Remove(node);
                    break;
                }
                if (sn < seg.Sn)
                    break;
                node = node.Next;
            }
        }

        private void ParseWAsk()
        {
            _probe |= 1; // 需要回复窗口探测
        }

        private void ParseWins(ushort wnd)
        {
            _rmtWnd = wnd;
        }

        private void ParseUnalignedAck(int maxAck)
        {
            // 处理累积 ACK（una 字段）
            _sndUna = (uint)maxAck;
        }

        private void UpdateAck(int rtt)
        {
            // Jacobson/Karels RTO 计算 (RFC 6298)
            if (rtt < 0) rtt = 0;

            if (_rxSrtt == 0)
            {
                _rxSrtt = (uint)rtt;
                _rxRttval = (uint)(rtt / 2);
            }
            else
            {
                int delta = rtt - (int)_rxSrtt;
                if (delta < 0) delta = -delta;

                _rxRttval = (uint)((3 * _rxRttval + (uint)delta) / 4);
                _rxSrtt = (uint)((7 * _rxSrtt + (uint)rtt) / 8);

                if (_rxSrtt < 1) _rxSrtt = 1;
            }

            uint rto = _rxSrtt + Math.Max(_interval, 4 * _rxRttval);
            _rxRto = Math.Max(_rxMinrto, rto);
        }

        #endregion

        #region 内部 — 发送队列管理

        private int SendMessage(byte[] data, int offset, int length)
        {
            int count = 0;

            if (length <= _mss)
            {
                count = 1;
                var seg = new KcpSegment((int)length);
                Buffer.BlockCopy(data, offset, seg.Data, 0, length);
                seg.Frg = 0;
                _sndQueue.AddLast(seg);
            }
            else
            {
                int frgCount = (int)((length + _mss - 1) / _mss);
                count = frgCount;

                for (int i = 0; i < frgCount; i++)
                {
                    int segLen = (int)Math.Min(_mss, length - i * (int)_mss);
                    var seg = new KcpSegment(segLen);
                    Buffer.BlockCopy(data, offset + i * (int)_mss, seg.Data, 0, segLen);
                    seg.Frg = (byte)(frgCount - i - 1); // 倒序
                    _sndQueue.AddLast(seg);
                }
            }

            return count;
        }

        private int SendStream(byte[] data, int offset, int length)
        {
            // 流模式：尝试合并到最后一个段
            if (_sndQueue.Count > 0)
            {
                var last = _sndQueue.Last.Value;
                uint space = _mss - (uint)last.Data.Length;
                if (space > 0)
                {
                    int fill = (int)Math.Min(space, length);
                    var newData = new byte[last.Data.Length + fill];
                    Buffer.BlockCopy(last.Data, 0, newData, 0, last.Data.Length);
                    Buffer.BlockCopy(data, offset, newData, last.Data.Length, fill);
                    last.Data = newData;

                    if (fill == length) return 1;
                    offset += fill;
                    length -= fill;
                }
            }

            // 剩余数据按 MSS 分片
            return SendMessage(data, offset, length);
        }

        #endregion

        #region 内部 — 数据发送 (Flush)

        private void Flush()
        {
            if (_updated == 0) return;

            _currentRto = _rxRto;

            // 1. 发送窗口探测回复
            if (_probe != 0)
            {
                // 发送窗口通知
                FlushWins();
            }

            // 2. 计算拥塞窗口
            uint cwnd = Math.Min(_sndWnd, _rmtWnd);
            if (_noCwnd == 0)
            {
                cwnd = Math.Min(_cwnd, cwnd);
            }

            // 3. 将 sndQueue 中的段移到 sndBuf（分配 SN）
            MoveSegmentsToSendBuf(cwnd);

            // 4. 发送 ACK
            FlushAck();

            // 5. 检查重传
            uint rtomin = _nodelay != 0 ? 0 : _rxRto >> 3;
            uint resentCount = ProcessRetransmissions(rtomin);

            // 6. 拥塞控制
            ApplyCongestionControl(cwnd, resentCount);
        }

        // ─── Flush 子步骤 3：从 sndQueue 搬移到 sndBuf 并分配 SN ───
        private void MoveSegmentsToSendBuf(uint cwnd)
        {
            while (_sndNxt < _sndUna + cwnd && _sndQueue.Count > 0)
            {
                var seg = _sndQueue.First.Value;
                _sndQueue.RemoveFirst();

                seg.Conv = _conv;
                seg.Cmd = KcpCmd.Push;
                seg.Wnd = (ushort)_rcvWnd;
                seg.Ts = _current;
                seg.Sn = _sndNxt++;
                seg.Una = _rcvNxt;
                seg.ResendTs = _current + _rxRto;
                seg.Rto = _rxRto;
                seg.Xmit = 0;

                _sndBuf.AddLast(seg);
            }
        }

        // ─── Flush 子步骤 5：遍历 sndBuf，重传超时/快速重传段并输出到 UDP ───
        private uint ProcessRetransmissions(uint rtomin)
        {
            uint resentCount = 0;

            var node = _sndBuf.First;
            while (node != null)
            {
                var seg = node.Value;
                bool needSend = false;

                // 首次发送
                if (seg.Xmit == 0)
                {
                    needSend = true;
                    seg.Xmit++;
                    seg.Rto = _rxRto;
                    seg.ResendTs = _current + _rxRto + rtomin;
                }
                // 超时重传
                else if (_current >= seg.ResendTs)
                {
                    needSend = true;
                    seg.Xmit++;
                    seg.Rto = Math.Max(_rxRto, seg.Rto * 2);
                    seg.ResendTs = _current + seg.Rto + rtomin;

                    if (_rxMaxTry < seg.Xmit) _rxMaxTry = seg.Xmit;
                    resentCount++;
                }
                // 快速重传
                else if (seg.FastAck >= _fastResend)
                {
                    needSend = true;
                    seg.Xmit++;
                    seg.FastAck = 0;
                    seg.ResendTs = _current + seg.Rto;

                    if (_rxMaxTry < seg.Xmit) _rxMaxTry = seg.Xmit;
                    resentCount++;
                }

                if (needSend)
                {
                    seg.Ts = _current;
                    seg.Wnd = (ushort)_rcvWnd;
                    seg.Una = _rcvNxt;

                    int headerSize = KcpOverhead;
                    int totalLen = headerSize + seg.Data.Length;

                    // 使用预分配缓冲区（避免每包GC分配）
                    // WARNING: _buffer 非线程安全；Flush 必须与 Input/Update 串行化
                    if (_buffer == null || _buffer.Length < totalLen)
                        _buffer = new byte[totalLen];
                    var tmpBuffer = _buffer;
                    WriteU32(tmpBuffer, 0, seg.Conv);
                    tmpBuffer[4] = seg.Cmd;
                    tmpBuffer[5] = seg.Frg;
                    WriteU16(tmpBuffer, 6, seg.Wnd);
                    WriteU32(tmpBuffer, 8, seg.Ts);
                    WriteU32(tmpBuffer, 12, seg.Sn);
                    WriteU32(tmpBuffer, 16, seg.Una);
                    WriteU32(tmpBuffer, 20, (uint)seg.Data.Length);
                    Buffer.BlockCopy(seg.Data, 0, tmpBuffer, headerSize, seg.Data.Length);

                    _output(tmpBuffer, totalLen);
                }

                node = node.Next;
            }

            return resentCount;
        }

        // ─── Flush 子步骤 6：拥塞控制窗口更新 ───
        private void ApplyCongestionControl(uint cwnd, uint resentCount)
        {
            // 拥塞控制 — 仅在检测到新丢包时减半（不在每次Flush重复触发）
            if (resentCount > 0)
            {
                _ssthresh = Math.Max(1, cwnd / 2);
                _cwnd = 1;
                _incr = _mss;
            }

            if (_cwnd < 1)
            {
                _cwnd = 1;
                _incr = _mss;
            }
        }

        private void FlushAck()
        {
            if (_ackList.Count == 0) return;

            // 尝试合并多个 ACK
            int headerSize = KcpOverhead;
            int totalLen = headerSize;
            int count = Math.Min(_ackList.Count, 32); // 单次最多 32 个 ACK

            var tmpBuffer = new byte[headerSize + count * 8];

            uint maxSn = 0;
            for (int i = 0; i < count; i++)
            {
                var (sn, ts) = _ackList[i];
                if (sn > maxSn) maxSn = sn;
                WriteU32(tmpBuffer, headerSize + i * 8, sn);
                WriteU32(tmpBuffer, headerSize + i * 8 + 4, ts);
            }

            WriteU32(tmpBuffer, 0, _conv);
            tmpBuffer[4] = KcpCmd.Ack;
            tmpBuffer[5] = 0;
            WriteU16(tmpBuffer, 6, (ushort)_rcvWnd);
            WriteU32(tmpBuffer, 8, _current);
            WriteU32(tmpBuffer, 12, maxSn);
            WriteU32(tmpBuffer, 16, _rcvNxt);
            WriteU32(tmpBuffer, 20, (uint)(count * 8));

            _output(tmpBuffer, headerSize + count * 8);

            _ackList.RemoveRange(0, count);
        }

        private void FlushWins()
        {
            // 发送窗口通知
            var tmpBuffer = new byte[KcpOverhead];
            WriteU32(tmpBuffer, 0, _conv);
            tmpBuffer[4] = KcpCmd.Wins;
            tmpBuffer[5] = 0;
            WriteU16(tmpBuffer, 6, (ushort)_rcvWnd);
            WriteU32(tmpBuffer, 8, _current);
            WriteU32(tmpBuffer, 12, 0);
            WriteU32(tmpBuffer, 16, _rcvNxt);
            WriteU32(tmpBuffer, 20, 0);

            _output(tmpBuffer, KcpOverhead);
            _probe = 0;
        }

        #endregion

        #region 内部 — 接收队列管理

        private void InsertSorted(LinkedList<KcpSegment> list, uint sn, byte frg, uint ts,
            byte[] data, int offset, int len)
        {
            // 检查重复
            var node = list.First;
            while (node != null)
            {
                if (node.Value.Sn == sn) return; // 重复，忽略
                if (node.Value.Sn > sn) break;
                node = node.Next;
            }

            var seg = new KcpSegment(len)
            {
                Sn = sn,
                Frg = frg,
                Ts = ts,
            };
            Buffer.BlockCopy(data, offset, seg.Data, 0, len);

            if (node == null)
                list.AddLast(seg);
            else
                list.AddBefore(node, seg);
        }

        private void MoveRcvData()
        {
            // 从 rcvBuf 按序移动到 rcvQueue
            while (_rcvBuf.Count > 0)
            {
                var seg = _rcvBuf.First.Value;
                if (seg.Sn == _rcvNxt)
                {
                    _rcvBuf.RemoveFirst();
                    _rcvQueue.AddLast(seg);
                    _rcvNxt++;
                }
                else if (seg.Sn > _rcvNxt)
                {
                    break; // 不连续，等待
                }
                else
                {
                    _rcvBuf.RemoveFirst(); // 过期，丢弃
                }
            }
        }

        private void UpdateSndBuf()
        {
            // 移除已确认的段（sn < snd_una）
            var node = _sndBuf.First;
            while (node != null)
            {
                var next = node.Next;
                var seg = node.Value;

                if (seg.Sn < _sndUna)
                {
                    // 拥塞控制：段被确认，增加拥塞窗口
                    if (_noCwnd == 0)
                    {
                        if (_cwnd < _rmtWnd)
                        {
                            if (_cwnd < _ssthresh)
                            {
                                // 慢启动
                                _cwnd++;
                                _incr += _mss;
                            }
                            else
                            {
                                // 拥塞避免
                                if (_incr < _mss) _incr = _mss;
                                _incr += _mss * _mss / _incr + _mss / 16;
                                if ((_cwnd + 1) * _mss <= _incr)
                                {
                                    _cwnd++;
                                }
                            }
                            if (_cwnd > _rmtWnd)
                            {
                                _cwnd = _rmtWnd;
                                _incr = _rmtWnd * _mss;
                            }
                        }
                    }

                    _sndBuf.Remove(node);
                }

                node = next;
            }
        }

        #endregion

        #region 工具方法

        private static void ClearList(LinkedList<KcpSegment> list)
        {
            list.Clear();
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   data[offset + 3];
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)((value >> 24) & 0xFF);
            data[offset + 1] = (byte)((value >> 16) & 0xFF);
            data[offset + 2] = (byte)((value >> 8) & 0xFF);
            data[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)((value >> 8) & 0xFF);
            data[offset + 1] = (byte)(value & 0xFF);
        }

        #endregion

        // ==================== 调试属性 ====================

        public uint SndUna => _sndUna;
        public uint SndNxt => _sndNxt;
        public uint RcvNxt => _rcvNxt;
        public uint Cwnd => _cwnd;
        public uint RxSrtt => _rxSrtt;
        public uint RxRto => _rxRto;
        public int SndQueueCount => _sndQueue.Count;
        public int RcvQueueCount => _rcvQueue.Count;
    }
}
