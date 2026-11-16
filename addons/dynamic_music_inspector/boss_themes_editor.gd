@tool
extends EditorProperty

const BossThemeScript := preload("res://scripts/DynamicMusicBossTheme.cs")

var _themes_box := VBoxContainer.new()
var _updating := false

func _init() -> void:
	_themes_box.add_theme_constant_override("separation", 8)
	add_child(_themes_box)
	set_bottom_editor(_themes_box)

func _update_property() -> void:
	_rebuild()

func _rebuild() -> void:
	_updating = true
	for child in _themes_box.get_children():
		child.queue_free()
	var themes: Array = get_edited_object().get(get_edited_property())
	for index in themes.size():
		var theme: Resource = themes[index] as Resource
		_themes_box.add_child(_make_theme_editor(index, theme))
	_updating = false

func _make_theme_editor(index: int, theme: Resource) -> VBoxContainer:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 4)
	var title := Label.new()
	title.text = "Boss Theme %d" % (index + 1)
	title.add_theme_font_size_override("font_size", 15)
	box.add_child(title)
	box.add_child(_make_label_row(theme, index))
	box.add_child(_make_track_row(theme, index))
	box.add_child(_make_tempo_row(theme, index))
	box.add_child(HSeparator.new())
	return box

func _make_label_row(theme: Resource, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = "Boss Label"
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var input := LineEdit.new()
	input.text = String(theme.get("BossLabel")) if theme != null else "Boss Theme"
	input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	# Do not write while the user is typing: changing the exported array causes
	# Godot to rebuild this inspector editor, which would steal focus after every
	# character. Commit once on Enter or when the field loses focus instead.
	input.text_submitted.connect(_set_theme_label.bind(index))
	input.focus_exited.connect(_commit_theme_label.bind(input, index))
	add_focusable(input)
	row.add_child(input)
	return row

func _make_track_row(theme: Resource, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = "Track"
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var picker := EditorResourcePicker.new()
	picker.base_type = "AudioStream"
	picker.edited_resource = theme.get("Track") if theme != null else null
	picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	picker.resource_changed.connect(_set_theme_track.bind(index))
	add_focusable(picker)
	row.add_child(picker)
	return row

func _make_tempo_row(theme: Resource, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = "Tempo (BPM)"
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var tempo := SpinBox.new()
	tempo.min_value = 30.0
	tempo.max_value = 300.0
	tempo.step = 0.01
	tempo.value = float(theme.get("TempoBpm")) if theme != null else 135.0
	tempo.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tempo.value_changed.connect(_set_theme_tempo.bind(index))
	add_focusable(tempo)
	row.add_child(tempo)
	return row

func _set_theme_track(resource: Resource, index: int) -> void:
	if not _updating:
		_set_theme_property(index, "Track", resource)

func _set_theme_label(value: String, index: int) -> void:
	if not _updating:
		_set_theme_property(index, "BossLabel", value)

func _commit_theme_label(input: LineEdit, index: int) -> void:
	if not _updating:
		_set_theme_property(index, "BossLabel", input.text)

func _set_theme_tempo(value: float, index: int) -> void:
	if not _updating:
		_set_theme_property(index, "TempoBpm", value)

func _set_theme_property(index: int, property_name: String, value: Variant) -> void:
	var themes: Array = get_edited_object().get(get_edited_property()).duplicate()
	if index < 0 or index >= themes.size():
		return
	var theme: Resource = themes[index] as Resource
	if theme == null:
		theme = BossThemeScript.new()
		themes[index] = theme
	theme.set(property_name, value)
	theme.emit_changed()
	get_edited_object().set(get_edited_property(), themes)
	emit_changed(get_edited_property(), themes)
