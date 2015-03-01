﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using NetMQ.zmq;

namespace NetMQ
{
    /// <summary>
    /// This static class serves to provide extension methods for IReceivingSocket.
    /// </summary>
    public static class ReceivingSocketExtensions
    {
        #region receiving byte-array data

        /// <summary>
        /// get the data section of the available message as <c>byte[]</c>
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <param name="hasMore"><c>true</c> when more parts of a multi-part message are available</param>
        /// <returns>a newly allocated array of bytes</returns>
        [NotNull]
        public static byte[] Receive(this IReceivingSocket socket, SendReceiveOptions options, out bool hasMore)
        {
            var msg = new Msg();
            msg.InitEmpty();

            socket.Receive(ref msg, options);

            var data = new byte[msg.Size];

            if (msg.Size > 0)
            {
                Buffer.BlockCopy(msg.Data, 0, data, 0, msg.Size);
            }

            hasMore = msg.HasMore;

            msg.Close();

            return data;
        }

        /// <summary>
        /// get the data section of the available message as <c>byte[]</c>
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <returns>a newly allocated array of bytes</returns>
        [NotNull]
        public static byte[] Receive(this IReceivingSocket socket, SendReceiveOptions options)
        {
            bool hasMore;
            return socket.Receive(options, out hasMore);
        }

        /// <summary>
        /// potentially non-blocking get the data section of the available message as <c>byte[]</c>
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="dontWait">non-blocking if <c>true</c> and blocking otherwise</param>
        /// <param name="hasMore"><c>true</c> when more parts of a multi-part message are available</param>
        /// <returns>a newly allocated array of bytes</returns>
        [NotNull]
        public static byte[] Receive(this IReceivingSocket socket, bool dontWait, out bool hasMore)
        {
            var options = SendReceiveOptions.None;

            if (dontWait)
            {
                options |= SendReceiveOptions.DontWait;
            }

            return socket.Receive(options, out hasMore);
        }

        /// <summary>
        /// get the data section of the available message as <c>byte[]</c>
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="hasMore"><c>true</c> when more parts of a multi-part message are available</param>
        /// <returns>a newly allocated array of bytes</returns>
        [NotNull]
        public static byte[] Receive(this IReceivingSocket socket, out bool hasMore)
        {
            return socket.Receive(false, out hasMore);
        }

        /// <summary>
        /// get the data section of the available message as <c>byte[]</c> within a timespan
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="timeout">a timespan to wait for arriving messages</param>
        /// <returns>a newly allocated array of bytes or <c>null</c> if no message arrived within the timeout period</returns>
        /// <exception cref="InvalidCastException">if the socket not a NetMQSocket</exception>
        [CanBeNull]
        public static byte[] Receive(this IReceivingSocket socket, TimeSpan timeout)
        {
            var s = socket as NetMQSocket;

            if (s == null)
                throw new InvalidCastException(string.Format("Expected a NetMQSocket but got a {0}", socket.GetType()));

            var result = s.Poll(PollEvents.PollIn, timeout);

            if (!result.HasFlag(PollEvents.PollIn))
                return null;

            return socket.Receive();
        }

        /// <summary>
        /// get the data section of the available message as <c>byte[]</c>
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <returns>a newly allocated array of bytes</returns>
        [NotNull]
        public static byte[] Receive(this IReceivingSocket socket)
        {
            bool hasMore;
            return socket.Receive(false, out hasMore);
        }

        /// <summary>
        ///     get all data parts of a multi-part message
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <returns>iterable sequence of byte arrays</returns>
        [NotNull]
        [ItemNotNull]
        public static IEnumerable<byte[]> ReceiveMessages(this IReceivingSocket socket)
        {
            bool hasMore = true;

            while (hasMore)
                yield return socket.Receive(false, out hasMore);
        }

        #endregion receiving byte-array data

        #region receiving a String

        /// <summary>
        /// Read an available message, extract the data which is converted to a string
        /// using the specified Encoding, and inform of the availability of more messages via the hasMore parameter.
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the Encoding to use</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the string representation of the data of the message - as interpreted using the given encoding</returns>
        /// <exception cref="ArgumentNullException">is thrown if encoding is null</exception>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, [NotNull] Encoding encoding, SendReceiveOptions options, out bool hasMore)
        {
            if (ReferenceEquals(encoding, null))
                throw new ArgumentNullException("encoding");

            var msg = new Msg();
            msg.InitEmpty();

            socket.Receive(ref msg, options);

            hasMore = msg.HasMore;

            string data = string.Empty;

            if (msg.Size > 0)
            {
                data = encoding.GetString(msg.Data, 0, msg.Size);
            }

            msg.Close();

            return data;
        }

