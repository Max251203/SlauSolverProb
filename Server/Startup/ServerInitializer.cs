using Shared.Models;
using Shared.Network;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Text;
using Server.Services;
using Shared.Utils;
using Node.Services;

public class ServerInitializer
{
    private readonly UdpClient udpServer;
    private readonly TaskDistributionService taskDistributionService;
    private readonly NodeManagementService nodeManagementService;
    private readonly SolutionAssemblyService solutionAssemblyService;
    private readonly BlockResultAssembler blockResultAssembler;
    private readonly LoadBalancer loadBalancer;
    private readonly NodeHealthMonitor healthMonitor;
    private readonly ErrorHandler errorHandler;
    private readonly NetworkMetrics networkMetrics;
    private readonly Stopwatch distributedSolveTimer;

    private List<string> receivedChunks;
    private int expectedChunksCount;
    private bool isReceivingChunks;
    private int matrixRows;
    private int matrixCols;
    private bool isInitialized;
    private int totalNodes;
    private BlockMatrix blockMatrix;
    private double[] vector;
    private IPEndPoint clientEndPoint;

    private readonly ConfigurationService configService;
    private readonly NodeConfiguration nodeConfig;
    private HashSet<int> reportedNodes = new HashSet<int>();

    public ServerInitializer(int port)
    {
        configService = new ConfigurationService();
        nodeConfig = configService.LoadConfiguration();
        totalNodes = nodeConfig.Nodes.Count;

        udpServer = new UdpClient(port);
        udpServer.Client.ReceiveBufferSize = NetworkConfiguration.Sizes.RECEIVE_BUFFER_SIZE;
        udpServer.Client.SendBufferSize = NetworkConfiguration.Sizes.SEND_BUFFER_SIZE;

        taskDistributionService = new TaskDistributionService();
        nodeManagementService = new NodeManagementService();
        solutionAssemblyService = new SolutionAssemblyService(udpServer);
        blockResultAssembler = new BlockResultAssembler();
        loadBalancer = new LoadBalancer();
        healthMonitor = new NodeHealthMonitor();
        errorHandler = new ErrorHandler();
        networkMetrics = new NetworkMetrics();
        distributedSolveTimer = new Stopwatch();
    }

    public async Task Initialize()
    {
        try
        {
            await nodeManagementService.StartNodes(totalNodes);
            await Task.Delay(2000);

            var tasks = new List<Task>();
            for (int i = 0; i < totalNodes; i++)
            {
                tasks.Add(CheckNodeAvailability(i));
            }

            await Task.WhenAll(tasks);
            StartHealthCheck();
            Console.WriteLine("Все узлы готовы к работе");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при инициализации: {ex.Message}");
            throw;
        }
    }

