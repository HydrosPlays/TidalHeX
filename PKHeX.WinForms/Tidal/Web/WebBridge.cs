using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// JSON RPC bridge between the page and the host (see API.md).
/// </summary>
/// <remarks>
/// Calls are <c>{id, method, args}</c>; replies are <c>{id, ok, result}</c> or <c>{id, ok:false, error}</c>; events are <c>{event, data}</c>.
/// Handlers always run on the UI thread, outside of the WebView2 event callback (WebView2 does not support running a
/// nested message loop, e.g. a modal dialog, inside its own event handlers). Calls that arrive while a handler is
/// running a modal dialog are queued and dispatched in order once it returns.
/// </remarks>
internal sealed class WebBridge(Control ui)
{
    public static readonly JsonSerializerOptions Json = CreateOptions();

    private readonly Dictionary<string, Func<RpcCall, object?>> Handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<RpcCall, Task<object?>>> AsyncHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<RpcCall>> ImmediateHandlers = new(StringComparer.Ordinal);
    private readonly Queue<RpcCall> Pending = new();
    private bool Dispatching;

    /// <summary> The page's WebView; null until initialized (events are dropped until then). </summary>
    public CoreWebView2? Core { get; set; }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    public void Register(string method, Func<RpcCall, object?> handler) => Handlers.Add(method, handler);

    /// <summary>
    /// Registers a notification that is handled as soon as it arrives instead of being queued: used for answers the
    /// running handler is waiting for (an in-page dialog). It must not show UI or pump messages.
    /// </summary>
    public void RegisterImmediate(string method, Action<RpcCall> handler) => ImmediateHandlers.Add(method, handler);

    /// <summary>
    /// Registers a handler that does background work. Its synchronous part runs on the UI thread; it must not show dialogs.
    /// </summary>
    public void RegisterAsync(string method, Func<RpcCall, Task<object?>> handler) => AsyncHandlers.Add(method, handler);

    /// <summary>
    /// Entry point from <see cref="CoreWebView2.WebMessageReceived"/>: parses the call immediately (additional objects are
    /// only valid during the event) and defers the handler.
    /// </summary>
    public void Receive(CoreWebView2WebMessageReceivedEventArgs e)
    {
        RpcCall? call;
        try
        {
            call = RpcCall.Parse(e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal bridge: malformed message: {ex.Message}");
            return;
        }
        if (call is null)
            return;

        if (ImmediateHandlers.TryGetValue(call.Method, out var immediate))
        {
            try { immediate(call); }
            catch (Exception ex) { Debug.WriteLine($"Tidal bridge: {call.Method} failed: {ex.Message}"); }
            return;
        }

        Pending.Enqueue(call);
        if (ui.IsDisposed || !ui.IsHandleCreated)
            return; // window is closing
        try
        {
            ui.BeginInvoke(Pump);
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check and the call; nothing left to answer.
        }
    }

    /// <summary>
    /// Runs host-initiated UI work (e.g. startup popups) with the same exclusivity as a call: calls arriving meanwhile wait.
    /// </summary>
    public void RunExclusive(Action action)
    {
        if (Dispatching)
        {
            action();
            return;
        }

        Dispatching = true;
        try
        {
            action();
        }
        finally
        {
            Dispatching = false;
        }
        Pump();
    }

    private void Pump()
    {
        if (Dispatching)
            return; // re-entered from a modal dialog's message loop; the outer pump drains the queue afterward.

        Dispatching = true;
        try
        {
            while (Pending.TryDequeue(out var call))
                Dispatch(call);
        }
        finally
        {
            Dispatching = false;
        }
    }

    private void Dispatch(RpcCall call)
    {
        if (Handlers.TryGetValue(call.Method, out var handler))
        {
            object? result;
            try
            {
                result = handler(call);
            }
            catch (Exception ex)
            {
                ReplyError(call, ex);
                return;
            }
            Reply(call, result);
            return;
        }

        if (AsyncHandlers.TryGetValue(call.Method, out var asyncHandler))
        {
            Task<object?> task;
            try
            {
                task = asyncHandler(call);
            }
            catch (Exception ex)
            {
                ReplyError(call, ex);
                return;
            }
            _ = CompleteAsync(call, task);
            return;
        }

        ReplyError(call, new NotSupportedException($"Unknown method '{call.Method}'."));
    }

    private async Task CompleteAsync(RpcCall call, Task<object?> task)
    {
        object? result;
        try
        {
            result = await task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReplyError(call, ex);
            return;
        }
        Reply(call, result);
    }

    private void Reply(RpcCall call, object? result)
    {
        if (!call.ExpectsReply)
            return;
        string json;
        try
        {
            json = JsonSerializer.Serialize(new { id = call.Id, ok = true, result }, Json);
        }
        catch (Exception ex)
        {
            ReplyError(call, ex);
            return;
        }
        Post(json);
    }

    private void ReplyError(RpcCall call, Exception ex)
    {
        Debug.WriteLine($"Tidal bridge: {call.Method} failed: {ex}");
#if DEBUG
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tidal-bridge-errors.log"), $"{DateTime.Now:T} {call.Method}: {ex}\n\n"); } catch { /* dev log only */ }
#endif
        if (!call.ExpectsReply)
            return;
        var message = ex is OperationCanceledException ? "Cancelled." : ex.Message;
        Post(JsonSerializer.Serialize(new { id = call.Id, ok = false, error = message }, Json));
    }

    /// <summary>
    /// Sends an event to the page (<c>{event, data}</c>).
    /// </summary>
    public void Emit(string name, object? data)
    {
        if (Core is null)
            return;
        try
        {
            Post(JsonSerializer.Serialize(new { @event = name, data }, Json));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal bridge: event {name} failed: {ex}");
        }
    }

    private void Post(string json)
    {
        try
        {
            Core?.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            // The WebView may be closing; nothing to deliver to.
            Debug.WriteLine($"Tidal bridge: post failed: {ex.Message}");
        }
    }
}

/// <summary>
/// A parsed call from the page.
/// </summary>
internal sealed class RpcCall
{
    public required JsonElement Id { get; init; }
    public required string Method { get; init; }
    public JsonElement? Args { get; init; }

    /// <summary> Calls without an id are fire-and-forget. </summary>
    public bool ExpectsReply => Id.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    /// <summary> File paths passed with <c>postMessageWithAdditionalObjects</c> (drag and drop). </summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>
    /// Deserializes the call's arguments.
    /// </summary>
    public T Get<T>() where T : class
    {
        if (Args is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } args)
            throw new ArgumentException($"{Method}: missing arguments.");
        return args.Deserialize<T>(WebBridge.Json) ?? throw new ArgumentException($"{Method}: missing arguments.");
    }

    public static RpcCall? Parse(CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            return null;

        var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : default;
        JsonElement? args = root.TryGetProperty("args", out var a) ? a.Clone() : null;
        return new RpcCall
        {
            Id = id,
            Method = method.GetString() ?? string.Empty,
            Args = args,
            Files = GetFiles(e),
        };
    }

    private static List<string> GetFiles(CoreWebView2WebMessageReceivedEventArgs e)
    {
        var result = new List<string>();
        var objects = e.AdditionalObjects;
        if (objects is null)
            return result;
        foreach (var item in objects)
        {
            if (item is CoreWebView2File { Path: { Length: not 0 } path })
                result.Add(path);
        }
        return result;
    }
}
