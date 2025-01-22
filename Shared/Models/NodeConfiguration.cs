namespace Shared.Models;

public class NodeConfiguration
{
    public List<NodeInfo> Nodes { get; set; }
}

public class NodeInfo
{
    public int NodeId { get; set; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
}