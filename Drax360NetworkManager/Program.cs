using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
               
                DraxService service = new DraxService();
                service.Run(args);
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