using System.Runtime.Serialization;
using ClassicUO.LegionScripting;
using ClassicUO.LegionScripting.Runtime;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class LegacyScriptRuntimeAdapterTests
{
    [Fact]
    public void Play_And_Stop_Should_Route_Through_Adapter_And_Runtime()
    {
        var runtime = new ScriptRuntimeManager();
        var adapter = new LegacyScriptRuntimeAdapter(runtime);
        ScriptFile script = CreateScriptFile("healer.py");

        int playCalls = 0;
        int stopCalls = 0;

        adapter.PlayScript(script, _ => playCalls++);
        runtime.Tick();

        playCalls.Should().Be(1);
        adapter.TrackedScripts.Should().Be(1);

        adapter.StopScript(script, _ => stopCalls++);

        stopCalls.Should().Be(1);
        adapter.TrackedScripts.Should().Be(0);
    }

    [Fact]
    public void Completed_Legacy_Script_Should_Be_Untracked_After_Runtime_Ticks()
    {
        var runtime = new ScriptRuntimeManager();
        var adapter = new LegacyScriptRuntimeAdapter(runtime);
        ScriptFile script = CreateScriptFile("potion.py");

        adapter.PlayScript(script, _ => { });

        runtime.Tick(); // invokes legacy play
        runtime.Tick(); // observes IsPlaying == false and completes context

        adapter.TrackedScripts.Should().Be(0);
    }

    private static ScriptFile CreateScriptFile(string fileName)
    {
#pragma warning disable SYSLIB0050
        ScriptFile script = (ScriptFile)FormatterServices.GetUninitializedObject(typeof(ScriptFile));
#pragma warning restore SYSLIB0050
        script.FileName = fileName;
        return script;
    }
}
