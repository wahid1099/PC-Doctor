PC Doctor: Roadmap of Features to Add
Prepared October 19, 2026, as a follow-up to the C# rewrite of PC Doctor.
This document lists what can be added next, grouped by what each item gets you, followed by a suggested build order.
Fix more problems
• Slow-PC deep dive: show which apps use the most CPU, RAM, disk and network right now, with one-click end task.
• Startup manager: actually disable or enable startup items, instead of just listing them.
• Storage analyzer: a folder-size map showing what is eating the disk, plus Storage Sense-style cleanup (old updates, Windows.old, caches, large files).
• Wi-Fi and network doctor: signal strength, DNS speed test, router ping, and per-adapter power-saving settings (a common cause of dropouts).
• Display and GPU tools: detect wrong refresh rate or scaling and external monitor problems, and reinstall the graphics driver cleanly.
• Audio, camera and mic privacy checks: show which apps have access and fix blocked permissions.
• Overheating and fan analysis: read temperatures and throttling where Windows exposes them.
• Battery health tracking: log capacity over time and alert when it drops sharply.
• Browser fixes: clear caches, reset proxy settings and remove unwanted extensions.
• Windows Update tools: pause or defer updates, roll back a bad driver or update, and fix specific error codes.
• Blue-screen decoder: read crash dumps and translate stop codes into a likely cause and fix.
• Software issues: repair or reinstall common runtimes (Visual C++, .NET, DirectX) that games and apps need.
Drivers
• Driver rollback: one click to restore the previous driver version.
• Restore from backup: a button to reinstall drivers from the backup folder.
• Chipset, graphics and touchpad drivers from the maker: direct vendor lookup for drivers Windows Update doesn't carry.
• Driver age report: flag drivers that are years old.
Security
• Hosts file, proxy and browser hijack checks.
• Open ports and sharing check (what is exposed on your network).
• Windows security baseline score: Secure Boot, TPM, BitLocker, SmartScreen, UAC and account settings.
• Unknown startup and scheduled tasks flagged for review.
• Optional malware scan hand-off to Microsoft Defender.
Safety and trust
• Per-fix undo: record what each fix changed and revert just that fix.
• Dry-run mode: show exactly what a fix would do before running it.
• Code signing of the app and of fixes.json, so tampered updates are rejected.
• Tamper-proof report history: save every scan with a timestamp.
Intelligence
• AI assistant: an opt-in chat that explains findings in plain language and suggests next steps. Redacted scan data only, with a preview of exactly what is sent.
• Natural-language problem box with smarter matching, instead of keyword matching.
• Health trend: a chart of the health score over weeks, with alerts when it drops.
• Predictive warnings: for example, "disk full in about 3 weeks" or "SSD errors rising."
Reach and business
• Multi-PC dashboard: a technician or family admin sees every PC's health in one place.
• Remote help: send a link so someone can view a diagnostic report or run a fix.
• Scheduled maintenance: weekly automatic scan and cleanup with a summary.
• Installer (MSI/MSIX), winget and Microsoft Store listings.
• Licensing, free and paid tiers, and an in-app support/feedback button.
Polish
• Multi-language support (Bangla, Hindi, Spanish and others) and right-to-left layouts.
• Accessibility: keyboard navigation, screen reader labels, high-contrast and larger text.
• Onboarding: a first-run tour and a "what was fixed" summary after each run.
• Telemetry that is off by default, opt-in only, to see which fixes work.
• Crash reporting inside the app itself.
• PDF reports with charts and branding, using a proper PDF library.
Suggested build order

1. Per-fix undo and dry-run mode (the biggest trust gain).
2. Startup manager and storage analyzer (common complaints).
3. Driver rollback and vendor driver lookup.
4. Code signing and an installer.
5. AI assistant and the multi-PC dashboard.
