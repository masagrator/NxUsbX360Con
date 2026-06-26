/*
 * SwitchInputClient  v1.0.0
 * ──────────────────────────────────────────────────────────────────────────
 * Nintendo Switch homebrew that reads the default gamepad and streams
 * 18-byte input packets over USB to a connected Windows PC.
 *
 * Build with devkitPro / libnx:
 *   cd switch-client && make
 *
 * Protocol (18 bytes, little-endian):
 *   [0]     magic 0xAB
 *   [1]     magic 0xCD
 *   [2]     version 0x01
 *   [3]     reserved 0x00
 *   [4-7]   buttons  (uint32 – lower 16 bits of HidNpadButton)
 *   [8-9]   left_x   (int16, −32767…32767)
 *   [10-11] left_y   (int16)
 *   [12-13] right_x  (int16)
 *   [14-15] right_y  (int16)
 *   [16]    left_trig  (0 or 255 — ZL is digital)
 *   [17]    right_trig (0 or 255 — ZR is digital)
 */

#include <switch.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

/* ── Protocol constants ─────────────────────────────────────────────────── */
#define MAGIC_0      0xABu
#define MAGIC_1      0xCDu
#define PKT_VERSION  0x01u
#define PKT_SIZE     18u
#define POLL_HZ      120u
#define POLL_NS      (1000000000LL / POLL_HZ)  /* ~8.33 ms */

/* ── Packed packet struct ────────────────────────────────────────────────── */
typedef struct __attribute__((packed)) {
    uint8_t  magic[2];
    uint8_t  version;
    uint8_t  reserved;
    uint32_t buttons;
    int16_t  left_x;
    int16_t  left_y;
    int16_t  right_x;
    int16_t  right_y;
    uint8_t  left_trig;
    uint8_t  right_trig;
} InputPacket;

_Static_assert(sizeof(InputPacket) == PKT_SIZE, "InputPacket must be 18 bytes");

/* ── Button label table (for on-screen display) ─────────────────────────── */
typedef struct { HidNpadButton bit; const char *label; } ButtonLabel;

static const ButtonLabel LABELS[] = {
    { HidNpadButton_A,      "A"    },
    { HidNpadButton_B,      "B"    },
    { HidNpadButton_X,      "X"    },
    { HidNpadButton_Y,      "Y"    },
    { HidNpadButton_L,      "L"    },
    { HidNpadButton_R,      "R"    },
    { HidNpadButton_ZL,     "ZL"   },
    { HidNpadButton_ZR,     "ZR"   },
    { HidNpadButton_Plus,   "+"    },
    { HidNpadButton_Minus,  "-"    },
    { HidNpadButton_StickL, "LS"   },
    { HidNpadButton_StickR, "RS"   },
    { HidNpadButton_Up,     "U"    },
    { HidNpadButton_Down,   "D"    },
    { HidNpadButton_Left,   "L"    },
    { HidNpadButton_Right,  "R"    },
};
#define NUM_LABELS (sizeof(LABELS) / sizeof(LABELS[0]))

/* ── Helpers ────────────────────────────────────────────────────────────── */
static void draw_header(void)
{
    printf("\033[2J\033[H"); /* clear + home */
    printf("--------------------------------------------\n");
    printf("|      SwitchInputClient   v1.0.0          |\n");
    printf("|      USB -> XInput Bridge                |\n");
    printf("--------------------------------------------\n\n");
}

static void draw_buttons(u64 held)
{
    printf("\033[8;0H"); /* row 6 */
    printf("Buttons : ");
    bool any = false;
    for (size_t i = 0; i < NUM_LABELS; i++) {
        if (held & LABELS[i].bit) {
            printf("%s ", LABELS[i].label);
            any = true;
        }
    }
    if (!any) printf("(none)         ");
    printf("\n");
}

