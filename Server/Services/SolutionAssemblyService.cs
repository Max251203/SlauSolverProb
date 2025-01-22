using Shared.Models;
using Shared.Network;
using System.Net.Sockets;
using System.Net;

namespace Server.Services;
public class SolutionAssemblyService
{
    private readonly Dictionary<(int row, int col), double[,]> processedBlocks;
    private readonly HashSet<(int row, int col)> completedBlocks;
    private readonly UdpClient udpServer;
    private IPEndPoint clientEndPoint;
    private int totalBlocksCount;
    private readonly object processLock = new object();
    private int totalNodes;
    private BlockMatrix blockMatrix;

    public SolutionAssemblyService(UdpClient udpServer)
    {
        this.udpServer = udpServer;
        processedBlocks = new Dictionary<(int row, int col), double[,]>();
        completedBlocks = new HashSet<(int row, int col)>();
    }

    public void Initialize(int totalBlocks, IPEndPoint clientEP, int nodesCount, BlockMatrix matrix = null)
    {
        lock (processLock)
        {
            totalBlocksCount = totalBlocks;
            clientEndPoint = clientEP;
            totalNodes = nodesCount;
            blockMatrix = matrix;
            processedBlocks.Clear();
            completedBlocks.Clear();
        }
    }

    public void AddProcessedBlock(int blockRow, int blockCol, double[,] blockData)
    {
        lock (processLock)
        {
            var blockKey = (blockRow, blockCol);
            processedBlocks[blockKey] = blockData;
            completedBlocks.Add(blockKey);
        }
    }

    public bool IsProcessingComplete
    {
        get
        {
            lock (processLock)
            {
                int totalBlocks = blockMatrix?.BlocksInRow * blockMatrix?.BlocksInCol ?? totalBlocksCount;
                bool isComplete = completedBlocks.Count == totalBlocks;
                if (isComplete)
                {
                    Console.WriteLine($"Обработка завершена: получены все {totalBlocks} блоков");
                }
                return isComplete;
            }
        }
    }

    public int GetCompletedBlocksCount()
    {
        lock (processLock)
        {
            return completedBlocks.Count;
        }
    }

