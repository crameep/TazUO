using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.LegionScripting.Runtime;

namespace ClassicUO.LegionScripting;

internal sealed class LegacyScriptRuntimeAdapter
{
    private readonly ScriptRuntimeManager _runtime;
    private readonly Dictionary<ScriptFile, int> _scriptToContext = new();

    public LegacyScriptRuntimeAdapter(ScriptRuntimeManager runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public int TrackedScripts => _scriptToContext.Count;

    public void PlayScript(ScriptFile script, Action<ScriptFile> legacyPlay)
    {
        if (script == null || legacyPlay == null)
            return;

        if (_scriptToContext.ContainsKey(script))
            return;

        bool started = false;

        ScriptContext context = _runtime.StartScript($"legacy:{script.FileName}", _ =>
        {
            if (!started)
            {
                started = true;
                legacyPlay(script);
                return ScriptDirective.WaitTicksFor(1);
            }

            if (script.IsPlaying)
                return ScriptDirective.WaitTicksFor(1);

            _scriptToContext.Remove(script);
            return ScriptDirective.Complete();
        });

        _scriptToContext[script] = context.Id;
    }

    public void StopScript(ScriptFile script, Action<ScriptFile> legacyStop)
    {
        if (script == null || legacyStop == null)
            return;

        if (_scriptToContext.TryGetValue(script, out int contextId))
        {
            _runtime.CancelScript(contextId, "legacy-stop");
            _scriptToContext.Remove(script);
        }

        legacyStop(script);
    }

    public void StopAll(IEnumerable<ScriptFile> scripts, Action<ScriptFile> legacyStop)
    {
        if (scripts == null || legacyStop == null)
            return;

        foreach (ScriptFile script in scripts.ToList())
            StopScript(script, legacyStop);
    }
}
