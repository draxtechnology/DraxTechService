using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Drax360Service
{
    public class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static async Task Main(string[] args)
        {
            // check if running as a service or console app
            if (!Elements.isService)
            {
                DraxService service = new DraxService();
                service.Run(args);

                //AMXTransfer amxtransfer = new AMXTransfer();
                //await AMXTransfer.Instance.Run(args);

                waitcr();
                service.Stopit();
                service.Dispose();
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                new DraxService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }

        private static void waitcr()
        {
            string msg = "Waiting For CR";
            string line = "".PadRight(msg.Length, '-');
            Console.WriteLine(line);
            Console.WriteLine(msg);
            Console.WriteLine(line);
            Console.ReadLine();
        }
    }
}