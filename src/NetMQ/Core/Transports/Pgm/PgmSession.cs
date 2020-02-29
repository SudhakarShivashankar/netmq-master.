﻿using System.Diagnostics;
using System.Net.Sockets;
using AsyncIO;
using JetBrains.Annotations;

namespace NetMQ.Core.Transports.Pgm
{
    internal sealed class PgmSession : IEngine, IProactorEvents
    {
        private AsyncSocket m_handle;
        private readonly Options m_options;
        private IOObject m_ioObject;
        private SessionBase m_session;
        private V1Decoder m_decoder;
        private bool m_joined;

        private int m_pendingBytes;
        private ByteArraySegment m_pendingData;

        private readonly ByteArraySegment m_data;

        /// <summary>
        /// This enum-type is Idle, Receiving, Stuck, or Error.
        /// </summary>
        private enum State
        {
            Idle,
            Receiving,
            Stuck,
            Error
        }

        private State m_state;

        public PgmSession([NotNull] PgmSocket pgmSocket, [NotNull] Options options)
        {
            m_handle = pgmSocket.Handle;
            m_options = options;
            m_data = new byte[Config.PgmMaxTPDU];
            m_joined = false;

            m_state = State.Idle;
        }

        void IEngine.Plug(IOThread ioThread, SessionBase session)
        {
            m_session = session;
            m_ioObject = new IOObject(null);
            m_ioObject.SetHandler(this);
            m_ioObject.Plug(ioThread);
            m_ioObject.AddSocket(m_handle);

            DropSubscriptions();

            var msg = new Msg();
            msg.InitEmpty();

            // push message to the session because there is no identity message with pgm
            session.PushMsg(ref msg);

            m_state = State.Receiving;
            BeginReceive();
        }

        public void Terminate()
        {}

        public void BeginReceive()
        {
            m_data.Reset();
            try 
            { 
                m_handle.Receive((byte[])m_data); 
            } 
            catch (SocketException ex) 
            { 
                // For a UDP datagram socket, this error would indicate that a previous  
                // send operation resulted in an ICMP "Port Unreachable" message. 
                if (ex.SocketErrorCode == SocketError.ConnectionReset) 
                    Error(); 
                else 
                    throw NetMQException.Create(ex.SocketErrorCode, ex); 
            } 
        }

        public void ActivateIn()
        {
            if (m_state == State.Stuck)
            {
                var pushResult = m_decoder.PushMsg(m_session.PushMsg);
                if (pushResult == PushMsgResult.Ok)
                {
                    m_state = State.Receiving;
                    ProcessInput();
                }
            }
        }

        /// <summary>
        /// This method is be called when a message receive operation has been completed.
        /// </summary>
        /// <param name="socketError">a SocketError value that indicates whether Success or an error occurred</param>
        /// <param name="bytesTransferred">the number of bytes that were transferred</param>
        public void InCompleted(SocketError socketError, int bytesTransferred)
        {
            if (socketError != SocketError.Success || bytesTransferred == 0)
            {
                m_joined = false;
                Error();
            }
            else
            {
                // Read the offset of the fist message in the current packet.
                Debug.Assert(bytesTransferred >= sizeof(ushort));

                ushort offset = m_data.GetUnsignedShort(m_options.Endian, 0);
                m_data.AdvanceOffset(sizeof(ushort));
                bytesTransferred -= sizeof(ushort);

                // Join the stream if needed.
                if (!m_joined)
                {
                    // There is no beginning of the message in current packet.
                    // Ignore the data.
                    if (offset == 0xffff)
                    {
                        BeginReceive();
                        return;
                    }

                    Debug.Assert(offset <= bytesTransferred);
                    Debug.Assert(m_decoder == null);

                    // We have to move data to the beginning of the first message.
                    m_data.AdvanceOffset(offset);
                    bytesTransferred -= offset;

                    // Mark the stream as joined.
                    m_joined = true;

                    // Create and connect decoder for the peer.
                    m_decoder = new V1Decoder(0, m_options.MaxMessageSize, m_options.Endian);
                }

                // Push all the data to the decoder.
                m_pendingBytes = bytesTransferred;
                m_pendingData = new ByteArraySegment(m_data, 0);
                ProcessInput();
            }
        }
        
        void ProcessInput ()
        {
            while (m_pendingBytes > 0) {
                var result = m_decoder.Decode(m_pendingData, m_pendingBytes, out var processed);
                m_pendingData.AdvanceOffset(processed);
                m_pendingBytes -= processed;
                if (result == DecodeResult.Error)
                {
                    m_joined = false;
                    Error();
                    return;
                }
                
                if (result == DecodeResult.Processing)
                    break;
                
                var pushResult = m_decoder.PushMsg(m_session.PushMsg);
                if (pushResult == PushMsgResult.Full)
                {
                    m_state = State.Stuck;
                    m_session.Flush();
                    return;
                }
                else if (pushResult == PushMsgResult.Error)
                {
                    m_joined = false;
                    Error();
                    return;
                }
            }
            
            m_session.Flush();
            
            BeginReceive();
        }

        private void Error()
        {
            Debug.Assert(m_session != null);

            m_session.Detach();

            m_ioObject.RemoveSocket(m_handle);

            // Disconnect from I/O threads poller object.
            m_ioObject.Unplug();

            m_session = null;

            m_state = State.Error;

            Destroy();
        }

        public void Destroy()
        {
            if (m_handle != null)
            {
                try
                {
                    m_handle.Dispose();
                }
                catch (SocketException)
                {}
                m_handle = null;
            }
        }

        /// <summary>
        /// This method would be called when a message Send operation has been completed, except in this case this method does nothing.
        /// </summary>
        /// <param name="socketError">a SocketError value that indicates whether Success or an error occurred</param>
        /// <param name="bytesTransferred">the number of bytes that were transferred</param>
        public void OutCompleted(SocketError socketError, int bytesTransferred)
        {}

        /// <summary>
        /// This would be called when a timer expires, although here it does nothing.
        /// </summary>
        /// <param name="id">an integer used to identify the timer (not used here)</param>
        public void TimerEvent(int id)
        {}

        private void DropSubscriptions()
        {
            var msg = new Msg();
            msg.InitEmpty();

            while (m_session.PullMsg(ref msg) == PullMsgResult.Ok)
            {
                msg.Close();
            }
        }

        public void ActivateOut()
        {
            DropSubscriptions();
        }
    }
}
