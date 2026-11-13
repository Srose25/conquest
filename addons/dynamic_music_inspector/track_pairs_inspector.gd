@tool
extends EditorInspectorPlugin

const TrackPairsEditor := preload("res://addons/dynamic_music_inspector/track_pairs_editor.gd")

func _can_handle(object: Object) -> bool:
	# C# script instances do not consistently expose their .cs resource path to
	# editor plugins. The property-name check below keeps this plugin scoped to
	# DynamicMusicController without excluding that node before it is parsed.
	return object is Node

func _parse_property(object: Object, type: Variant.Type, name: String, hint_type: PropertyHint, hint_string: String, usage_flags: int, wide: bool) -> bool:
	# C# export names can reach editor plugins in either C# or Godot casing.
	if name != "track_pairs" and name != "TrackPairs":
		return false
	add_property_editor(name, TrackPairsEditor.new())
	return true
