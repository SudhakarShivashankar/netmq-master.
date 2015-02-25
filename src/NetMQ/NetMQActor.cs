﻿using System;
using System.Threading;
using NetMQ.Sockets;
using NetMQ.zmq;
using JetBrains.Annotations;

namespace NetMQ
{
    public interface IShimHandler
    {
        void Run([NotNull] PairSocket shim);
    }

    public class NetMQActorEventArgs : EventArgs
    {
        public NetMQActorEventArgs([NotNull] NetMQActor actor)
        {
            Actor = actor;
        }

        [NotNull]
        public NetMQActor Actor { get; private set; }
    }

    public delegate void ShimAction(PairSocket shim);

    public delegate void ShimAction<T>(PairSocket shim, T state);

    /// <summary>
    /// The Actor represents one end of a two way pipe between 2 PairSocket(s). Where
    /// the actor may be passed messages, that are sent to the other end of the pipe
    /// which called the "shim"
    /// </summary>
    public class NetMQActor : IOutgoingSocket, IReceivingSocket, ISocketPollable, IDisposable
    {
        public const string EndShimMessage = "endPipe";

        #region Action shim handlers

        class ActionShimHandler<T> : IShimHandler
        {
            private readonly ShimAction<T> m_action;
            private readonly T m_state;

            public ActionShimHandler(ShimAction<T> action, T state)
            {
                m_action = action;
                m_state = state;
            }

            public void Run([NotNull] PairSocket shim)
            {
                m_action(shim, m_state);
            }
        }

        class ActionShimHandler : IShimHandler
        {
            private readonly ShimAction m_action;

            public ActionShimHandler(ShimAction action)
            {
                m_action = action;
            }

            public void Run([NotNull] PairSocket shim)
            {
                m_action(shim);
            }
        }

        #endregion

        private readonly PairSocket m_self;
        private readonly PairSocket m_shim;
        
        private readonly Thread m_shimThread;        
        private readonly IShimHandler m_shimHandler;

        private readonly EventDelegatorHelper<NetMQActorEventArgs> m_receiveEventDelegatorHelper;
        private readonly EventDelegatorHelper<NetMQActorEventArgs> m_sendEventDelegatorHelper;

        #region Creating Actor

        private NetMQActor([NotNull] NetMQContext context, [NotNull] IShimHandler shimHandler)
        {
            m_shimHandler = shimHandler;

            m_self = context.CreatePairSocket();
            m_shim = context.CreatePairSocket();

            m_receiveEventDelegatorHelper = new EventDelegatorHelper<NetMQActorEventArgs>(
                () => m_self.ReceiveReady += OnReceive,
                () => m_self.ReceiveReady += OnReceive);

            m_sendEventDelegatorHelper = new EventDelegatorHelper<NetMQActorEventArgs>(
                () => m_self.SendReady += OnReceive,
                () => m_self.SendReady += OnSend);

            Random random = new Random();

            //now binding and connect pipe ends
            string endPoint = string.Empty;
            while (true)
            {
                try
                {
                    endPoint = string.Format("inproc://NetMQActor-{0}-{1}", random.Next(0, 10000), random.Next(0, 10000));
                    m_self.Bind(endPoint);
                    break;
                }
                catch (AddressAlreadyInUseException)
                {
                    // In case address already in use we continue searching for an adderess
                }
            }

            m_shim.Connect(endPoint);

            m_shimThread = new Thread(RunShim);
            m_shimThread.Start();

            //  Mandatory handshake for new actor so that constructor returns only
            //  when actor has also initialized. This eliminates timing issues at
            //  application start up.
            m_self.WaitForSignal();
        }

        public static NetMQActor Create([NotNull] NetMQContext context, [NotNull] IShimHandler shimHandler)
        {
            return new NetMQActor(context, shimHandler);
        }

        public static NetMQActor Create<T>([NotNull] NetMQContext context, [NotNull] ShimAction<T> action, T state)
        {
            return new NetMQActor(context, new ActionShimHandler<T>(action, state));
        }

        public static NetMQActor Create([NotNull] NetMQContext context, [NotNull] ShimAction action)
        {
            return new NetMQActor(context, new ActionShimHandler(action));
        }

        #endregion
  
        void RunShim()
        {
            try
            {
                m_shimHandler.Run(m_shim);
            }
            catch (TerminatingException)
            {

            }        

            //  Do not block, if the other end of the pipe is already deleted
            m_shim.Options.SendTimeout = TimeSpan.Zero;

            try
            {
                m_shim.SignalOK();
            }
            catch (AgainException)
            {
            }

            m_shim.Dispose();
        }

        public void Send(ref Msg msg, SendReceiveOptions options)
        {
            m_self.Send(ref msg, options);
        }

        public void Receive(ref Msg msg, SendReceiveOptions options)
        {
            m_self.Receive(ref msg, options);
        }

        #region Events Handling

        public event EventHandler<NetMQActorEventArgs> ReceiveReady
        {
            add { m_receiveEventDelegatorHelper.Event += value; }
            remove { m_receiveEventDelegatorHelper.Event -= value; }
        }

        public event EventHandler<NetMQActorEventArgs> SendReady
        {
            add { m_sendEventDelegatorHelper.Event += value; }
            remove { m_sendEventDelegatorHelper.Event -= value; }
        }

        [NotNull]
        NetMQSocket ISocketPollable.Socket
        {
            get { return m_self; }
        }

        private void OnReceive(object sender, NetMQSocketEventArgs e)
        {
            m_receiveEventDelegatorHelper.Fire(this, new NetMQActorEventArgs(this));
        }

        private void OnSend(object sender, NetMQSocketEventArgs e)
        {
            m_sendEventDelegatorHelper.Fire(this, new NetMQActorEventArgs(this));
        }

        #endregion

        #region Disposing

        ~NetMQActor()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // release other disposable objects
            if (disposing)
            {
                // send destroy message to pipe
                m_self.Options.SendTimeout = TimeSpan.Zero;
                try
                {
                    m_self.Send(EndShimMessage);
                    m_self.WaitForSignal();
                }
                catch (AgainException)
                {

                }

                m_shimThread.Join();
                m_self.Dispose();
            }
        }

        #endregion
    }
}
