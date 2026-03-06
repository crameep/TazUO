# Starter Template Pack

Starter templates are shipped as runtime-backed scripts designed for mobile-safe execution.

## Included templates

- `healer`: casts greater heal and self-targets under HP threshold.
- `potion`: uses configured potion serial under HP threshold.
- `combo`: runs healer + potion checks in the same cooperative script.

## Launching

- `runstarter healer`
- `runstarter potion 0x40000001`
- `runstarter combo 0x40000001`

## Stopping

- `stopstarter healer`
- `stopstarter potion`
- `stopstarter combo`

## Notes

- Templates execute in the new runtime scheduler.
- Actions are queued on authoritative thread.
- Templates are safe for suspend/resume and watchdog monitoring.