    public async Task AssembleSolution(double[,] originalMatrix, double[] originalVector, long processingTime)
    {
        try
        {
            int matrixSize = originalMatrix.GetLength(0);
            int blockSize = blockMatrix.BlockSize;
            int blocksInRow = (int)Math.Ceiling((double)matrixSize / blockSize);

            Console.WriteLine($"\nНачало сборки решения:");
            Console.WriteLine($"Размер матрицы: {matrixSize}x{matrixSize}");
            Console.WriteLine($"Размер блока: {blockSize}");
            Console.WriteLine($"Количество блоков: {blocksInRow}x{blocksInRow}");

            // Проверяем все ли блоки на месте
            for (int i = 0; i < blocksInRow; i++)
            {
                for (int j = 0; j < blocksInRow; j++)
                {
                    if (!processedBlocks.ContainsKey((i, j)))
                    {
                        throw new Exception($"Отсутствует блок [{i}, {j}]");
                    }
                }
            }

            double[] solution = new double[matrixSize];
            Dictionary<int, double> indexValueMap = new Dictionary<int, double>();
            HashSet<int> coveredIndices = new HashSet<int>();

            Console.WriteLine("\nОбработка блоков:");
            // Собираем значения из всех блоков
            foreach (var ((row, col), blockData) in processedBlocks)
            {
                int startRow = row * blockSize;
                int rows = blockData.GetLength(0);

                Console.WriteLine($"Блок [{row}, {col}]: startRow={startRow}, rows={rows}");

                for (int localRow = 0; localRow < rows; localRow++)
                {
                    int globalRow = startRow + localRow;
                    if (globalRow < matrixSize)
                    {
                        double value = Math.Round(blockData[localRow, blockData.GetLength(1) - 1], 7);
                        indexValueMap[globalRow] = value;
                        coveredIndices.Add(globalRow);
                        Console.WriteLine($"  Индекс {globalRow}: значение {value}");
                    }
                }
            }

            // Проверка покрытия всех индексов
            var missingIndices = Enumerable.Range(0, matrixSize)
                                         .Except(coveredIndices)
                                         .ToList();

            if (missingIndices.Any())
            {
                Console.WriteLine("\nОтсутствующие индексы:");
                foreach (var index in missingIndices)
                {
                    int blockRow = index / blockSize;
                    int blockCol = 0;
                    Console.WriteLine($"Индекс {index}: должен быть в блоке [{blockRow}, {blockCol}]");
                }
                throw new Exception($"Отсутствуют значения для {missingIndices.Count} индексов");
            }

            Console.WriteLine("\nФормирование итогового решения:");
            // Формируем итоговое решение
            for (int i = 0; i < matrixSize; i++)
            {
                if (indexValueMap.ContainsKey(i))
                {
                    solution[i] = indexValueMap[i];
                }
                else
                {
                    int blockRow = i / blockSize;
                    int blockCol = 0;
                    throw new Exception($"Отсутствует значение для индекса {i} (блок [{blockRow}, {blockCol}])");
                }
            }

            // Проверяем точность решения
            double maxResidual = CalculateResidual(originalMatrix, solution, originalVector);
            Console.WriteLine($"\nПроверка решения:");
            Console.WriteLine($"Максимальная невязка: {maxResidual:E6}");

            // Дополнительная проверка корректности решения
            double[] testVector = new double[matrixSize];
            for (int i = 0; i < matrixSize; i++)
            {
                double sum = 0;
                for (int j = 0; j < matrixSize; j++)
                {
                    sum += originalMatrix[i, j] * solution[j];
                }
                testVector[i] = sum;
            }

            Console.WriteLine("\nВыборочная проверка (первые 5 строк):");
            for (int i = 0; i < Math.Min(5, matrixSize); i++)
            {
                double diff = Math.Abs(testVector[i] - originalVector[i]);
                Console.WriteLine($"Строка {i}: ожидается {originalVector[i]:E6}, получено {testVector[i]:E6}, разница {diff:E6}");
            }

            // Проверка наличия нулевых или очень маленьких значений
            var suspiciousValues = solution.Select((value, index) => (value, index))
                                         .Where(x => Math.Abs(x.value) < 1e-10)
                                         .ToList();
            if (suspiciousValues.Any())
            {
                Console.WriteLine("\nПодозрительные значения (близкие к нулю):");
                foreach (var (value, index) in suspiciousValues)
                {
                    Console.WriteLine($"Индекс {index}: значение {value:E10}");
                }
            }

            Console.WriteLine("\nОтправка решения клиенту...");
            await SendSolutionToClient(solution, maxResidual, processingTime, matrixSize);
            Console.WriteLine("Решение успешно отправлено.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nОШИБКА при сборке решения: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private double CalculateResidual(double[,] matrix, double[] solution, double[] vector)
    {
        double[] residual = new double[solution.Length];

        for (int i = 0; i < solution.Length; i++)
        {
            double sum = 0;
            for (int j = 0; j < solution.Length; j++)
            {
                sum += matrix[i, j] * solution[j];
            }
            residual[i] = Math.Abs(sum - vector[i]);
        }

        return residual.Max();
    }

    private async Task SendSolutionToClient(double[] solution, double maxResidual, long processingTime, int matrixSize)
    {
        try
        {
            // Округляем результаты до 7 знаков после запятой
            for (int i = 0; i < solution.Length; i++)
            {
                solution[i] = Math.Round(solution[i], 7);
            }

            var result = new NetworkMessages.SolutionResult
            {
                Solution = solution,
                MaxResidual = Math.Round(maxResidual, 7),
                DistributedTime = processingTime,
                MatrixSize = matrixSize,
                NodesCount = totalNodes
            };

            await UdpHelper.SendAsync(udpServer, result, clientEndPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при отправке результатов: {ex.Message}");
            throw;
        }
    }
}