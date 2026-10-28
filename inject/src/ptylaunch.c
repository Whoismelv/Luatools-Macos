/*
 * ptylaunch — run a child under a controlling pty.
 *
 * Steam's inner client re-execs through its bootstrapper when it has no
 * controlling terminal, which drops DYLD_* on the floor. Starting it under a
 * pty keeps the injection alive.
 *
 * Usage: ptylaunch /path/to/steam_osx [args...]
 */

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <pty.h>
#include <sys/wait.h>

int main(int argc, char **argv)
{
    if (argc < 2) {
        fprintf(stderr, "usage: %s <program> [args...]\n", argv[0]);
        return 2;
    }

    int master = -1;
    pid_t pid = forkpty(&master, NULL, NULL, NULL);

    if (pid < 0) {
        perror("forkpty");
        return 1;
    }

    if (pid == 0) {
        execv(argv[1], &argv[1]);
        perror("execv");
        _exit(127);
    }

    /* Pump the pty so the child never blocks on write(). */
    char buf[4096];
    for (;;) {
        ssize_t n = read(master, buf, sizeof(buf));
        if (n > 0) {
            fwrite(buf, 1, (size_t)n, stdout);
            fflush(stdout);
            continue;
        }
        if (n < 0 && errno == EINTR) continue;
        break;
    }

    int status = 0;
    waitpid(pid, &status, 0);
    close(master);

    if (WIFEXITED(status))   return WEXITSTATUS(status);
    if (WIFSIGNALED(status)) return 128 + WTERMSIG(status);
    return 1;
}
