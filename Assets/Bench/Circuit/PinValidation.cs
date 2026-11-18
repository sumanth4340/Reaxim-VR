// PinValidation.cs — live wiring rules (Task 7).
// Non-blocking toasts (icon + word + one sentence). Only true shorts
// BLOCK; everything else warns and lets the user proceed.
// Twin truth: 5V is supply-only, GPIO logic is 3.3V.
using System;
using System.Collections.Generic;

namespace Bench.Circuit
{
    public enum Verdict { Ok, Warn, Block }

    public class ValidationResult
    {
        public Verdict verdict = Verdict.Ok;
        public string title = "";
        public string message = "";
        public string fixLabel;      // null when no one-tap fix exists
        public Action fix;           // invoked by the Fix button
    }

    public static class PinValidation
    {
        public static bool IsSupply(string node, out bool is5V, out bool isGnd)
        {
            is5V = false; isGnd = false;
            if (node == null || !node.StartsWith("power:")) return false;
            if (!int.TryParse(node.Substring(6), out var phys)) return false;
            foreach (var p in Twin.TwinCatalog.Header40)
            {
                if (p.physical != phys) continue;
                is5V = p.kind == Twin.TwinPinKind.Power5V;
                isGnd = p.kind == Twin.TwinPinKind.Ground;
                return p.kind != Twin.TwinPinKind.Gpio;
            }
            return false;
        }

        public static bool IsGpio(string node) =>
            node != null && node.StartsWith("gpio:");

        /// <summary>Check a prospective wire before committing it.</summary>
        public static ValidationResult Check(string a, string b,
            CircuitModel model, Func<string, string> nearest3V3)
        {
            var r = new ValidationResult();
            bool aSup = IsSupply(a, out var a5, out var aGnd);
            bool bSup = IsSupply(b, out var b5, out var bGnd);
            // Rule 1: any supply-to-ground or supply-to-supply short BLOCKS.
            if ((aGnd && bSup && !bGnd) || (bGnd && aSup && !aGnd)
                || (aSup && !aGnd && bSup && !bGnd))
            {
                r.verdict = Verdict.Block;
                r.title = "Short blocked";
                r.message = ShortText(a, b) + " would short the supply.";
                return r;
            }
            // Rule 2: 5V into a 3.3V GPIO warns + offers reroute to 3V3.
            if ((a5 && IsGpio(b)) || (b5 && IsGpio(a)))
            {
                r.verdict = Verdict.Warn;
                r.title = "5V into GPIO";
                r.message = "GPIO logic is 3.3V — 5V risks the board.";
                string fiveCopy = a5 ? a : b, otherCopy = a5 ? b : a;
                r.fixLabel = "Use 3V3 instead";
                r.fix = () =>
                {
                    string alt = nearest3V3?.Invoke(fiveCopy);
                    if (!string.IsNullOrEmpty(alt))
                        PinMode.WireTool.Rewire(fiveCopy, otherCopy, alt);
                };
                return r;
            }
            // Rule 3: LED with no series resistor warns (twin models no
            // resistor part yet, so there is no auto-fix — honest warn).
            if (LedBare(a, b, model))
            {
                r.verdict = Verdict.Warn;
                r.title = "LED without resistor";
                r.message = "Add series resistance to limit LED current.";
                return r;
            }
            // Rule 4: two drivers, one net warns. GPIO counts only
            // when configured OUT (default IN is high-Z, not a driver).
            if (model != null && TwoDrivers(a, b, model))
            {
                r.verdict = Verdict.Warn;
                r.title = "Two drivers";
                r.message = "Two sources drive one net — check intent.";
                return r;
            }
            return r;
        }

        private static string ShortText(string a, string b) =>
            "Wiring " + Short(a) + " to " + Short(b);

        private static string Short(string node)
        {
            if (node == null) return "?";
            if (node.StartsWith("power:"))
            {
                IsSupply(node, out var is5, out var isGnd);
                if (isGnd) return "GND";
                return is5 ? "5V" : "3V3";
            }
            return node;
        }

        private static bool LedBare(string a, string b, CircuitModel model)
        {
            // True when either endpoint is an LED leg: the twin has no
            // resistor part, so every LED wire trips this warn rule.
            return IsLedLeg(a) || IsLedLeg(b);
        }

        private static bool IsLedLeg(string node)
        {
            if (node == null || !node.StartsWith("comp:")) return false;
            // comp:{partId}:{idx} — resolve kind via live parts.
            foreach (var p in Workspace.WorkspacePhysics.PartsSnapshot())
            {
                if (p != null && node.StartsWith("comp:" + p.partId + ":"))
                    return p.type == "LED";
            }
            return false;
        }

        private static bool TwoDrivers(string a, string b, CircuitModel model)
        {
            int drivers = 0;
            foreach (var n in new[] { a, b })
            {
                if (IsSupply(n, out _, out var gnd) && !gnd) { drivers++; continue; }
                if (IsGpio(n) && int.TryParse(n.Substring(5), out var bcm)
                    && model.IsGpioOut(bcm))
                    drivers++;
            }
            return drivers >= 2;
        }
    }
}
