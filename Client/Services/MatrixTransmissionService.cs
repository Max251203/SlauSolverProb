using Shared.Network;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client.Services;

public class MatrixTransmissionService
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _serverEndPoint;
    private readonly double[,] _matrix;
    private readonly double[] _vector;

    public MatrixTransmissionService(UdpClient udpClient, IPEndPoint serverEndPoint, double[,] matrix, double[] vector)
    {
        _udpClient = udpClient;
        _serverEndPoint = serverEndPoint;
        _matrix = matrix;
        _vector = vector;
    }

    public async Task SendMatrix(int rows, int cols)
    {
        await SendInitialData(rows, cols);
        var dataChunks = UdpHelper.PrepareDataChunks(_matrix, _vector);
        await SendChunksCount(dataChunks.Count);
        await SendAllChunks(dataChunks);
    }

    private async Task SendInitialData(int rows, int cols)
    {
        var initData = $"INIT|{rows}|{cols}";
        var initBytes = Encoding.UTF8.GetBytes(initData);
        await _udpClient.SendAsync(initBytes, initBytes.Length, _serverEndPoint);
        await Task.Delay(100);
    }

    private async Task SendChunksCount(int count)
    {
        var countData = $"COUNT|{count}";
        var countBytes = Encoding.UTF8.GetBytes(countData);
        await _udpClient.SendAsync(countBytes, countBytes.Length, _serverEndPoint);
        await Task.Delay(100);
    }

    private async Task SendAllChunks(List<string> chunks)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkData = $"CHUNK|{i}|{chunks[i]}";
            var chunkBytes = Encoding.UTF8.GetBytes(chunkData);
            await _udpClient.SendAsync(chunkBytes, chunkBytes.Length, _serverEndPoint);
            await Task.Delay(50);
            Console.WriteLine($"Отправлена часть {i + 1} из {chunks.Count}");
        }
    }
}