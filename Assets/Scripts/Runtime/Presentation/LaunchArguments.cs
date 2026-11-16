using System;

namespace DotsCrowd
{
    /// <summary>Command-line options for players: <c>-agents N</c> overrides the crowd size, <c>-benchmark</c> runs the timed benchmark.</summary>
    public static class LaunchArguments
    {
        public static bool Has(string name)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool TryGetInt(string name, out int value)
        {
            value = 0;
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                    return int.TryParse(arguments[i + 1], out value);
            }

            return false;
        }
    }
}
