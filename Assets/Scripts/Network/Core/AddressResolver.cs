using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Network
{
    /// <summary>DNS 解析结果</summary>
    public struct DnsResult
    {
        public string IPv4Address;
        public string IPv6Address;
        public bool HasIPv4 => !string.IsNullOrEmpty(IPv4Address);
        public bool HasIPv6 => !string.IsNullOrEmpty(IPv6Address);
        public long ResolveTime; // 解析时间戳

        public bool IsExpired => 
            (DateTime.UtcNow.Ticks - ResolveTime) / TimeSpan.TicksPerSecond > NetworkDefine.DnsCacheTTL;
    }

    /// <summary>
    /// 地址解析器 — IPv4/IPv6 双栈 DNS 解析 + Happy Eyeballs (RFC 8305)。
    /// 
    /// 算法：
    /// 1. 同时发起 A (IPv4) 和 AAAA (IPv6) 查询
    /// 2. 优先尝试 IPv6 连接
    /// 3. 250ms 内 IPv6 无响应 → 并发尝试 IPv4
    /// 4. 取先连接成功的地址
    /// 
    /// 苹果 App Store 要求：应用必须在纯 IPv6 网络下正常工作。
    /// </summary>
    public static class AddressResolver
    {
        private static readonly Dictionary<string, DnsResult> _cache = 
            new Dictionary<string, DnsResult>();

        /// <summary>解析主机名，同时获取 IPv4 和 IPv6 地址</summary>
        public static async Task<DnsResult> ResolveAsync(string host)
        {
            // 检查缓存
            lock (_cache)
            {
                if (_cache.TryGetValue(host, out var cached) && !cached.IsExpired)
                    return cached;
            }

            // 如果已经是 IP 地址，直接返回
            if (IPAddress.TryParse(host, out var ip))
            {
                var result = new DnsResult
                {
                    IPv4Address = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? host : null,
                    IPv6Address = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? host : null,
                    ResolveTime = DateTime.UtcNow.Ticks,
                };
                lock (_cache) { _cache[host] = result; }
                return result;
            }

            // DNS 解析
            DnsResult dnsResult;
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);

                string ipv4 = null, ipv6 = null;
                foreach (var addr in addresses)
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ipv4 = addr.ToString();
                    else if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        ipv6 = addr.ToString();
                }

                dnsResult = new DnsResult
                {
                    IPv4Address = ipv4,
                    IPv6Address = ipv6,
                    ResolveTime = DateTime.UtcNow.Ticks,
                };
            }
            catch (Exception)
            {
                dnsResult = new DnsResult
                {
                    ResolveTime = DateTime.UtcNow.Ticks,
                };
            }

            // 缓存
            lock (_cache) { _cache[host] = dnsResult; }

            return dnsResult;
        }

        /// <summary>
        /// Happy Eyeballs 连接策略 — 优先 IPv6，250ms 超时则并发 IPv4。
        /// 返回先连接成功的 Socket 和地址族。
        /// </summary>
        public static async Task<(Socket socket, AddressFamily family)> ConnectWithHappyEyeballsAsync(
            string host, int port)
        {
            var dnsResult = await ResolveAsync(host);

            // 只有 IPv4
            if (dnsResult.HasIPv4 && !dnsResult.HasIPv6)
            {
                var socket = await ConnectAsync(dnsResult.IPv4Address, port, 
                    System.Net.Sockets.AddressFamily.InterNetwork);
                return (socket, Network.AddressFamily.IPv4);
            }

            // 只有 IPv6
            if (dnsResult.HasIPv6 && !dnsResult.HasIPv4)
            {
                var socket = await ConnectAsync(dnsResult.IPv6Address, port,
                    System.Net.Sockets.AddressFamily.InterNetworkV6);
                return (socket, Network.AddressFamily.IPv6);
            }

            // 双栈 → Happy Eyeballs
            // 优先发起 IPv6 连接
            var ipv6Task = ConnectAsync(dnsResult.IPv6Address, port,
                System.Net.Sockets.AddressFamily.InterNetworkV6);

            // 等待 250ms
            var delayTask = Task.Delay(NetworkDefine.HappyEyeballsIPv6Delay);
            var completed = await Task.WhenAny(ipv6Task, delayTask);

            if (completed == ipv6Task && ipv6Task.IsCompletedSuccessfully)
            {
                return (ipv6Task.Result, Network.AddressFamily.IPv6);
            }

            // IPv6 未及时响应，并发 IPv4
            var ipv4Task = ConnectAsync(dnsResult.IPv4Address, port,
                System.Net.Sockets.AddressFamily.InterNetwork);

            // 竞速：取先成功的
            var completedTask = await Task.WhenAny(ipv6Task, ipv4Task);
            var loserTask = completedTask == ipv6Task ? ipv4Task : ipv6Task;

            Socket winnerSocket;
            AddressFamily winnerFamily;

            try
            {
                winnerSocket = await completedTask;
                winnerFamily = completedTask == ipv6Task ? Network.AddressFamily.IPv6 : Network.AddressFamily.IPv4;
                // 关闭失败方 Socket
                CloseLoserSocket(loserTask);
            }
            catch
            {
                // 第一个失败，等第二个
                CloseLoserSocket(completedTask);
                winnerSocket = await loserTask;
                winnerFamily = loserTask == ipv6Task ? Network.AddressFamily.IPv6 : Network.AddressFamily.IPv4;
            }

            return (winnerSocket, winnerFamily);
        }

        private static void CloseLoserSocket(Task<Socket> task)
        {
            if (task.IsCompletedSuccessfully)
            {
                try { task.Result?.Close(); } catch { }
            }
        }

        private static async Task<Socket> ConnectAsync(string ip, int port,
            System.Net.Sockets.AddressFamily addressFamily)
        {
            var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            
            var tcs = new TaskCompletionSource<bool>();
            socket.BeginConnect(ip, port, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            await tcs.Task;
            return socket;
        }

        /// <summary>清除 DNS 缓存</summary>
        public static void ClearCache()
        {
            lock (_cache) { _cache.Clear(); }
        }
    }
}
