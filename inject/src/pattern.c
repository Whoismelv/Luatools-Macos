#include "pattern.h"

#include <string.h>

static int hexval(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

int pattern_compile(const char *pattern,
                    uint8_t *bytes,
                    uint8_t *mask,
                    size_t max)
{
    size_t n = 0;

    while (*pattern && n < max) {
        while (*pattern == ' ') pattern++;

        if (pattern[0] == '?' && pattern[1] == '?') {
            bytes[n] = 0;
            mask[n]  = 0;
            n++;
            pattern += 2;
            continue;
        }

        int hi = hexval(pattern[0]);
        int lo = hexval(pattern[1]);
        if (hi < 0 || lo < 0) return -1;

        bytes[n] = (uint8_t)((hi << 4) | lo);
        mask[n]  = 0xFF;
        n++;
        pattern += 2;
    }

    return (int)n;
}

const uint8_t *pattern_scan(const uint8_t *base, size_t size, const char *pattern)
{
    uint8_t bytes[PATTERN_MAX_BYTES];
    uint8_t mask[PATTERN_MAX_BYTES];

    int len = pattern_compile(pattern, bytes, mask, PATTERN_MAX_BYTES);
    if (len <= 0) return NULL;

    size_t plen = (size_t)len;
    if (size < plen) return NULL;

    /* Skip leading wildcard run so the inner loop can compare on a fixed byte. */
    size_t anchor = 0;
    while (anchor < plen && mask[anchor] == 0) anchor++;
    if (anchor == plen) return NULL;

    const size_t limit = size - plen;

    for (size_t i = 0; i <= limit; i++) {
        if (base[i + anchor] != bytes[anchor]) continue;

        size_t j = 0;
        for (; j < plen; j++) {
            if (mask[j] && base[i + j] != bytes[j]) break;
        }
        if (j == plen) return base + i;
    }

    return NULL;
}
