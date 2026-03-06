# Runtime Migration Guide (Python -> Runtime Templates)

This guide maps common Legion Python macro patterns to the runtime scheduler introduced in M1-M4.

## Why migrate

- Multiple scripts execute cooperatively with fairness.
- World reads are immutable snapshots (`execution.Snapshot`).
- Writes are routed via authoritative action queue.
- Lifecycle suspend/resume and watchdog protections are built in.

## API mapping

- `API.Cast("Greater Heal")` -> `RuntimeScriptApi.Heal(execution, "greater heal")`
- `API.UseObject(0x... )` -> `RuntimeScriptApi.DrinkPotion(execution, "0x...")`
- `API.Target(0x... )` -> `RuntimeScriptApi.Target(execution, 0x...)`
- `API.Pause(seconds)` -> `RuntimeScriptApi.Wait(ticks)`
- journal/event waits -> `RuntimeScriptApi.WaitForEvent("event.type", timeoutTicks)`

## Worked conversion: healer loop

Legacy intent:

```python
while True:
  if API.Player.Hits < 60:
    API.Cast("Greater Heal")
    API.Target(API.Player.Serial)
  API.Pause(0.1)
```

Runtime template intent:

```csharp
runtime.StartScript("starter:healer", RuntimeStarterTemplates.CreateHealerTemplate());
```

## Worked conversion: potion chugger

Legacy intent:

```python
if API.Player.Hits < 35:
  API.UseObject(0x40000001)
```

Runtime template intent:

```csharp
runtime.StartScript("starter:potion", RuntimeStarterTemplates.CreatePotionTemplate("0x40000001"));
```

## Runtime commands

- `runstarter healer`
- `runstarter potion 0x40000001`
- `runstarter combo 0x40000001`
- `stopstarter healer|potion|combo`

## Behavior notes

- Scripts cannot mutate world state directly.
- If a script waits too long, watchdog can fault it (`watchdog.wait-timeout`).
- Runtime emits telemetry and a beta report file in Legion logs on unload.
