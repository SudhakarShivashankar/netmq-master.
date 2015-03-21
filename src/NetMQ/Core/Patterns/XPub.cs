/*      
    Copyright (c) 2010-2011 250bpm s.r.o.
    Copyright (c) 2011 VMware, Inc.
    Copyright (c) 2010-2011 Other contributors as noted in the AUTHORS file
        
    This file is part of 0MQ.

    0MQ is free software; you can redistribute it and/or modify it under
    the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation; either version 3 of the License, or
    (at your option) any later version.
        
    0MQ is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.
        
    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using JetBrains.Annotations;
using NetMQ.Core.Patterns.Utils;

namespace NetMQ.Core.Patterns
{
    internal class XPub : SocketBase
    {
        public class XPubSession : SessionBase
        {
            public XPubSession([NotNull] IOThread ioThread, bool connect, [NotNull] SocketBase socket, [NotNull] Options options, [NotNull] Address addr)
                : base(ioThread, connect, socket, options, addr)
            {}
        }

        /// <summary>
        /// List of all subscriptions mapped to corresponding pipes.
        /// </summary>
        private readonly MultiTrie m_subscriptions;

        /// <summary>
        /// Distributor of messages holding the list of outbound pipes.
        /// </summary>
        private readonly Distribution m_distribution;

        /// <summary>
        /// If true, send all subscription messages upstream, not just
        /// unique ones. The default is false.
        /// </summary>
        private bool m_verbose;

        /// <summary>
        /// 
        /// The default value is false.
        /// </summary>
        private bool m_manual;

        private Pipe m_lastPipe;

        private Msg m_welcomeMessage;

        /// <summary>
        /// True if we are in the middle of sending a multipart message.
        /// </summary>
        private bool m_more;

        /// <summary>
        /// List of pending (un)subscriptions, ie. those that were already
        /// applied to the trie, but not yet received by the user.
        /// </summary>
        private readonly Queue<byte[]> m_pending;

        private static readonly MultiTrie.MultiTrieDelegate s_markAsMatching;
        private static readonly MultiTrie.MultiTrieDelegate s_sendUnsubscription;

        static XPub()
        {
            s_markAsMatching = (pipe, data, size, arg) =>
            {
                var self = (XPub)arg;
                self.m_distribution.Match(pipe);
            };

            s_sendUnsubscription = (pipe, data, size, arg) =>
            {
                var self = (XPub)arg;

                if (self.m_options.SocketType != ZmqSocketType.Pub)
                {
                    // Place the unsubscription to the queue of pending (un)subscriptions
                    // to be retrieved by the user later on.

                    var unsub = new byte[size + 1];
                    unsub[0] = 0;
                    Buffer.BlockCopy(data, 0, unsub, 1, size);

                    self.m_pending.Enqueue(unsub);
                }
            };
        }

        public XPub([NotNull] Ctx parent, int threadId, int socketId)
            : base(parent, threadId, socketId)
        {
            m_options.SocketType = ZmqSocketType.Xpub;

            m_welcomeMessage = new Msg();
            m_welcomeMessage.InitEmpty();

            m_subscriptions = new MultiTrie();
            m_distribution = new Distribution();
            m_pending = new Queue<byte[]>();
        }

        /// <summary>
        /// Register the pipe with this socket.
        /// </summary>
        /// <param name="pipe">the Pipe to attach</param>
        /// <param name="icanhasall">if true - subscribe to all data on the pipe</param>
        protected override void XAttachPipe(Pipe pipe, bool icanhasall)
        {
            Debug.Assert(pipe != null);
            m_distribution.Attach(pipe);

            // If icanhasall is specified, the caller would like to subscribe
            // to all data on this pipe, implicitly.
            if (icanhasall)
                m_subscriptions.Add(null, 0, 0, pipe);

            // if welcome message was set, write one to the pipe.
            if (m_welcomeMessage.Size > 0)
            {
                var copy = new Msg();
                copy.InitEmpty();
                copy.Copy(ref m_welcomeMessage);

                pipe.Write(ref copy);
                pipe.Flush();
            }

            // The pipe is active when attached. Let's read the subscriptions from
            // it, if any.
            XReadActivated(pipe);
        }

        /// <summary>
        /// Indicate the given pipe as being ready for reading by this socket.
        /// </summary>
        /// <param name="pipe">the <c>Pipe</c> that is now becoming available for reading</param>
        protected override void XReadActivated(Pipe pipe)
        {
            // There are some subscriptions waiting. Let's process them.
            var sub = new Msg();
            while (pipe.Read(ref sub))
            {
                // Apply the subscription to the trie.
                byte[] data = sub.Data;
                int size = sub.Size;
                if (size > 0 && (data[0] == 0 || data[0] == 1))
                {
                    if (m_manual)
                    {
                        m_lastPipe = pipe;

                        m_pending.Enqueue(sub.CloneData());
                    }
                    else
                    {
                        var unique = data[0] == 0 
                            ? m_subscriptions.Remove(data, 1, size - 1, pipe) 
                            : m_subscriptions.Add(data, 1, size - 1, pipe);

                        // If the subscription is not a duplicate, store it so that it can be
                        // passed to used on next recv call.
                        if (m_options.SocketType == ZmqSocketType.Xpub && (unique || m_verbose))
                            m_pending.Enqueue(sub.CloneData());
                    }
                }
                else // process message unrelated to sub/unsub
                {
                    m_pending.Enqueue(sub.CloneData());
                }

                sub.Close();
            }
        }

        /// <summary>
        /// Indicate the given pipe as being ready for writing to by this socket.
        /// This gets called by the WriteActivated method.
        /// </summary>
        /// <param name="pipe">the <c>Pipe</c> that is now becoming available for writing</param>
        protected override void XWriteActivated(Pipe pipe)
        {
            m_distribution.Activated(pipe);
        }

        /// <summary>
        /// Set the specified option on this socket.
        /// </summary>
        /// <param name="option">which option to set</param>
        /// <param name="optionValue">the value to set the option to</param>
        /// <returns><c>true</c> if successful</returns>
        /// <exception cref="InvalidException">optionValue must be a byte-array.</exception>
        protected override bool XSetSocketOption(ZmqSocketOption option, object optionValue)
        {
            switch (option)
            {
                case ZmqSocketOption.XpubVerbose:
                {
                    m_verbose = (bool)optionValue;
                    return true;
                }
                case ZmqSocketOption.XPublisherManual:
                {
                    m_manual = true;
                    return true;
                }
                case ZmqSocketOption.Subscribe:
                {
                    if (m_manual && m_lastPipe != null)
                    {
                        var subscription = optionValue as byte[] ?? Encoding.ASCII.GetBytes((string)optionValue);
                        m_subscriptions.Add(subscription, 0, subscription.Length, m_lastPipe);
                        return true;
                    }
                    break;
                }
                case ZmqSocketOption.Unsubscribe:
                {
                    if (m_manual && m_lastPipe != null)
                    {
                        var subscription = optionValue as byte[] ?? Encoding.ASCII.GetBytes((string)optionValue);
                        m_subscriptions.Remove(subscription, 0, subscription.Length, m_lastPipe);
                        return true;
                    }
                    break;
                }
                case ZmqSocketOption.XPublisherWelcomeMessage:
                {
                    m_welcomeMessage.Close();

                    if (optionValue != null)
                    {
                        var bytes = optionValue as byte[];
                        if (bytes == null)
                            throw new InvalidException(string.Format("In XPub.XSetSocketOption({0},{1}), optionValue must be a byte-array.", option, optionValue));
                        var welcomeBytes = new byte[bytes.Length];
                        bytes.CopyTo(welcomeBytes, 0);
                        m_welcomeMessage.InitGC(welcomeBytes, welcomeBytes.Length);
                    }
                    else
                    {
                        m_welcomeMessage.InitEmpty();
                    }
                    
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// This is an override of the abstract method that gets called to signal that the given pipe is to be removed from this socket.
        /// </summary>
        /// <param name="pipe">the Pipe that is being removed</param>
        protected override void XTerminated(Pipe pipe)
        {
            // Remove the pipe from the trie. If there are topics that nobody
            // is interested in anymore, send corresponding un-subscriptions
            // upstream.

            m_subscriptions.RemoveHelper(pipe, s_sendUnsubscription, this);

            m_distribution.Terminated(pipe);
        }

        /// <summary>
        /// Transmit the given message. The <c>Send</c> method calls this to do the actual sending.
        /// </summary>
        /// <param name="msg">the message to transmit</param>
        /// <returns><c>true</c> if the message was sent successfully</returns>
        protected override bool XSend(ref Msg msg)
        {
            bool msgMore = msg.HasMore;

            // For the first part of multipart message, find the matching pipes.
            if (!m_more)
                m_subscriptions.Match(msg.Data, msg.Size, s_markAsMatching, this);

            // Send the message to all the pipes that were marked as matching
            // in the previous step.
            m_distribution.SendToMatching(ref msg);

            // If we are at the end of multipart message we can mark all the pipes
            // as non-matching.
            if (!msgMore)
                m_distribution.Unmatch();

            m_more = msgMore;

            return true;
        }

        protected override bool XHasOut()
        {
            return m_distribution.HasOut();
        }

        /// <summary>
        /// Receive a message. The <c>Recv</c> method calls this lower-level method to do the actual receiving.
        /// </summary>
        /// <param name="msg">the <c>Msg</c> to receive the message into</param>
        /// <returns><c>true</c> if the message was received successfully, <c>false</c> if there were no messages to receive</returns>
        protected override bool XRecv(ref Msg msg)
        {
            // If there is at least one 
            if (m_pending.Count == 0)
                return false;

            msg.Close();

            byte[] first = m_pending.Dequeue();
            msg.InitPool(first.Length);

            msg.Put(first, 0, first.Length);

            return true;
        }

        protected override bool XHasIn()
        {
            return m_pending.Count != 0;
        }
    }
}