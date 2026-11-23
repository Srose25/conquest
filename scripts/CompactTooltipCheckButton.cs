#nullable enable
using Godot;

/// <summary>
/// A settings toggle whose tooltip uses the same compact typography as the
/// toggle itself, rather than inheriting the larger settings-panel theme size.
/// </summary>
public partial class CompactTooltipCheckButton : CheckButton
{
	public override Control _MakeCustomTooltip(string forText)
	{
		PanelContainer panel = new();
		StyleBoxFlat background = new()
		{
			BgColor = new Color(0.035f, 0.045f, 0.065f, 0.96f),
			BorderColor = new Color(0.28f, 0.72f, 0.82f, 0.72f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomRight = 3,
			CornerRadiusBottomLeft = 3,
		};
		background.SetContentMargin(Side.Left, 8.0f);
		background.SetContentMargin(Side.Top, 5.0f);
		background.SetContentMargin(Side.Right, 8.0f);
		background.SetContentMargin(Side.Bottom, 5.0f);
		panel.AddThemeStyleboxOverride("panel", background);

		Label label = new()
		{
			Text = forText,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(290.0f, 0.0f),
		};
		label.AddThemeFontOverride("font", GetThemeFont("font"));
		label.AddThemeFontSizeOverride("font_size", GetThemeFontSize("font_size"));
		panel.AddChild(label);
		return panel;
	}
}
