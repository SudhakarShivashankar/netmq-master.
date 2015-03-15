﻿using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetMQ.Monitoring;
using NetMQ.zmq;
using NUnit.Framework;

// ReSharper disable AccessToDisposedClosure
// ReSharper disable ExceptionNotDocumented

namespace NetMQ.Tests
{
    // Note: you can have failures here if you execute these on a machine that has only one processor-core.

    [TestFixture]
    public class PollerTests
    {
        private const int TestPollTimeoutMillis = 10;

        [Test]
        public void ResponsePoll()
        {
            using (var context = NetMQContext.Create())
            using (var rep = context.CreateResponseSocket())
            using (var req = context.CreateRequestSocket())
            using (var poller = new Poller(rep) { PollTimeout = TestPollTimeoutMillis })
            {
                int port = rep.BindRandomPort("tcp://127.0.0.1");

                req.Connect("tcp://127.0.0.1:" + port);

                rep.ReceiveReady += (s, e) =>
                {
                    bool more;
                    Assert.AreEqual("Hello", e.Socket.ReceiveFrameString(out more));
                    Assert.False(more);

                    e.Socket.Send("World");
                };

                poller.PollTillCancelledNonBlocking();

                req.Send("Hello");

                bool more2;
                Assert.AreEqual("World", req.ReceiveFrameString(out more2));
                Assert.IsFalse(more2);

                poller.CancelAndJoin();
            }
        }

        [Test]
        public void Monitoring()
        {
            var listeningEvent = new ManualResetEvent(false);
            var acceptedEvent = new ManualResetEvent(false);
            var connectedEvent = new ManualResetEvent(false);

            using (var context = NetMQContext.Create())
            using (var rep = context.CreateResponseSocket())
            using (var req = context.CreateRequestSocket())
            using (var poller = new Poller { PollTimeout = TestPollTimeoutMillis })
            using (var repMonitor = new NetMQMonitor(context, rep, "inproc://rep.inproc", SocketEvent.Accepted | SocketEvent.Listening))
            using (var reqMonitor = new NetMQMonitor(context, req, "inproc://req.inproc", SocketEvent.Connected))
            {
                repMonitor.Accepted += (s, e) => acceptedEvent.Set();
                repMonitor.Listening += (s, e) => listeningEvent.Set();

                repMonitor.AttachToPoller(poller);

                int port = rep.BindRandomPort("tcp://127.0.0.1");

                reqMonitor.Connected += (s, e) => connectedEvent.Set();

                reqMonitor.AttachToPoller(poller);

                poller.PollTillCancelledNonBlocking();

                req.Connect("tcp://127.0.0.1:" + port);
                req.Send("a");

                rep.SkipFrame();

                rep.Send("b");

                req.SkipFrame();

                Assert.IsTrue(listeningEvent.WaitOne(300));
                Assert.IsTrue(connectedEvent.WaitOne(300));
                Assert.IsTrue(acceptedEvent.WaitOne(300));

                poller.CancelAndJoin();
            }
        }

        [Test]
        public void AddSocketDuringWork()
        {
            using (var context = NetMQContext.Create())
            using (var router1 = context.CreateRouterSocket())
            using (var router2 = context.CreateRouterSocket())
            using (var dealer1 = context.CreateDealerSocket())
            using (var dealer2 = context.CreateDealerSocket())
            using (var poller = new Poller(router1) { PollTimeout = TestPollTimeoutMillis })
            {
                int port1 = router1.BindRandomPort("tcp://127.0.0.1");
                int port2 = router2.BindRandomPort("tcp://127.0.0.1");

                dealer1.Connect("tcp://127.0.0.1:" + port1);
                dealer2.Connect("tcp://127.0.0.1:" + port2);

                bool router1Arrived = false;
                bool router2Arrived = false;

                var signal1 = new ManualResetEvent(false);
                var signal2 = new ManualResetEvent(false);

                router1.ReceiveReady += (s, e) =>
                {
                    router1.SkipFrame();
                    router1.SkipFrame();
                    router1Arrived = true;
                    poller.AddSocket(router2);
                    signal1.Set();
                };

                router2.ReceiveReady += (s, e) =>
                {
                    router2.SkipFrame();
                    router2.SkipFrame();
                    router2Arrived = true;
                    signal2.Set();
                };

                poller.PollTillCancelledNonBlocking();

                dealer1.Send("1");
                Assert.IsTrue(signal1.WaitOne(300));
                dealer2.Send("2");
                Assert.IsTrue(signal1.WaitOne(300));

                poller.CancelAndJoin();

                Assert.IsTrue(router1Arrived);
                Assert.IsTrue(router2Arrived);
            }
        }

