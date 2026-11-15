/* DualSense Hub ETS2 telemetry plugin: forwards truck state over UDP to 127.0.0.1:47555. See PROTOCOL.md. */
#include "sdk/scs_min.h"

#define EXPORT __declspec(dllexport)
#define PORT 47555
#define MIN_INTERVAL_US 33000ull
#define MAX_WHEELS 8
#define PROTOCOL_VERSION 4

enum {
    F_LBLINKER = 1 << 0, F_RBLINKER = 1 << 1, F_HAZARD = 1 << 2, F_BRAKE = 1 << 3,
    F_ENGINE = 1 << 4, F_ELECTRIC = 1 << 5, F_PAUSED = 1 << 6, F_PARKING = 1 << 7,
};

#pragma pack(push, 1)
typedef struct {
    char magic[4];
    uint8_t version;
    uint8_t reserved;
    uint16_t flags;
    float speed;        /* m/s */
    float rpm;
    float rpm_limit;
    float fuel;         /* liters */
    float fuel_capacity;
    uint32_t seq;
    float throttle;     /* 0..1, truck.effective.throttle */
    /* version 2 */
    float brake;        /* 0..1, truck.effective.brake */
    float air_pressure; /* psi, truck.brake.air.pressure */
    int32_t gear;       /* truck.engine.gear: >0 forward, 0 neutral, <0 reverse */
    uint8_t wheel_count;
    uint8_t ground_mask; /* bit i: wheel i touches ground */
    uint8_t left_mask;   /* bit i: wheel i is on the left side (config wheel.position x < 0) */
    uint8_t reserved2;
    float susp[MAX_WHEELS];   /* truck.wheel.suspension.deflection, m */
    float vel[MAX_WHEELS];    /* truck.wheel.angular_velocity, rotations/s */
    float radius[MAX_WHEELS]; /* config wheel.radius, m */
    /* version 3 */
    float input_throttle;     /* 0..1, truck.input.throttle (raw pedal) */
    /* version 4 */
    float wear_total;         /* sum of truck.wear.engine/transmission/cabin/chassis/wheels */
    float accel[3];           /* truck.local.acceleration.linear x,y,z, m/s^2 (peak since the previous packet) */
    float cruise_speed;       /* m/s, truck.cruise_control (0 = off) */
    uint8_t retarder;         /* truck.brake.retarder current level */
    uint8_t retarder_steps;   /* config retarder.steps */
    uint16_t reserved3;
} packet_t;
#pragma pack(pop)
_Static_assert(sizeof(packet_t) == 176, "packet size");

/* ws2_32 (stdcall == default on x64) */
typedef uintptr_t SOCKET;
typedef struct { uint16_t family; uint16_t port; uint32_t addr; uint8_t zero[8]; } sockaddr_in_t;
__declspec(dllimport) int WSAStartup(uint16_t ver, void *data);
__declspec(dllimport) int WSACleanup(void);
__declspec(dllimport) SOCKET socket(int af, int type, int proto);
__declspec(dllimport) int sendto(SOCKET s, const char *buf, int len, int flags, const sockaddr_in_t *to, int tolen);
__declspec(dllimport) int closesocket(SOCKET s);

int _fltused;
static SOCKET g_sock = (SOCKET)~0;
static int g_wsa;
static sockaddr_in_t g_dest;
static packet_t g_pkt;
static uint64_t g_last_send, g_render_time;
static uint16_t g_last_flags = 0xFFFF;
static float g_wear[5];
static float g_peak2;

static void set_flag(uint16_t f, int on) { if (on) g_pkt.flags |= f; else g_pkt.flags &= (uint16_t)~f; }

static void send_packet(void)
{
    if (g_sock == (SOCKET)~0) return;
    g_pkt.seq++;
    g_pkt.wear_total = g_wear[0] + g_wear[1] + g_wear[2] + g_wear[3] + g_wear[4];
    sendto(g_sock, (const char *)&g_pkt, (int)sizeof g_pkt, 0, &g_dest, (int)sizeof g_dest);
    g_last_send = g_render_time;
    g_last_flags = g_pkt.flags;
    g_peak2 = 0.0f; /* the next acceleration sample starts a new peak window */
}

static int streq(const char *a, const char *b)
{
    while (*a && *a == *b) { a++; b++; }
    return *a == *b;
}

static void on_bool(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index;
    set_flag((uint16_t)(uintptr_t)ctx, v && v->value_bool.value);
}

static void on_s32(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index; (void)ctx;
    g_pkt.gear = v ? v->value_s32.value : 0;
}

static void on_ground(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)ctx;
    if (index >= MAX_WHEELS) return;
    if (v && v->value_bool.value) g_pkt.ground_mask |= (uint8_t)(1u << index);
    else g_pkt.ground_mask &= (uint8_t)~(1u << index);
}

static void on_u32(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index; (void)ctx;
    uint32_t n = v ? v->value_u32.value : 0;
    g_pkt.retarder = (uint8_t)(n > 255 ? 255 : n);
}

