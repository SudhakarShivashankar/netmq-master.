using System;
using System.Collections.Generic;

namespace NetMQ.Tests
{
    public class MockBufferPool : IBufferPool
    {
        public int TakeCallCount { get; private set; }
        public List<int> TakeSize { get; private set; }
        
        public int ReturnCallCount { get; private set; }
        public List<byte[]> ReturnBuffer { get; private set; }

        public MockBufferPool()
        {
            TakeSize = new List<int>();
            ReturnBuffer = new List<byte[]>();
        }

        public void Reset()
        {
            TakeCallCount = 0;
            TakeSize.Clear();

            ReturnCallCount = 0;
            ReturnBuffer.Clear();
        }

        public byte[] Take(int size)
        {
            TakeCallCount++;
            TakeSize.Add(size);

            return new byte[size];
        }

        public void Return(byte[] buffer)
        {
            ReturnCallCount++;
            ReturnBuffer.Add(buffer);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}