using System.Drawing;
using System.Windows.Forms;

namespace BIMlify.ModelCleaner;

public sealed class LightGoldComboBox : ComboBox
{
	public override Color BackColor
	{
		get
		{
			return base.BackColor;
		}
		set
		{
			base.BackColor = Color.FromArgb(255, 244, 216);
		}
	}
}
