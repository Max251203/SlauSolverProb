using Client.Models;
using Client.Services;
using Shared.Utils;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Client.Windows;

public partial class MainWindow : Window
{
    private double[,] matrix;
    private double[] vector;
    private double[] trueSolution;
    private double[] currentSequentialSolution;
    private double[] currentDistributedSolution;
    private UdpClient udpClient;
    private CancellationTokenSource cancellationTokenSource;
    private const int DEFAULT_DISPLAY_SIZE = 20;

    public MainWindow()
    {
        InitializeComponent();
        InitializeControls();
    }

    private void InitializeControls()
    {
        SolveButton.IsEnabled = false;
        SaveMatrixButton.IsEnabled = false;
        EditModeCheckBox.IsChecked = false;
        MatrixGrid.IsReadOnly = true;

        StartSolutionIndexTextBox.Text = "1";
        EndSolutionIndexTextBox.Text = DEFAULT_DISPLAY_SIZE.ToString();
        TotalSolutionElementsLabel.Content = "0";
    }

    private bool ValidateRange(string start, string end, int maxValue, out int startIndex, out int endIndex, bool isRow = true)
    {
        startIndex = endIndex = 0;
        if (!int.TryParse(start, out startIndex) || !int.TryParse(end, out endIndex))
        {
            MessageBox.Show("Введите корректные значения", "Ошибка");
            return false;
        }

        startIndex--;
        endIndex--;

        if (startIndex >= 0 && endIndex <= maxValue && startIndex <= endIndex)
            return true;

        MessageBox.Show($"Указан неверный диапазон. Допустимые значения: {(isRow ? "строки" : "элементы")} от 1 до {maxValue + 1}", "Ошибка");
        return false;
    }

