/* DualSense Hub ETS2 telemetry plugin: forwards truck state over UDP to 127.0.0.1:47555. See PROTOCOL.md. */
#include "sdk/scs_min.h"

#define EXPORT __declspec(dllexport)
#define PORT 47555
#define MIN_INTERVAL_US 33000ull

enum {
    F_LBLINKER = 1 << 0, F_RBLINKER = 1 << 1, F_HAZARD = 1 << 2, F_BRAKE = 1 << 3,
    F_ENGINE = 1 << 4, F_ELECTRIC = 1 << 5, F_PAUSED = 1 << 6,
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
} packet_t;
#pragma pack(pop)
_Static_assert(sizeof(packet_t) == 36, "packet size");

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

static void set_flag(uint16_t f, int on) { if (on) g_pkt.flags |= f; else g_pkt.flags &= (uint16_t)~f; }

static void send_packet(void)
{
    if (g_sock == (SOCKET)~0) return;
    g_pkt.seq++;
    sendto(g_sock, (const char *)&g_pkt, (int)sizeof g_pkt, 0, &g_dest, (int)sizeof g_dest);
    g_last_send = g_render_time;
    g_last_flags = g_pkt.flags;
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

static void on_float(scs_string_t name, scs_u32_t index, const scs_value_t *v, scs_context_t ctx)
{
    (void)name; (void)index;
    float f = v ? v->value_float.value : 0.0f;
    switch ((uintptr_t)ctx) {
    case 0: g_pkt.speed = f; break;
    case 1: g_pkt.rpm = f; break;
    case 2: g_pkt.fuel = f; break;
    case 3: g_pkt.throttle = f; break;
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
        for (const scs_named_value_t *a = c->attributes; a && a->name; a++) {
            if (a->value.type != SCS_VALUE_TYPE_float) continue;
            if (streq(a->name, "fuel.capacity")) g_pkt.fuel_capacity = a->value.value_float.value;
            else if (streq(a->name, "rpm.limit")) g_pkt.rpm_limit = a->value.value_float.value;
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
    g_pkt.version = 1;
    g_pkt.reserved = 0;
    g_pkt.flags = 0;
    g_pkt.speed = g_pkt.rpm = g_pkt.rpm_limit = g_pkt.fuel = g_pkt.fuel_capacity = g_pkt.throttle = 0.0f;
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