    private async Task CheckNodeAvailability(int nodeId)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"),
            NetworkConfiguration.Ports.GetNodePort(nodeId));
        var pingBytes = Encoding.UTF8.GetBytes("PING");

        int retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
        {
            try
            {
                await udpServer.SendAsync(pingBytes, pingBytes.Length, endpoint);
                var receiveTask = udpServer.ReceiveAsync();

                if (await Task.WhenAny(receiveTask, Task.Delay(1000)) == receiveTask)
                {
                    var response = await receiveTask;
                    string responseData = Encoding.UTF8.GetString(response.Buffer);
                    if (responseData == "PONG")
                    {
                        Console.WriteLine($"Узел {nodeId} готов к работе");
                        return;
                    }
                }
            }
            catch
            {
                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(500);
                }
            }
        }

        throw new Exception($"Узел не ответил после {maxRetries} попыток");
    }


    public async Task Start()
    {
        try
        {
            await ProcessIncomingData();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке данных: {ex.Message}");
            throw;
        }
    }

    private async void StartHealthCheck()
    {
        while (true)
        {
            var unhealthyNodes = healthMonitor.GetUnhealthyNodes();
            foreach (var nodeId in unhealthyNodes)
            {
                await HandleUnhealthyNode(nodeId);
            }
            await Task.Delay(NetworkConfiguration.Timeouts.HEALTH_CHECK_INTERVAL_MS);
        }
    }

    private async Task HandleUnhealthyNode(int nodeId)
    {
        if (!reportedNodes.Contains(nodeId))
        {
            Console.WriteLine($"Узел {nodeId} завершил работу");
            reportedNodes.Add(nodeId);
        }

        var affectedBlocks = taskDistributionService.GetNodeBlocks(nodeId);
        foreach (var block in affectedBlocks)
        {
            if (errorHandler.ShouldRetryBlock(block))
            {
                int newNodeId = loadBalancer.GetOptimalNode(totalNodes);
                await RedistributeBlock(block, newNodeId);
            }
            else
            {
                Console.WriteLine($"Превышено количество попыток для блока {block}");
            }
        }
    }

    private async Task RedistributeBlock((int row, int col) block, int newNodeId)
    {
        var task = taskDistributionService.CreateBlockTask(block.row, block.col, blockMatrix);
        await SendTaskToNode(newNodeId, task);
        Console.WriteLine($"Блок [{block.row}, {block.col}] перераспределен на узел {newNodeId}");
    }

    private async Task ProcessIncomingData()
    {
        while (true)
        {
            try
            {
                var result = await udpServer.ReceiveAsync();
                networkMetrics.RecordReceivedData(result.Buffer.Length);

                string data = Encoding.UTF8.GetString(result.Buffer);

                if (data == "PONG")
                {
                    continue;
                }

                if (data.StartsWith("INIT"))
                {
                    ProcessInitMessage(data, result.RemoteEndPoint);
                }
                else if (data.StartsWith("COUNT"))
                {
                    ProcessCountMessage(data);
                }
                else if (data.StartsWith("CHUNK"))
                {
                    ProcessChunk(data);
                }
                else if (data.StartsWith("ACK"))
                {
                    ProcessAcknowledgement(data);
                }
                else if (data.StartsWith("HEARTBEAT"))
                {
                    ProcessHeartbeat(data, result.RemoteEndPoint.Port - NetworkConfiguration.Ports.BASE_NODE_PORT);
                }
                else
                {
                    try
                    {
                        var blockChunk = JsonSerializer.Deserialize<NetworkMessages.BlockResultChunk>(data);
                        await ProcessBlockResultChunk(blockChunk);
                    }
                    catch
                    {
                        Console.WriteLine($"Получены неизвестные данные: {data.Substring(0, Math.Min(100, data.Length))}...");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении данных: {ex.Message}");
            }
        }
    }

    private void ProcessInitMessage(string data, IPEndPoint clientEP)
    {
        var parts = data.Split('|');
        matrixRows = int.Parse(parts[1]);
        matrixCols = int.Parse(parts[2]);
        clientEndPoint = clientEP;
        isInitialized = true;

        Console.WriteLine($"Инициализация: матрица {matrixRows}x{matrixCols}, узлов: {totalNodes}");
    }

    private void ProcessCountMessage(string data)
    {
        if (!isInitialized)
        {
            throw new Exception("Получено сообщение о количестве чанков до инициализации");
        }

        var parts = data.Split('|');
        expectedChunksCount = int.Parse(parts[1]);
        receivedChunks = new List<string>(expectedChunksCount);
        isReceivingChunks = true;

        Console.WriteLine($"Ожидается {expectedChunksCount} частей данных");
    }

    private async void ProcessChunk(string data)
    {
        if (!isReceivingChunks) return;

        try
        {
            var parts = data.Split(new[] { '|' }, 3);
            int chunkIndex = int.Parse(parts[1]);
            string chunkData = parts[2];

            while (receivedChunks.Count <= chunkIndex)
            {
                receivedChunks.Add(null);
            }
            receivedChunks[chunkIndex] = chunkData;

            Console.WriteLine($"Получена часть {chunkIndex + 1} из {expectedChunksCount}");

            if (receivedChunks.Count == expectedChunksCount && !receivedChunks.Contains(null))
            {
                isReceivingChunks = false;
                var completeData = string.Concat(receivedChunks);
                await ProcessCompleteData(completeData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке чанка: {ex.Message}\n{ex.StackTrace}");
        }
    }
    private void ProcessAcknowledgement(string data)
    {
        var ack = JsonSerializer.Deserialize<ChunkAck>(data.Substring(4));
        blockResultAssembler.ConfirmChunkReceived(ack);
    }

    private void ProcessHeartbeat(string data, int nodeId)
    {
        healthMonitor.UpdateHeartbeat(nodeId);
    }

    private async Task ProcessCompleteData(string data)
    {
        try
        {
            reportedNodes.Clear();
            var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine($"Получено строк данных: {lines.Length}");

            if (lines.Length != matrixRows)
            {
                throw new Exception($"Несоответствие количества строк: ожидалось {matrixRows}, получено {lines.Length}");
            }

            (var matrix, vector) = ParseMatrixData(lines);

            int blockSize = BlockSizeOptimizer.CalculateOptimalBlockSize(Math.Min(matrixRows, matrixCols), totalNodes);
            Console.WriteLine($"Оптимальный размер блока: {blockSize}x{blockSize}");

            blockMatrix = new BlockMatrix(matrix, blockSize);

            taskDistributionService.Initialize(blockMatrix, vector);
            solutionAssemblyService.Initialize(blockMatrix.BlocksInRow * blockMatrix.BlocksInCol, clientEndPoint, totalNodes, blockMatrix);

            distributedSolveTimer.Restart();
            Console.WriteLine("Начало распределения задач...");

            // Распределяем все блоки матрицы
            for (int i = 0; i < blockMatrix.BlocksInRow; i++)
            {
                for (int j = 0; j < blockMatrix.BlocksInCol; j++)
                {
                    int targetNode = loadBalancer.GetOptimalNode(totalNodes);
                    var task = taskDistributionService.CreateBlockTask(i, j, blockMatrix);
                    taskDistributionService.AssignBlockToNode(targetNode, (i, j));

                    try
                    {
                        await SendTaskToNode(targetNode, task);
                        Console.WriteLine($"Распределен блок [{i}, {j}] узлу {targetNode}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при отправке блока [{i}, {j}] узлу {targetNode}: {ex.Message}");
                        targetNode = (targetNode + 1) % totalNodes;
                        await SendTaskToNode(targetNode, task);
                        Console.WriteLine($"Блок [{i}, {j}] переназначен узлу {targetNode}");
                    }

                    await Task.Delay(NetworkConfiguration.Timeouts.TASK_DISTRIBUTION_DELAY_MS);
                }
            }

            Console.WriteLine("Все задачи распределены, ожидание результатов...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке данных: {ex.Message}");
            throw;
        }
    }

    private async Task SendTaskToNode(int nodeId, NetworkMessages.BlockTask task)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"),
            NetworkConfiguration.Ports.GetNodePort(nodeId));

        var retryPolicy = new TransmissionRetryPolicy();
        await retryPolicy.ExecuteWithRetryAsync(async () =>
        {
            var jsonData = JsonSerializer.Serialize(task);
            var bytes = Encoding.UTF8.GetBytes(jsonData);
            await udpServer.SendAsync(bytes, bytes.Length, endpoint);
            networkMetrics.RecordSentData(bytes.Length);
        });
    }

    private async Task ProcessBlockResultChunk(NetworkMessages.BlockResultChunk chunk)
    {
        try
        {
            Console.WriteLine($"Получен чанк [{chunk.BlockRow}, {chunk.BlockCol}] #{chunk.ChunkId} от узла {chunk.NodeId}");

            // Отправляем подтверждение получения чанка
            var ack = new ChunkAck
            {
                BlockRow = chunk.BlockRow,
                BlockCol = chunk.BlockCol,
                ChunkId = chunk.ChunkId,
                NodeId = chunk.NodeId,
                IsReceived = true
            };

            await SendAcknowledgement(ack);
            Console.WriteLine($"Отправлено подтверждение для чанка [{chunk.BlockRow}, {chunk.BlockCol}] #{chunk.ChunkId}");

            if (blockResultAssembler.TryAddChunk(chunk))
            {
                var completeBlock = blockResultAssembler.AssembleBlock(chunk.BlockRow, chunk.BlockCol);
                await ProcessNodeResult(completeBlock);
                errorHandler.ResetRetryCount((chunk.BlockRow, chunk.BlockCol));
                loadBalancer.RegisterTaskCompletion(chunk.NodeId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке чанка блока [{chunk.BlockRow}, {chunk.BlockCol}]: {ex.Message}");
        }
    }

    private async Task ProcessNodeResult(NetworkMessages.BlockResult result)
    {
        try
        {
            var blockKey = (result.BlockRow, result.BlockCol);

            if (taskDistributionService.IsBlockPending(blockKey))
            {
                Console.WriteLine($"Получен результат для блока [{result.BlockRow}, {result.BlockCol}]");
                taskDistributionService.MarkBlockComplete(blockKey);
                solutionAssemblyService.AddProcessedBlock(result.BlockRow, result.BlockCol, result.BlockData.ToMatrix());

                if (solutionAssemblyService.IsProcessingComplete)
                {
                    Console.WriteLine("Все блоки обработаны, формируем итоговое решение");
                    taskDistributionService.SetProcessingComplete();
                    distributedSolveTimer.Stop();
                    await solutionAssemblyService.AssembleSolution(blockMatrix.Matrix, vector, distributedSolveTimer.ElapsedMilliseconds);
                    networkMetrics.PrintMetrics();
                }
                else
                {
                    Console.WriteLine($"Обработано блоков: {solutionAssemblyService.GetCompletedBlocksCount()} из {blockMatrix.BlocksInRow * blockMatrix.BlocksInCol}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке результата от узла: {ex.Message}");
        }
    }

    private async Task SendAcknowledgement(ChunkAck ack)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string jsonAck = JsonSerializer.Serialize(ack, jsonOptions);
            string data = $"ACK|{jsonAck}";  // Изменили формат
            byte[] bytes = Encoding.UTF8.GetBytes(data);

            await udpServer.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("127.0.0.1"),
                NetworkConfiguration.Ports.GetNodePort(ack.NodeId)));

            networkMetrics.RecordSentData(bytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при отправке подтверждения: {ex.Message}");
        }
    }

    private (double[,] matrix, double[] vector) ParseMatrixData(string[] lines)
    {
        var matrix = new double[matrixRows, matrixCols];
        var vector = new double[matrixRows];

        for (int i = 0; i < matrixRows; i++)
        {
            var values = lines[i].Split('|')[0].Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v.Replace(",", "."),
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            for (int j = 0; j < matrixCols; j++)
            {
                matrix[i, j] = values[j];
            }

            vector[i] = double.Parse(lines[i].Split('|')[1].Trim().Replace(",", "."),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return (matrix, vector);
    }

    public void Cleanup()
    {
        try
        {
            nodeManagementService.CleanupNodes();
            udpServer?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при очистке ресурсов: {ex.Message}");
        }
    }
}