        [Test]
        public void AddSocketAfterRemoving()
        {
            using (var context = NetMQContext.Create())
            using (var router1 = context.CreateRouterSocket())
            using (var router2 = context.CreateRouterSocket())
            using (var router3 = context.CreateRouterSocket())
            using (var dealer1 = context.CreateDealerSocket())
            using (var dealer2 = context.CreateDealerSocket())
            using (var dealer3 = context.CreateDealerSocket())
            using (var poller = new Poller(router1, router2) { PollTimeout = TestPollTimeoutMillis })
            {
                int port1 = router1.BindRandomPort("tcp://127.0.0.1");
                int port2 = router2.BindRandomPort("tcp://127.0.0.1");
                int port3 = router3.BindRandomPort("tcp://127.0.0.1");

                dealer1.Connect("tcp://127.0.0.1:" + port1);
                dealer2.Connect("tcp://127.0.0.1:" + port2);
                dealer3.Connect("tcp://127.0.0.1:" + port3);

                bool router1Arrived = false;
                bool router2Arrived = false;
                bool router3Arrived = false;

                var signal1 = new ManualResetEvent(false);
                var signal2 = new ManualResetEvent(false);
                var signal3 = new ManualResetEvent(false);

                router1.ReceiveReady += (s, e) =>
                {
                    router1Arrived = true;
                    router1.SkipFrame();
                    router1.SkipFrame();
                    poller.RemoveSocket(router1);
                    signal1.Set();
                };

                router2.ReceiveReady += (s, e) =>
                {
                    router2Arrived = true;
                    router2.SkipFrame();
                    router2.SkipFrame();
                    poller.AddSocket(router3);
                    signal2.Set();
                };

                router3.ReceiveReady += (s, e) =>
                {
                    router3Arrived = true;
                    router3.SkipFrame();
                    router3.SkipFrame();
                    signal3.Set();
                };

                poller.PollTillCancelledNonBlocking();

                dealer1.Send("1");
                Assert.IsTrue(signal1.WaitOne(300));
                dealer2.Send("2");
                Assert.IsTrue(signal2.WaitOne(300));
                dealer3.Send("3");
                Assert.IsTrue(signal3.WaitOne(300));

                poller.CancelAndJoin();

                Assert.IsTrue(router1Arrived);
                Assert.IsTrue(router2Arrived);
                Assert.IsTrue(router3Arrived);
            }
        }

        [Test]
        public void AddTwoSocketAfterRemoving()
        {
            using (var context = NetMQContext.Create())
            using (var router1 = context.CreateRouterSocket())
            using (var router2 = context.CreateRouterSocket())
            using (var router3 = context.CreateRouterSocket())
            using (var router4 = context.CreateRouterSocket())
            using (var dealer1 = context.CreateDealerSocket())
            using (var dealer2 = context.CreateDealerSocket())
            using (var dealer3 = context.CreateDealerSocket())
            using (var dealer4 = context.CreateDealerSocket())
            using (var poller = new Poller(router1, router2) { PollTimeout = TestPollTimeoutMillis })
            {
                int port1 = router1.BindRandomPort("tcp://127.0.0.1");
                int port2 = router2.BindRandomPort("tcp://127.0.0.1");
                int port3 = router3.BindRandomPort("tcp://127.0.0.1");
                int port4 = router4.BindRandomPort("tcp://127.0.0.1");

                dealer1.Connect("tcp://127.0.0.1:" + port1);
                dealer2.Connect("tcp://127.0.0.1:" + port2);
                dealer3.Connect("tcp://127.0.0.1:" + port3);
                dealer4.Connect("tcp://127.0.0.1:" + port4);

                int router1Arrived = 0;
                int router2Arrived = 0;
                bool router3Arrived = false;
                bool router4Arrived = false;

                var signal1 = new ManualResetEvent(false);
                var signal2 = new ManualResetEvent(false);
                var signal3 = new ManualResetEvent(false);
                var signal4 = new ManualResetEvent(false);

                router1.ReceiveReady += (s, e) =>
                {
                    router1Arrived++;
                    router1.SkipFrame(); // identity
                    router1.SkipFrame(); // message
                    poller.RemoveSocket(router1);
                    signal1.Set();
                };

                router2.ReceiveReady += (s, e) =>
                {
                    router2Arrived++;
                    router2.SkipFrame(); // identity
                    router2.SkipFrame(); // message

                    if (router2Arrived == 1)
                    {
                        poller.AddSocket(router3);
                        poller.AddSocket(router4);
                        signal2.Set();
                    }
                };

                router3.ReceiveReady += (s, e) =>
                {
                    router3.SkipFrame(); // identity
                    router3.SkipFrame(); // message
                    router3Arrived = true;
                    signal3.Set();
                };

                router4.ReceiveReady += (s, e) =>
                {
                    router4.SkipFrame(); // identity
                    router4.SkipFrame(); // message
                    router4Arrived = true;
                    signal4.Set();
                };

                poller.PollTillCancelledNonBlocking();

                dealer1.Send("1");
                Assert.IsTrue(signal1.WaitOne(300));
                dealer2.Send("2");
                Assert.IsTrue(signal2.WaitOne(300));
                dealer3.Send("3");
                dealer4.Send("4");
                dealer2.Send("2");
                dealer1.Send("1");
                Assert.IsTrue(signal3.WaitOne(300));
                Assert.IsTrue(signal4.WaitOne(300));

                poller.CancelAndJoin();

                router1.SkipFrame();
                bool more;
                Assert.AreEqual("1", router1.ReceiveFrameString(out more));
                Assert.IsFalse(more);

                Assert.AreEqual(1, router1Arrived);
                Assert.AreEqual(2, router2Arrived);
                Assert.IsTrue(router3Arrived);
                Assert.IsTrue(router4Arrived);
            }
        }


