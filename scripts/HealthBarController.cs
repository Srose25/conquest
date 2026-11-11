#nullable enable
using Godot;

/// <summary>Draws the player's health as twenty red segments at the top-left of the screen.</summary>
[Tool]
public partial class HealthBarController : Control
{
	private const float LowHealthWarningThreshold = 6.0f;
	private const float IntroFlashDuration = 0.48f;
	private const float SegmentTransitionDuration = 0.25f;
	private const float HitShakeDuration = 0.18f;
	private const float HealthBarHeight = 32.0f;
	private const float ShieldLossAnimationSeconds = 1.0f / 8.0f;
	private const float ShieldHitCollectionSeconds = 0.20f;
	[Export] public int SegmentCount { get; set; } = 20;

	private PlayerController? _player;
	private float _lowHealthFlashElapsed;
	private float _introFlashElapsed = -1.0f;
	private bool _wasInLowHealthState;
	private int _lastFilledSegments = -1;
	private int _transitionStartSegment;
	private int _transitionSegmentCount;
	private bool _repairingSegments;
	private float _segmentTransitionElapsed = -1.0f;
	private float _healthHitShakeElapsed = -1.0f;
	private float _shieldHitShakeElapsed = -1.0f;
	private float _displayedShield = -1.0f;
	private float _shieldDisplayStart;
	private float _shieldDisplayTarget;
	private float _shieldLossAnimationElapsed = -1.0f;
	private float _lastObservedShield = -1.0f;
	private float _pendingShieldLossTarget;
	private float _shieldHitCollectionElapsed = -1.0f;
	private int _shieldHitsInCollection;
	private bool _showShieldLossFlash;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Ignore;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		// The HUD control doubles as its own layout dummy in the 2D editor. It
		// renders the same full-health, full-shield launch appearance there, while
		// its Control offsets remain directly draggable and are used at runtime.
		if (Engine.IsEditorHint())
		{
			QueueRedraw();
			return;
		}
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		bool isInLowHealthState = _player != null
			&& !_player.IsDead
			&& _player.Health > 0.0f
			&& _player.Health <= LowHealthWarningThreshold;
		if (isInLowHealthState)
		{
			if (!_wasInLowHealthState)
			{
				_lowHealthFlashElapsed = 0.0f;
				_introFlashElapsed = 0.0f;
			}
			_lowHealthFlashElapsed += (float)delta;
			if (_introFlashElapsed >= 0.0f)
			{
				_introFlashElapsed += (float)delta;
				if (_introFlashElapsed >= IntroFlashDuration)
					_introFlashElapsed = -1.0f;
			}
		}
		else
		{
			_lowHealthFlashElapsed = 0.0f;
			_introFlashElapsed = -1.0f;
		}
		UpdateSegmentTransition();
		UpdateShieldDisplay((float)delta);
		_wasInLowHealthState = isInLowHealthState;
		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawSetTransform(GetHealthHitShakeOffset());
		const float border = 2.0f;
		float width = Size.X;
		float height = Mathf.Min(HealthBarHeight, Size.Y);
		int filledSegments = _player == null || _player.MaxHealth <= 0.0f
			? SegmentCount
			: Mathf.Clamp(Mathf.CeilToInt(_player.Health), 0, SegmentCount);
		const float dividerWidth = 1.0f;
		float segmentWidth = (width - border * 2.0f - dividerWidth * (SegmentCount - 1)) / SegmentCount;

		DrawMetalGradient(new Rect2(Vector2.Zero, new Vector2(width, height)));
		DrawEmptySteelFill(
			new Rect2(border, border, width - border * 2.0f, height - border * 2.0f),
			GetLowHealthFlashStrength(),
			GetIntroFlashStrength());
		for (int segment = 0; segment < filledSegments; segment++)
		{
			if (IsIncomingTransitionSegment(segment))
				continue;
			float left = border + segment * (segmentWidth + dividerWidth);
			DrawShinyHealthFill(new Rect2(left, border, segmentWidth, height - border * 2.0f));
		}
		DrawSegmentTransition(border, height, segmentWidth, dividerWidth);

