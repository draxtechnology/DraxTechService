#region usings
using System;
#endregion

namespace DraxTechnology
{
    class CustomEventArgs : EventArgs
    {
        public object Message { get; }
        public bool NotifyUI { get; internal set; }

        public CustomEventArgs(object message, bool notifyui = true)
        {
            Message = message;
            NotifyUI = notifyui;
        }
    }
}