/* Keeps the strongest acceleration since the last packet so a one-frame impact spike is not lost between sends. */
static void on_fvec(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index; (void)ctx;
    if (!v) return;
    float x = v->value_fvector.x, y = v->value_fvector.y, z = v->value_fvector.z;
    float m2 = x * x + y * y + z * z;
    if (m2 >= g_peak2) { g_peak2 = m2; g_pkt.accel[0] = x; g_pkt.accel[1] = y; g_pkt.accel[2] = z; }
}

static void on_wheel_float(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name;
    if (index >= MAX_WHEELS) return;
    float f = v ? v->value_float.value : 0.0f;
    if ((uintptr_t)ctx == 0) g_pkt.susp[index] = f; else g_pkt.vel[index] = f;
}

static void on_float(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index;
    float f = v ? v->value_float.value : 0.0f;
    switch ((uintptr_t)ctx) {
    case 0: g_pkt.speed = f; break;
    case 1: g_pkt.rpm = f; break;
    case 2: g_pkt.fuel = f; break;
    case 3: g_pkt.throttle = f; break;
    case 4: g_pkt.brake = f; break;
    case 5: g_pkt.air_pressure = f; break;
    case 6: g_pkt.input_throttle = f; break;
    case 7: g_pkt.cruise_speed = f; break;
    case 8: case 9: case 10: case 11: case 12: g_wear[(uintptr_t)ctx - 8] = f; break;
    }
}

static void on_event(scs_event_t ev, const void *info, scs_context_t ctx)
{
    (void)ctx;
    switch (ev) {
    case SCS_TELEMETRY_EVENT_frame_start:
        if (info) g_render_time = ((const scs_telemetry_frame_start_t *)info)->render_time;
        break;
    case SCS_TELEMETRY_EVENT_frame_end:
        if (g_pkt.flags != g_last_flags || g_render_time - g_last_send >= MIN_INTERVAL_US || g_render_time < g_last_send)
            send_packet();
        break;
    case SCS_TELEMETRY_EVENT_paused: set_flag(F_PAUSED, 1); break;
    case SCS_TELEMETRY_EVENT_started: set_flag(F_PAUSED, 0); break;
    case SCS_TELEMETRY_EVENT_configuration: {
        const scs_telemetry_configuration_t *c = (const scs_telemetry_configuration_t *)info;
        if (!c || !c->id || !streq(c->id, "truck")) break;
        g_pkt.wheel_count = 0; g_pkt.left_mask = 0; g_pkt.retarder_steps = 0;
        for (int i = 0; i < MAX_WHEELS; i++) g_pkt.radius[i] = 0.0f;
        for (const scs_named_value_t *a = c->attributes; a && a->name; a++) {
            if (a->value.type == SCS_VALUE_TYPE_float) {
                if (streq(a->name, "fuel.capacity")) g_pkt.fuel_capacity = a->value.value_float.value;
                else if (streq(a->name, "rpm.limit")) g_pkt.rpm_limit = a->value.value_float.value;
                else if (streq(a->name, "wheel.radius") && a->index < MAX_WHEELS) g_pkt.radius[a->index] = a->value.value_float.value;
            } else if (a->value.type == SCS_VALUE_TYPE_u32) {
                if (streq(a->name, "wheels.count")) {
                    uint32_t n = a->value.value_u32.value;
                    g_pkt.wheel_count = (uint8_t)(n > MAX_WHEELS ? MAX_WHEELS : n);
                } else if (streq(a->name, "retarder.steps")) {
                    uint32_t n = a->value.value_u32.value;
                    g_pkt.retarder_steps = (uint8_t)(n > 255 ? 255 : n);
                }
            } else if (a->value.type == SCS_VALUE_TYPE_fvector) {
                /* vehicle space: +x is right, so x < 0 is a left-side wheel (assumption, see PROTOCOL.md) */
                if (streq(a->name, "wheel.position") && a->index < MAX_WHEELS && a->value.value_fvector.x < 0.0f)
                    g_pkt.left_mask |= (uint8_t)(1u << a->index);
            }
        }
        break;
    }
    }
}

