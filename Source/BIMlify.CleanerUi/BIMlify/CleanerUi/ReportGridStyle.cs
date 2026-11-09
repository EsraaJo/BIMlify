using System.Drawing;
using System.Windows.Forms;

namespace BIMlify.CleanerUi
{
    public static class ReportGridStyle
    {
        private static readonly Font Body = new Font("Segoe UI", 7.8f);
        private static readonly Font Header = new Font("Segoe UI", 8.2f, FontStyle.Bold);
        private static readonly Color Ink = Color.FromArgb(55, 67, 79);

        public static void Apply(DataGridView grid)
        {
            grid.Font = Body;
            DashboardEnhancements.AttachGridEditEscape(grid);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 30;
            grid.RowTemplate.Height = 28;
            grid.GridColor = Color.FromArgb(220, 224, 229);
            grid.BackgroundColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = Header;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 245);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 240, 201);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.DefaultCellStyle.Padding = new Padding(3, 2, 3, 2);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
        }
    }
}
