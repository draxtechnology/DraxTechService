using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    class CustomEventArgs : EventArgs
    {
        public object Message { get; }
        public bool NotifyUI { get; internal set; }

        public CustomEventArgs(object message, bool notifyui=true)
        {
            Message = message;
            NotifyUI = notifyui;
        }
    }
}