EXPORT scs_result_t scs_telemetry_init(const scs_u32_t version, const void *params)
{
    if (version != SCS_TELEMETRY_VERSION_1_00 && version != SCS_MAKE_VERSION(1, 1)) return SCS_RESULT_unsupported;
    const scs_telemetry_init_params_v100_t *p = params;

    g_pkt.magic[0] = 'D'; g_pkt.magic[1] = 'S'; g_pkt.magic[2] = 'H'; g_pkt.magic[3] = '1';
    g_pkt.version = PROTOCOL_VERSION;
    g_pkt.reserved = 0;
    g_pkt.flags = 0;
    g_pkt.speed = g_pkt.rpm = g_pkt.rpm_limit = g_pkt.fuel = g_pkt.fuel_capacity = g_pkt.throttle = 0.0f;
    g_pkt.brake = g_pkt.air_pressure = g_pkt.input_throttle = 0.0f;
    g_pkt.gear = 0;
    g_pkt.wear_total = g_pkt.cruise_speed = 0.0f;
    g_pkt.accel[0] = g_pkt.accel[1] = g_pkt.accel[2] = 0.0f;
    g_pkt.retarder = g_pkt.retarder_steps = 0; g_pkt.reserved3 = 0;
    for (int i = 0; i < 5; i++) g_wear[i] = 0.0f;
    g_peak2 = 0.0f;
    g_pkt.wheel_count = g_pkt.ground_mask = g_pkt.left_mask = g_pkt.reserved2 = 0;
    for (int i = 0; i < MAX_WHEELS; i++) g_pkt.susp[i] = g_pkt.vel[i] = g_pkt.radius[i] = 0.0f;
    g_pkt.seq = 0;
    g_last_flags = 0xFFFF; g_last_send = g_render_time = 0;

    uint8_t wsadata[512];
    for (int i = 0; i < 512; i++) wsadata[i] = 0;
    if (WSAStartup(0x0202, wsadata) == 0) g_wsa = 1;
    g_sock = socket(2 /*AF_INET*/, 2 /*SOCK_DGRAM*/, 17 /*UDP*/);
    g_dest.family = 2;
    g_dest.port = (uint16_t)(((PORT & 0xFF) << 8) | (PORT >> 8));
    g_dest.addr = 0x0100007Fu; /* 127.0.0.1 in network order, little-endian load */
    for (int i = 0; i < 8; i++) g_dest.zero[i] = 0;

    p->register_for_event(SCS_TELEMETRY_EVENT_frame_start, on_event, 0);
    p->register_for_event(SCS_TELEMETRY_EVENT_frame_end, on_event, 0);
    p->register_for_event(SCS_TELEMETRY_EVENT_paused, on_event, 0);
    p->register_for_event(SCS_TELEMETRY_EVENT_started, on_event, 0);
    p->register_for_event(SCS_TELEMETRY_EVENT_configuration, on_event, 0);

    p->register_for_channel("truck.light.lblinker", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_LBLINKER);
    p->register_for_channel("truck.light.rblinker", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_RBLINKER);
    p->register_for_channel("truck.hazard.warning", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_HAZARD);
    p->register_for_channel("truck.light.brake", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_BRAKE);
    p->register_for_channel("truck.engine.enabled", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_ENGINE);
    p->register_for_channel("truck.electric.enabled", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_ELECTRIC);
    p->register_for_channel("truck.speed", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)0);
    p->register_for_channel("truck.engine.rpm", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)1);
    p->register_for_channel("truck.fuel.amount", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)2);
    p->register_for_channel("truck.effective.throttle", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)3);
    p->register_for_channel("truck.input.throttle", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)6);
    p->register_for_channel("truck.effective.brake", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)4);
    p->register_for_channel("truck.brake.air.pressure", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)5);
    p->register_for_channel("truck.brake.parking", SCS_U32_NIL, SCS_VALUE_TYPE_bool, 0, on_bool, (void *)(uintptr_t)F_PARKING);
    p->register_for_channel("truck.engine.gear", SCS_U32_NIL, SCS_VALUE_TYPE_s32, 0, on_s32, 0);
    p->register_for_channel("truck.cruise_control", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)7);
    p->register_for_channel("truck.brake.retarder", SCS_U32_NIL, SCS_VALUE_TYPE_u32, 0, on_u32, 0);
    p->register_for_channel("truck.local.acceleration.linear", SCS_U32_NIL, SCS_VALUE_TYPE_fvector, 0, on_fvec, 0);
    p->register_for_channel("truck.wear.engine", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)8);
    p->register_for_channel("truck.wear.transmission", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)9);
    p->register_for_channel("truck.wear.cabin", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)10);
    p->register_for_channel("truck.wear.chassis", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)11);
    p->register_for_channel("truck.wear.wheels", SCS_U32_NIL, SCS_VALUE_TYPE_float, 0, on_float, (void *)(uintptr_t)12);
    /* Indexed channels: indices beyond the truck's real wheel count simply fail to register. */
    for (scs_u32_t i = 0; i < MAX_WHEELS; i++) {
        p->register_for_channel("truck.wheel.suspension.deflection", i, SCS_VALUE_TYPE_float, 0, on_wheel_float, (void *)(uintptr_t)0);
        p->register_for_channel("truck.wheel.angular_velocity", i, SCS_VALUE_TYPE_float, 0, on_wheel_float, (void *)(uintptr_t)1);
        p->register_for_channel("truck.wheel.on_ground", i, SCS_VALUE_TYPE_bool, 0, on_ground, 0);
    }
    return SCS_RESULT_ok;
}

EXPORT void scs_telemetry_shutdown(void)
{
    if (g_sock != (SOCKET)~0) { closesocket(g_sock); g_sock = (SOCKET)~0; }
    if (g_wsa) { WSACleanup(); g_wsa = 0; }
}

EXPORT int DllMain(void *inst, uint32_t reason, void *reserved)
{
    (void)inst; (void)reason; (void)reserved;
    return 1;
}
