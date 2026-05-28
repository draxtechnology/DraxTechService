using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace DraxTechnology
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
            Console.WriteLine(DateTime.Now + ": " + line);
            Console.WriteLine(DateTime.Now + ": " + msg);
            Console.WriteLine(DateTime.Now + ": " + line);
            Console.ReadLine();
        }
    }
}
