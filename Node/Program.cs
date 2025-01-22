using Node.Startup;
using Shared.Network;

class Program
{
    static void Main(string[] args)
    {
        NodeInitializer node = null;
        try
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int nodeId))
            {
                Console.WriteLine("Необходимо указать корректный номер узла.");
                return;
            }

            int port = NetworkConfiguration.Ports.GetNodePort(nodeId);
            Console.WriteLine($"=== Узел {nodeId} для распределенного решения СЛАУ ===");
            Console.WriteLine($"Узел запущен на порту {port}");

            node = new NodeInitializer(port);
            Task.Run(async () => await node.Start()).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Критическая ошибка: {ex.Message}");
        }
        finally
        {
            node?.Stop();
        }
    }
}