        [Test]
        public void RemoveSocket()
        {
            using (var context = NetMQContext.Create())
            using (var router1 = context.CreateRouterSocket())
            using (var router2 = context.CreateRouterSocket())
            using (var router3 = context.CreateRouterSocket())
            using (var dealer1 = context.CreateDealerSocket())
            using (var dealer2 = context.CreateDealerSocket())
            using (var dealer3 = context.CreateDealerSocket())
            using (var poller = new Poller(router1, router2, router3) { PollTimeout = TestPollTimeoutMillis })
            {
                int port1 = router1.BindRandomPort("tcp://127.0.0.1");
                int port2 = router2.BindRandomPort("tcp://127.0.0.1");
                int port3 = router3.BindRandomPort("tcp://127.0.0.1");

                dealer1.Connect("tcp://127.0.0.1:" + port1);
                dealer2.Connect("tcp://127.0.0.1:" + port2);
                dealer3.Connect("tcp://127.0.0.1:" + port3);

                bool first = true;

                router1.ReceiveReady += (s, e) =>
                {
                    if (!first)
                        Assert.Fail("This should not happen because we cancelled the socket");
                    first = false;

                    // identity
                    e.Socket.SkipFrame();

                    bool more;
                    Assert.AreEqual("Hello", e.Socket.ReceiveFrameString(out more));
                    Assert.False(more);

                    // cancelling the socket
                    poller.RemoveSocket(e.Socket); // remove self
                };

                router2.ReceiveReady += (s, e) =>
                {
                    // identity
                    byte[] identity = e.Socket.ReceiveFrameBytes();

                    // message
                    e.Socket.SkipFrame();

                    e.Socket.SendMore(identity);
                    e.Socket.Send("2");
                };

                router3.ReceiveReady += (s, e) =>
                {
                    // identity
                    byte[] identity = e.Socket.ReceiveFrameBytes();

                    // message
                    e.Socket.SkipFrame();

                    e.Socket.SendMore(identity).Send("3");
                };

                Task pollerTask = Task.Factory.StartNew(poller.PollTillCancelled);

                // Send three messages. Only the first will be processes, as then handler removes
                // the socket from the poller.
                dealer1.Send("Hello");
                dealer1.Send("Hello2");
                dealer1.Send("Hello3");

                // making sure the socket defined before the one cancelled still works
                dealer2.Send("1");
                Assert.AreEqual("2", dealer2.ReceiveFrameString());

                // making sure the socket defined after the one cancelled still works
                dealer3.Send("1");
                Assert.AreEqual("3", dealer3.ReceiveFrameString());

                poller.CancelAndJoin();
                Assert.IsTrue(pollerTask.IsCompleted);
            }
        }

