/*
 * Minimal C declarations of the SCS Telemetry SDK 1.x ABI (Windows x64 only).
 *
 * The official SDK headers are C++ (struct inheritance, static_cast), so this
 * project re-declares just the layouts and constants it needs, with identical
 * binary layout (sizes asserted below). Derived from the SCS SDK, see
 * SCS_SDK_LICENSE.txt (MIT-style, Copyright (C) 2016 SCS Software).
 */
#ifndef SCS_MIN_H
#define SCS_MIN_H
#include <stdint.h>

typedef uint8_t  scs_u8_t;
typedef int32_t  scs_s32_t;
typedef uint32_t scs_u32_t;
typedef uint64_t scs_u64_t;
typedef const char *scs_string_t;
typedef void *scs_context_t;
typedef scs_s32_t scs_result_t;
typedef scs_u32_t scs_value_type_t;
typedef scs_u32_t scs_event_t;

#define SCS_MAKE_VERSION(major, minor) (((major) << 16) | (minor))
#define SCS_TELEMETRY_VERSION_1_00 SCS_MAKE_VERSION(1, 0)
#define SCS_RESULT_ok 0
#define SCS_RESULT_unsupported (-1)
#define SCS_U32_NIL 0xFFFFFFFFu

#define SCS_VALUE_TYPE_bool    1
#define SCS_VALUE_TYPE_s32     2
#define SCS_VALUE_TYPE_u32     3
#define SCS_VALUE_TYPE_float   5
#define SCS_VALUE_TYPE_fvector 7

#define SCS_TELEMETRY_EVENT_frame_start   1
#define SCS_TELEMETRY_EVENT_frame_end     2
#define SCS_TELEMETRY_EVENT_paused        3
#define SCS_TELEMETRY_EVENT_started       4
#define SCS_TELEMETRY_EVENT_configuration 5

typedef struct { scs_u8_t value; } scs_value_bool_t;
typedef struct { scs_s32_t value; } scs_value_s32_t;
typedef struct { scs_u32_t value; } scs_value_u32_t;
typedef struct { float value; } scs_value_float_t;
typedef struct { float x, y, z; } scs_value_fvector_t;

typedef struct {
    scs_value_type_t type;
    scs_u32_t _padding;
    union {
        scs_value_bool_t value_bool;
        scs_value_s32_t value_s32;
        scs_value_u32_t value_u32;
        scs_value_float_t value_float;
        scs_value_fvector_t value_fvector;
        scs_u64_t _size_pad[5]; /* largest member (dplacement) is 40 bytes */
    };
} scs_value_t;

typedef struct {
    scs_string_t name;
    scs_u32_t index;
    scs_u32_t _padding;
    scs_value_t value;
} scs_named_value_t;

typedef struct {
    scs_u32_t flags;
    scs_u32_t _padding;
    scs_u64_t render_time;
    scs_u64_t simulation_time;
    scs_u64_t paused_simulation_time;
} scs_telemetry_frame_start_t;

typedef struct {
    scs_string_t id;
    const scs_named_value_t *attributes; /* terminated by name == NULL */
} scs_telemetry_configuration_t;

typedef void (*scs_log_t)(scs_s32_t type, scs_string_t message);

typedef void (*scs_telemetry_channel_callback_t)(scs_string_t name, scs_u32_t index, const scs_value_t *value, scs_context_t context);
typedef scs_result_t (*scs_telemetry_register_for_channel_t)(scs_string_t name, scs_u32_t index, scs_value_type_t type, scs_u32_t flags, scs_telemetry_channel_callback_t callback, scs_context_t context);
typedef scs_result_t (*scs_telemetry_unregister_from_channel_t)(scs_string_t name, scs_u32_t index, scs_value_type_t type);
typedef void (*scs_telemetry_event_callback_t)(scs_event_t event, const void *event_info, scs_context_t context);
typedef scs_result_t (*scs_telemetry_register_for_event_t)(scs_event_t event, scs_telemetry_event_callback_t callback, scs_context_t context);
typedef scs_result_t (*scs_telemetry_unregister_from_event_t)(scs_event_t event);

typedef struct {
    scs_string_t game_name;
    scs_string_t game_id;
    scs_u32_t game_version;
    scs_u32_t _padding;
    scs_log_t log;
} scs_sdk_init_params_v100_t;

typedef struct {
    scs_sdk_init_params_v100_t common;
    scs_telemetry_register_for_event_t register_for_event;
    scs_telemetry_unregister_from_event_t unregister_from_event;
    scs_telemetry_register_for_channel_t register_for_channel;
    scs_telemetry_unregister_from_channel_t unregister_from_channel;
} scs_telemetry_init_params_v100_t;

_Static_assert(sizeof(scs_value_t) == 48, "scs_value_t");
_Static_assert(sizeof(scs_named_value_t) == 64, "scs_named_value_t");
_Static_assert(sizeof(scs_sdk_init_params_v100_t) == 32, "init common");
_Static_assert(sizeof(scs_telemetry_init_params_v100_t) == 64, "init params");
_Static_assert(sizeof(scs_telemetry_frame_start_t) == 32, "frame_start");

#endif
