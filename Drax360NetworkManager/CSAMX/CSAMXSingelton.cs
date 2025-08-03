using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    public sealed class CSAMXSingleton
    {
        private static CSAMXSingleton instance = null;
         public static CSAMX CS = new CSAMX();
        private CSAMXSingleton()
        {
        }

        public static CSAMXSingleton Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new CSAMXSingleton();
                }
                return instance;
            }
        }
    }
}
