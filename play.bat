@echo off
REM Double-click this file (or run it) to launch 3D Eggs.
REM Tip: the Godot editor's F5 is the fastest way to play repeatedly;
REM this is here for a no-editor, one-click run.
set GODOT="C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
%GODOT% --path "%~dp0"
