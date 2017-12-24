﻿using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Xunit.Abstractions;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Reactive.Threading.Tasks;
using System.Reactive.Disposables;

namespace RxSockets.Tests
{
    public class Examples
    {
        private IPEndPoint EndPoint = NetworkHelper.GetEndPointOnLoopbackRandomPort();

        private readonly Action<string> Write;
        public Examples(ITestOutputHelper output) => Write = output.WriteLine;

        [Fact]
        public async Task T00_SendAndReceiveStringMessage()
        {
            // Create a socket server on the endpoint.
            var server = RxSocketServer.Create(EndPoint);

            // Start a task to allow the server to accept the next client connection.
            var acceptTask = server.AcceptObservable.FirstAsync().ToTask();

            // Create a socket client by successfully connecting to the server at EndPoint.
            var client = await RxSocket.TryConnectAsync(EndPoint);

            // Get the client socket accepted by the server.
            var accept = await acceptTask;
            Assert.True(accept.Connected && client.Connected);

            // start a task to receive the first string from the server.
            var dataTask = client.ReceiveObservable.ToStrings().FirstAsync().ToTask();

            // The server sends a string to the client.
            accept.Send("Welcome!".ToByteArray());
            Assert.Equal("Welcome!", await dataTask);

            await Task.WhenAll(client.DisconnectAsync(), accept.DisconnectAsync(), server.DisconnectAsync());
        }

        [Fact]
        public async Task T10_ReceiveObservable()
        {
            var server = RxSocketServer.Create(EndPoint);
            var acceptTask = server.AcceptObservable.FirstAsync().ToTask();
            var client = await RxSocket.TryConnectAsync(EndPoint);
            var accept = await acceptTask;
            Assert.True(accept.Connected && client.Connected);

            var subscription = client.ReceiveObservable.ToStrings().Subscribe(str =>
            {
                Write(str);
            });

            accept.Send("Welcome!".ToByteArray());
            "Welcome Again!".ToByteArray().SendTo(accept); // Note SendTo() extension method.

            subscription.Dispose();
            await Task.WhenAll(client.DisconnectAsync(), accept.DisconnectAsync(), server.DisconnectAsync());
        }

        [Fact]
        public async Task T20_AcceptObservable()
        {
            var disposables = new CompositeDisposable();

            var server = RxSocketServer.Create(EndPoint, 10);
            disposables.Add(server);

            server.AcceptObservable.Subscribe(accepted =>
            {
                disposables.Add(accepted);
                "Welcome!".ToByteArray().SendTo(accepted);
            });

            var client1 = await RxSocket.TryConnectAsync(EndPoint);
            var client2 = await RxSocket.TryConnectAsync(EndPoint);
            var client3 = await RxSocket.TryConnectAsync(EndPoint);

            Assert.Equal("Welcome!", await client1.ReceiveObservable.ToStrings().Take(1).FirstAsync());
            Assert.Equal("Welcome!", await client2.ReceiveObservable.ToStrings().Take(1).FirstAsync());
            Assert.Equal("Welcome!", await client3.ReceiveObservable.ToStrings().Take(1).FirstAsync());

            disposables.Add(client1);
            disposables.Add(client2);
            disposables.Add(client3);

            disposables.Dispose();
        }

        [Fact]
        public async Task T30_Both()
        {
            var disposables = new CompositeDisposable();

            var server = RxSocketServer.Create(EndPoint).AddDisposableTo(disposables);

            server.AcceptObservable.Subscribe(accepted =>
            {
                "Welcome!".ToByteArray().SendTo(accepted);

                accepted
                    .AddDisposableTo(disposables)
                    .ReceiveObservable
                    .ToStrings()
                    .Subscribe(s => s.ToByteArray().SendTo(accepted));
            });

            List<IRxSocket> clients = new List<IRxSocket>();
            for (var i = 0; i < 100; i++)
            {
                var client = await RxSocket.TryConnectAsync(EndPoint);
                client.Send("Hello".ToByteArray());
                clients.Add(client);
                disposables.Add(client);
            }

            foreach (var client in clients)
                Assert.Equal("Hello", await client.ReceiveObservable.ToStrings().Skip(1).Take(1).FirstAsync());

            disposables.Dispose();
        }

    }

}

