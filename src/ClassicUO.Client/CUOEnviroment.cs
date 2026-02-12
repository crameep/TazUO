// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ClassicUO
{
    internal static class CUOEnviroment
    {
        public static Thread GameThread;
        public static float DPIScaleFactor = 1.0f;
        public static bool NoSound;
        public static string[] Args;
        public static string[] Plugins;
        public static bool Debug;
        public static bool IsHighDPI;
        public static uint CurrentRefreshRate;
        public static bool SkipLoginScreen;
        public static bool NoServerPing;
        public static Assembly Assembly => Assembly.GetEntryAssembly();

        public static readonly bool IsUnix = Environment.OSVersion.Platform != PlatformID.Win32NT && Environment.OSVersion.Platform != PlatformID.Win32Windows && Environment.OSVersion.Platform != PlatformID.Win32S && Environment.OSVersion.Platform != PlatformID.WinCE;

        public static readonly string Version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0";
        public static readonly string ExecutablePath = ResolveBasePath();

        private static string ResolveBasePath()
        {
            var baseDir = AppContext.BaseDirectory;

            // If running inside a macOS .app bundle, resolve to the directory
            // containing the .app so Data/, LegionScripts/, etc. live alongside it.
            // e.g. /path/to/TazUO/TazUO.app/Contents/MacOS/ → /path/to/TazUO/
            var macOSSuffix = ".app" + Path.DirectorySeparatorChar + "Contents" + Path.DirectorySeparatorChar + "MacOS";
            var idx = baseDir.IndexOf(macOSSuffix, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var appPath = baseDir.Substring(0, idx + 4); // up to and including .app
                return Path.GetDirectoryName(appPath) + Path.DirectorySeparatorChar;
            }

            return baseDir;
        }
    }
}