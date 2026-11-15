# DualSense Hub ETS2 UDP protocol (version 1)

The plugin sends one datagram per update (about 30 Hz, plus immediately when a flag changes) to `127.0.0.1:47555`.
Fixed 32-byte packet, little-endian, packed:

| Offset | Type    | Field         | Notes |
|-------:|---------|---------------|-------|
| 0      | char[4] | magic         | `"DSH1"` |
| 4      | u8      | version       | 1 |
| 5      | u8      | reserved      | 0 |
| 6      | u16     | flags         | see below |
| 8      | f32     | speed         | m/s (signed) |
| 12     | f32     | rpm           | engine RPM |
| 16     | f32     | rpm_limit     | from truck config `rpm.limit`, 0 until known |
| 20     | f32     | fuel          | liters (`truck.fuel.amount`) |
| 24     | f32     | fuel_capacity | liters (config `fuel.capacity`), 0 until known |
| 28     | u32     | seq           | increments per packet |

Flags: bit0 left blinker light (actual blinking state), bit1 right blinker light, bit2 hazard warning (`truck.hazard.warning`),
bit3 brake light, bit4 engine enabled, bit5 electric enabled, bit6 game paused (`paused`/`started` events).

Channels used: `truck.light.lblinker`, `truck.light.rblinker`, `truck.hazard.warning`, `truck.light.brake`, `truck.engine.enabled`,
`truck.electric.enabled`, `truck.speed`, `truck.engine.rpm`, `truck.fuel.amount`. The switch-state channels are not used because
the light channels already follow the blink cycle.

Plugin source uses its own minimal C declarations of the SCS SDK ABI (`sdk/scs_min.h`) because the official headers are C++ only.
Build: `./build.sh` (clang + lld-link + llvm-dlltool, no CRT).
