using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace DraxTechnology
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
            serviceInstaller1.StartType = ServiceStartMode.Manual;
        }

        

        private void serviceInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {
            System.ServiceProcess.ServiceController sc = new System.ServiceProcess.ServiceController(serviceInstaller1.ServiceName);
            //sc.Start();
        }
    }
}