        [Test]
        public void SimpleTimer()
        {
            // TODO it is not really clear what this test is actually testing -- maybe split it into a few smaller tests

            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            using (var dealer = context.CreateDealerSocket())
            using (var poller = new Poller(router) { PollTimeout = TestPollTimeoutMillis })
            {
                int port = router.BindRandomPort("tcp://127.0.0.1");

                dealer.Connect("tcp://127.0.0.1:" + port);

                bool messageArrived = false;

                router.ReceiveReady += (s, e) =>
                {
                    Assert.IsFalse(messageArrived);
                    router.SkipFrame();
                    router.SkipFrame();
                    messageArrived = true;
                };

                bool timerTriggered = false;

                int count = 0;

                const int timerIntervalMillis = 100;

                var timer = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis));
                timer.Elapsed += (a, s) =>
                {
                    // the timer should jump before the message
                    Assert.IsFalse(messageArrived);
                    timerTriggered = true;
                    timer.Enable = false;
                    count++;
                };
                poller.AddTimer(timer);

                poller.PollTillCancelledNonBlocking();

                Thread.Sleep(150);

                dealer.Send("hello");

                Thread.Sleep(300);

                poller.CancelAndJoin();

                Assert.IsTrue(messageArrived);
                Assert.IsTrue(timerTriggered);
                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void RemoveTimer()
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            using (var dealer = context.CreateDealerSocket())
            using (var poller = new Poller(router) { PollTimeout = TestPollTimeoutMillis })
            {
                int port = router.BindRandomPort("tcp://127.0.0.1");

                dealer.Connect("tcp://127.0.0.1:" + port);

                bool timerTriggered = false;

                var timer = new NetMQTimer(TimeSpan.FromMilliseconds(100));
                timer.Elapsed += (a, s) => { timerTriggered = true; };

                // The timer will fire after 100ms
                poller.AddTimer(timer);

                bool messageArrived = false;

                router.ReceiveReady += (s, e) =>
                {
                    router.SkipFrame();
                    router.SkipFrame();
                    messageArrived = true;
                    // Remove timer
                    poller.RemoveTimer(timer);
                };

                poller.PollTillCancelledNonBlocking();

                Thread.Sleep(20);

                dealer.Send("hello");

                Thread.Sleep(300);

                poller.CancelAndJoin();

                Assert.IsTrue(messageArrived);
                Assert.IsFalse(timerTriggered);
            }
        }

