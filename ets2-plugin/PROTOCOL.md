# DualSense Hub ETS2 UDP protocol (version 2)

The plugin sends one datagram per update (about 30 Hz, plus immediately when a flag changes) to `127.0.0.1:47555`.
Fixed 148-byte packet (version 2). Version-1 packets (36 bytes, or 32 in early builds without throttle) are still accepted; missing fields read as 0. Little-endian, packed. The magic stays `DSH1`; the version byte decides the layout.

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
| 32     | f32     | throttle      | 0..1 (`truck.effective.throttle`, includes cruise control) |

Flags: bit0 left blinker light (actual blinking state), bit1 right blinker light, bit2 hazard warning (`truck.hazard.warning`),
bit3 brake light, bit4 engine enabled, bit5 electric enabled, bit6 game paused (`paused`/`started` events), bit7 parking brake (`truck.brake.parking`).

Version 2 fields (offset 36 onward):

| Offset | Type    | Field        | Notes |
|-------:|---------|--------------|-------|
| 36     | f32     | brake        | 0..1, `truck.effective.brake` |
| 40     | f32     | air_pressure | psi, `truck.brake.air.pressure` |
| 44     | s32     | gear         | `truck.engine.gear`: >0 forward, 0 neutral, <0 reverse |
| 48     | u8      | wheel_count  | config `wheels.count`, capped at 8 |
| 49     | u8      | ground_mask  | bit i set when wheel i touches the ground (`truck.wheel.on_ground`) |
| 50     | u8      | left_mask    | bit i set when wheel i is on the left (config `wheel.position` x < 0) |
| 51     | u8      | reserved     | 0 |
| 52     | f32[8]  | susp         | `truck.wheel.suspension.deflection`, meters |
| 84     | f32[8]  | vel          | `truck.wheel.angular_velocity`, rotations per second (positive = forward) |
| 116    | f32[8]  | radius       | config `wheel.radius`, meters |

Surface speed of a wheel = `abs(vel) * 2 * pi * radius` (m/s). Assumptions: vehicle space has +x to the right, so x < 0 means a left wheel;
the SDK has no ABS channel, so the app infers lockup from wheel speed versus truck speed. Indexed channels are registered for indices 0..7;
indices beyond the truck's real wheel count fail to register and stay 0.

Channels used: `truck.light.lblinker`, `truck.light.rblinker`, `truck.hazard.warning`, `truck.light.brake`, `truck.engine.enabled`,
`truck.electric.enabled`, `truck.speed`, `truck.engine.rpm`, `truck.fuel.amount`, `truck.effective.throttle`, and (v2) `truck.effective.brake`,
`truck.brake.air.pressure`, `truck.brake.parking`, `truck.engine.gear`, indexed `truck.wheel.suspension.deflection`, `truck.wheel.angular_velocity`, `truck.wheel.on_ground`. The switch-state channels are not used because
the light channels already follow the blink cycle.

Plugin source uses its own minimal C declarations of the SCS SDK ABI (`sdk/scs_min.h`) because the official headers are C++ only.
Build: `./build.sh` (clang + lld-link + llvm-dlltool, no CRT).
