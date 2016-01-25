﻿using NetMQ.Core;

namespace NetMQ.Sockets
{
    /// <summary>
    /// A ResponseSocket is a NetMQSocket intended to be used as the Response part of the Request-Response pattern.
    /// This is generally paired with a RequestSocket.
    /// </summary>
    public class ResponseSocket : NetMQSocket
    {
        /// <summary>
        /// The type identifier for this Socket Class
        /// </summary>
        public static ZmqSocketType TypeId
        {
            get { return ZmqSocketType.Rep; }
        }

        /// <summary>
        /// The Socket Class type identifier for this Socket
        /// </summary>
        public override ZmqSocketType SocketType
        {
            get { return PairSocket.TypeId; }
        }

        /// <summary>
        /// Create a new ResponseSocket and attach socket to zero or more endpoints.               
        /// </summary>                
        /// <param name="connectionString">List of NetMQ endpoints, seperated by commas and prefixed by '@' (to bind the socket) or '>' (to connect the socket).
        /// Default action is bind (if endpoint doesn't start with '@' or '>')</param>
        /// <example><code>var socket = new ResponseSocket(">tcp://127.0.0.1:5555,>127.0.0.1:55556");</code></example>  
        public ResponseSocket(string connectionString = null) : base(TypeId, connectionString, DefaultAction.Bind)
        {
            
        }

        /// <summary>
        /// Create a new ResponseSocket based upon the given SocketBase.
        /// </summary>
        /// <param name="socketHandle">the SocketBase to create the new socket from</param>
        internal ResponseSocket(SocketBase socketHandle)
            : base(socketHandle)
        {
        }
    }
}