        [Test]
        public void RunMultipleTimes()
        {
            int count = 0;

            const int timerIntervalMillis = 20;

            var timer = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis));
            timer.Elapsed += (a, s) =>
            {
                count++;

                if (count == 3)
                {
                    timer.Enable = false;
                }
            };

            using (var poller = new Poller(timer) { PollTimeout = TestPollTimeoutMillis })
            {
                poller.PollTillCancelledNonBlocking();

                Thread.Sleep(timerIntervalMillis * 6);

                poller.CancelAndJoin();

                Assert.AreEqual(3, count);
            }
        }

        [Test]
        public void PollOnce()
        {
            int count = 0;

            var timer = new NetMQTimer(TimeSpan.FromMilliseconds(50));
            timer.Elapsed += (a, s) =>
            {
                count++;

                if (count == 3)
                {
                    timer.Enable = false;
                }
            };

            // NOTE if the PollTimeout here is less than the timer period, it won't fire during PollOnce -- is this by design?

            using (var poller = new Poller(timer) { PollTimeout = 1000 })
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                poller.PollOnce();

                var pollOnceElapsedTime = stopwatch.ElapsedMilliseconds;

                Assert.AreEqual(1, count, "the timer should have fired just once during the call to PollOnce()");
                Assert.Less(pollOnceElapsedTime, 90, "pollonce should return soon after the first timer firing.");
            }
        }

        [Test]
        public void TwoTimers()
        {
            var timer1 = new NetMQTimer(TimeSpan.FromMilliseconds(52));
            var timer2 = new NetMQTimer(TimeSpan.FromMilliseconds(40));

            int count = 0;
            int count2 = 0;

            var signal1 = new ManualResetEvent(false);
            var signal2 = new ManualResetEvent(false);

            timer1.Elapsed += (a, s) =>
            {
                count++;
                timer1.Enable = false;
                timer2.Enable = false;
                signal1.Set();
            };

            timer2.Elapsed += (s, e) =>
            {
                count2++;
                signal2.Set();
            };

            using (var poller = new Poller(timer1, timer2) { PollTimeout = TestPollTimeoutMillis })
            {
                poller.PollTillCancelledNonBlocking();

                Assert.IsTrue(signal1.WaitOne(300));
                Assert.IsTrue(signal2.WaitOne(300));

                poller.CancelAndJoin();
            }

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, count2);
        }

        [Test]
        public void EnableTimer()
        {
            const int timerIntervalMillis = 20;

            var timer1 = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis));
            var timer2 = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis)) { Enable = false};

            int count = 0;
            int count2 = 0;

            timer1.Elapsed += (a, s) =>
            {
                count++;

                if (count == 1)
                {
                    timer2.Enable = true;
                    timer1.Enable = false;
                }
                else if (count == 2)
                {
                    timer1.Enable = false;
                }
            };

            timer2.Elapsed += (s, e) =>
            {
                timer1.Enable = true;
                timer2.Enable = false;

                count2++;
            };

            using (var poller = new Poller(timer1, timer2) { PollTimeout = TestPollTimeoutMillis })
            {
                poller.PollTillCancelledNonBlocking();

                Thread.Sleep(timerIntervalMillis * 6);

                poller.CancelAndJoin();
            }

            Assert.AreEqual(2, count);
            Assert.AreEqual(1, count2);
        }

        [Test]
        public void ChangeTimerInterval()
        {
            int count = 0;

            const int timerIntervalMillis = 10;

            var timer = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis));

            var stopwatch = new Stopwatch();

            long length1 = 0;
            long length2 = 0;

            timer.Elapsed += (a, s) =>
            {
                count++;

                if (count == 1)
                {
                    stopwatch.Start();
                }
                else if (count == 2)
                {
                    length1 = stopwatch.ElapsedMilliseconds;

                    timer.Interval = 20;
                    stopwatch.Restart();
                }
                else if (count == 3)
                {
                    length2 = stopwatch.ElapsedMilliseconds;

                    stopwatch.Stop();

                    timer.Enable = false;
                }
            };

            using (var poller = new Poller(timer) { PollTimeout = TestPollTimeoutMillis })
            {
                poller.PollTillCancelledNonBlocking();

                Thread.Sleep(timerIntervalMillis * 6);

                poller.CancelAndJoin();
            }

            Assert.AreEqual(3, count);

            Assert.AreEqual(10.0, length1, 2.0);
            Assert.AreEqual(20.0, length2, 2.0);
        }

        [Test]
        public void TestPollerDispose()
        {
            const int timerIntervalMillis = 10;

            var timer = new NetMQTimer(TimeSpan.FromMilliseconds(timerIntervalMillis));

            var signal = new ManualResetEvent(false);

            var count = 0;

            timer.Elapsed += (a, s) =>
            {
                if (count++ == 5)
                    signal.Set();
            };

            Poller poller;
            using (poller = new Poller(timer) { PollTimeout = TestPollTimeoutMillis })
            {
                poller.PollTillCancelledNonBlocking();
                Assert.IsTrue(signal.WaitOne(500));
                Assert.IsTrue(poller.IsStarted);
                Assert.Throws<InvalidOperationException>(() => poller.PollTillCancelled());
            }

            Assert.IsFalse(poller.IsStarted);
            Assert.Throws<ObjectDisposedException>(() => poller.PollTillCancelled());
            Assert.Throws<ObjectDisposedException>(() => poller.CancelAndJoin());
            Assert.Throws<ObjectDisposedException>(() => poller.AddTimer(timer));
            Assert.Throws<ObjectDisposedException>(() => poller.RemoveTimer(timer));
        }

        [Test]
        public void NativeSocket()
        {
            using (var context = NetMQContext.Create())
            using (var streamServer = context.CreateStreamSocket())
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                int port = streamServer.BindRandomPort("tcp://*");

                socket.Connect("127.0.0.1", port);

                var buffer = new byte[] { 1 };
                socket.Send(buffer);

                byte[] identity = streamServer.ReceiveFrameBytes();
                byte[] message = streamServer.ReceiveFrameBytes();

                Assert.AreEqual(buffer[0], message[0]);

                var socketSignal = new ManualResetEvent(false);

                var poller = new Poller { PollTimeout = TestPollTimeoutMillis };
                poller.AddPollInSocket(socket, s =>
                {
                    socket.Receive(buffer);

                    socketSignal.Set();

                    // removing the socket
                    poller.RemovePollInSocket(socket);
                });

                poller.PollTillCancelledNonBlocking();

                // no message is waiting for the socket so it should fail
                Assert.IsFalse(socketSignal.WaitOne(100));

                // sending a message back to the socket
                streamServer.SendMore(identity).Send("a");

                Assert.IsTrue(socketSignal.WaitOne(100));

                socketSignal.Reset();

                // sending a message back to the socket
                streamServer.SendMore(identity).Send("a");

                // we remove the native socket so it should fail
                Assert.IsFalse(socketSignal.WaitOne(100));

                poller.CancelAndJoin();
            }
        }
    }
}
