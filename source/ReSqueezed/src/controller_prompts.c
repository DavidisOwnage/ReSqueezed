#include "controller_prompts.h"
#include "nv2a_texrep.h"
#include "pad_glyphs.h"
#include <math.h>

enum { COLORED = 1, MONOCHROME, COLORED_SWAPPED, MONOCHROME_SWAPPED };
enum { CROSS, CIRCLE, SQUARE, TRIANGLE, BUTTON_COUNT };

static uint32_t images[4][BUTTON_COUNT + 1][32 * 32];
static struct nv2a_texrep_variant variants[BUTTON_COUNT + 1][4];
static const struct nv2a_texrep_rect full_button = {0, 0, 32, 32};
static const struct nv2a_texrep_rect full_pair = {0, 0, 32, 16};

/* Hashes of level-zero P8 indices in the Xbox disc's ui.dat. The three
 * copies of the Y prompt share the same indices and palette. */
static const struct nv2a_texrep_tex prompts[] = {
    {"Juiced A",   0xd0078b4f2f8110b4ULL, 0x0b, 32, 32, variants[CROSS], 4},
    {"Juiced B",   0x02d2a60a7f6e1622ULL, 0x0b, 32, 32, variants[CIRCLE], 4},
    {"Juiced X",   0x798ea60a9054ad7eULL, 0x0b, 32, 32, variants[SQUARE], 4},
    {"Juiced Y",   0x8c33748f2504b446ULL, 0x0b, 32, 32, variants[TRIANGLE], 4},
    {"Juiced X/B", 0xa8a00ff3c3061428ULL, 0x0b, 32, 16, variants[BUTTON_COUNT], 4},
};

static float line_distance(float x, float y, float ax, float ay, float bx, float by)
{
    float dx = bx - ax, dy = by - ay;
    float t = ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy);
    t = fminf(1.0f, fmaxf(0.0f, t));
    return hypotf(x - ax - t * dx, y - ay - t * dy);
}

static float symbol_distance(int button, float x, float y, float radius)
{
    float r = radius * 0.55f;
    switch (button) {
    case CROSS:
        return fminf(line_distance(x, y, -r, -r, r, r),
                     line_distance(x, y, -r, r, r, -r));
    case CIRCLE:
        return fabsf(hypotf(x, y) - r);
    case SQUARE:
        return fminf(line_distance(fabsf(x), fabsf(y), 0, r, r, r),
                     line_distance(fabsf(x), fabsf(y), r, 0, r, r));
    default:
        return fminf(line_distance(fabsf(x), y, 0, -r, r, r * 0.8f),
                     line_distance(fabsf(x), y, 0, r * 0.8f, r, r * 0.8f));
    }
}

static void draw_button(uint32_t *image, int height, float cx, float cy,
                        float radius, int button, int monochrome)
{
    static const unsigned colors[BUTTON_COUNT][3] = {
        {145, 188, 255}, {255, 130, 128}, {249, 150, 218}, {121, 231, 197}
    };
    for (int y = 0; y < height; y++)
        for (int x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++) {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float distance = hypotf(dx, dy);
            float coverage = fminf(1.0f, fmaxf(0.0f, radius + 0.5f - distance));
            if (!coverage || x < 0 || x >= 32)
                continue;
            float weight = radius > 10.0f ? 1.3f : 0.85f;
            float mark = fminf(1.0f, fmaxf(0.0f, weight + 0.5f -
                                          symbol_distance(button, dx, dy, radius)));
            unsigned shade = distance > radius - 1.5f ? 102u : 30u;
            unsigned pixel = (unsigned)(255.0f * coverage + 0.5f) << 24;
            for (int c = 0; c < 3; c++) {
                unsigned ink = monochrome ? 238u : colors[button][c];
                unsigned value = (unsigned)(shade + (ink - shade) * mark + 0.5f);
                pixel |= value << (16 - c * 8);
            }
            image[y * 32 + x] = pixel;
        }
}

static void change_prompts(int set)
{
    int variant = 0;
    switch (set) {
    case XBOX_GLYPH_SET_PLAYSTATION:          variant = COLORED; break;
    case XBOX_GLYPH_SET_PLAYSTATION5:         variant = MONOCHROME; break;
    case XBOX_GLYPH_SET_PLAYSTATION_SWAPPED:  variant = COLORED_SWAPPED; break;
    case XBOX_GLYPH_SET_PLAYSTATION5_SWAPPED: variant = MONOCHROME_SWAPPED; break;
    }
    nv2a_texrep_set_variant(variant);
}

void juiced_InitControllerPrompts(void)
{
    for (int v = 0; v < 4; v++) {
        int swapped = v >= 2;
        int monochrome = v & 1;
        for (int b = 0; b < BUTTON_COUNT; b++) {
            draw_button(images[v][b], 32, 16.0f, 16.5f, 12.5f, b ^ swapped, monochrome);
            variants[b][v] = (struct nv2a_texrep_variant){images[v][b], &full_button, 1};
        }
        draw_button(images[v][BUTTON_COUNT], 16, 8.0f, 8.0f, 7.0f, SQUARE ^ swapped, monochrome);
        draw_button(images[v][BUTTON_COUNT], 16, 24.0f, 8.0f, 7.0f, CIRCLE ^ swapped, monochrome);
        variants[BUTTON_COUNT][v] = (struct nv2a_texrep_variant){
            images[v][BUTTON_COUNT], &full_pair, 1
        };
    }
    nv2a_texrep_register(prompts, sizeof prompts / sizeof prompts[0]);
    xbox_InputSetGlyphListener(change_prompts);
}
