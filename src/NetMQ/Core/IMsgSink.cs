namespace NetMQ.Core
{
    interface IMsgSink
    {
        //  Delivers a message. Returns true if successful; false otherwise.
        //  The function takes ownership of the passed message.
        void PushMsg(ref Msg msg);
    }
}
