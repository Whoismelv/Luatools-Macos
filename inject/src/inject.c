/*
 * luatools-inject.dylib
 *
 * Loaded into steam_osx via DYLD_INSERT_LIBRARIES. Does three things:
 *   1. proves the injection landed (log + env dump)
 *   2. locates the main image's __TEXT segment (what we pattern-scan)
 *   3. registers a signature table and resolves it against the live image
 *
 * Nothing here touches licenses yet. This is the instrument that tells us
 * which Steam functions we can actually find on arm64.
 *
 * Build: see ../Makefile
 */

#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <unistd.h>
#include <mach-o/dyld.h>
#include <mach-o/loader.h>
#include <mach-o/nlist.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <time.h>

#include "pattern.h"

static FILE *g_log;

static void log_open(void)
{
    if (g_log) return;
    const char *path = getenv("LUATOOLS_LOG");
    if (!path) path = "/tmp/luatools-inject.log";
    g_log = fopen(path, "a");
    if (!g_log) g_log = stderr;
}

static void llog(const char *fmt, ...)
{
    log_open();
    time_t now = time(NULL);
    struct tm tm;
    localtime_r(&now, &tm);
    fprintf(g_log, "[%02d:%02d:%02d] ", tm.tm_hour, tm.tm_min, tm.tm_sec);

    va_list ap;
    va_start(ap, fmt);
    vfprintf(g_log, fmt, ap);
    va_end(ap);

    fputc('\n', g_log);
    fflush(g_log);
}

/* ------------------------------------------------------------------ */
/* Mach-O image inspection                                            */
/* ------------------------------------------------------------------ */

typedef struct {
    const uint8_t *base;      /* start of __TEXT            */
    size_t         size;      /* __TEXT file size           */
    const char    *name;      /* image path                 */
    cpu_type_t     cpu;
} image_info_t;

static int image_text_segment(const struct mach_header_64 *mh,
                              const uint8_t *load_cmds,
                              image_info_t *out)
{
    const struct load_command *lc = (const struct load_command *)load_cmds;

    for (uint32_t i = 0; i < mh->ncmds; i++) {
        if (lc->cmd == LC_SEGMENT_64) {
            const struct segment_command_64 *seg = (const struct segment_command_64 *)lc;
            if (strcmp(seg->segname, "__TEXT") == 0) {
                out->base = (const uint8_t *)mh + seg->fileoff;
                out->size = (size_t)seg->filesize;
                return 0;
            }
        }
        lc = (const struct load_command *)((const uint8_t *)lc + lc->cmdsize);
    }
    return -1;
}

static int describe_image(uint32_t index, image_info_t *out)
{
    const struct mach_header *mh = _dyld_get_image_header(index);
    if (!mh) return -1;
    if (mh->magic != MH_MAGIC_64) return -1;

    const struct mach_header_64 *mh64 = (const struct mach_header_64 *)mh;
    memset(out, 0, sizeof(*out));
    out->name = _dyld_get_image_name(index);
    out->cpu  = mh64->cputype;

    return image_text_segment(mh64, (const uint8_t *)mh + sizeof(*mh64), out);
}

/* ------------------------------------------------------------------ */
/* Signature table                                                    */
/* ------------------------------------------------------------------ */

/*
 * Each entry is a candidate for the client-side function that decides
 * whether an app is owned. Patterns are IDA-style with ?? wildcards.
 * These are PLACEHOLDERS until we dump real bytes on a Mac.
 */
static const sig_entry_t g_signatures[] = {
    /* name                          pattern */
    { "probe.always_exists",         "FF 03 00 D1 FD 7B BF A9" },
    { "probe.ret_zero_arm64",        "00 00 80 D2 C0 03 5F D6" },
    { "probe.strcmp_like",           "?? 00 40 F9 ?? ?? ?? ?? ??" },
};
static const size_t g_signature_count =
    sizeof(g_signatures) / sizeof(g_signatures[0]);

static void resolve_signatures(const image_info_t *img)
{
    llog("scanning %zu signature(s) over __TEXT 0x%zx bytes",
         g_signature_count, img->size);

    for (size_t i = 0; i < g_signature_count; i++) {
        const uint8_t *hit = pattern_scan(img->base, img->size,
                                          g_signatures[i].pattern);
        if (hit) {
            llog("  [hit]  %-24s at %p (__TEXT+0x%lx)",
                 g_signatures[i].name, hit,
                 (unsigned long)(hit - img->base));
        } else {
            llog("  [miss] %-24s", g_signatures[i].name);
        }
    }
}

/* ------------------------------------------------------------------ */
/* Entry                                                              */
/* ------------------------------------------------------------------ */

__attribute__((constructor))
static void luatools_init(void)
{
    llog("======== injection landed ========");
    llog("pid=%d ppid=%d", getpid(), getppid());

    uint32_t count = _dyld_image_count();
    llog("dyld images: %u", count);

    for (uint32_t i = 0; i < count; i++) {
        image_info_t img;
        if (describe_image(i, &img) != 0) continue;
        llog("  [%02u] %s  __TEXT=0x%zx", i, img.name ? img.name : "?", img.size);
    }

    /* Image 0 is the executable that dyld launched: steam_osx. */
    image_info_t main_img;
    if (describe_image(0, &main_img) == 0) {
        llog("main image: %s", main_img.name ? main_img.name : "?");
        if (getenv("LUATOOLS_SCAN") != NULL) {
            resolve_signatures(&main_img);
        } else {
            llog("set LUATOOLS_SCAN=1 to run signature scan");
        }
    } else {
        llog("could not describe main image");
    }

    llog("======== ready ========");
}

__attribute__((destructor))
static void luatools_fini(void)
{
    llog("======== process exiting ========");
    if (g_log && g_log != stderr) fclose(g_log);
}
