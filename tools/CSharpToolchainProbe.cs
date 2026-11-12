using Godot;

/// <summary>
/// Phase 1 toolchain probe. Not a game system.
/// Run: godot --headless --path . --script res://tools/CSharpToolchainProbe.cs
/// Proves the .NET assembly loads, C# code executes, and Godot types resolve.
/// </summary>
public partial class CSharpToolchainProbe : SceneTree
{
    public override void _Initialize()
    {
        var probe = new StaticBody2D();
        probe.Name = "ProbeBody";
        var shape = new RectangleShape2D { Size = new Vector2(48.0f, 0.24f) };
        GD.Print($"[CSHARP_PROBE] ok=1 engine={Engine.GetVersionInfo()["string"]} shape_w={shape.Size.X}");
        shape.Dispose();
        probe.Free();
        Quit(0);
    }
}
