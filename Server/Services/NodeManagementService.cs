using System.Diagnostics;

public class NodeManagementService
{
    private readonly List<Process> nodeProcesses;

    public NodeManagementService()
    {
        nodeProcesses = [];
    }

    public async Task StartNodes(int nodesCount)
    {
        try
        {
            for (int i = 0; i < nodesCount; i++)
            {
                await StartNode(i);
                await Task.Delay(500); // Добавляем задержку между запусками узлов
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при запуске узлов: {ex.Message}");
            throw;
        }
    }

    private async Task StartNode(int nodeIndex)
    {
        try
        {
            string nodePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Node.dll");
            var nodeProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"{nodePath} {nodeIndex}",
                    UseShellExecute = true,
                    CreateNoWindow = false
                }
            };

            nodeProcess.Start();
            nodeProcesses.Add(nodeProcess);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при запуске узла {nodeIndex}: {ex.Message}");
            throw;
        }
    }

    public void CleanupNodes()
    {
        foreach (var process in nodeProcesses.ToList())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true); // Используем true для завершения всего дерева процессов
                    process.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при завершении узла: {ex.Message}");
            }
        }
        nodeProcesses.Clear();
    }
}