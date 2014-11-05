﻿using System;
using NetMQ.Core;

namespace NetMQ.Sockets
{
    /// <summary>
    /// Part of the push pull pattern, will pull messages from push socket
    /// </summary>
    public class PullSocket : NetMQSocket
    {
        internal PullSocket(SocketBase socketHandle)
            : base(socketHandle)
        {
        }

        public override void Send(ref Msg msg, SendReceiveOptions options)
        {        
            throw new NotSupportedException("Pull socket doesn't support sending");
        }
    }
}
