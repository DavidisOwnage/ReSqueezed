/* SDL handles HID and DirectInput mappings alongside the existing XInput path. */
#include "windows_gamepad.h"
#include "input_backend.h"
#include <SDL3/SDL_gamepad.h>
#include <SDL3/SDL_hints.h>
#include <SDL3/SDL_init.h>
#include <stdio.h>

#define SDL_PAD_FUNCTIONS(F) \
    F(bool, SDL_SetHint, (const char *, const char *)) \
    F(bool, SDL_InitSubSystem, (SDL_InitFlags)) \
    F(void, SDL_QuitSubSystem, (SDL_InitFlags)) \
    F(void, SDL_SetGamepadEventsEnabled, (bool)) \
    F(void, SDL_UpdateGamepads, (void)) \
    F(const char *, SDL_GetError, (void)) \
    F(SDL_JoystickID *, SDL_GetJoysticks, (int *)) \
    F(bool, SDL_IsGamepad, (SDL_JoystickID)) \
    F(Uint16, SDL_GetJoystickVendorForID, (SDL_JoystickID)) \
    F(Uint16, SDL_GetJoystickProductForID, (SDL_JoystickID)) \
    F(SDL_GUID, SDL_GetJoystickGUIDForID, (SDL_JoystickID)) \
    F(void, SDL_GUIDToString, (SDL_GUID, char *, int)) \
    F(SDL_Joystick *, SDL_OpenJoystick, (SDL_JoystickID)) \
    F(void, SDL_CloseJoystick, (SDL_Joystick *)) \
    F(int, SDL_GetNumJoystickAxes, (SDL_Joystick *)) \
    F(int, SDL_GetNumJoystickButtons, (SDL_Joystick *)) \
    F(int, SDL_GetNumJoystickHats, (SDL_Joystick *)) \
    F(int, SDL_AddGamepadMapping, (const char *)) \
    F(SDL_Gamepad *, SDL_OpenGamepad, (SDL_JoystickID)) \
    F(void, SDL_CloseGamepad, (SDL_Gamepad *)) \
    F(bool, SDL_GamepadConnected, (SDL_Gamepad *)) \
    F(SDL_GamepadType, SDL_GetGamepadType, (SDL_Gamepad *)) \
    F(Uint16, SDL_GetGamepadVendor, (SDL_Gamepad *)) \
    F(Uint16, SDL_GetGamepadProduct, (SDL_Gamepad *)) \
    F(const char *, SDL_GetGamepadName, (SDL_Gamepad *)) \
    F(bool, SDL_GetGamepadButton, (SDL_Gamepad *, SDL_GamepadButton)) \
    F(Sint16, SDL_GetGamepadAxis, (SDL_Gamepad *, SDL_GamepadAxis)) \
    F(bool, SDL_RumbleGamepad, (SDL_Gamepad *, Uint16, Uint16, Uint32)) \
    F(void, SDL_free, (void *))

#define DECLARE_FN(ret, name, args) static ret (SDLCALL *p_##name) args;
SDL_PAD_FUNCTIONS(DECLARE_FN)
#undef DECLARE_FN

static HMODULE s_sdl;
static SDL_Gamepad *s_pad;
static xbox_pad_family s_family;
static ULONGLONG s_next_scan;

_Static_assert((int)SDL_GAMEPAD_BUTTON_SOUTH == XBOX_SDL_BUTTON_A &&
               (int)SDL_GAMEPAD_BUTTON_DPAD_RIGHT == XBOX_SDL_BUTTON_DPAD_RIGHT &&
               (int)SDL_GAMEPAD_AXIS_LEFTX == XBOX_SDL_AXIS_LEFTX &&
               (int)SDL_GAMEPAD_AXIS_RIGHT_TRIGGER == XBOX_SDL_AXIS_TRIGGERRIGHT,
               "SDL gamepad indices must match the existing pad mapping");

