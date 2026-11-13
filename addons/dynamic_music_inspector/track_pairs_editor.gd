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

func _set_pair_track(resource: Resource, index: int, property_name: String) -> void:
	if not _updating:
		_set_pair_property(index, property_name, resource)

func _commit_always_first(button: CheckBox, index: int) -> void:
	if not _updating:
		_set_pair_property(index, "AlwaysLoadFirst", button.button_pressed)

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
