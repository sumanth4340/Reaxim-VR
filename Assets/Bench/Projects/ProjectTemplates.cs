// ProjectTemplates.cs — the three starter templates for the
// Create Project screen: Blank, Raspberry Pi starter, Jetson Nano
// starter. Templates only declare data; spawning parts is Task 5/6.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bench.Projects
{
    public static class ProjectTemplates
    {
        public const string Blank = "blank";
        public const string PiStarter = "pi-starter";
        public const string JetsonStarter = "jetson-starter";

        public static readonly string[] All = { Blank, PiStarter, JetsonStarter };

        public static string DisplayName(string template) => template switch
        {
            PiStarter => "Raspberry Pi starter",
            JetsonStarter => "Jetson Nano starter",
            _ => "Blank",
        };

        public static string DefaultBoard(string template) => template switch
        {
            JetsonStarter => "JetsonNano",
            _ => "RaspberryPi4B",
        };

        public static List<ProjectPart> StarterParts(string template)
        {
            var parts = new List<ProjectPart>();
            if (template == Blank) return parts;
            string boardKind = template == JetsonStarter ? "BoardJetsonNano" : "BoardRPi4B";
            parts.Add(Make(boardKind, new Vector3(0f, 0.02f, 0f), Vector3.zero));
            parts.Add(Make("Breadboard", new Vector3(0f, 0.01f, 0.32f), Vector3.zero));
            parts.Add(Make("LED", new Vector3(-0.1f, 0.01f, 0.32f), Vector3.zero,
                "{\"color\":\"red\"}"));
            parts.Add(Make("Button", new Vector3(0.1f, 0.01f, 0.32f), Vector3.zero));
            return parts;
        }

        private static ProjectPart Make(string kind, Vector3 pos, Vector3 rot,
            string paramsJson = "")
        {
            return new ProjectPart
            {
                id = Guid.NewGuid().ToString(),
                type = kind,
                position = pos,
                rotationEuler = rot,
                paramsJson = paramsJson,
            };
        }
    }
}
