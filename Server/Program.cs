using Common;
using System;
using System.ServiceModel;

namespace Server
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using (ServiceHost host = new ServiceHost(typeof(BatteryService)))
                {
                    host.Open();

                    Console.WriteLine("=== Li-ion Battery EIS Analysis Service ===");
                    Console.WriteLine($"Service is running on: {host.BaseAddresses[0]}");
                    Console.WriteLine("Waiting for client connections...");
                    Console.WriteLine("Ready to receive EIS measurements for voltage and impedance analysis");
                    Console.WriteLine("Press any key to stop the service");
                    Console.WriteLine("==========================================");
                    
                    Console.ReadKey();

                    host.Close();
                }

                Console.WriteLine("Service is closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting Battery service: {ex.Message}");
                Console.WriteLine("Make sure port 4100 is not in use by another application");
                Console.WriteLine("Check app.config for proper WCF configuration");
            }
            
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
