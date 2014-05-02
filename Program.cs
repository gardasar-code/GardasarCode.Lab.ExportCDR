using System.ServiceProcess;

namespace ExportCdr
{
    internal static class Program
    {
        public static CdrService Service;

        private static void Main()
        {
            using (Service = new CdrService())
            {
                ServiceBase.Run(Service);
            }
        }
    }
}