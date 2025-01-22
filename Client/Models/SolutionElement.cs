namespace Client.Models;

public class SolutionElement
{
    public int Index { get; set; }
    public double? SequentialSolution { get; set; }  
    public double? DistributedSolution { get; set; } 
    public double? TrueSolution { get; set; }        
}