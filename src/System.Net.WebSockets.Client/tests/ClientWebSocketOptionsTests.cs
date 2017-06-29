// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.Test.Common;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;

namespace System.Net.WebSockets.Client.Tests
{
    public partial class ClientWebSocketOptionsTests : ClientWebSocketTestBase
    {
        public static bool CanTestCertificates =>
            Capability.IsTrustedRootCertificateInstalled() &&
            (BackendSupportsCustomCertificateHandling || Capability.AreHostsFileNamesInstalled());

        public static bool CanTestClientCertificates =>
            CanTestCertificates && BackendSupportsCustomCertificateHandling;

        public ClientWebSocketOptionsTests(ITestOutputHelper output) : base(output) { }

        [ConditionalFact(nameof(WebSocketsSupported))]
        public static void UseDefaultCredentials_Roundtrips()
        {
            var cws = new ClientWebSocket();
            Assert.False(cws.Options.UseDefaultCredentials);
            cws.Options.UseDefaultCredentials = true;
            Assert.True(cws.Options.UseDefaultCredentials);
            cws.Options.UseDefaultCredentials = false;
            Assert.False(cws.Options.UseDefaultCredentials);
        }

        [ActiveIssue(20358, TargetFrameworkMonikers.NetFramework)]
        [ConditionalFact(nameof(WebSocketsSupported))]
        public static void SetBuffer_InvalidArgs_Throws()
        {
            var cws = new ClientWebSocket();
            AssertExtensions.Throws<ArgumentOutOfRangeException>("receiveBufferSize", () => cws.Options.SetBuffer(0, 0));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("receiveBufferSize", () => cws.Options.SetBuffer(0, 1));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("sendBufferSize", () => cws.Options.SetBuffer(1, 0));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("receiveBufferSize", () => cws.Options.SetBuffer(0, 0, new ArraySegment<byte>(new byte[1])));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("receiveBufferSize", () => cws.Options.SetBuffer(0, 1, new ArraySegment<byte>(new byte[1])));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("sendBufferSize", () => cws.Options.SetBuffer(1, 0, new ArraySegment<byte>(new byte[1])));
            AssertExtensions.Throws<ArgumentNullException>("buffer.Array", () => cws.Options.SetBuffer(1, 1, default(ArraySegment<byte>)));
            AssertExtensions.Throws<ArgumentOutOfRangeException>("buffer", () => cws.Options.SetBuffer(1, 1, new ArraySegment<byte>(new byte[0])));
        }

        [ConditionalFact(nameof(WebSocketsSupported))]
        public static void KeepAliveInterval_Roundtrips()
        {
            var cws = new ClientWebSocket();
            Assert.True(cws.Options.KeepAliveInterval > TimeSpan.Zero);

            cws.Options.KeepAliveInterval = TimeSpan.Zero;
            Assert.Equal(TimeSpan.Zero, cws.Options.KeepAliveInterval);

            cws.Options.KeepAliveInterval = TimeSpan.MaxValue;
            Assert.Equal(TimeSpan.MaxValue, cws.Options.KeepAliveInterval);

            cws.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
            Assert.Equal(Timeout.InfiniteTimeSpan, cws.Options.KeepAliveInterval);

            AssertExtensions.Throws<ArgumentOutOfRangeException>("value", () => cws.Options.KeepAliveInterval = TimeSpan.MinValue);
        }

        [OuterLoop] // TODO: Issue #11345
        [ActiveIssue(21393, TargetFrameworkMonikers.Uap)]
        [ActiveIssue(5120, TargetFrameworkMonikers.Netcoreapp)]
        [ConditionalFact(nameof(WebSocketsSupported), nameof(CanTestClientCertificates))]
        public async Task ClientCertificates_ValidCertificate_ServerReceivesCertificateAndConnectAsyncSucceeds()
        {
            var options = new LoopbackServer.Options { UseSsl = true, WebSocketEndpoint = true };

            Func<ClientWebSocket, Socket, Uri, X509Certificate2, Task> connectToServerWithClientCert = async (clientSocket, server, url, clientCert) =>
            {
                // Start listening for incoming connections on the server side.
                Task<List<string>> acceptTask = LoopbackServer.AcceptSocketAsync(server, async (socket, stream, reader, writer) =>
                    {
                        // Validate that the client certificate received by the server matches the one configured on
                        // the client-side socket.
                        SslStream sslStream = Assert.IsType<SslStream>(stream);
                        Assert.NotNull(sslStream.RemoteCertificate);
                        Assert.Equal(clientCert, new X509Certificate2(sslStream.RemoteCertificate));

                        // Complete the WebSocket upgrade over the secure channel. After this is done, the client-side
                        // ConnectAsync should complete.
                        Assert.True(await LoopbackServer.WebSocketHandshakeAsync(socket, reader, writer));
                        return null;
                    }, options);

                // Initiate a connection attempt with a client certificate configured on the socket.
                clientSocket.Options.ClientCertificates.Add(clientCert);
                var cts = new CancellationTokenSource(TimeOutMilliseconds);
                await clientSocket.ConnectAsync(url, cts.Token);
                acceptTask.Wait(cts.Token);
            };

            await LoopbackServer.CreateServerAsync(async (server, url) =>
            {
                using (X509Certificate2 clientCert = Test.Common.Configuration.Certificates.GetClientCertificate())
                {
                    using (ClientWebSocket clientSocket = new ClientWebSocket())
                    {
                        await connectToServerWithClientCert(clientSocket, server, url, clientCert);
                    }
                }
            }, options);
        }
    }
}
