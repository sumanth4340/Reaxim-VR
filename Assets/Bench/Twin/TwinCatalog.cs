// TwinCatalog.cs — the rpi-hvf twin's parts and pins, mirrored for MR.
// SOURCE OF TRUTH: ~/rpi-hvf (read 2026-11-17):
//  - BoardGeometry.pins (Scene/BoardGeometry.swift): 40-pin header,
//    PinKind .gpio/.v5/.v3/.gnd, BCM numbers + alt functions.
//  - ComponentKind (Lab/LabComponent.swift): LED, Button, Servo,
//    BME280, MCP3008, GPS. Breadboard in Lab/BreadboardModel.swift
//    (63 cols, rows a–e/f–j, +/- rails top+bottom).
//  - WiringEngine.swift: net priority GND > 5V > 3V3 > GPIO-out;
//    5V/3V3 are POWER sources (boolean when powered), GPIO is the
//    3.3 V logic. 5 V must NEVER drive a GPIO (Pin Mode warns).
// Live bridge values (levels, readings) arrive in Task 10; the
// catalog itself is firmware-like and validated against the twin
// schema here, not fetched per session.
using System.Collections.Generic;

namespace Bench.Twin
{
    public enum TwinPinKind { Gpio, Power5V, Power3V3, Ground }

    public struct TwinPin
    {
        public int physical;
        public string name;
        public int bcm;      // -1 when not a GPIO
        public TwinPinKind kind;
        public string alt;   // e.g. "I2C-1 SDA", null when none

        public bool IsLogic => kind == TwinPinKind.Gpio;
        public bool IsSupply => kind == TwinPinKind.Power5V
            || kind == TwinPinKind.Power3V3;
    }

    public struct TwinComponent
    {
        public string kind;       // twin ComponentKind string (+Breadboard)
        public string display;    // tile title
        public string sub;        // tile one-liner
        public string badge;      // tile badge, null when none
        public string[] pinTags;  // twin pin identity, in pin order
        public string glb;        // Assets/Models file, null = procedural
    }

    public static class TwinCatalog
    {
        /// <summary>GPIO logic levels are 3.3 V (BCM2711). The 5 V
        /// header pins are SUPPLY rails, not logic — feeding 5 V into
        /// a GPIO risks the board (Pin Mode Task 7 warns).</summary>
        public const float GpioLogicVolts = 3.3f;
        public const float Supply5VVolts = 5.0f;

        public static readonly TwinPin[] Header40 = {
            P(1, "3V3", -1, TwinPinKind.Power3V3, null),
            P(2, "5V", -1, TwinPinKind.Power5V, null),
            P(3, "GPIO2", 2, TwinPinKind.Gpio, "I2C-1 SDA"),
            P(4, "5V", -1, TwinPinKind.Power5V, null),
            P(5, "GPIO3", 3, TwinPinKind.Gpio, "I2C-1 SCL"),
            P(6, "GND", -1, TwinPinKind.Ground, null),
            P(7, "GPIO4", 4, TwinPinKind.Gpio, "GPCLK0"),
            P(8, "GPIO14", 14, TwinPinKind.Gpio, "UART0 TX"),
            P(9, "GND", -1, TwinPinKind.Ground, null),
            P(10, "GPIO15", 15, TwinPinKind.Gpio, "UART0 RX"),
            P(11, "GPIO17", 17, TwinPinKind.Gpio, null),
            P(12, "GPIO18", 18, TwinPinKind.Gpio, "PWM ch0"),
            P(13, "GPIO27", 27, TwinPinKind.Gpio, null),
            P(14, "GND", -1, TwinPinKind.Ground, null),
            P(15, "GPIO22", 22, TwinPinKind.Gpio, null),
            P(16, "GPIO23", 23, TwinPinKind.Gpio, null),
            P(17, "3V3", -1, TwinPinKind.Power3V3, null),
            P(18, "GPIO24", 24, TwinPinKind.Gpio, null),
            P(19, "GPIO10", 10, TwinPinKind.Gpio, "SPI0 MOSI"),
            P(20, "GND", -1, TwinPinKind.Ground, null),
            P(21, "GPIO9", 9, TwinPinKind.Gpio, "SPI0 MISO"),
            P(22, "GPIO25", 25, TwinPinKind.Gpio, null),
            P(23, "GPIO11", 11, TwinPinKind.Gpio, "SPI0 SCLK"),
            P(24, "GPIO8", 8, TwinPinKind.Gpio, "SPI0 CE0"),
            P(25, "GND", -1, TwinPinKind.Ground, null),
            P(26, "GPIO7", 7, TwinPinKind.Gpio, "SPI0 CE1"),
            P(27, "GPIO0", 0, TwinPinKind.Gpio, "ID EEPROM SDA"),
            P(28, "GPIO1", 1, TwinPinKind.Gpio, "ID EEPROM SCL"),
            P(29, "GPIO5", 5, TwinPinKind.Gpio, null),
            P(30, "GND", -1, TwinPinKind.Ground, null),
            P(31, "GPIO6", 6, TwinPinKind.Gpio, null),
            P(32, "GPIO12", 12, TwinPinKind.Gpio, "PWM ch0"),
            P(33, "GPIO13", 13, TwinPinKind.Gpio, "PWM ch1"),
            P(34, "GND", -1, TwinPinKind.Ground, null),
            P(35, "GPIO19", 19, TwinPinKind.Gpio, "PWM ch1"),
            P(36, "GPIO16", 16, TwinPinKind.Gpio, null),
            P(37, "GPIO26", 26, TwinPinKind.Gpio, null),
            P(38, "GPIO20", 20, TwinPinKind.Gpio, "SPI1 MOSI"),
            P(39, "GND", -1, TwinPinKind.Ground, null),
            P(40, "GPIO21", 21, TwinPinKind.Gpio, "SPI1 SCLK"),
        };