/* ── Main ───────────────────────────────────────────────────────────────── */
int main(int argc, char *argv[])
{
    (void)argc; (void)argv;

    consoleInit(NULL);
    draw_header();

    /* ── USB init ─────────────────────────────────────────────────────── */
    printf("\n[USB] Initialising usbComms...\n");
    consoleUpdate(NULL);
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);

    Result rc = usbCommsInitialize();
    if (R_FAILED(rc)) {
        printf("[USB] ERROR: usbCommsInitialize() = 0x%08X\n", rc);
        printf("      Ensure no other app owns the USB gadget service.\n");
        printf("      Press any button to exit.\n");
        consoleUpdate(NULL);

        /* Wait for input so user can read the error */
        PadState errPad;
        padInitializeDefault(&errPad);
        while (appletMainLoop()) {
            padUpdate(&errPad);
            if (padGetButtonsDown(&errPad)) break;
            svcSleepThread(POLL_NS);
        }
        consoleExit(NULL);
        return 1;
    }

    printf("[USB] Ready - connect cable and start the PC server.\n");
    printf("----------------------------------------------------\n");
    consoleUpdate(NULL);

    /* ── Pad init ─────────────────────────────────────────────────────── */
    PadState pad;
    padInitializeDefault(&pad);

    /* ── Packet template ──────────────────────────────────────────────── */
    alignas(0x1000) InputPacket pkt;
    memset(&pkt, 0, sizeof(pkt));
    pkt.magic[0] = MAGIC_0;
    pkt.magic[1] = MAGIC_1;
    pkt.version  = PKT_VERSION;

    /* ── Counters ─────────────────────────────────────────────────────── */
    uint64_t total_bytes = 0;
    uint64_t send_ok     = 0;
    uint64_t send_fail   = 0;

    /* ── Main loop ────────────────────────────────────────────────────── */
    while (appletMainLoop())
    {
        uint64_t startTick = svcGetSystemTick();
        padUpdate(&pad);
        u64 held = padGetButtons(&pad);

        /* Graceful exit on [+] */
        if ((held & 0b1111000000001111) == 0b1111000000001111) break;
        static bool backlightOff = false;
        static uint64_t lastTick = 0;
        if (held & HidNpadButton_Plus) {
            if (!lastTick) lastTick = svcGetSystemTick();
            if ((svcGetSystemTick() - lastTick) > 19200000 * 3) {
                lblInitialize();
                if (backlightOff == false) {
                    lblSwitchBacklightOff(0);
                    backlightOff = true;
                }
                else {
                    lblSwitchBacklightOn(0);
                    backlightOff = false;
                }
                lblExit();
                lastTick = 0;
            }
        }
        else lastTick = 0;

        /* Read analogue sticks */
        HidAnalogStickState ls = padGetStickPos(&pad, 0);
        HidAnalogStickState rs = padGetStickPos(&pad, 1);

        /* Fill packet
         * We send ZL/ZR both as trigger bytes (0 or 255, since they are
         * digital on Switch) AND as bits inside the buttons bitmask.
         * The PC server uses the trigger bytes for the analogue trigger
         * axes and can ignore the bits in the bitmask for those buttons.
         */
        pkt.buttons    = (uint32_t)(held & 0xFFFFu);
        pkt.left_x     = (int16_t)ls.x;
        pkt.left_y     = (int16_t)ls.y;
        pkt.right_x    = (int16_t)rs.x;
        pkt.right_y    = (int16_t)rs.y;
        pkt.left_trig  = (held & HidNpadButton_ZL) ? 0xFFu : 0x00u;
        pkt.right_trig = (held & HidNpadButton_ZR) ? 0xFFu : 0x00u;

        /* Send packet over USB bulk */
        static u32 urbId = 0;
        Result rc = 0;
        if (urbId == 0) rc = usbCommsWriteAsync(&pkt, PKT_SIZE, &urbId, 0);
        static bool initialized = false;
        if (R_FAILED(rc)) {
            consoleUpdate(NULL);
            continue;
        }
        u32 written = 0;
        rc = usbCommsGetWriteResult(urbId, &written, 0);
        if (R_FAILED(rc)) {
            consoleUpdate(NULL);
            continue;
        }
        if (written == PKT_SIZE) {
            total_bytes += written;
            send_ok++;
            urbId = 0;
        } else {
            send_fail++;
            consoleUpdate(NULL);
            continue;
        }
        if (!initialized) {
            for (size_t i = 0; i < 8; i++) {
                hidEnableUnintendedHomeButtonInputProtection(i, true);
            }
            hidEnableUnintendedHomeButtonInputProtection(0x10, true);
            hidEnableUnintendedHomeButtonInputProtection(0x20, true);
            appletSetMediaPlaybackState(false);
            initialized = true;
        }

        if (backlightOff == false) {
            consoleClear();
            draw_header();
            printf("Exit by pressing All D-Pad buttons and X + Y + A + B. Home button is disabled.\n");
            printf("To turn off/on backlight press + for 3 seconds.\n");
            draw_buttons(held);
            printf("LStick  : X=%7d  Y=%7d   \n", ls.x, ls.y);
            printf("RStick  : X=%7d  Y=%7d   \n", rs.x, rs.y);
            printf("Triggers: ZL=%3u   ZR=%3u      \n",
                    pkt.left_trig, pkt.right_trig);
            printf("\n");
            printf("USB OK  : %llu pkts             \n",
                    (unsigned long long)send_ok);
            printf("USB FAIL: %llu pkts             \n",
                    (unsigned long long)send_fail);
            printf("Total B : %llu bytes            \n",
                    (unsigned long long)total_bytes);
            consoleUpdate(NULL);
        }
        else {
            uint64_t deltaTick = svcGetSystemTick() - startTick;
            uint64_t nanoseconds = armTicksToNs(deltaTick);
            if (nanoseconds < 15'000'000)
               svcSleepThread(15'000'000 - nanoseconds);
        }
    }

    for (size_t i = 0; i < 8; i++) {
        hidEnableUnintendedHomeButtonInputProtection(i, false);
    }
    hidEnableUnintendedHomeButtonInputProtection(0x10, false);
    hidEnableUnintendedHomeButtonInputProtection(0x20, false);
    appletSetMediaPlaybackState(true);
    lblInitialize();
    lblSwitchBacklightOn(1000);
    lblExit();

    /* ── Cleanup ──────────────────────────────────────────────────────── */
    printf("\n\n[USB] Closing connection...\n");
    consoleUpdate(NULL);
    svcSleepThread(500000000LL); /* 0.5 s so user can read message */

    usbCommsExit();
    consoleExit(NULL);
    return 0;
}