    private void SafeExecute(Action action, string errorMessage = "Произошла ошибка")
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{errorMessage}: {ex.Message}", "Ошибка");
            UpdateMatrixView();
        }
    }

    private async void GenerateMatrixButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MatrixSizeTextBox.Text, out int size) || size <= 0 || size > 50000)
        {
            MessageBox.Show("Размер матрицы должен быть больше 0 и не превышать 50000", "Ошибка");
            return;
        }

        SafeExecute(() =>
        {
            (matrix, vector) = MatrixGenerator.Generate(size, size);
            trueSolution = null;
            UpdateMatrixView();
            SolveButton.IsEnabled = true;
            SaveMatrixButton.IsEnabled = true;
            MessageBox.Show("Матрица успешно сгенерирована", "Успех");
        });
    }

    private async Task<(double[,] matrix, double[] vector)> LoadFiles(string matrixFile, string vectorFile)
    {
        var matrixLines = await File.ReadAllLinesAsync(matrixFile);
        var vectorLines = await File.ReadAllLinesAsync(vectorFile);
        int size = matrixLines.Length;

        var resultMatrix = new double[size, size];
        var resultVector = new double[size];

        // Проверка данных
        Console.WriteLine($"Загружено строк матрицы: {matrixLines.Length}");
        Console.WriteLine($"Загружено элементов вектора: {vectorLines.Length}");
        Console.WriteLine($"Пример строки матрицы: {matrixLines[0]}");
        Console.WriteLine($"Пример элемента вектора: {vectorLines[0]}");

        // Загрузка матрицы
        for (int i = 0; i < size; i++)
        {
            var values = matrixLines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v =>
                {
                    var parsed = double.Parse(v.Replace(",", "."),
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (double.IsNaN(parsed) || double.IsInfinity(parsed))
                        throw new Exception($"Некорректное значение в матрице: {v}");
                    return parsed;
                })
                .ToArray();

            if (values.Length != size)
                throw new Exception($"Неверное количество элементов в строке {i}: {values.Length}");

            for (int j = 0; j < size; j++)
                resultMatrix[i, j] = values[j];
        }

        // Загрузка вектора
        for (int i = 0; i < size; i++)
        {
            var value = double.Parse(vectorLines[i].Replace(",", "."),
                System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new Exception($"Некорректное значение в векторе: {vectorLines[i]}");
            resultVector[i] = value;
        }

        return (resultMatrix, resultVector);
    }

    private async void LoadMatrixButton_Click(object sender, RoutedEventArgs e)
    {
        var matrixDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.A.txt)|*.A.txt|All files (*.*)|*.*",
            Title = "Выберите файл матрицы (*.A.txt)"
        };

        if (matrixDialog.ShowDialog() == true)
        {
            var vectorDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text files (*.B.txt)|*.B.txt|All files (*.*)|*.*",
                Title = "Выберите файл вектора (*.B.txt)"
            };

            if (vectorDialog.ShowDialog() == true)
            {
                try
                {
                    (matrix, vector) = await LoadFiles(matrixDialog.FileName, vectorDialog.FileName);
                    trueSolution = null;
                    MatrixSizeTextBox.Text = matrix.GetLength(0).ToString();
                    UpdateMatrixView();
                    SolveButton.IsEnabled = true;
                    SaveMatrixButton.IsEnabled = true;
                    MessageBox.Show("Матрица и вектор успешно загружены", "Успех");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}", "Ошибка");
                }
            }
        }
    }
    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var matrixDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.A.txt)|*.A.txt|All files (*.*)|*.*",
            Title = "Выберите файл матрицы (*.A.txt)"
        };

        if (matrixDialog.ShowDialog() == true)
        {
            var vectorDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text files (*.B.txt)|*.B.txt|All files (*.*)|*.*",
                Title = "Выберите файл вектора (*.B.txt)"
            };

            if (vectorDialog.ShowDialog() == true)
            {
                var solutionDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Text files (*.des.txt)|*.des.txt|All files (*.*)|*.*",
                    Title = "Выберите файл эталонного решения (*.des.txt)"
                };

                if (solutionDialog.ShowDialog() == true)
                {
                    try
                    {
                        (matrix, vector) = await LoadFiles(matrixDialog.FileName, vectorDialog.FileName);
                        trueSolution = await File.ReadAllLinesAsync(solutionDialog.FileName)
                            .ContinueWith(t => t.Result
                                .Select(line => double.Parse(line.Replace(",", "."),
                                    System.Globalization.CultureInfo.InvariantCulture))
                                .ToArray());

                        MatrixSizeTextBox.Text = matrix.GetLength(0).ToString();
                        UpdateMatrixView();

                        SolveButton.IsEnabled = false;
                        await SolveMatrix();
                        SolveButton.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при загрузке тестовых данных: {ex.Message}", "Ошибка");
                        SolveButton.IsEnabled = true;
                    }
                }
            }
        }
    }

    private async void SaveMatrixButton_Click(object sender, RoutedEventArgs e)
    {
        if (matrix == null || vector == null)
        {
            MessageBox.Show("Нет данных для сохранения", "Предупреждение");
            return;
        }

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.A.txt)|*.A.txt|All files (*.*)|*.*",
            Title = "Сохранить файл матрицы"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                string matrixFile = saveFileDialog.FileName;
                string vectorFile = matrixFile.Replace(".A.txt", ".B.txt");

                var matrixBuilder = new StringBuilder();
                var vectorBuilder = new StringBuilder();

                for (int i = 0; i < matrix.GetLength(0); i++)
                {
                    // Сохранение матрицы с выравниванием
                    for (int j = 0; j < matrix.GetLength(1); j++)
                    {
                        matrixBuilder.Append(matrix[i, j].ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture)
                            .Replace(".", ",").PadLeft(15));
                    }
                    matrixBuilder.AppendLine();

                    // Сохранение вектора
                    vectorBuilder.AppendLine(vector[i].ToString("F7",
                        System.Globalization.CultureInfo.InvariantCulture)
                        .Replace(".", ","));
                }

                await File.WriteAllTextAsync(matrixFile, matrixBuilder.ToString());
                await File.WriteAllTextAsync(vectorFile, vectorBuilder.ToString());
                MessageBox.Show("Данные успешно сохранены", "Успех");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении данных: {ex.Message}", "Ошибка");
            }
        }
    }

    private void SetControlsState(bool enabled)
    {
        foreach (var control in new UIElement[] { SolveButton, TestButton, GenerateMatrixButton,
                                                LoadMatrixButton, SaveMatrixButton, EditModeCheckBox })
        {
            control.IsEnabled = enabled && (control != SaveMatrixButton || matrix != null);
        }
    }

    private void DisableControls() => SetControlsState(false);
    private void EnableControls() => SetControlsState(true);

    private async void SolveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInput())
            return;

        await SolveMatrix();
    }

    private async Task SolveMatrix()
    {
        try
        {
            DisableControls();

            // Очистка предыдущих результатов
            MetricsTextBox.Clear();
            currentDistributedSolution = null;
            currentSequentialSolution = null;
            SolutionGrid.ItemsSource = null;

            TotalSolutionElementsLabel.Content = "0";
            StartSolutionIndexTextBox.Text = "1";
            EndSolutionIndexTextBox.Text = DEFAULT_DISPLAY_SIZE.ToString();

            cancellationTokenSource = new CancellationTokenSource();

            using (udpClient = new UdpClient(0))
            {
                var serverEndPoint = new IPEndPoint(
                    IPAddress.Parse(ServerIpTextBox.Text),
                    int.Parse(ServerPortTextBox.Text));

                var transmissionService = new MatrixTransmissionService(udpClient, serverEndPoint, matrix, vector);
                var resultService = new ResultReceivingService(udpClient, matrix, vector);

                await transmissionService.SendMatrix(matrix.GetLength(0), matrix.GetLength(1));
                var result = await resultService.ReceiveResults();
                currentDistributedSolution = result.Solution;
                currentSequentialSolution = resultService.GetSequentialSolution();
                UpdateSolutionGrid();
            }
        }
        catch (OperationCanceledException)
        {
            MetricsTextBox.AppendText("Операция была отменена\n");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
            MetricsTextBox.AppendText($"Ошибка: {ex.Message}\n");
        }
        finally
        {
            EnableControls();
            cancellationTokenSource?.Dispose();
        }
    }

    private void DisplayData(bool isMatrixMode)
    {
        if ((isMatrixMode && matrix == null) || (!isMatrixMode && vector == null))
            return;

        int maxRow = isMatrixMode ? matrix.GetLength(0) - 1 : vector.Length - 1;
        int maxCol = isMatrixMode ? matrix.GetLength(1) - 1 : 0;

        int startRow, endRow, startCol = 0, endCol = 0;

        if (ValidateRange(StartRowTextBox.Text, EndRowTextBox.Text, maxRow, out startRow, out endRow) &&
            (!isMatrixMode || ValidateRange(StartColTextBox.Text, EndColTextBox.Text, maxCol, out startCol, out endCol, false)))
        {
            ShowMatrixRange(startRow, endRow, isMatrixMode ? startCol : 0, isMatrixMode ? endCol : 0);
        }
        else
        {
            UpdateMatrixView();
        }
    }

    private void MatrixRadioButton_Checked(object sender, RoutedEventArgs e) => DisplayData(true);
    private void VectorRadioButton_Checked(object sender, RoutedEventArgs e) => DisplayData(false);

    private void UpdateMatrixView()
    {
        if (matrix != null)
        {
            int totalRows = matrix.GetLength(0);
            int totalCols = matrix.GetLength(1);

            TotalRowsLabel.Content = totalRows.ToString();
            TotalColsLabel.Content = totalCols.ToString();

            StartRowTextBox.Text = "1";
            EndRowTextBox.Text = Math.Min(DEFAULT_DISPLAY_SIZE, totalRows).ToString();
            StartColTextBox.Text = "1";
            EndColTextBox.Text = Math.Min(DEFAULT_DISPLAY_SIZE, totalCols).ToString();

            ShowMatrixRange(0, Math.Min(DEFAULT_DISPLAY_SIZE - 1, totalRows - 1),
                          0, Math.Min(DEFAULT_DISPLAY_SIZE - 1, totalCols - 1));
        }
    }

    private void ShowMatrixRange(int startRow, int endRow, int startCol, int endCol)
    {
        MatrixGrid.Columns.Clear();
        MatrixGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "№",
            Binding = new Binding("RowNumber") { StringFormat = "N0" },
            IsReadOnly = true
        });

        bool isMatrixMode = MatrixRadioButton.IsChecked == true;
        if (isMatrixMode)
        {
            for (int j = startCol; j <= endCol; j++)
            {
                MatrixGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = $"X{j + 1}",
                    Binding = new Binding($"Values[{j - startCol}]") { StringFormat = "F6" }
                });
            }
        }
        else
        {
            MatrixGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "b",
                Binding = new Binding("VectorValue") { StringFormat = "F6" }
            });
        }

        var data = new List<MatrixRow>();
        for (int i = startRow; i <= endRow; i++)
        {
            if (isMatrixMode)
            {
                var row = new MatrixRow(i + 1, endCol - startCol + 1);
                for (int j = startCol; j <= endCol; j++)
                {
                    row.Values.Add(matrix[i, j]);
                }
                data.Add(row);
            }
            else
            {
                data.Add(new MatrixRow(i + 1, 1) { VectorValue = vector[i] });
            }
        }

        MatrixGrid.ItemsSource = data;
    }

    private void UpdateSolutionGrid()
    {
        if (currentDistributedSolution == null) return;

        if (ValidateRange(StartSolutionIndexTextBox.Text, EndSolutionIndexTextBox.Text,
            currentDistributedSolution.Length - 1, out int startIndex, out int endIndex))
        {
            var data = new List<SolutionElement>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                data.Add(new SolutionElement
                {
                    Index = i + 1,
                    SequentialSolution = currentSequentialSolution?[i],
                    DistributedSolution = currentDistributedSolution[i],
                    TrueSolution = trueSolution?[i]
                });
            }

            SolutionGrid.ItemsSource = data;
            TotalSolutionElementsLabel.Content = currentDistributedSolution.Length.ToString();
        }
    }

    private void EditModeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        MatrixGrid.IsReadOnly = false;
        MatrixGrid.CellEditEnding += MatrixGrid_CellEditEnding;
    }

    private void EditModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        MatrixGrid.IsReadOnly = true;
        MatrixGrid.CellEditEnding -= MatrixGrid_CellEditEnding;
    }

    private void MatrixGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            var row = e.Row.Item as MatrixRow;
            var newValue = (e.EditingElement as TextBox)?.Text;

            if (row != null && double.TryParse(newValue,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value))
            {
                if (VectorRadioButton.IsChecked == true)
                {
                    vector[row.RowNumber - 1] = value;
                    row.VectorValue = value;
                }
                else
                {
                    var columnHeader = (e.Column as DataGridTextColumn)?.Header.ToString();
                    if (columnHeader?.StartsWith("X") == true)
                    {
                        int colIndex = int.Parse(columnHeader.Substring(1)) - 1;
                        matrix[row.RowNumber - 1, colIndex] = value;
                        row.Values[colIndex - int.Parse(StartColTextBox.Text) + 1] = value;
                    }
                }
            }
        }
    }

    private void ShowRangeButton_Click(object sender, RoutedEventArgs e) => DisplayData(MatrixRadioButton.IsChecked == true);

    private void ShowSolutionRangeButton_Click(object sender, RoutedEventArgs e) => UpdateSolutionGrid();

    private bool ValidateInput()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ServerIpTextBox.Text) ||
                string.IsNullOrWhiteSpace(ServerPortTextBox.Text))
            {
                throw new Exception("Укажите IP адрес и порт сервера");
            }

            if (!IPAddress.TryParse(ServerIpTextBox.Text, out _))
            {
                throw new Exception("Некорректный IP адрес сервера");
            }

            if (!int.TryParse(ServerPortTextBox.Text, out int port) || port <= 0 || port > 65535)
            {
                throw new Exception("Некорректный порт сервера");
            }

            if (matrix == null || vector == null)
            {
                throw new Exception("Матрица не загружена");
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public void UpdateResults(string metrics, double[] distributedSolution, double[] sequentialSolution)
    {
        // Установка новых результатов
        MetricsTextBox.AppendText(metrics);
        currentDistributedSolution = distributedSolution;
        currentSequentialSolution = sequentialSolution;

        if (trueSolution != null)
        {
            double maxDiff = 0;
            double avgDiff = 0;
            for (int i = 0; i < distributedSolution.Length; i++)
            {
                double diff = Math.Abs(distributedSolution[i] - trueSolution[i]);
                maxDiff = Math.Max(maxDiff, diff);
                avgDiff += diff;
            }
            avgDiff /= distributedSolution.Length;

            MetricsTextBox.AppendText("\n\nСравнение с эталонным решением:");
            MetricsTextBox.AppendText($"\nМаксимальное отклонение: {maxDiff:E6}");
            MetricsTextBox.AppendText($"\nСреднее отклонение: {avgDiff:E6}");
        }

        // Обновление отображения решения
        StartSolutionIndexTextBox.Text = "1";
        EndSolutionIndexTextBox.Text = Math.Min(DEFAULT_DISPLAY_SIZE, distributedSolution.Length).ToString();
        UpdateSolutionGrid();
    }
}