        public static readonly TwinComponent[] Components = {
            C("LED", "LED red", "2-pin · GPIO", "NEW",
                new[] { "+", "−" }, "LED.glb"),
            C("Button", "Button", "Momentary · GPIO", null,
                new[] { "pin 1", "pin 2" }, null),
            C("Servo", "Servo", "PWM signal", "PWM",
                new[] { "S", "+", "−" }, null),
            C("BME280", "BME280", "Temp/Hum/Press · I2C", "I2C",
                new[] { "+", "−", "C", "D" }, null),
            C("MCP3008", "MCP3008", "8-ch ADC · SPI", "SPI",
                new[] { "+", "−", "C", "S", "I", "O",
                    "0", "1", "2", "3", "4", "5", "6", "7" }, null),
            C("GPS", "GPS", "UART NMEA", "UART",
                new[] { "+", "−", "T", "R" }, null),
            C("Breadboard", "Breadboard", "63-col · rails", null,
                new string[0], "Breadboard_.glb"),
        };

        /// <summary>True physical max extent in metres, for
        /// placed-part sizing. Chips ride tiny carrier PCBs (as in
        /// real kits) so they stay grabbable; MCP3008 is the twin's
        /// 14-signal DIP, not the datasheet 16-pin.</summary>
        public static float RealSizeM(string kind)
        {
            switch (kind)
            {
                case "BoardRPi4B": return 0.085f;
                case "BoardJetsonNano": return 0.100f;
                case "Breadboard": return 0.165f;
                case "LED": return 0.009f;
                case "Button": return 0.012f;
                case "Servo": return 0.055f;
                case "BME280": return 0.020f;
                case "MCP3008": return 0.022f;
                case "GPS": return 0.030f;
                default: return 0f;
            }
        }

        public static int CountKind(TwinPinKind kind)
        {
            int n = 0;
            foreach (var p in Header40)
                if (p.kind == kind) n++;
            return n;
        }

        public static bool TryBcm(int physical, out int bcm, out string alt)
        {
            foreach (var p in Header40)
            {
                if (p.physical != physical) continue;
                bcm = p.bcm;
                alt = p.alt;
                return p.kind == TwinPinKind.Gpio;
            }
            bcm = -1;
            alt = null;
            return false;
        }

        private static TwinPin P(int physical, string name, int bcm,
            TwinPinKind kind, string alt) =>
            new TwinPin { physical = physical, name = name, bcm = bcm,
                kind = kind, alt = alt };

        private static TwinComponent C(string kind, string display,
            string sub, string badge, string[] pinTags, string glb) =>
            new TwinComponent { kind = kind, display = display, sub = sub,
                badge = badge, pinTags = pinTags, glb = glb };
    }
}
