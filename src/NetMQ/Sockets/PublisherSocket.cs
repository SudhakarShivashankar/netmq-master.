﻿using System;
using NetMQ.Core;

namespace NetMQ.Sockets
{
    /// <summary>
    /// Publisher socket, is the pub in pubsub pattern. publish a message to all subscribers which subscribed for the topic
    /// </summary>
    public class PublisherSocket : NetMQSocket
    {
        internal PublisherSocket(SocketBase socketHandle)
            : base(socketHandle)
        {
        }

        public override void Receive(ref Msg msg, SendReceiveOptions options)
        {
            throw new NotSupportedException("Publisher doesn't support receiving");
        }        
    }
}
