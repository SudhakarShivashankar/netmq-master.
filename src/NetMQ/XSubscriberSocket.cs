﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetMQ.zmq;

namespace NetMQ
{
    public class XSubscriberSocket : SubscriberSocket
    {
        public XSubscriberSocket(SocketBase socketHandle)
            : base(socketHandle)
        {
        }
    }
}
