namespace Client.Models;

public class MatrixRow
{
    public int RowNumber { get; set; }
    public List<double> Values { get; set; }
    public double VectorValue { get; set; }

    public MatrixRow(int rowNumber, int columnsCount)
    {
        RowNumber = rowNumber;
        Values = new List<double>(columnsCount);
    }
}