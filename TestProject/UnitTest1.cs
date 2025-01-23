using Node.Services;
using Shared.Models;
using Shared.Network;
using Shared.Solvers;
using System.Net.Sockets;
using System.Text.Json;

namespace SlauSolverProb.TestProject
{
    public class GaussSolverTests
    {
        private readonly GaussSolver _solver = new GaussSolver();

        [Fact]
        public void Solve_Simple2x2System_ReturnsCorrectSolution()
        {
            var matrix = new double[,] { { 2, 1 }, { 1, 3 } };
            var vector = new double[] { 5, 10 };
            var expected = new double[] { 1, 3 };

            var result = _solver.Solve(matrix, vector);

            Assert.Equal(expected, result); // Исправлено: сравнение массивов
        }
        

        [Fact]
        public void Solve_ZeroVector_ReturnsZeroSolution()
        {
            var matrix = new double[,] { { 1, 0 }, { 0, 1 } };
            var vector = new double[] { 0, 0 };
            var expected = new double[] { 0, 0 };

            var result = _solver.Solve(matrix, vector);

            Assert.Equal(expected, result); // Исправлено: сравнение массивов
        }

        [Fact]
        public void Solve_LargeSystem_ReturnsCorrectSolution()
        {
            var matrix = new double[100, 100];
            var vector = new double[100];
            var expected = new double[100];

            for (int i = 0; i < 100; i++)
            {
                matrix[i, i] = 1;
                vector[i] = i + 1;
                expected[i] = i + 1;
            }

            var result = _solver.Solve(matrix, vector);

            Assert.Equal(expected, result); // Исправлено: сравнение массивов
        }
    }

    

    public class TransmissionRetryPolicyTests
    {
        [Fact]
        public async Task ExecuteWithRetryAsync_SuccessOnFirstAttempt()
        {
            var policy = new TransmissionRetryPolicy();
            int attempts = 0;

            await policy.ExecuteWithRetryAsync(async () =>
            {
                attempts++;
                await Task.CompletedTask;
            });

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_FailsAfterMaxRetries()
        {
            var policy = new TransmissionRetryPolicy(maxRetries: 2);
            int attempts = 0;

            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await policy.ExecuteWithRetryAsync(async () =>
                {
                    attempts++;
                    throw new Exception("Test exception");
                });
            });

            Assert.Equal(2, attempts);
        }
    }

    public class BlockResultAssemblerTests
    {
        [Fact]
        public void TryAddChunk_AddsChunkCorrectly()
        {
            var assembler = new BlockResultAssembler();
            var chunk = new NetworkMessages.BlockResultChunk
            {
                BlockRow = 0,
                BlockCol = 0,
                ChunkId = 0,
                TotalChunks = 1,
                Data = "test"
            };

            bool result = assembler.TryAddChunk(chunk);

            Assert.True(result);
        }
    }

    public class MatrixBlockTests
    {
        [Fact]
        public void FromMatrix_CreatesCorrectBlock()
        {
            var matrix = new double[,] { { 1, 2 }, { 3, 4 } };
            var block = MatrixBlock.FromMatrix(matrix);

            Assert.Equal(2, block.Rows);
            Assert.Equal(2, block.Cols);
            Assert.Equal(new double[] { 1, 2, 3, 4 }, block.Data);
        }

        [Fact]
        public void ToMatrix_ConvertsBlockCorrectly()
        {
            var block = new MatrixBlock
            {
                Data = new double[] { 1, 2, 3, 4 },
                Rows = 2,
                Cols = 2
            };

            var matrix = block.ToMatrix();

            Assert.Equal(1, matrix[0, 0]);
            Assert.Equal(4, matrix[1, 1]);
        }
    }

    public class NetworkMessagesTests
    {
        [Fact]
        public void BlockTask_Serialization_WorksCorrectly()
        {
            var task = new NetworkMessages.BlockTask
            {
                BlockRow = 0,
                BlockCol = 0,
                Matrix = MatrixBlock.FromMatrix(new double[,] { { 1, 2 }, { 3, 4 } }),
                Vector = new double[] { 5, 6 }
            };

            var json = JsonSerializer.Serialize(task);
            var deserializedTask = JsonSerializer.Deserialize<NetworkMessages.BlockTask>(json);

            Assert.Equal(task.BlockRow, deserializedTask.BlockRow);
            Assert.Equal(task.BlockCol, deserializedTask.BlockCol);
            Assert.Equal(task.Vector, deserializedTask.Vector);
        }

        [Fact]
        public void BlockResultChunk_Serialization_WorksCorrectly()
        {
            var chunk = new NetworkMessages.BlockResultChunk
            {
                BlockRow = 0,
                BlockCol = 0,
                ChunkId = 0,
                TotalChunks = 1,
                Data = "test"
            };

            var json = JsonSerializer.Serialize(chunk);
            var deserializedChunk = JsonSerializer.Deserialize<NetworkMessages.BlockResultChunk>(json);

            Assert.Equal(chunk.BlockRow, deserializedChunk.BlockRow);
            Assert.Equal(chunk.BlockCol, deserializedChunk.BlockCol);
            Assert.Equal(chunk.Data, deserializedChunk.Data);
        }
    }

    public class AdditionalBlockMatrixTests
    {
        [Fact]
        public void GetBlock_InvalidBlock_ThrowsException()
        {
            var matrix = new double[2, 2];
            var blockMatrix = new BlockMatrix(matrix, 2);

            Assert.Throws<ArgumentException>(() => blockMatrix.GetBlock(2, 2));
        }

        [Fact]
        public void SetBlock_InvalidBlock_ThrowsException()
        {
            var matrix = new double[2, 2];
            var blockMatrix = new BlockMatrix(matrix, 2);
            var block = new double[3, 3]; // Некорректный размер

            Assert.Throws<IndexOutOfRangeException>(() => blockMatrix.SetBlock(block, 0, 0));
        }
    }

    public class AdditionalGaussSolverTests
    {
        [Fact]
        public void Solve_NonSquareMatrix_ThrowsException()
        {
            var solver = new GaussSolver();
            var matrix = new double[2, 3];
            var vector = new double[2];

            Assert.Throws<ArgumentException>(() => solver.Solve(matrix, vector));
        }

        [Fact]
        public void Solve_VectorLengthMismatch_ThrowsException()
        {
            var solver = new GaussSolver();
            var matrix = new double[2, 2];
            var vector = new double[3];

            Assert.Throws<ArgumentException>(() => solver.Solve(matrix, vector));
        }

        [Fact]
        public void Solve_NullMatrix_ThrowsArgumentNullException()
        {
            var solver = new GaussSolver();
            double[,] matrix = null;
            var vector = new double[2];

            Assert.Throws<ArgumentNullException>(() => solver.Solve(matrix, vector));
        }

        [Fact]
        public void Solve_NullVector_ThrowsArgumentNullException()
        {
            var solver = new GaussSolver();
            var matrix = new double[2, 2];
            double[] vector = null;

            Assert.Throws<ArgumentNullException>(() => solver.Solve(matrix, vector));
        }

        [Fact]
        public void Solve_SingularMatrix_ThrowsInvalidOperationException()
        {
            var solver = new GaussSolver();
            var matrix = new double[,] {
            { 1, 1 },
            { 1, 1 }
        };
            var vector = new double[] { 1, 1 };

            Assert.Throws<InvalidOperationException>(() => solver.Solve(matrix, vector));
        }
    }
}