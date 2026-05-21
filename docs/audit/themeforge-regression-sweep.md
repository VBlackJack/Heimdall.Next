# ThemeForge Post-Migration Regression Sweep

Static resource audit generated after the ThemeForge migration. This report is analysis-only; it intentionally makes no code or XAML fixes.

## Summary

- Consumer XAML files scanned: 96
- Resource references checked: 7758
- C# resource lookups checked: 191
- Defined global resource keys modeled: 375
- Heimdall bridge theme brushes modeled: 74
- Hardcoded colour literal rows: 100
- StaticResource-on-theme-brush rows: 0

## Section 1 - Unresolved Resource Keys (XAML)

| File | Line | Key |
|---|---:|---|
| `src/Heimdall.App/Views/Dialogs/CommandLibraryPickerDialog.xaml` | 83 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/CommandLibraryPickerDialog.xaml` | 88 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/CommandLibraryPickerDialog.xaml` | 122 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/RdpImportDialog.xaml` | 57 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/RdpImportDialog.xaml` | 107 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/SnapshotRestoreDialog.xaml` | 48 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Dialogs/SnapshotRestoreDialog.xaml` | 73 | `CardBackgroundBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 298 | `DiffRemovedLineBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 301 | `DiffAddedLineBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 337 | `DiffRemovedPrefixBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 340 | `DiffAddedPrefixBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 357 | `DiffRemovedWordBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 360 | `DiffAddedWordBrush` |

## Section 2 - Unresolved Resource Keys (C#)

| File | Line | Key |
|---|---:|---|
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 101 | `DiffRemovedLineBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 102 | `DiffAddedLineBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 103 | `DiffRemovedWordBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 104 | `DiffAddedWordBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 105 | `DiffRemovedPrefixBrush` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml.cs` | 106 | `DiffAddedPrefixBrush` |

## Section 3 - Hardcoded Colour Literals In Views

| File | Line | Attribute | Value |
|---|---:|---|---|
| `src/Heimdall.App/App.xaml` | 44 | `Background` | `Transparent` |
| `src/Heimdall.App/App.xaml` | 46 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 216 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 399 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 453 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 491 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 492 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 593 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 594 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 680 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 681 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 762 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 953 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 958 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 1082 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 1863 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 3146 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 3364 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 3372 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 3381 | `Background` | `Transparent` |
| `src/Heimdall.App/MainWindow.xaml` | 3489 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 41 | `Stroke` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 45 | `Fill` | `White` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 120 | `Background` | `#3B82F6` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 125 | `Background` | `#22C55E` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 130 | `Background` | `#EF4444` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 135 | `Background` | `#F59E0B` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 140 | `Background` | `#8B5CF6` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 145 | `Background` | `#EC4899` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 150 | `Background` | `#06B6D4` |
| `src/Heimdall.App/Views/Dialogs/ProjectDialog.xaml` | 155 | `Background` | `#F97316` |
| `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml` | 225 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml` | 265 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml` | 276 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml` | 279 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ToolPickerDialog.xaml` | 64 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Dialogs/ToolPickerDialog.xaml` | 114 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedCitrixView.xaml` | 78 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedRdpView.xaml` | 611 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSftpView.xaml` | 185 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSftpView.xaml` | 316 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSftpView.xaml` | 320 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSftpView.xaml` | 444 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSshView.xaml` | 228 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedSshView.xaml` | 266 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/EmbeddedVncView.xaml` | 80 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/LocalFileBrowserView.xaml` | 133 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/LocalFileBrowserView.xaml` | 137 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/LocalFileBrowserView.xaml` | 234 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/SessionPaneControl.xaml` | 16 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/Tools/ArpMonitorView.xaml` | 121 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/ArpMonitorView.xaml` | 171 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/BannerGrabberView.xaml` | 206 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CertificateGeneratorView.xaml` | 289 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CertificateGeneratorView.xaml` | 328 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CertificateGeneratorView.xaml` | 372 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CertificateGeneratorView.xaml` | 408 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CertificateGeneratorView.xaml` | 440 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/ChmodCalculatorView.xaml` | 238 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/ChmodCalculatorView.xaml` | 277 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml` | 208 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml` | 219 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml` | 560 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml` | 567 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CronJobManagerView.xaml` | 111 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CronJobManagerView.xaml` | 190 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CronJobManagerView.xaml` | 311 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/CrontabBuilderView.xaml` | 188 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/DefaultCredentialView.xaml` | 179 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/DnsBatchResolverView.xaml` | 156 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/HackerSimulatorView.xaml` | 221 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/HackerSimulatorView.xaml` | 227 | `Color` | `#00FF41` |
| `src/Heimdall.App/Views/Tools/HashGeneratorView.xaml` | 221 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/HttpHeaderAnalyzerView.xaml` | 287 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/HttpStatusCodesView.xaml` | 86 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/HttpStatusCodesView.xaml` | 95 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/JwtParserView.xaml` | 180 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/JwtParserView.xaml` | 219 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/JwtParserView.xaml` | 257 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/NetworkCalculatorView.xaml` | 251 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/NetworkCartographyView.xaml` | 188 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/NotesToolView.xaml` | 312 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/NotesToolView.xaml` | 339 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/NotesToolView.xaml` | 340 | `BorderBrush` | `Transparent` |
| `src/Heimdall.App/Views/Tools/PasswordGeneratorView.xaml` | 111 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/PortScannerView.xaml` | 225 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/PrivilegeLauncherView.xaml` | 105 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/PrivilegeLauncherView.xaml` | 161 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/ServiceStatusView.xaml` | 144 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SnmpWalkerView.xaml` | 196 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SshConfigGeneratorView.xaml` | 222 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SshKeyAuditView.xaml` | 213 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SshKeyGeneratorView.xaml` | 163 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SshKeyGeneratorView.xaml` | 200 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/SshKeyGeneratorView.xaml` | 241 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/TcpPingView.xaml` | 182 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/TcpTracerouteView.xaml` | 163 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/TextDiffView.xaml` | 295 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/WhoisLookupView.xaml` | 168 | `Background` | `Transparent` |
| `src/Heimdall.App/Views/Tools/WifiNetworksView.xaml` | 107 | `Background` | `Transparent` |

## Section 4 - StaticResource On Theme Brushes

None.