void xbox_WindowsGamepadInit(void)
{
    HMODULE module = LoadLibraryExW(L"SDL3.dll", NULL,
        LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) {
        fprintf(stderr, "[INPUT] SDL3.dll could not be loaded (%lu); XInput remains available\n",
                (unsigned long)GetLastError());
        return;
    }
#define LOAD_FN(ret, name, args) \
    p_##name = (ret (SDLCALL *) args)(void *)GetProcAddress(module, #name); \
    if (!p_##name) goto unavailable;
    SDL_PAD_FUNCTIONS(LOAD_FN)
#undef LOAD_FN
    p_SDL_SetHint(SDL_HINT_XINPUT_ENABLED, "0");
    p_SDL_SetHint(SDL_HINT_JOYSTICK_WGI, "1");
    p_SDL_SetHint(SDL_HINT_JOYSTICK_RAWINPUT, "0");
    p_SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    p_SDL_SetHint(SDL_HINT_JOYSTICK_ENHANCED_REPORTS, "1");
    if (!p_SDL_InitSubSystem(SDL_INIT_GAMEPAD))
        goto unavailable;
    p_SDL_SetGamepadEventsEnabled(false);
    s_sdl = module;
    return;

unavailable:
    fprintf(stderr, "[INPUT] SDL controller initialization failed%s%s\n",
            p_SDL_GetError ? ": " : "", p_SDL_GetError ? p_SDL_GetError() : "");
    FreeLibrary(module);
}

static xbox_pad_family pad_family(SDL_Gamepad *pad)
{
    xbox_pad_family family = XBOX_PAD_FAMILY_XBOX;
    switch (p_SDL_GetGamepadType(pad)) {
    case SDL_GAMEPAD_TYPE_XBOX360:
    case SDL_GAMEPAD_TYPE_XBOXONE:
        return XBOX_PAD_FAMILY_XBOX_MODERN;
    case SDL_GAMEPAD_TYPE_PS3:
    case SDL_GAMEPAD_TYPE_PS4:
        family = XBOX_PAD_FAMILY_PLAYSTATION;
        break;
    case SDL_GAMEPAD_TYPE_PS5:
        family = XBOX_PAD_FAMILY_PLAYSTATION5;
        break;
    default:
        break;
    }
    return xbox_PadFamilyFromIds(family, p_SDL_GetGamepadVendor(pad),
        p_SDL_GetGamepadProduct(pad), p_SDL_GetGamepadName(pad), 0);
}

static void map_dualshock4(SDL_JoystickID id)
{
    unsigned product = p_SDL_GetJoystickProductForID(id);
    if (p_SDL_GetJoystickVendorForID(id) != 0x054c ||
        (product != 0x05c4 && product != 0x09cc))
        return;

    /* The Windows.Gaming.Input raw DualShock 4 exposes six axes, a POV
     * hat and fourteen buttons. Keep SDL's own HIDAPI mapping when present. */
    SDL_Joystick *joystick = p_SDL_OpenJoystick(id);
    if (!joystick)
        return;
    int matches = p_SDL_GetNumJoystickAxes(joystick) == 6 &&
                  p_SDL_GetNumJoystickButtons(joystick) == 14 &&
                  p_SDL_GetNumJoystickHats(joystick) == 1;
    p_SDL_CloseJoystick(joystick);
    if (!matches)
        return;

    char guid[33], mapping[384];
    p_SDL_GUIDToString(p_SDL_GetJoystickGUIDForID(id), guid, sizeof guid);
    snprintf(mapping, sizeof mapping,
             "%s,DualShock 4,a:b1,b:b2,x:b0,y:b3,leftshoulder:b4,rightshoulder:b5,"
             "back:b8,start:b9,leftstick:b10,rightstick:b11,guide:b12,touchpad:b13,"
             "leftx:a0,lefty:a1,rightx:a2,righty:a5,lefttrigger:a3,righttrigger:a4,"
             "dpup:h0.1,dpright:h0.2,dpdown:h0.4,dpleft:h0.8,type:ps4,platform:Windows,",
             guid);
    if (p_SDL_AddGamepadMapping(mapping) < 0)
        fprintf(stderr, "[INPUT] Could not map DualShock 4: %s\n", p_SDL_GetError());
}

static void scan_gamepads(void)
{
    int count = 0;
    SDL_JoystickID *ids = p_SDL_GetJoysticks(&count);
    for (int i = 0; i < count; i++) {
        SDL_JoystickID id = ids[i];
        if (!p_SDL_IsGamepad(id))
            map_dualshock4(id);
        if (!p_SDL_IsGamepad(id))
            continue;
        SDL_Gamepad *pad = p_SDL_OpenGamepad(id);
        if (!pad)
            continue;
        xbox_pad_family family = pad_family(pad);
        if (family != XBOX_PAD_FAMILY_XBOX_MODERN) {
            s_pad = pad;
            s_family = family;
            fprintf(stderr, "[INPUT] SDL controller: %s (%04x:%04x)\n",
                    p_SDL_GetGamepadName(pad), (unsigned)p_SDL_GetGamepadVendor(pad),
                    (unsigned)p_SDL_GetGamepadProduct(pad));
            break;
        }
        p_SDL_CloseGamepad(pad);
    }
    p_SDL_free(ids);
}

int xbox_WindowsGamepadRead(XBOX_HOST_SDL_GAMEPAD *pad, xbox_pad_family *family)
{
    if (!s_sdl)
        return 0;
    ULONGLONG now = GetTickCount64();
    if (!s_pad && now < s_next_scan)
        return 0;
    p_SDL_UpdateGamepads();
    if (s_pad && !p_SDL_GamepadConnected(s_pad)) {
        p_SDL_CloseGamepad(s_pad);
        s_pad = NULL;
    }
    if (!s_pad && now >= s_next_scan) {
        s_next_scan = now + XBOX_INPUT_PROBE_MS;
        scan_gamepads();
    }
    if (!s_pad)
        return 0;
    for (int b = 0; b < XBOX_SDL_BUTTON_COUNT; b++)
        pad->button[b] = p_SDL_GetGamepadButton(s_pad, (SDL_GamepadButton)b);
    for (int a = 0; a < XBOX_SDL_AXIS_COUNT; a++)
        pad->axis[a] = p_SDL_GetGamepadAxis(s_pad, (SDL_GamepadAxis)a);
    *family = s_family;
    return 1;
}

DWORD xbox_WindowsGamepadRumble(const XBOX_VIBRATION *vibration)
{
    if (!s_pad)
        return ERROR_DEVICE_NOT_CONNECTED;
    p_SDL_RumbleGamepad(s_pad, vibration->wLeftMotorSpeed,
                      vibration->wRightMotorSpeed, XBOX_RUMBLE_EFFECT_MS);
    return ERROR_SUCCESS;
}

void xbox_WindowsGamepadShutdown(void)
{
    if (!s_sdl)
        return;
    if (s_pad) {
        p_SDL_CloseGamepad(s_pad);
        s_pad = NULL;
    }
    p_SDL_QuitSubSystem(SDL_INIT_GAMEPAD);
    FreeLibrary(s_sdl);
    s_sdl = NULL;
}
