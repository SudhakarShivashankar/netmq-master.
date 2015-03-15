﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Devices;
using NetMQ.Sockets;
using NetMQ.zmq;

namespace MultithreadedService
{
    internal static class Program
    {
        private static CancellationToken s_token;

        private static void Main()
        {
            Console.Title = "NetMQ Multi-threaded Service";

            using (var context = NetMQContext.Create())
            {
                //var queue = new QueueDevice(context, "tcp://localhost:5555", "inproc://workers", DeviceMode.Threaded);
                var queue = new QueueDevice(context, "tcp://localhost:5555", "tcp://localhost:5556", DeviceMode.Threaded);

                var source = new CancellationTokenSource();
                s_token = source.Token;

                for (int threadId = 0; threadId < 10; threadId++)
                    Task.Factory.StartNew(() => WorkerRoutine(new Worker(Guid.NewGuid(), context)), s_token);

                queue.Start();

                var clientThreads = new List<Task>();
                for (int threadId = 0; threadId < 1000; threadId++)
                {
                    int id = threadId;
                    clientThreads.Add(Task.Factory.StartNew(() => ClientRoutine(id)));
                }

                Task.WaitAll(clientThreads.ToArray());

                source.Cancel();

                queue.Stop();
            }

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        private static void ClientRoutine(object clientId)
        {
            try
            {
                using (var context = NetMQContext.Create())
                using (var req = context.CreateRequestSocket())
                {
                    req.Connect("tcp://localhost:5555");

                    byte[] message = Encoding.Unicode.GetBytes(string.Format("{0} Hello", clientId));

                    Console.WriteLine("Client {0} sent \"{0} Hello\"", clientId);
                    req.Send(message, message.Length);

                    var response = req.ReceiveFrameString(Encoding.Unicode);
                    Console.WriteLine("Client {0} received \"{1}\"", clientId, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception on ClientRoutine: {0}", ex.Message);
            }
        }

        private static void WorkerRoutine(object workerContext)
        {
            try
            {
                var thisWorkerContext = (Worker)workerContext;

                using (ResponseSocket rep = thisWorkerContext.Context.CreateResponseSocket())
                {
                    rep.Options.Identity = Encoding.Unicode.GetBytes(Guid.NewGuid().ToString());
                    rep.Connect("tcp://localhost:5556");
                    //rep.Connect("inproc://workers");
                    rep.ReceiveReady += RepOnReceiveReady;
                    while (!s_token.IsCancellationRequested)
                    {
                        rep.Poll(TimeSpan.FromMilliseconds(100));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception on WorkerRoutine: {0}", ex.Message);
                throw;
            }
        }

        private static void RepOnReceiveReady(object sender, NetMQSocketEventArgs args)
        {
            try
            {
                NetMQSocket rep = args.Socket;

                byte[] message = rep.ReceiveFrameBytes();

                //Thread.Sleep(1000); //  Simulate 'work'

                byte[] response =
                    Encoding.Unicode.GetBytes(Encoding.Unicode.GetString(message) + " World from worker " + Encoding.Unicode.GetString(rep.Options.Identity));

                rep.Send(response, response.Length, SendReceiveOptions.DontWait);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception on RepOnReceiveReady: {0}", ex.Message);
                throw;
            }
        }
    }

    public class Worker
    {
        public Guid WorkerId { get; private set; }
        public NetMQContext Context { get; private set; }

        public Worker(Guid workerId, NetMQContext context)
        {
            WorkerId = workerId;
            Context = context;
        }
    }
}