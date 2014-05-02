using System.ComponentModel;
using System.ServiceProcess;

namespace ExportCdr
{
    [RunInstaller(true)]
    public class Installer : System.Configuration.Install.Installer
    {
        public Installer()
        {
            var process = new ServiceProcessInstaller();
            var service = new ServiceInstaller();

            process.Account = ServiceAccount.LocalSystem;
            service.StartType = ServiceStartMode.Automatic;
            service.ServiceName = "ExportCdr";
            service.Description = "Pre-billing";

            Installers.Add(service);
            Installers.Add(process);
        }
    }
}