using System.Reflection;

[assembly: AssemblyTitle("NoHandsNeeded")]
[assembly: AssemblyProduct("NoHandsNeeded")]
[assembly: AssemblyCompany("MDM3D")]
[assembly: AssemblyCopyright("MDM3D")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.01")]

namespace Tastatur
{
    internal static class AppInfo
    {
        /// <summary>Versionsnummer, wie sie überall angezeigt wird.</summary>
        public const string Version = "1.01";

        /// <summary>Name des Programms (bis 1.01 hieß es Bildschirmtastatur bzw. Tastatur).</summary>
        public const string Name = "NoHandsNeeded";
    }
}
