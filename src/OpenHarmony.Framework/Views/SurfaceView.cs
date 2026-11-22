using System.Text.Json;
using System.Text.Json.Serialization;
using OpenHarmony.Graphics;
using OpenHarmony.Threading;

namespace OpenHarmony.Views;

public enum EditingKey { Unknown, Backspace, Delete, Left, Right, Home, End, Enter }
public enum TextInputType { Text, Password, Number, Multiline }
public sealed record TextEditorState(int Revision, bool Focused, string Text, int Cursor, TextInputType InputType, int ShowRequest);
public readonly record struct SurfaceFrame(int Width, int Height, float Density, double Seconds);

/// <summary>One XComponent surface per process. The host owns EGL and invokes these callbacks
/// on its UI thread. Dispose all graphics resources in OnDestroyed, while EGL is current.</summary>
public abstract class SurfaceView
{
    public PlatformDispatcher Dispatcher { get; } = new();
    public EglContext GraphicsContext { get; internal set; } = null!;
    public virtual void OnCreated() { }
    public abstract bool OnFrame(SurfaceFrame frame);
    public virtual void OnDestroyed() { }
    public virtual void OnTouch(int action, int id, float x, float y) { }
    public virtual bool OnBack() => false;
    public virtual void OnActiveChanged(bool active) { }
    public virtual void OnText(string text) { }
    public virtual void OnKey(EditingKey key) { }
    public virtual TextEditorState? GetTextEditorState() => null;
    public virtual void OnNavigate(string path) { }
    internal string SerializeEditor() => GetTextEditorState() is { } state
        ? JsonSerializer.Serialize(state, EditorJsonContext.Default.TextEditorState) : "";
}

[JsonSerializable(typeof(TextEditorState))]
internal partial class EditorJsonContext : JsonSerializerContext;