		for (int i = 1; i < SegmentCount; i++)
		{
			float x = border + i * segmentWidth + (i - 1) * dividerWidth;
			DrawMetallicDivider(x, height);
		}
		DrawSetTransform(Vector2.Zero);
		DrawSetTransform(GetShieldHitShakeOffset());
		DrawPlayerShieldBar(width, height);
		DrawSetTransform(Vector2.Zero);
	}

	private void DrawPlayerShieldBar(float width, float healthBarHeight)
	{
		float maxShield = _player?.MaxShield ?? 20.0f;
		if (maxShield <= 0.0f)
			return;
		float currentShield = _player?.Shield ?? maxShield;

		float height = healthBarHeight * 0.25f;
		Rect2 outerRect = new(0.0f, healthBarHeight + 2.0f, width, height);
		// Layered capsules make the bar read as an illuminated glass tube: a soft
		// cyan halo, a bright rim, then a dark transparent-looking interior.
		DrawCapsule(outerRect.Grow(2.0f), new Color(0.0f, 0.80f, 1.0f, 0.12f));
		DrawCapsule(outerRect.Grow(1.0f), new Color(0.0f, 0.93f, 1.0f, 0.34f));
		DrawCapsule(outerRect, new Color(0.12f, 0.95f, 1.0f, 1.0f));

		Rect2 innerRect = outerRect.GrowIndividual(-1.0f, -1.0f, -1.0f, -1.0f);
		DrawCapsule(innerRect, new Color(0.0f, 0.08f, 0.13f, 0.94f));
		float displayedShield = _displayedShield >= 0.0f ? _displayedShield : currentShield;
		float fraction = Mathf.Clamp(displayedShield / maxShield, 0.0f, 1.0f);
		float fillWidth = innerRect.Size.X * fraction;
		if (fillWidth <= 0.0f)
			return;

		DrawShieldGradientFill(innerRect, fillWidth);
		DrawShieldLossFlash(innerRect);
		float highlightWidth = Mathf.Max(0.0f, fillWidth - innerRect.Size.Y);
		if (highlightWidth > 0.0f)
		{
			float radius = innerRect.Size.Y * 0.5f;
			DrawLine(
				innerRect.Position + new Vector2(radius, 1.1f),
				innerRect.Position + new Vector2(radius + highlightWidth, 1.1f),
				new Color(0.78f, 1.0f, 1.0f, 0.82f),
				0.65f,
				true);
		}
	}

	private void DrawShieldLossFlash(Rect2 rect)
	{
		if (!_showShieldLossFlash
			|| (_shieldHitCollectionElapsed < 0.0f && _shieldLossAnimationElapsed < 0.0f)
			|| _displayedShield <= _shieldDisplayTarget)
			return;

		float startX = rect.Size.X * Mathf.Clamp(_shieldDisplayTarget / Mathf.Max(1.0f, _player!.MaxShield), 0.0f, 1.0f);
		float endX = rect.Size.X * Mathf.Clamp(_displayedShield / Mathf.Max(1.0f, _player.MaxShield), 0.0f, 1.0f);
		if (endX <= startX)
			return;

		int rows = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y));
		float rowHeight = rect.Size.Y / rows;
		float radius = rect.Size.Y * 0.5f;
		bool reachesTubeEnd = endX >= rect.Size.X - 0.01f;
		for (int row = 0; row < rows; row++)
		{
			float y = (row + 0.5f) * rowHeight;
			float offset = y - radius;
			float inset = radius - Mathf.Sqrt(Mathf.Max(0.0f, radius * radius - offset * offset));
			float left = Mathf.Max(startX, inset);
			float right = reachesTubeEnd ? rect.Size.X - inset : endX;
			if (right <= left)
				continue;
			Color whiteFlash = new(0.88f, 1.0f, 1.0f, 0.94f);
			DrawRect(new Rect2(rect.Position + new Vector2(left, row * rowHeight), new Vector2(right - left, rowHeight + 0.1f)), whiteFlash);
		}
	}

	private void UpdateShieldDisplay(float delta)
	{
		if (_player == null)
			return;

		float actualShield = _player.Shield;
		if (_displayedShield < 0.0f)
		{
			_displayedShield = actualShield;
			_shieldDisplayTarget = actualShield;
			_lastObservedShield = actualShield;
			return;
		}

		if (actualShield < _lastObservedShield - 0.001f)
		{
			// Shield impacts use the same feedback as direct hull damage. Resetting
			// the timer on each hit keeps a heavy burst visibly punchy rather than
			// allowing its shake to expire midway through the burst.
			_shieldHitShakeElapsed = 0.0f;
			if (_shieldHitCollectionElapsed < 0.0f)
			{
				_shieldHitsInCollection = 0;
				_shieldDisplayStart = _displayedShield;
				_shieldLossAnimationElapsed = 0.0f;
				_showShieldLossFlash = true;
			}
			_shieldHitsInCollection++;
			_pendingShieldLossTarget = actualShield;
			_shieldDisplayTarget = actualShield;
			_shieldHitCollectionElapsed = ShieldHitCollectionSeconds;
		}
		else if (actualShield > _lastObservedShield + 0.001f)
		{
			// Regeneration already advances gradually over five seconds, so it can
			// be shown directly without adding another artificial delay.
			_displayedShield = actualShield;
			_shieldDisplayTarget = actualShield;
			_shieldLossAnimationElapsed = -1.0f;
			_shieldHitCollectionElapsed = -1.0f;
			_showShieldLossFlash = false;
		}
		_lastObservedShield = actualShield;

		if (_shieldHitCollectionElapsed >= 0.0f)
		{
			_shieldHitCollectionElapsed -= delta;
			if (_shieldHitCollectionElapsed <= 0.0f)
			{
				_shieldDisplayTarget = _pendingShieldLossTarget;
				_shieldLossAnimationElapsed = 0.0f;
				_shieldHitCollectionElapsed = -1.0f;
			}
		}

		if (_shieldHitCollectionElapsed >= 0.0f || _shieldLossAnimationElapsed < 0.0f)
			return;
		_shieldLossAnimationElapsed += delta;
		float progress = Mathf.Clamp(_shieldLossAnimationElapsed / ShieldLossAnimationSeconds, 0.0f, 1.0f);
		_displayedShield = Mathf.Lerp(_shieldDisplayStart, _shieldDisplayTarget, Mathf.Ease(progress, -1.4f));
		if (progress >= 1.0f)
		{
			_shieldLossAnimationElapsed = -1.0f;
			_showShieldLossFlash = false;
		}
	}

	private void DrawCapsule(Rect2 rect, Color color)
	{
		float radius = rect.Size.Y * 0.5f;
		Vector2 leftCenter = rect.Position + new Vector2(radius, radius);
		Vector2 rightCenter = rect.Position + new Vector2(rect.Size.X - radius, radius);
		DrawCircle(leftCenter, radius, color);
		DrawCircle(rightCenter, radius, color);
		DrawRect(new Rect2(rect.Position + Vector2.Right * radius, new Vector2(Mathf.Max(0.0f, rect.Size.X - radius * 2.0f), rect.Size.Y)), color);
	}

	private void DrawCapsuleFill(Rect2 rect, float fillWidth, Color color)
	{
		float radius = rect.Size.Y * 0.5f;
		if (fillWidth <= rect.Size.Y)
		{
			DrawCircle(rect.Position + new Vector2(fillWidth * 0.5f, radius), fillWidth * 0.5f, color);
			return;
		}

		Vector2 leftCenter = rect.Position + new Vector2(radius, radius);
		DrawCircle(leftCenter, radius, color);
		if (fillWidth >= rect.Size.X - 0.01f)
		{
			Vector2 rightCenter = rect.Position + new Vector2(rect.Size.X - radius, radius);
			DrawCircle(rightCenter, radius, color);
			DrawRect(new Rect2(rect.Position + Vector2.Right * radius, new Vector2(rect.Size.X - radius * 2.0f, rect.Size.Y)), color);
		}
		else
		{
			// Partial shield levels keep a clean, flat fill boundary; only the tube
			// itself owns the rounded outer end caps.
			DrawRect(new Rect2(rect.Position + Vector2.Right * radius, new Vector2(fillWidth - radius, rect.Size.Y)), color);
		}
	}

	private void DrawShieldGradientFill(Rect2 rect, float fillWidth)
	{
		int rows = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y));
		float rowHeight = rect.Size.Y / rows;
		float radius = rect.Size.Y * 0.5f;
		bool fullyFilled = fillWidth >= rect.Size.X - 0.01f;
		Color topCyan = new(0.20f, 1.0f, 1.0f, 1.0f);
		Color bottomBlue = new(0.05f, 0.30f, 0.92f, 1.0f);
		for (int row = 0; row < rows; row++)
		{
			float y = (row + 0.5f) * rowHeight;
			float verticalOffset = y - radius;
			float circleInset = radius - Mathf.Sqrt(Mathf.Max(0.0f, radius * radius - verticalOffset * verticalOffset));
			float left = circleInset;
			float right;
			if (fillWidth <= rect.Size.Y)
			{
				float partialRadius = fillWidth * 0.5f;
				float partialOffset = y - radius;
				float halfWidth = Mathf.Sqrt(Mathf.Max(0.0f, partialRadius * partialRadius - partialOffset * partialOffset));
				left = partialRadius - halfWidth;
				right = partialRadius + halfWidth;
			}
			else
			{
				right = fullyFilled ? rect.Size.X - circleInset : fillWidth;
			}
			if (right <= left)
				continue;
			float t = (row + 0.5f) / rows;
			Color rowColor = topCyan.Lerp(bottomBlue, t);
			DrawRect(new Rect2(rect.Position + new Vector2(left, row * rowHeight), new Vector2(right - left, rowHeight + 0.1f)), rowColor);
		}
	}

	private void UpdateSegmentTransition()
	{
		float step = (float)GetProcessDeltaTime();
		AdvanceHitShake(ref _healthHitShakeElapsed, step);
		AdvanceHitShake(ref _shieldHitShakeElapsed, step);

		int filledSegments = _player == null || _player.MaxHealth <= 0.0f
			? SegmentCount
			: Mathf.Clamp(Mathf.CeilToInt(_player.Health), 0, SegmentCount);
		if (_player?.IsDead == true)
		{
			_lastFilledSegments = filledSegments;
			_segmentTransitionElapsed = -1.0f;
			_healthHitShakeElapsed = -1.0f;
			_shieldHitShakeElapsed = -1.0f;
			return;
		}
		if (_lastFilledSegments < 0)
		{
			_lastFilledSegments = filledSegments;
			return;
		}

		if (filledSegments != _lastFilledSegments)
		{
			if (filledSegments < _lastFilledSegments)
				_healthHitShakeElapsed = 0.0f;
			_repairingSegments = filledSegments > _lastFilledSegments;
			_transitionStartSegment = Mathf.Min(filledSegments, _lastFilledSegments);
			_transitionSegmentCount = Mathf.Abs(filledSegments - _lastFilledSegments);
			_segmentTransitionElapsed = 0.0f;
			_lastFilledSegments = filledSegments;
		}
		else if (_segmentTransitionElapsed >= 0.0f)
		{
			_segmentTransitionElapsed += step;
			if (_segmentTransitionElapsed >= SegmentTransitionDuration)
				_segmentTransitionElapsed = -1.0f;
		}
	}

	private void AdvanceHitShake(ref float elapsed, float delta)
	{
		if (elapsed < 0.0f)
			return;

		elapsed += delta;
		if (elapsed >= HitShakeDuration)
			elapsed = -1.0f;
	}

	private Vector2 GetHealthHitShakeOffset()
	{
		return GetHitShakeOffset(_healthHitShakeElapsed);
	}

	private Vector2 GetShieldHitShakeOffset()
	{
		return GetHitShakeOffset(_shieldHitShakeElapsed);
	}

	private static Vector2 GetHitShakeOffset(float elapsed)
	{
		if (elapsed < 0.0f)
			return Vector2.Zero;

		float progress = elapsed / HitShakeDuration;
		float strength = (1.0f - progress) * 2.0f;
		return new Vector2(
			Mathf.Sin(elapsed * 170.0f) * strength,
			Mathf.Cos(elapsed * 223.0f) * strength * 0.65f);
	}

	private bool IsIncomingTransitionSegment(int segment)
	{
		return _segmentTransitionElapsed >= 0.0f
			&& _repairingSegments
			&& segment >= _transitionStartSegment
			&& segment < _transitionStartSegment + _transitionSegmentCount;
	}

	private void DrawSegmentTransition(float border, float height, float segmentWidth, float dividerWidth)
	{
		if (_segmentTransitionElapsed < 0.0f || _transitionSegmentCount <= 0)
			return;

		float progress = Mathf.Clamp(_segmentTransitionElapsed / SegmentTransitionDuration, 0.0f, 1.0f);
		float heightFactor;
		Color transitionColor;
		float transitionStrength;
		if (_repairingSegments)
		{
			heightFactor = Mathf.Ease(progress, -1.8f);
			transitionColor = new Color("26ffff");
			transitionStrength = 1.0f - Mathf.Ease(progress, 1.8f);
		}
		else
		{
			// The damage cue reaches white first, then the cell collapses vertically.
			const float fadeToWhiteEnd = 0.30f;
			transitionColor = Colors.White;
			transitionStrength = Mathf.Clamp(progress / fadeToWhiteEnd, 0.0f, 1.0f);
			float shrinkProgress = Mathf.Clamp((progress - fadeToWhiteEnd) / (1.0f - fadeToWhiteEnd), 0.0f, 1.0f);
			heightFactor = 1.0f - Mathf.Ease(shrinkProgress, -1.8f);
		}

		float availableHeight = height - border * 2.0f;
		for (int index = 0; index < _transitionSegmentCount; index++)
		{
			int segment = _transitionStartSegment + index;
			float animatedHeight = availableHeight * heightFactor;
			if (animatedHeight <= 0.0f)
				continue;
			float left = border + segment * (segmentWidth + dividerWidth);
			float top = border + (availableHeight - animatedHeight) * 0.5f;
			DrawAnimatedHealthFill(new Rect2(left, top, segmentWidth, animatedHeight), transitionColor, transitionStrength);
		}
	}

	private void DrawEmptySteelFill(Rect2 fillRect, float warningFlashStrength, float introFlashStrength)
	{
		if (fillRect.Size.X <= 0.0f || fillRect.Size.Y <= 0.0f)
			return;

		int rows = Mathf.Max(1, Mathf.CeilToInt(fillRect.Size.Y));
		float rowHeight = fillRect.Size.Y / rows;
		Color warningRed = new("ff1624");
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			Color darkSteel = GetMetalColor(t) * new Color(0.17f, 0.055f, 0.065f, 1.0f);
			Color fillColor = darkSteel.Lerp(Colors.White, introFlashStrength).Lerp(warningRed, warningFlashStrength);
			DrawRect(new Rect2(fillRect.Position + Vector2.Down * row * rowHeight, new Vector2(fillRect.Size.X, rowHeight + 0.1f)), fillColor);
		}
	}

	private float GetLowHealthFlashStrength()
	{
		if (_introFlashElapsed >= 0.0f
			|| _player == null
			|| _player.IsDead
			|| _player.Health <= 0.0f
			|| _player.Health > LowHealthWarningThreshold)
			return 0.0f;

		// Six HP begins as a slow, restrained warning. One HP reaches a fast,
		// conspicuous pulse, while death immediately returns to the dark steel fill.
		float urgency = Mathf.Clamp((LowHealthWarningThreshold - _player.Health) / (LowHealthWarningThreshold - 1.0f), 0.0f, 1.0f);
		float frequency = Mathf.Lerp(1.1f, 7.0f, urgency);
		float pulse = (Mathf.Sin(_lowHealthFlashElapsed * Mathf.Tau * frequency) + 1.0f) * 0.5f;
		return Mathf.Lerp(0.08f, 0.92f, pulse);
	}

	private float GetIntroFlashStrength()
	{
		if (_introFlashElapsed < 0.0f || _player == null || _player.IsDead)
			return 0.0f;

		float progress = _introFlashElapsed / IntroFlashDuration;
		// Three short bright pulses acknowledge entry into the danger state before
		// the recurring red warning begins.
		return Mathf.Pow(Mathf.Abs(Mathf.Sin(progress * Mathf.Pi * 3.0f)), 2.0f);
	}

	private void DrawShinyHealthFill(Rect2 fillRect)
	{
		DrawAnimatedHealthFill(fillRect, Colors.White, 0.0f);
	}

	private void DrawAnimatedHealthFill(Rect2 fillRect, Color transitionColor, float transitionStrength)
	{
		if (fillRect.Size.X <= 0.0f || fillRect.Size.Y <= 0.0f)
			return;

		int rows = Mathf.Max(1, Mathf.CeilToInt(fillRect.Size.Y));
		float rowHeight = fillRect.Size.Y / rows;
		for (int row = 0; row < rows; row++)
		{
			float t = (row + 0.5f) / rows;
			Color fillColor = GetRubyColor(t).Lerp(transitionColor, transitionStrength);
			DrawRect(new Rect2(fillRect.Position + Vector2.Down * row * rowHeight, new Vector2(fillRect.Size.X, rowHeight + 0.1f)), fillColor);
		}
		Color shine = new(1.0f, 0.88f, 0.92f, 0.82f);
		DrawRect(new Rect2(fillRect.Position + Vector2.Down * Mathf.Min(2.0f, fillRect.Size.Y * 0.22f), new Vector2(fillRect.Size.X, 1.0f)), shine.Lerp(transitionColor, transitionStrength));
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
