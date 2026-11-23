#ifndef XBOX_WINDOWS_GAMEPAD_H
#define XBOX_WINDOWS_GAMEPAD_H

#include "input_map.h"
#include "pad_glyphs.h"

void xbox_WindowsGamepadInit(void);
void xbox_WindowsGamepadShutdown(void);
int xbox_WindowsGamepadRead(XBOX_HOST_SDL_GAMEPAD *pad, xbox_pad_family *family);
DWORD xbox_WindowsGamepadRumble(const XBOX_VIBRATION *vibration);

#endif
