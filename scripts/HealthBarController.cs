#nullable enable
using Godot;

/// <summary>Draws the player's health as twenty red segments at the top-left of the screen.</summary>
public partial class HealthBarController : Control
{
	[Export] public int SegmentCount { get; set; } = 20;

	private PlayerController? _player;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Ignore;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		QueueRedraw();
	}

	public override void _Draw()
	{
		const float border = 2.0f;
		float width = Size.X;
		float height = Size.Y;
		int filledSegments = _player == null || _player.MaxHealth <= 0.0f
			? SegmentCount
			: Mathf.Clamp(Mathf.CeilToInt(_player.Health), 0, SegmentCount);
		const float dividerWidth = 1.0f;
		float segmentWidth = (width - border * 2.0f - dividerWidth * (SegmentCount - 1)) / SegmentCount;

		DrawMetalGradient(new Rect2(Vector2.Zero, Size));
		DrawRect(new Rect2(border, border, width - border * 2.0f, height - border * 2.0f), new Color("260b0b"));
		for (int segment = 0; segment < filledSegments; segment++)
		{
			float left = border + segment * (segmentWidth + dividerWidth);
			DrawShinyHealthFill(new Rect2(left, border, segmentWidth, height - border * 2.0f));
		}

		for (int i = 1; i < SegmentCount; i++)
		{
			float x = border + i * segmentWidth + (i - 1) * dividerWidth;
			DrawMetallicDivider(x, height);
		}
	}

	private void DrawShinyHealthFill(Rect2 fillRect)
	{
		if (fillRect.Size.X <= 0.0f || fillRect.Size.Y <= 0.0f)
			return;

		int rows = Mathf.Max(1, Mathf.CeilToInt(fillRect.Size.Y));
		float rowHeight = fillRect.Size.Y / rows;
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			DrawRect(new Rect2(fillRect.Position + Vector2.Down * row * rowHeight, new Vector2(fillRect.Size.X, rowHeight + 0.1f)), GetRubyColor(t));
		}
		DrawRect(new Rect2(fillRect.Position + Vector2.Down * Mathf.Min(2.0f, fillRect.Size.Y * 0.22f), new Vector2(fillRect.Size.X, 1.0f)), new Color(1.0f, 0.88f, 0.92f, 0.82f));
	}

	private void DrawMetalGradient(Rect2 rect)
	{
		int rows = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y));
		float rowHeight = rect.Size.Y / rows;
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			float cornerInset = GetCornerInset(row, rows);
			DrawRect(new Rect2(rect.Position + new Vector2(cornerInset, row * rowHeight), new Vector2(rect.Size.X - cornerInset * 2.0f, rowHeight + 0.1f)), GetMetalColor(t));
		}
	}

	private void DrawMetallicDivider(float x, float height)
	{
		int rows = Mathf.Max(1, Mathf.CeilToInt(height));
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			DrawRect(new Rect2(x, row, 1.0f, 1.0f), GetMetalColor(t));
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

	private static Color GetRubyColor(float t)
	{
		Color topRim = new("71000e");
		Color specular = new("ffd5dd");
		Color rubyRed = new("e40036");
		Color garnetShade = new("430007");
		return t < 0.16f
			? topRim.Lerp(specular, t / 0.16f)
			: t < 0.34f
				? specular.Lerp(rubyRed, (t - 0.16f) / 0.18f)
				: rubyRed.Lerp(garnetShade, (t - 0.34f) / 0.66f);
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