        /// <summary>
        /// Read an available message, extract the data which is converted to a string
        /// using the default Encoding.ASCII, and inform of the availability of more messages via the hasMore parameter.
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the string representation of the data of the message - as interpreted using the ASCII-encoding.</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, SendReceiveOptions options, out bool hasMore)
        {
            return socket.ReceiveString(Encoding.ASCII, options, out hasMore);
        }

        /// <summary>
        /// Read an available message, and extract the data which is converted to a string
        /// using ASCII as the Encoding.
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <returns>the ASCII string representation of the data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, SendReceiveOptions options)
        {
            bool hasMore;
            return socket.ReceiveString(options, out hasMore);
        }

        /// <summary>
        /// reads an available message and extracts the data which is converted to a string
        /// using ASCII as encoding
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the encoding to use</param>
        /// <param name="options">a SendReceiveOptions value that can specify the DontWait or SendMore bits</param>
        /// <returns>the string representation of the encoded data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, [NotNull] Encoding encoding, SendReceiveOptions options)
        {
            bool hasMore;
            return socket.ReceiveString(encoding, options, out hasMore);
        }

        /// <summary>
        /// non-blocking reads an available message and extracts the data which is converted to a string
        /// using ASCII as encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="dontWait">if true the method is non-blocking</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the ASCII string representation of the data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, bool dontWait, out bool hasMore)
        {
            return ReceiveString(socket, Encoding.ASCII, dontWait, out hasMore);
        }

        /// <summary>
        /// non-blocking reads an available message and extracts the data which is converted to a string
        /// using ASCII as encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the encoding to use for the string representation</param>
        /// <param name="dontWait">if true the method is non-blocking</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the ASCII string representation of the data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, [NotNull] Encoding encoding, bool dontWait, out bool hasMore)
        {
            var options = SendReceiveOptions.None;

            if (dontWait)
            {
                options |= SendReceiveOptions.DontWait;
            }

            return socket.ReceiveString(encoding, options, out hasMore);
        }

        /// <summary>
        /// reads an available message and extracts the data which is converted to a string
        /// using ASCII as encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the ASCII string representation of the data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, out bool hasMore)
        {
            return socket.ReceiveString(false, out hasMore);
        }

        /// <summary>
        /// reads an available message and extracts the data which is converted to a string
        /// using the encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the encoding to use for the string representation</param>
        /// <param name="hasMore">true if more messages are available, false otherwise</param>
        /// <returns>the string representation of the encoded data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, [NotNull] Encoding encoding, out bool hasMore)
        {
            return socket.ReceiveString(encoding, false, out hasMore);
        }

        /// <summary>
        /// reads an available message and extracts the data which is converted to a string
        /// using encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the encoding to use for the string representation</param>
        /// <returns>the string representation of the encoded data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket, [NotNull] Encoding encoding)
        {
            bool hasMore;
            return socket.ReceiveString(encoding, false, out hasMore);
        }

        /// <summary>
        /// reads an available message and extracts the data which is converted to a string
        /// using encoding and informs about the availability of more messages
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <returns>the ASCII string representation of the data of the message</returns>
        [NotNull]
        public static string ReceiveString(this IReceivingSocket socket)
        {
            bool hasMore;
            return socket.ReceiveString(false, out hasMore);
        }

        /// <summary>
        /// waits for a message to be read for a specified timespan
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="timeout">the time span to wait for a message to receive</param>
        /// <returns>
        /// the ASCII string representation of the data of the message or <c>null</c> if 
        /// no message arrived with in the timespan
        /// </returns>
        [CanBeNull]
        public static string ReceiveString(this NetMQSocket socket, TimeSpan timeout)
        {
            return ReceiveString(socket, Encoding.ASCII, timeout);
        }

        /// <summary>
        /// waits for a message to be read for a specified timespan
        /// </summary>
        /// <param name="socket">the socket to read from</param>
        /// <param name="encoding">the encoding to use for the string representation</param>
        /// <param name="timeout">the time span to wait for a message to receive</param>
        /// <returns>
        /// the string representation of the encoded data of the message or <c>null</c> if 
        /// no message arrived with in the timespan
        /// </returns>
        [CanBeNull]
        public static string ReceiveString(this NetMQSocket socket, [NotNull] Encoding encoding, TimeSpan timeout)
        {
            var result = socket.Poll(PollEvents.PollIn, timeout);

            if (!result.HasFlag(PollEvents.PollIn))
                return null;

            var msg = socket.ReceiveString(encoding);
            return msg;
        }

        /// <summary>
        /// returns all message parts of a multi-part message as strings
        /// </summary>
        /// <param name="socket">the socket to receive from</param>
        /// <returns>the iterable ASCII strings of all data parts of the multi-part message</returns>
        [NotNull]
        [ItemNotNull]
        public static IEnumerable<string> ReceiveStringMessages(this IReceivingSocket socket)
        {
            return ReceiveStringMessages(socket, Encoding.ASCII);
        }

        /// <summary>
        /// returns all message parts of a multi-part message as strings
        /// </summary>
        /// <param name="socket">the socket to receive from</param>
        /// <param name="encoding">the encoding to use for the string representation</param>
        /// <returns>the iterable strings of all encoded data parts of the multi-part message</returns>
        [NotNull]
        [ItemNotNull]
        public static IEnumerable<string> ReceiveStringMessages(this IReceivingSocket socket, [NotNull] Encoding encoding)
        {
            bool hasMore = true;

            while (hasMore)
                yield return socket.ReceiveString(encoding, SendReceiveOptions.None, out hasMore);
        }

        #endregion receiving a String

        #region receiving a NetMQMessge

        /// <summary>
        /// non-blocking receive of a (multi-part)message and stores it in the NetMQMessage object
        /// </summary>
        /// <param name="socket">the IReceivingSocket to receive bytes from</param>
        /// <param name="message">the NetMQMessage to receive the bytes into</param>
        /// <param name="dontWait">non-blocking if <c>true</c> and blocking otherwise</param>
        public static void ReceiveMessage(this IReceivingSocket socket, [NotNull] NetMQMessage message, bool dontWait = false)
        {
            message.Clear();

            bool more = true;

            while (more)
            {
                byte[] buffer = socket.Receive(dontWait, out more);
                message.Append(buffer);
            }
        }

        /// <summary>
        /// non-blocking receive of a (multi-part)message
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="dontWait">non-blocking if <c>true</c> and blocking otherwise</param>
        /// <returns>the received message</returns>
        [NotNull]
        public static NetMQMessage ReceiveMessage(this IReceivingSocket socket, bool dontWait = false)
        {
            var message = new NetMQMessage();
            socket.ReceiveMessage(message, dontWait);
            return message;
        }

        /// <summary>
        /// receive of a (multi-part)message within a specified timespan
        /// </summary>
        /// <param name="socket">the socket to use</param>
        /// <param name="timeout">the timespan to wait for a message</param>
        /// <returns>the received message or <c>null</c> if non arrived within the timeout period</returns>
        [CanBeNull]
        public static NetMQMessage ReceiveMessage(this NetMQSocket socket, TimeSpan timeout)
        {
            var result = socket.Poll(PollEvents.PollIn, timeout);

            if (!result.HasFlag(PollEvents.PollIn))
                return null;

            var msg = socket.ReceiveMessage();
            return msg;
        }

        #endregion receiving a NetMQMessge

        #region WaitForSignal
        /// <summary>
        /// Extension-method for IReceivingSocket: repeatedly call ReceiveMessage on this socket, until we receive
        /// a message with one 8-byte frame, which matches a specific pattern.
        /// </summary>
        /// <param name="socket">this socket to receive the messages from</param>
        /// <returns>true if that one frame has no bits set other than in the lowest-order byte</returns>
        public static bool WaitForSignal(this IReceivingSocket socket)
        {
            while (true)
            {
                var message = socket.ReceiveMessage();

                if (message.FrameCount == 1 && message.First.MessageSize == 8)
                {
                    long signalValue = message.First.ConvertToInt64();

                    if ((signalValue & 0x7FFFFFFFFFFFFF00L) == 0x7766554433221100L)
                    {
                        return (signalValue & 255) == 0;
                    }
                }
            }
        }
        #endregion

        #region obsolete methods

        [Obsolete("Use ReceiveMessages extension method instead")]
        [NotNull]
        [ItemNotNull]
        public static IList<byte[]> ReceiveAll(this IReceivingSocket socket)
        {
            return socket.ReceiveMessages().ToList();
        }

        [Obsolete("Use ReceiveStringMessages extension method instead")]
        [NotNull]
        [ItemNotNull]
        public static IList<string> ReceiveAllString(this IReceivingSocket socket)
        {
            return socket.ReceiveStringMessages().ToList();
        }

        #endregion obsolete methods
    }
}
