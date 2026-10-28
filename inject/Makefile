# luatools-inject — macOS injection harness
# Build on a Mac with Xcode command line tools.

CC      ?= clang
CFLAGS  ?= -O2 -Wall -Wextra -fPIC
LDFLAGS ?=

SRC     := src
OUT     := build

DYLIB   := $(OUT)/luatools-inject.dylib
PTY     := $(OUT)/ptylaunch

ARCHS   := -arch arm64 -arch x86_64

all: $(DYLIB) $(PTY)

$(OUT):
	@mkdir -p $(OUT)

$(DYLIB): $(SRC)/inject.c $(SRC)/pattern.c $(SRC)/pattern.h | $(OUT)
	$(CC) $(CFLAGS) $(ARCHS) -dynamiclib \
		-install_name @rpath/luatools-inject.dylib \
		-o $@ $(SRC)/inject.c $(SRC)/pattern.c \
		-framework Foundation $(LDFLAGS)

$(PTY): $(SRC)/ptylaunch.c | $(OUT)
	$(CC) $(CFLAGS) $(ARCHS) -o $@ $(SRC)/ptylaunch.c -lutil

clean:
	rm -rf $(OUT)

.PHONY: all clean
