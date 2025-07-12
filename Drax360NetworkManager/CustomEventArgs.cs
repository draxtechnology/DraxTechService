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

        public CustomEventArgs(object message)
        {
            Message = message;
        }
    }
}
