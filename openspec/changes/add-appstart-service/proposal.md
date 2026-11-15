## Why
Mehrere Applikationen brauchen Admin-Rechte. Bediener arbeiten deshalb heute als Administrator und können Austauschdateien ändern und Passwörter in der Registry lesen. Befund bei Audits und Mangel nach EU CRA.

## What Changes
- Neuer Windows-Dienst AppStart.Service (LocalSystem) startet freigegebene Applikationen als Konto appstart-app in der Session des Bedieners
- Launcher ohne Rechte, eine Verknüpfung je Applikation mit deren Icon
- Setup mit Mehrfachauswahl der Applikationen, legt Konten, Gruppe, Konfiguration, Verknüpfungen und Rechte an
- Prüfbefehl verify für das Service and Security Manual
- Installationspaket Velopack MSI per-machine für stille Installation und Softwareverteilung
- Bootstrapper AppStart-Setup.exe für die interaktive Installation: installiert das MSI und startet danach den Wizard aus dem Programmordner

## Impact
- Keine Änderung an Applikationen und deren Installern
- Neue Komponenten: Service, Launcher, Setup, Core, Bootstrapper, MSI
- Restrisiko: Administratoren können Passwörter weiterhin lesen (Option 4)
