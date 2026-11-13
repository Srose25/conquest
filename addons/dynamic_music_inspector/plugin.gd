@tool
extends EditorPlugin

var _inspector_plugin: EditorInspectorPlugin

func _enter_tree() -> void:
	_inspector_plugin = preload("res://addons/dynamic_music_inspector/track_pairs_inspector.gd").new()
	add_inspector_plugin(_inspector_plugin)

func _exit_tree() -> void:
	if _inspector_plugin != null:
		remove_inspector_plugin(_inspector_plugin)
