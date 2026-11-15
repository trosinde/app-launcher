using System.Security.Principal;

// AppStart TestHelper: a helper program of the TestApp. When it runs, the start was allowed.
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"AppStart TestHelper läuft als {identity.Name}, Admin-Rechte: {(elevated ? "ja" : "nein")}.");
return 0;
