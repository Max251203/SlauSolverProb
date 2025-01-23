namespace Shared.Solvers;

public class GaussSolver
{
    public double[] Solve(double[,] matrix, double[] vector)
    {
        // Проверка входных параметров на null
        if (matrix == null)
            throw new ArgumentNullException(nameof(matrix), "Matrix cannot be null");
        if (vector == null)
            throw new ArgumentNullException(nameof(vector), "Vector cannot be null");

        // Проверка размерности матрицы
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        int n = vector.Length;

        // Проверка, что матрица квадратная
        if (rows != cols)
            throw new ArgumentException("Matrix must be square", nameof(matrix));

        // Проверка соответствия размерностей матрицы и вектора
        if (rows != n)
            throw new ArgumentException("Vector length must match matrix dimensions", nameof(vector));

        double[,] augmentedMatrix = Utils.MatrixUtils.CreateAugmentedMatrix(matrix, vector);

        // Прямой ход
        for (int i = 0; i < n; i++)
        {
            // Выбор главного элемента
            int maxRow = i;
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(augmentedMatrix[k, i]) > Math.Abs(augmentedMatrix[maxRow, i]))
                    maxRow = k;
            }

            // Проверка на вырожденность матрицы
            if (Math.Abs(augmentedMatrix[maxRow, i]) < 1e-10)
                throw new InvalidOperationException("Matrix is singular or poorly conditioned");

            // Перестановка строк
            if (maxRow != i)
            {
                for (int j = i; j <= n; j++)
                {
                    (augmentedMatrix[i, j], augmentedMatrix[maxRow, j]) =
                        (augmentedMatrix[maxRow, j], augmentedMatrix[i, j]);
                }
            }

            // Приведение к треугольному виду
            for (int k = i + 1; k < n; k++)
            {
                try
                {
                    double factor = augmentedMatrix[k, i] / augmentedMatrix[i, i];
                    for (int j = i; j <= n; j++)
                    {
                        augmentedMatrix[k, j] -= factor * augmentedMatrix[i, j];
                        // Обработка очень малых значений
                        if (Math.Abs(augmentedMatrix[k, j]) < 1e-15)
                            augmentedMatrix[k, j] = 0;
                    }
                }
                catch (DivideByZeroException)
                {
                    throw new InvalidOperationException("Division by zero encountered during elimination");
                }
            }
        }

        // Обратный ход
        double[] solution = new double[n];
        try
        {
            for (int i = n - 1; i >= 0; i--)
            {
                solution[i] = augmentedMatrix[i, n];
                for (int j = i + 1; j < n; j++)
                    solution[i] -= augmentedMatrix[i, j] * solution[j];

                if (Math.Abs(augmentedMatrix[i, i]) < 1e-10)
                    throw new InvalidOperationException("Zero encountered on diagonal during back-substitution");

                solution[i] /= augmentedMatrix[i, i];

                // Проверка на переполнение
                if (double.IsInfinity(solution[i]) || double.IsNaN(solution[i]))
                    throw new OverflowException("Numerical overflow in solution");
            }
        }
        catch (DivideByZeroException)
        {
            throw new InvalidOperationException("Division by zero encountered during back-substitution");
        }

        return solution;
    }
} 