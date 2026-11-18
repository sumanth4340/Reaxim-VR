// TwinBreadboard.cs — breadboard geometry from rpi-hvf for snapping.
// SOURCE: Lab/BreadboardModel.swift — top-to-bottom: [+ rail][− rail]
// rows a–e · channel · rows f–j · [− rail][+ rail]; pitch 2.54 mm,
// 63 columns, rows a–e at z 13.5…23.66 mm, rows f–j at 32.2…42.36 mm.
// Physical placement here is proportional to the placed model's own
// bounds (models are real-size); the logical net id follows the twin
// convention (letter a–j + 1-based column, e.g. "c12").
namespace Bench.Twin
{
    public static class TwinBreadboard
    {
        public const float PitchM = 0.00254f;
        public const int Columns = 63;
        public const int Rows = 10;
        public const int TopStripRows = 5;

        /// <summary>Twin logical net id, e.g. row 2 col 11 → "c12".</summary>
        public static string NetId(int row, int column) =>
            "abcdefghij"[row] + (column + 1).ToString();
    }
}
