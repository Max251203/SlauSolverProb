using Client.Windows;
using Shared.Network;
using Shared.Solvers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using static Shared.Models.NetworkMessages;

namespace Client.Services;

public class ResultReceivingService
{
    private readonly UdpClient _udpClient;
    private readonly double[,] _originalMatrix;
    private readonly double[] _originalVector;
    private readonly GaussSolver _sequentialSolver;
    private double[] _sequentialSolution;

    public ResultReceivingService(UdpClient udpClient, double[,] matrix, double[] vector)
    {
        _udpClient = udpClient;
        _originalMatrix = matrix;
        _originalVector = vector;
        _sequentialSolver = new GaussSolver();
    }

    public async Task<SolutionResult> ReceiveResults()
    {
        try
        {
            Console.WriteLine("Ожидание результатов...");
            var result = await UdpHelper.ReceiveAsync<SolutionResult>(_udpClient);

            var sequentialTime = MeasureSequentialSolution();
            result.SequentialTime = sequentialTime;

            PrintResults(result, sequentialTime);

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении результатов: {ex.Message}");
            throw;
        }
    }

    private long MeasureSequentialSolution()
    {
        Console.WriteLine("Выполняем последовательное решение для сравнения...");
        var watch = Stopwatch.StartNew();
        _sequentialSolution = _sequentialSolver.Solve(_originalMatrix, _originalVector);
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    public double[] GetSequentialSolution()
    {
        return _sequentialSolution;
    }

    private void PrintResults(SolutionResult result, long sequentialTime)
    {
        var metrics = new StringBuilder();
        metrics.AppendLine($"Размер матрицы: {result.MatrixSize}x{result.MatrixSize}");
        metrics.AppendLine($"Количество узлов: {result.NodesCount}");
        metrics.AppendLine($"Время распределённого решения: {result.DistributedTime} мс");
        metrics.AppendLine($"Время последовательного решения: {sequentialTime} мс");
        metrics.AppendLine($"Ускорение: {(double)sequentialTime / result.DistributedTime:F2}x");
        metrics.AppendLine($"Максимальная невязка: {result.MaxResidual:E6}");

        if (result.MaxDeviation > 0)
        {
            metrics.AppendLine($"Максимальное отклонение от эталона: {result.MaxDeviation:E6}");
            metrics.AppendLine($"Среднее отклонение от эталона: {result.AverageDeviation:E6}");
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            // Передаем оба решения
            mainWindow?.UpdateResults(metrics.ToString(), result.Solution, _sequentialSolution);
        });
    }
}