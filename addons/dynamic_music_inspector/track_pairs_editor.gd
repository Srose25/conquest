@tool
extends EditorProperty

const TrackPairScript := preload("res://scripts/DynamicMusicTrackPair.cs")

var _pairs_box := VBoxContainer.new()
var _updating := false

func _init() -> void:
	_pairs_box.add_theme_constant_override("separation", 8)
	add_child(_pairs_box)
	set_bottom_editor(_pairs_box)

func _update_property() -> void:
	_rebuild()

func _rebuild() -> void:
	_updating = true
	for child in _pairs_box.get_children():
		child.queue_free()
	var pairs: Array = get_edited_object().get(get_edited_property())
	for index in pairs.size():
		var pair: Resource = pairs[index] as Resource
		_pairs_box.add_child(_make_pair_editor(index, pair))
	_updating = false

func _make_pair_editor(index: int, pair: Resource) -> VBoxContainer:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 4)
	var title := Label.new()
	title.text = "Pair %d" % (index + 1)
	title.add_theme_font_size_override("font_size", 15)
	box.add_child(title)
	box.add_child(_make_track_row("Out of Combat (empty = Silence)", pair, "OutOfCombatTrack", index))
	box.add_child(_make_track_row("Combat", pair, "CombatTrack", index))
	var overlay_combat := CheckBox.new()
	overlay_combat.text = "Overlay Combat Track"
	overlay_combat.button_pressed = pair != null and pair.get("OverlayCombatTrack")
	overlay_combat.pressed.connect(_commit_overlay_combat.bind(overlay_combat, index))
	add_focusable(overlay_combat)
	box.add_child(overlay_combat)
	box.add_child(_make_tempo_row(pair, index))
	var boundary_fades := CheckBox.new()
	boundary_fades.text = "Fade at Track Boundaries"
	boundary_fades.button_pressed = pair != null and pair.get("FadeAtTrackBoundaries")
	boundary_fades.pressed.connect(_commit_boundary_fades.bind(boundary_fades, index))
	add_focusable(boundary_fades)
	box.add_child(boundary_fades)
	box.add_child(_make_fade_row("Fade In at Start (seconds)", pair, "FadeInAtStartSeconds", index))
	box.add_child(_make_fade_row("Fade Out at End (seconds)", pair, "FadeOutAtEndSeconds", index))
	var always_first := CheckBox.new()
	always_first.text = "Always Load First"
	always_first.button_pressed = pair != null and pair.get("AlwaysLoadFirst")
	# Commit on the button press rather than relying on the transient toggled
	# state while the Inspector rebuilds its custom property editor.
	always_first.disabled = false
	always_first.pressed.connect(_commit_always_first.bind(always_first, index))
	add_focusable(always_first)
	box.add_child(always_first)
	box.add_child(HSeparator.new())
	return box

func _make_track_row(label_text: String, pair: Resource, property_name: String, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var picker := EditorResourcePicker.new()
	picker.base_type = "AudioStream"
	picker.edited_resource = pair.get(property_name) if pair != null else null
	picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	picker.resource_changed.connect(_set_pair_track.bind(index, property_name))
	add_focusable(picker)
	row.add_child(picker)
	return row

func _make_tempo_row(pair: Resource, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var label := Label.new()
	label.text = "Tempo (BPM)"
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var tempo := SpinBox.new()
	tempo.min_value = 30.0
	tempo.max_value = 300.0
	tempo.step = 0.01
	tempo.allow_greater = false
	tempo.value = float(pair.get("TempoBpm")) if pair != null else 135.0
	tempo.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tempo.value_changed.connect(_set_pair_tempo.bind(index))
	add_focusable(tempo)
	row.add_child(tempo)
	return row

func _make_fade_row(label_text: String, pair: Resource, property_name: String, index: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(188, 0)
	row.add_child(label)
	var duration := SpinBox.new()
	duration.min_value = 0.0
	duration.max_value = 60.0
	duration.allow_greater = true
	duration.step = 0.01
	duration.value = float(pair.get(property_name)) if pair != null else 0.40
	duration.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	duration.value_changed.connect(_set_pair_fade.bind(index, property_name))
	add_focusable(duration)
	row.add_child(duration)
	return row

func _set_pair_track(resource: Resource, index: int, property_name: String) -> void:
	if not _updating:
		_set_pair_property(index, property_name, resource)

func _commit_always_first(button: CheckBox, index: int) -> void:
	if not _updating:
		_set_pair_property(index, "AlwaysLoadFirst", button.button_pressed)

func _commit_overlay_combat(button: CheckBox, index: int) -> void:
	if not _updating:
		_set_pair_property(index, "OverlayCombatTrack", button.button_pressed)

func _commit_boundary_fades(button: CheckBox, index: int) -> void:
	if not _updating:
		_set_pair_property(index, "FadeAtTrackBoundaries", button.button_pressed)

func _set_pair_tempo(value: float, index: int) -> void:
	if not _updating:
		_set_pair_property(index, "TempoBpm", value)

func _set_pair_fade(value: float, index: int, property_name: String) -> void:
	if not _updating:
		_set_pair_property(index, property_name, value)

func _set_pair_property(index: int, property_name: String, value: Variant) -> void:
	var pairs: Array = get_edited_object().get(get_edited_property()).duplicate()
	if index < 0 or index >= pairs.size():
		return
	var pair: Resource = pairs[index] as Resource
	if pair == null:
		pair = TrackPairScript.new()
		pairs[index] = pair
	pair.set(property_name, value)
	pair.emit_changed()
	# The array is a C# export. Assigning a new array value, rather than mutating
	# its current instance, makes Godot retain checkbox and resource edits.
	get_edited_object().set(get_edited_property(), pairs)
	emit_changed(get_edited_property(), pairs)
