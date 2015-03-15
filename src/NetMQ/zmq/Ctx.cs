/*
    Copyright (c) 2007-2012 iMatix Corporation
    Copyright (c) 2009-2011 250bpm s.r.o.
    Copyright (c) 2007-2011 Other contributors as noted in the AUTHORS file

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
using System.Linq;
using System.Threading;
using JetBrains.Annotations;

namespace NetMQ.zmq
{
    /// <summary>
    /// Objects of class Ctx are intended to encapsulate all of the global state
    /// associated with the NetMQ library.
    /// </summary>
    /// <remarks>Internal analog of the public <see cref="NetMQContext"/> class.</remarks>
    internal sealed class Ctx
    {
        private const int DefaultIOThreads = 1;
        private const int DefaultMaxSockets = 1024;

        #region Nested class: Endpoint

        /// <summary>
        /// Information associated with inproc endpoint. Note that endpoint options
        /// are registered as well so that the peer can access them without a need
        /// for synchronisation, handshaking or similar.
        /// </summary>
        public class Endpoint
        {
            public Endpoint([NotNull] SocketBase socket, [NotNull] Options options)
            {
                Socket = socket;
                Options = options;
            }

            [NotNull]
            public SocketBase Socket { get; private set; }

            [NotNull]
            public Options Options { get; private set; }
        }

        #endregion

        private bool m_disposed;

        /// <summary>
        /// Sockets belonging to this context. We need the list so that
        /// we can notify the sockets when zmq_term() is called. The sockets
        /// will return ETERM then.
        /// </summary>
        private readonly List<SocketBase> m_sockets = new List<SocketBase>();

        /// <summary>
        /// List of unused thread slots.
        /// </summary>
        private readonly Stack<int> m_emptySlots = new Stack<int>();

        /// <summary>
        /// If true, zmq_init has been called but no socket has been created
        /// yet. Launching of I/O threads is delayed.
        /// </summary>
        private volatile bool m_starting = true;

        /// <summary>
        /// If true, zmq_term was already called.
        /// </summary>
        private bool m_terminating;

        /// <summary>
        /// This object is for synchronisation of accesses to global slot-related data:
        /// sockets, empty_slots, terminating. It also synchronises
        /// access to zombie sockets as such (as opposed to slots) and provides
        /// a memory barrier to ensure that all CPU cores see the same data.
        /// </summary>
        private readonly object m_slotSync = new object();

        /// <summary>
        /// The reaper thread.
        /// </summary>
        [CanBeNull] private Reaper m_reaper;

        /// <summary>
        /// List of I/O threads.
        /// </summary>
        private readonly List<IOThread> m_ioThreads = new List<IOThread>();

        /// <summary>
        /// Length of the mailbox-array.
        /// </summary>
        private int m_slotCount;

        /// <summary>
        /// Array of pointers to mailboxes for both application and I/O threads.
        /// </summary>
        [CanBeNull] private IMailbox[] m_slots;

        /// <summary>
        /// Mailbox for zmq_term thread.
        /// </summary>
        private readonly Mailbox m_termMailbox = new Mailbox("terminator");

        /// <summary>
        /// Dictionary containing the inproc endpoints within this context.
        /// </summary>
        private readonly Dictionary<string, Endpoint> m_endpoints = new Dictionary<string, Endpoint>();

        /// <summary>
        /// This object provides synchronisation of access to the list of inproc endpoints.
        /// </summary>
        private readonly object m_endpointsSync = new object();

        /// <summary>
        /// The maximum socket ID.  CBL
        /// </summary>
        private static int s_maxSocketId;

        /// <summary>
        /// The maximum number of sockets that can be opened at the same time.
        /// </summary>
        private int m_maxSockets = DefaultMaxSockets;

        /// <summary>
        /// The number of I/O threads to launch.
        /// </summary>
        private int m_ioThreadCount = DefaultIOThreads;

        /// <summary>
        /// This object is used to synchronize access to context options.
        /// </summary>
        private readonly object m_optSync = new object();

        public const int TermTid = 0;
        public const int ReaperTid = 1;

        /// <summary>
        /// Dump all of this object's resources by stopping and destroying all of it's threads,
        /// destroying the reaper, and closing the mailbox.
        /// </summary>
        private void Destroy()
        {
            foreach (IOThread it in m_ioThreads)
                it.Stop();

            foreach (IOThread it in m_ioThreads)
                it.Destroy();

            if (m_reaper != null)
                m_reaper.Destroy();

            m_termMailbox.Close();

            m_disposed = true;
        }

        /// <summary>Throws <see cref="ObjectDisposedException"/> if this is already disposed.</summary>
        /// <exception cref="ObjectDisposedException">This object has already been disposed.</exception>
        public void CheckDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// This function is called when user invokes zmq_term. If there are
        /// no more sockets open it'll cause all the infrastructure to be shut
        /// down. If there are open sockets still, the deallocation happens
        /// after the last one is closed.
        /// </summary>
        public void Terminate()
        {
            m_disposed = true;

            Monitor.Enter(m_slotSync);

            if (!m_starting)
            {
                //  Check whether termination was already underway, but interrupted and now
                //  restarted.
                bool restarted = m_terminating;
                m_terminating = true;
                Monitor.Exit(m_slotSync);

                //  First attempt to terminate the context.
                if (!restarted)
                {
                    //  First send stop command to sockets so that any blocking calls
                    //  can be interrupted. If there are no sockets we can ask reaper
                    //  thread to stop.
                    Monitor.Enter(m_slotSync);
                    try
                    {
                        foreach (var socket in m_sockets)
                            socket.Stop();

                        if (m_sockets.Count == 0)
                            m_reaper.Stop();
                    }
                    finally
                    {
                        Monitor.Exit(m_slotSync);
                    }
                }

                //  Wait till reaper thread closes all the sockets.
                Command cmd = m_termMailbox.Recv(-1);

                Debug.Assert(cmd != null);
                Debug.Assert(cmd.CommandType == CommandType.Done);
                Monitor.Enter(m_slotSync);
                Debug.Assert(m_sockets.Count == 0);
            }
            Monitor.Exit(m_slotSync);

            //  Deallocate the resources.
            Destroy();
        }

        /// <summary>
        /// Set either the max-sockets or the I/O-thread-count, depending upon which ContextOption is indicated.
        /// </summary>
        /// <param name="option">this determines which of the two properties to set</param>
        /// <param name="optionValue">the value to assign to that property</param>
        /// <exception cref="InvalidException">option must be MaxSockets with optionValue >= 1, or IOThreads with optionValue >= 0.</exception>
        public void Set(ContextOption option, int optionValue)
        {
            if (option == ContextOption.MaxSockets && optionValue >= 1)
            {
                lock (m_optSync)
                {
                    m_maxSockets = optionValue;
                }
            }
            else if (option == ContextOption.IOThreads && optionValue >= 0)
            {
                lock (m_optSync)
                {
                    m_ioThreadCount = optionValue;
                }
            }
            else
            {
                throw new InvalidException(string.Format("In Ctx.Set({0}, {1}), option must be MaxSockets with optionValue >= 1, or IOThreads with optionValue >= 0.", option, optionValue));
            }
        }

        /// <summary>
        /// Return either the max-sockets or the I/O-thread-count, depending upon which ContextOption is indicated.
        /// </summary>
        /// <param name="option">this determines which of the two properties to get</param>
        /// <exception cref="InvalidException">option must be MaxSockets or IOThreads.</exception>
        public int Get(ContextOption option)
        {
            if (option == ContextOption.MaxSockets)
                return m_maxSockets;
            if (option == ContextOption.IOThreads)
                return m_ioThreadCount;
            throw new InvalidException(string.Format("In Ctx.Get({0}), option must be MaxSockets or IOThreads.", option));
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="TerminatingException">Cannot create new socket while terminating.</exception>
        /// <exception cref="NetMQException">Maximum number of sockets reached.</exception>
        [NotNull]
        public SocketBase CreateSocket(ZmqSocketType type)
        {
            lock (m_slotSync)
            {
                if (m_starting)
                {
                    m_starting = false;
                    //  Initialise the array of mailboxes. Additional three slots are for
                    //  zmq_term thread and reaper thread.

                    int ios;
                    int mazmq;

                    lock (m_optSync)
                    {
                        mazmq = m_maxSockets;
                        ios = m_ioThreadCount;
                    }
                    m_slotCount = mazmq + ios + 2;
                    m_slots = new IMailbox[m_slotCount];
                    //alloc_Debug.Assert(slots);

                    //  Initialise the infrastructure for zmq_term thread.
                    m_slots[TermTid] = m_termMailbox;

                    //  Create the reaper thread.
                    m_reaper = new Reaper(this, ReaperTid);
                    //alloc_Debug.Assert(reaper);
                    m_slots[ReaperTid] = m_reaper.Mailbox;
                    m_reaper.Start();

                    //  Create I/O thread objects and launch them.
                    for (int i = 2; i != ios + 2; i++)
                    {
                        var ioThread = new IOThread(this, i);
                        //alloc_Debug.Assert(io_thread);
                        m_ioThreads.Add(ioThread);
                        m_slots[i] = ioThread.Mailbox;
                        ioThread.Start();
                    }

                    //  In the unused part of the slot array, create a list of empty slots.
                    for (int i = m_slotCount - 1; i >= ios + 2; i--)
                    {
                        m_emptySlots.Push(i);
                        m_slots[i] = null;
                    }
                }

                //  Once zmq_term() was called, we can't create new sockets.
                if (m_terminating)
                {
                    string xMsg = string.Format("Ctx.CreateSocket({0}), cannot create new socket while terminating.", type);
                    throw new TerminatingException(innerException: null, message: xMsg);
                }

                //  If max_sockets limit was reached, return error.
                if (m_emptySlots.Count == 0)
                {
#if DEBUG
                    string xMsg = string.Format("Ctx.CreateSocket({0}), max number of sockets {1} reached.", type, m_maxSockets);
                    throw NetMQException.Create(xMsg, ErrorCode.TooManyOpenSockets);
#else
                    throw NetMQException.Create(ErrorCode.TooManyOpenSockets);
#endif
                }

                //  Choose a slot for the socket.
                int slot = m_emptySlots.Pop();

                //  Generate new unique socket ID.
                int socketId = Interlocked.Increment(ref s_maxSocketId);

                //  Create the socket and register its mailbox.
                SocketBase s = SocketBase.Create(type, this, slot, socketId);

                m_sockets.Add(s);
                m_slots[slot] = s.Mailbox;

                //LOG.debug("NEW Slot [" + slot + "] " + s);

                return s;
            }
        }

        public void DestroySocket([NotNull] SocketBase socket)
        {
            //  Free the associated thread slot.
            lock (m_slotSync)
            {
                int threadId = socket.ThreadId;
                m_emptySlots.Push(threadId);
                m_slots[threadId].Close();
                m_slots[threadId] = null;

                //  Remove the socket from the list of sockets.
                m_sockets.Remove(socket);

                //  If zmq_term() was already called and there are no more socket
                //  we can ask reaper thread to terminate.
                if (m_terminating && m_sockets.Count == 0)
                    m_reaper.Stop();
            }

            //LOG.debug("Released Slot [" + socket_ + "] ");
        }

        /// <summary>
        /// Returns reaper thread object.
        /// </summary>
        public ZObject GetReaper()
        {
            return m_reaper;
        }

        /// <summary>
        /// Send a command to the given destination thread.
        /// </summary>
        public void SendCommand(int threadId, [NotNull] Command command)
        {
            m_slots[threadId].Send(command);
        }

        /// <summary>
        /// Returns the <see cref="IOThread"/> that is the least busy at the moment.
        /// </summary>
        /// <paramref name="affinity">Which threads are eligible (0 = all).</paramref>
        /// <returns>The least busy thread, or <c>null</c> if none is available.</returns>
        [CanBeNull]
        public IOThread ChooseIOThread(long affinity)
        {
            if (m_ioThreads.Count == 0)
                return null;

            //  Find the I/O thread with minimum load.
            int minLoad = -1;
            IOThread selectedIOThread = null;

            for (int i = 0; i != m_ioThreads.Count; i++)
            {
                var ioThread = m_ioThreads[i];

                if (affinity == 0 || (affinity & (1L << i)) > 0)
                {
                    if (selectedIOThread == null || ioThread.Load < minLoad)
                    {
                        minLoad = ioThread.Load;
                        selectedIOThread = ioThread;
                    }
                }
            }
            return selectedIOThread;
        }

        /// <summary>
        /// Save the given addr and Endpoint within our internal list.
        /// This is used for management of inproc endpoints.
        /// </summary>
        /// <param name="addr">the textual name to give this endpoint</param>
        /// <param name="endpoint">the Endpoint to remember</param>
        /// <returns>true if the given addr was NOT already registered</returns>
        public bool RegisterEndpoint([NotNull] string addr, [NotNull] Endpoint endpoint)
        {
            lock (m_endpointsSync)
            {
                if (m_endpoints.ContainsKey(addr))
                    return false;

                m_endpoints[addr] = endpoint;
                return true;
            }
        }

        public bool UnregisterEndpoint([NotNull] string addr, [NotNull] SocketBase socket)
        {
            lock (m_endpointsSync)
            {
                Endpoint endpoint;
                
                if (!m_endpoints.TryGetValue(addr, out endpoint))
                    return false;
                
                if (socket != endpoint.Socket)
                    return false;

                m_endpoints.Remove(addr);
                return true;
            }
        }

        public void UnregisterEndpoints([NotNull] SocketBase socket)
        {
            lock (m_endpointsSync)
            {
                IList<string> removeList = m_endpoints.Where(e => e.Value.Socket == socket).Select(e => e.Key).ToList();

                foreach (var item in removeList)
                    m_endpoints.Remove(item);
            }
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="addr"></param>
        /// <returns></returns>
        /// <exception cref="EndpointNotFoundException">The given address was not found in the list of endpoints.</exception>
        [NotNull]
        public Endpoint FindEndpoint([NotNull] string addr)
        {
            Debug.Assert(addr != null);

            lock (m_endpointsSync)
            {
                if (!m_endpoints.ContainsKey(addr))
                    throw new EndpointNotFoundException();

                var endpoint = m_endpoints[addr];

                if (endpoint == null)
                    throw new EndpointNotFoundException();

                //  Increment the command sequence number of the peer so that it won't
                //  get deallocated until "bind" command is issued by the caller.
                //  The subsequent 'bind' has to be called with inc_seqnum parameter
                //  set to false, so that the seqnum isn't incremented twice.
                endpoint.Socket.IncSeqnum();

                return endpoint;
            }
        }
    }
}
