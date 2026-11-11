#nullable enable
using Godot;

/// <summary>Draws the player's XP progress and current level in the HUD.</summary>
[Tool]
public partial class ExperienceBarController : Control
{
	private ProgressionManager? _progression;
	private const float BarHeight = 16.0f;
	private const float Border = 2.0f;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Ignore;
		_progression = GetTree().CurrentScene?.GetNodeOrNull<ProgressionManager>("ProgressionManager");
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		// In the editor this very same Control is the draggable visual dummy. The
		// fallback values match a brand-new run's empty level-0 XP meter.
		if (!Engine.IsEditorHint())
			_progression ??= GetTree().CurrentScene?.GetNodeOrNull<ProgressionManager>("ProgressionManager");
		QueueRedraw();
	}

	public override void _Draw()
	{
		// Keep the XP meter in the same visual family as HealthBarController:
		// metallic outer frame, inset dark fill, bright animated-color fill, and
		// thin edge highlights. The XP-specific green palette is retained here.
		float width = Size.X;
		float height = Mathf.Min(BarHeight, Size.Y);
		Font font = ThemeDB.FallbackFont;
		int level = _progression?.Level ?? 0;
		int experience = _progression?.Experience ?? 0;
		int requiredExperience = _progression?.ExperienceRequired ?? ProgressionManager.ExperienceThresholds[0];
		string title = $"LEVEL {level}";
		string value = $"XP {experience} / {requiredExperience}";
		DrawString(font, new Vector2(0, 15), title, HorizontalAlignment.Left, -1, 14, Colors.White);
		DrawString(font, new Vector2(width - 120, 15), value, HorizontalAlignment.Right, 120, 12, Colors.White);

		Rect2 bar = new(0, 22, width, height);
		DrawMetalGradient(bar);
		Rect2 innerBar = new(
			bar.Position + new Vector2(Border, Border),
			new Vector2(bar.Size.X - Border * 2.0f, bar.Size.Y - Border * 2.0f));
		DrawRect(innerBar, new Color("143110"));
		float fraction = Mathf.Clamp((float)experience / Mathf.Max(1, requiredExperience), 0.0f, 1.0f);
		if (fraction > 0.0f)
			DrawExperienceFill(innerBar, innerBar.Size.X * fraction);
		DrawLine(bar.Position, new Vector2(bar.End.X, bar.Position.Y), new Color("5dd64d"), 1.0f, true);
		DrawLine(new Vector2(bar.Position.X, bar.End.Y), bar.End, new Color("5dd64d"), 1.0f, true);
	}

	private void DrawExperienceFill(Rect2 rect, float fillWidth)
	{
		int rows = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y));
		float rowHeight = rect.Size.Y / rows;
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			Color fillColor = new Color("1ded0e").Lerp(new Color("0b7507"), t);
			DrawRect(
				new Rect2(rect.Position + Vector2.Down * row * rowHeight, new Vector2(fillWidth, rowHeight + 0.1f)),
				fillColor);
		}
		if (fillWidth > 2.0f)
			DrawRect(new Rect2(rect.Position + Vector2.Down * 2.0f, new Vector2(fillWidth, 1.0f)), new Color("9cff8d"));
	}

	private void DrawMetalGradient(Rect2 rect)
	{
		int rows = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y));
		float rowHeight = rect.Size.Y / rows;
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			float cornerInset = GetCornerInset(row, rows);
			DrawRect(
				new Rect2(rect.Position + new Vector2(cornerInset, row * rowHeight), new Vector2(rect.Size.X - cornerInset * 2.0f, rowHeight + 0.1f)),
				GetMetalColor(t));
		}
	}

	private static Color GetMetalColor(float t)
	{
		Color topEdge = new("5a6972");
		Color highlight = new("f5fdff");
		Color midTone = new("82949e");
		Color bottomEdge = new("c9d9e0");
		return t < 0.20f
			? topEdge.Lerp(highlight, t / 0.20f)
			: t < 0.62f
				? highlight.Lerp(midTone, (t - 0.20f) / 0.42f)
				: midTone.Lerp(bottomEdge, (t - 0.62f) / 0.38f);
	}

	private static float GetCornerInset(int row, int rowCount)
	{
		int distanceFromEdge = Mathf.Min(row, rowCount - 1 - row);
		return distanceFromEdge switch
		{
			0 => 3.0f,
			1 => 1.0f,
			_ => 0.0f
		};
	}
}
