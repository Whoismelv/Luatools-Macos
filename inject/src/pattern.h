/*
 * pattern.h / pattern.c
 *
 * IDA-style byte pattern matcher with wildcards.
 *   "48 8B 05 ?? ?? ?? ?? C3"   -> 8 fixed bytes, 4 wildcard bytes
 */

#ifndef LUATOOLS_PATTERN_H
#define LUATOOLS_PATTERN_H

#include <stddef.h>
#include <stdint.h>

#define PATTERN_MAX_BYTES 128

typedef struct {
    const char *name;
    const char *pattern;
} sig_entry_t;

/* Returns first match in [base, base+size), or NULL. */
const uint8_t *pattern_scan(const uint8_t *base, size_t size, const char *pattern);

/* Compiles a pattern string. Returns byte length, or -1 on bad input. */
int pattern_compile(const char *pattern,
                    uint8_t *bytes,
                    uint8_t *mask,
                    size_t  max);

#endif /* LUATOOLS_PATTERN_H */
