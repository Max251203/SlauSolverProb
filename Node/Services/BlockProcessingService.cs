using Shared.Models;
using Shared.Utils;

namespace Node.Services;

public class BlockProcessingService
{
    private readonly int nodeId;
    private readonly NetworkMetrics networkMetrics;
    private readonly PerformanceMetrics performanceMetrics;

    public BlockProcessingService(int nodeId)
    {
        this.nodeId = nodeId;
        this.networkMetrics = new NetworkMetrics();
        this.performanceMetrics = new PerformanceMetrics(0, 0);
    }
    public double[,] ProcessBlock(NetworkMessages.BlockTask task)
    {
        try
        {
            Console.WriteLine($"Начало обработки блока [{task.BlockRow}, {task.BlockCol}]");
            Console.WriteLine($"Размеры блока: {task.Matrix.Rows}x{task.Matrix.Cols}");
            Console.WriteLine($"Размер вектора: {task.Vector.Length}");

            performanceMetrics.Initialize(task.Matrix.Rows, 1);
            performanceMetrics.StartMeasurement();

            var matrix = task.Matrix.ToMatrix();

            if (matrix.GetLength(0) != task.Vector.Length)
            {
                throw new ArgumentException(
                    $"Несоответствие размерностей: строк в матрице {matrix.GetLength(0)}, элементов в векторе {task.Vector.Length}");
            }

            var processedBlock = BlockGaussianElimination(matrix, task.Vector);

            // Форматируем результаты с правильной точностью
            for (int i = 0; i < processedBlock.GetLength(0); i++)
            {
                for (int j = 0; j < processedBlock.GetLength(1); j++)
                {
                    processedBlock[i, j] = Math.Round(processedBlock[i, j], 7);
                }
            }

            performanceMetrics.StopMeasurement();
            networkMetrics.PrintMetrics();

            Console.WriteLine($"Блок [{task.BlockRow}, {task.BlockCol}] успешно обработан");

            return processedBlock;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке блока [{task.BlockRow}, {task.BlockCol}]: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    //private double[,] BlockGaussianElimination(double[,] block, double[] vectorPart)
    //{
    //    try
    //    {
    //        int rows = block.GetLength(0);
    //        int cols = block.GetLength(1);

    //        Console.WriteLine($"Обработка блока размером {rows}x{cols}");

    //        double[,] augmentedBlock = new double[rows, cols + 1];
    //        int[] columnPermutation = new int[cols]; // Добавляем отслеживание перестановок

    //        // Инициализация массива перестановок
    //        for (int i = 0; i < cols; i++)
    //            columnPermutation[i] = i;

    //        // Создание расширенной матрицы
    //        for (int i = 0; i < rows; i++)
    //        {
    //            for (int j = 0; j < cols; j++)
    //                augmentedBlock[i, j] = block[i, j];
    //            augmentedBlock[i, cols] = vectorPart[i];
    //        }

    //        // Прямой ход метода Гаусса
    //        for (int i = 0; i < rows; i++)
    //        {
    //            // Поиск максимального элемента
    //            double maxElement = 0;
    //            int maxRow = i;
    //            int maxCol = i;

    //            for (int k = i; k < rows; k++)
    //            {
    //                for (int l = i; l < cols; l++)
    //                {
    //                    if (Math.Abs(augmentedBlock[k, l]) > maxElement)
    //                    {
    //                        maxElement = Math.Abs(augmentedBlock[k, l]);
    //                        maxRow = k;
    //                        maxCol = l;
    //                    }
    //                }
    //            }

    //            if (maxElement < 1e-12)
    //                continue;

    //            // Перестановка строк
    //            if (maxRow != i)
    //            {
    //                for (int j = 0; j <= cols; j++)
    //                {
    //                    (augmentedBlock[i, j], augmentedBlock[maxRow, j]) =
    //                        (augmentedBlock[maxRow, j], augmentedBlock[i, j]);
    //                }
    //            }

    //            // Перестановка столбцов и запоминание порядка
    //            if (maxCol != i)
    //            {
    //                for (int j = 0; j < rows; j++)
    //                {
    //                    (augmentedBlock[j, i], augmentedBlock[j, maxCol]) =
    //                        (augmentedBlock[j, maxCol], augmentedBlock[j, i]);
    //                }
    //                (columnPermutation[i], columnPermutation[maxCol]) =
    //                    (columnPermutation[maxCol], columnPermutation[i]);
    //            }

    //            // Нормализация и исключение
    //            double pivot = augmentedBlock[i, i];
    //            if (Math.Abs(pivot) > 1e-12)
    //            {
    //                for (int j = i; j <= cols; j++)
    //                    augmentedBlock[i, j] /= pivot;

    //                for (int k = 0; k < rows; k++)
    //                {
    //                    if (k != i && Math.Abs(augmentedBlock[k, i]) > 1e-12)
    //                    {
    //                        double factor = augmentedBlock[k, i];
    //                        for (int j = i; j <= cols; j++)
    //                        {
    //                            augmentedBlock[k, j] -= factor * augmentedBlock[i, j];
    //                            if (Math.Abs(augmentedBlock[k, j]) < 1e-12)
    //                                augmentedBlock[k, j] = 0;
    //                        }
    //                    }
    //                }
    //            }
    //        }

    //        // Восстановление порядка столбцов
    //        double[,] restoredBlock = new double[rows, cols + 1];
    //        for (int i = 0; i < rows; i++)
    //        {
    //            restoredBlock[i, cols] = augmentedBlock[i, cols]; // Копируем правую часть
    //            for (int j = 0; j < cols; j++)
    //            {
    //                // Восстанавливаем исходный порядок столбцов
    //                int originalPos = Array.IndexOf(columnPermutation, j);
    //                restoredBlock[i, j] = augmentedBlock[i, originalPos];
    //            }
    //        }

    //        return restoredBlock;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"Ошибка в BlockGaussianElimination: {ex.Message}");
    //        throw;
    //    }
    //}
    private double[,] BlockGaussianElimination(double[,] block, double[] vectorPart)
    {
        try
        {
            int rows = block.GetLength(0);
            int cols = block.GetLength(1);

            Console.WriteLine($"Обработка блока размером {rows}x{cols}");

            // Массивы для отслеживания перестановок
            int[] rowPermutation = new int[rows];
            int[] columnPermutation = new int[cols];

            // Инициализация массивов перестановок
            for (int i = 0; i < rows; i++)
                rowPermutation[i] = i;
            for (int i = 0; i < cols; i++)
                columnPermutation[i] = i;

            double[,] augmentedBlock = new double[rows, cols + 1];

            // Создание расширенной матрицы
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                    augmentedBlock[i, j] = block[i, j];
                augmentedBlock[i, cols] = vectorPart[i];
            }

            // Прямой ход метода Гаусса
            for (int i = 0; i < rows; i++)
            {
                // Поиск максимального элемента
                double maxElement = 0;
                int maxRow = i;
                int maxCol = i;

                for (int k = i; k < rows; k++)
                {
                    for (int l = i; l < cols; l++)
                    {
                        if (Math.Abs(augmentedBlock[k, l]) > maxElement)
                        {
                            maxElement = Math.Abs(augmentedBlock[k, l]);
                            maxRow = k;
                            maxCol = l;
                        }
                    }
                }

                if (maxElement < 1e-12)
                    continue;

                // Перестановка строк и обновление массива перестановок
                if (maxRow != i)
                {
                    for (int j = 0; j <= cols; j++)
                    {
                        (augmentedBlock[i, j], augmentedBlock[maxRow, j]) =
                            (augmentedBlock[maxRow, j], augmentedBlock[i, j]);
                    }
                    (rowPermutation[i], rowPermutation[maxRow]) =
                        (rowPermutation[maxRow], rowPermutation[i]);
                }

                // Перестановка столбцов и обновление массива перестановок
                if (maxCol != i)
                {
                    for (int j = 0; j < rows; j++)
                    {
                        (augmentedBlock[j, i], augmentedBlock[j, maxCol]) =
                            (augmentedBlock[j, maxCol], augmentedBlock[j, i]);
                    }
                    (columnPermutation[i], columnPermutation[maxCol]) =
                        (columnPermutation[maxCol], columnPermutation[i]);
                }

                // Нормализация и исключение
                double pivot = augmentedBlock[i, i];
                if (Math.Abs(pivot) > 1e-12)
                {
                    for (int j = i; j <= cols; j++)
                        augmentedBlock[i, j] /= pivot;

                    for (int k = 0; k < rows; k++)
                    {
                        if (k != i && Math.Abs(augmentedBlock[k, i]) > 1e-12)
                        {
                            double factor = augmentedBlock[k, i];
                            for (int j = i; j <= cols; j++)
                            {
                                augmentedBlock[k, j] -= factor * augmentedBlock[i, j];
                                if (Math.Abs(augmentedBlock[k, j]) < 1e-12)
                                    augmentedBlock[k, j] = 0;
                            }
                        }
                    }
                }
            }

            // Восстановление исходного порядка
            double[,] restoredBlock = new double[rows, cols + 1];

            // Сначала восстанавливаем порядок строк
            for (int i = 0; i < rows; i++)
            {
                int originalRow = Array.IndexOf(rowPermutation, i);
                for (int j = 0; j <= cols; j++)
                {
                    restoredBlock[i, j] = augmentedBlock[originalRow, j];
                }
            }

            // Затем восстанавливаем порядок столбцов (кроме последнего столбца с вектором)
            double[,] finalBlock = new double[rows, cols + 1];
            for (int i = 0; i < rows; i++)
            {
                finalBlock[i, cols] = restoredBlock[i, cols]; // Копируем правую часть
                for (int j = 0; j < cols; j++)
                {
                    int originalCol = Array.IndexOf(columnPermutation, j);
                    finalBlock[i, j] = restoredBlock[i, originalCol];
                }
            }

            return finalBlock;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка в BlockGaussianElimination: {ex.Message}");
            throw;
        }
    }
} 