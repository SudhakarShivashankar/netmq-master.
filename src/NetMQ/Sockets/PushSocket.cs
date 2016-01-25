﻿using System;
using NetMQ.Core;

namespace NetMQ.Sockets
{
    /// <summary>
    /// A PushSocket is a NetMQSocket intended to be used as the "Push" part of the Push-Pull pattern.
    /// This will "push" messages to be pulled by the "pull" socket.
    /// </summary>
    public class PushSocket : NetMQSocket
    {
        /// <summary>
        /// The type identifier for this Socket Class
        /// </summary>
        public static ZmqSocketType TypeId
        {
            get { return ZmqSocketType.Push; }
        }

        /// <summary>
        /// The Socket Class type identifier for this Socket
        /// </summary>
        public override ZmqSocketType SocketType
        {
            get { return PairSocket.TypeId; }
        }

        /// <summary>
        /// Create a new PushSocket and attach socket to zero or more endpoints.               
        /// </summary>                
        /// <param name="connectionString">List of NetMQ endpoints, seperated by commas and prefixed by '@' (to bind the socket) or '>' (to connect the socket).
        /// Default action is connect (if endpoint doesn't start with '@' or '>')</param>
        /// <example><code>var socket = new PushSocket(">tcp://127.0.0.1:5555,@127.0.0.1:55556");</code></example>               
        public PushSocket(string connectionString = null) : base(TypeId, connectionString, DefaultAction.Connect)
        {
            
        }

        /// <summary>
        /// Create a new PushSocket based upon the given SocketBase.
        /// </summary>
        /// <param name="socketHandle">the SocketBase to create the new socket from</param>
        internal PushSocket(SocketBase socketHandle)
            : base(socketHandle)
        {
        }

        /// <summary><see cref="PushSocket"/> doesn't support sending, so this override throws <see cref="NotSupportedException"/>.</summary>
        /// <exception cref="NotSupportedException">Receive is not supported.</exception>
        [Obsolete("Use Receive(ref Msg) or TryReceive(ref Msg,TimeSpan) instead.")]
        public override void Receive(ref Msg msg, SendReceiveOptions options)
        {
            throw new NotSupportedException("PushSocket doesn't support receiving");
        }

        /// <summary><see cref="PushSocket"/> doesn't support sending, so this override throws <see cref="NotSupportedException"/>.</summary>
        /// <exception cref="NotSupportedException">Receive is not supported.</exception>
        public override bool TryReceive(ref Msg msg, TimeSpan timeout)
        {
            throw new NotSupportedException("PushSocket doesn't support receiving");
        }
    }
}
