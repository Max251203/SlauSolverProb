using Shared.Models;
using Shared.Network;
using System.Text.Json;

namespace Server.Services;

public class ConfigurationService
{
    private readonly string CONFIG_FILE;

    public ConfigurationService()
    {
        string serverDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Server"));
        CONFIG_FILE = Path.Combine(serverDirectory, "nodes.json");
        Directory.CreateDirectory(serverDirectory);
    }

    public NodeConfiguration LoadConfiguration()
    {
        if (!File.Exists(CONFIG_FILE))
        {
            Console.WriteLine($"Создан новый файл конфигурации: {CONFIG_FILE}");
            var defaultConfig = CreateDefaultConfiguration();
            SaveConfiguration(defaultConfig);
            return defaultConfig;
        }

        string jsonConfig = File.ReadAllText(CONFIG_FILE);
        var config = JsonSerializer.Deserialize<NodeConfiguration>(jsonConfig);

        if (config?.Nodes == null || config.Nodes.Count == 0)
        {
            Console.WriteLine($"Файл конфигурации пуст или повреждён. Создан новый файл: {CONFIG_FILE}");
            var defaultConfig = CreateDefaultConfiguration();
            SaveConfiguration(defaultConfig);
            return defaultConfig;
        }

        Console.WriteLine($"Загружен файл конфигурации: {CONFIG_FILE}");
        return config;
    }

    private NodeConfiguration CreateDefaultConfiguration()
    {
        return new NodeConfiguration
        {
            Nodes = Enumerable.Range(0,5)
                .Select(i => new NodeInfo
                {
                    NodeId = i,
                    IpAddress = "127.0.0.1",
                    Port = NetworkConfiguration.Ports.GetNodePort(i)
                })
                .ToList()
        };
    }

    private void SaveConfiguration(NodeConfiguration config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonConfig = JsonSerializer.Serialize(config, options);
        File.WriteAllText(CONFIG_FILE, jsonConfig);
    }
}