================================================================================
MAINTAINER-README: CodeBrix.Swarm
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING one of the NuGet packages, stop reading and open the
AGENT-README file for that package instead - README-INDEX.txt says which is
which. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, and the conventions the
source follows.


PURPOSE AND SCOPE
=================
CodeBrix.Swarm runs a swarm of worker processes across many hosts under one
coordinator. It produces THREE NuGet packages from FOUR projects:

  src/CodeBrix.Swarm.Queen    -> CodeBrix.Swarm.Queen.MitLicenseForever
  src/CodeBrix.Swarm.Hive     -> CodeBrix.Swarm.Hive.MitLicenseForever
  src/CodeBrix.Swarm.Worker   -> CodeBrix.Swarm.Worker.MitLicenseForever
  src/CodeBrix.Swarm.Core     -> no package at all

Each project's assembly is named after the project, with no licence suffix:
the suffix belongs to the PackageId only.

License: MIT, for all of it.

THE THREE PACKAGES ARE ALWAYS BUILT, PACKED AND PUBLISHED TOGETHER, at the
same version. See "THE Core PROJECT" below for why that matters more here than
it would in an ordinary repository.

Consumer documentation is ONE AGENT-README PER PACKAGE:
  AGENT-README.txt                          the Queen's (repository root)
  src/CodeBrix.Swarm.Hive/AGENT-README.txt   the Hive's
  src/CodeBrix.Swarm.Worker/AGENT-README.txt the Worker's
Each is packed into its own package at the package root, as AGENT-README.txt.
The Queen's lives at the root because that is where the family's audit and the
document-collection script look for a repository's first one.


REPOSITORY LAYOUT
=================
  .cursor/rules/agent-readme.mdc   |
  .github/copilot-instructions.md  |  the 8 AI-agent pointer files. Each is a
  .junie/guidelines.md             |  thin redirect to README-INDEX.txt and
  .clinerules                      |  must stay BYTE-FOR-BYTE identical to the
  .cursorrules                     |  canonical copies in CodeBrix.SkiaSvg on
  .windsurfrules                   |  GitHub. Never hand-edit one; re-fetch
  AGENTS.md                        |  them with curl from
  CLAUDE.md                        |  raw.githubusercontent.com/ellisnet/
                                   |  CodeBrix.SkiaSvg/main/<path>.

  src/CodeBrix.Swarm.Core/         the shared contract. NOT packable.
  src/CodeBrix.Swarm.Queen/        the coordinator library. Its AGENT-README
                                   is the one at the repository root.
  src/CodeBrix.Swarm.Hive/         the per-host library + AGENT-README.txt
  src/CodeBrix.Swarm.Worker/       the Worker library + AGENT-README.txt

  tests/CodeBrix.Swarm.Core.Tests/       one per library, plus:
  tests/CodeBrix.Swarm.Queen.Tests/
  tests/CodeBrix.Swarm.Hive.Tests/
  tests/CodeBrix.Swarm.Worker.Tests/
  tests/CodeBrix.Swarm.EndToEnd.Tests/   a real Queen, a real Hive and real
                                         Worker processes together

  samples/SwarmSample.Queen/       a console trio that can be run together as
  samples/SwarmSample.Hive/        a real swarm. EXTRAS-README.txt describes
  samples/SwarmSample.Worker/      them; the end-to-end suite starts the
                                   Worker one as a real process.

  CodeBrix.Swarm.slnx              the solution. Its "Solution Items" folder
                                   carries the canonical ten root files:
                                     .gitignore, AGENT-README.txt,
                                     EXTRAS-README.txt, global.json,
                                     icon-codebrix-128.png, LICENSE,
                                     MAINTAINER-README.txt, README-INDEX.txt,
                                     README.md, THIRD-PARTY-NOTICES.txt
                                   It also has a "Tests" folder holding the
                                   five test projects and a "Samples" folder
                                   holding the three sample projects.
  global.json                      selects the Microsoft.Testing.Platform test
                                   runner; pins no SDK. Do not delete it -
                                   without it `dotnet test` falls back to the
                                   VSTest bridge, which fails on the .NET 10
                                   SDK.
  icon-codebrix-128.png            the family NuGet icon, byte-identical
                                   across every CodeBrix repository.
  LICENSE, README.md, README-INDEX.txt, MAINTAINER-README.txt,
  EXTRAS-README.txt, THIRD-PARTY-NOTICES.txt, AGENT-README.txt

The .slnx Solution Items list, this section and the family audit all state the
same ten files. Keep the three in agreement.


BUILDING
========
    dotnet build --configuration Release CodeBrix.Swarm.slnx

0 warnings, 0 errors, and nothing less. GenerateDocumentationFile is on for
all four library projects, so CS1591 fires for any public member without an
XML doc comment - FIX IT BY WRITING THE COMMENT. Never add <NoWarn>, never add
a #pragma, never turn the documentation file off.

GeneratePackageOnBuild is on for the three packable projects, so an ordinary
build already produces three .nupkg files under
src/<project>/bin/<configuration>/.

If a build looks impossibly fast and you are chasing a warning, delete the
bin/ and obj/ folders first: analyzer warnings are not re-emitted on a no-op
rebuild.


TESTING
=======
xUnit v3 with SilverAssertions. THE TEST RUNNER IS Microsoft.Testing.Platform,
selected by the root global.json. No coverage collector is referenced - the
family dropped coverlet.collector and it must not come back.

    dotnet test --solution CodeBrix.Swarm.slnx --configuration Release

Known gotcha on this SDK line: `dotnet test --solution` sometimes reports
"zero tests ran" for xunit.v3 projects even when the suite is perfectly
healthy. When that happens, run each test assembly DIRECTLY and quote its own
counts - every test project builds an executable:

    for P in Core Queen Worker Hive EndToEnd; do
      N="CodeBrix.Swarm.$P.Tests"
      ./tests/$N/bin/Release/net10.0/$N
    done

If instead you see "Testing with VSTest target is no longer supported by
Microsoft.Testing.Platform", the root global.json is missing or malformed. Fix
global.json, never a csproj.

THE END-TO-END SUITE IS ON BY DEFAULT - it is not opt-in and must not become
so. It needs nothing but the SDK and localhost: it starts a real coordinator
on a FREE loopback port (never a fixed one), runs a real Hive in-process, and
starts the REAL sample Worker executable as child processes. The one thing it
substitutes is the host-load probe, which reports a quiet host so that a busy
machine cannot fail the run; the real Linux probe has tests of its own.

IT IS TIMING-SENSITIVE, and a flake in it is a defect, not noise. It has
already earned its place once: it found a real fault in the Worker that no
unit test could have found. Run it three times in a row before believing it.

Two test projects carry an xunit.runner.json that turns parallel collections
off - Queen.Tests and EndToEnd.Tests - because they start real hosts, real
connections and real processes. The file only takes effect beside the test
assembly, so each csproj copies it explicitly.

EVERY END-TO-END SCENARIO ASSERTS THAT NO SPAWNED PROCESS IS LEFT. Keep that
in any scenario you add. After a run, check the machine yourself:

    ps -eo pid,etimes,comm,args | grep -i swarm | grep -v grep
    ss -ltnp 2>/dev/null | grep -i dotnet

WHERE THE SAMPLE WORKER EXECUTABLE COMES FROM: the end-to-end project
references samples/SwarmSample.Worker with ReferenceOutputAssembly="false", so
it is always built first but is NOT copied beside the tests. Its path is
computed in the csproj - where MSBuild already knows the configuration and the
framework - and written into the test assembly as an AssemblyMetadata
attribute named SwarmSampleWorkerExecutable, which
Infrastructure/SampleWorkerProgram.cs reads back. Do not replace that with a
guess from the test's own location.

TIME IN TESTS: nothing in the libraries reads the clock or sleeps except
through the internal ISwarmClock. That is why a sixty-second rule and a
five-minute wait can both be exercised in microseconds. If you add a wait
anywhere in Core, Hive or Worker, put it through the clock.


PACKAGING / PUBLISHING
======================
Every packable csproj carries the canonical date-stamped version block
verbatim: a 4-segment 1.<years since 2026>.<day of year>.<minute of day>
computed from UTC at build time. There is no literal <Version> anywhere and
there must not be. Two builds in the same UTC minute produce the SAME version,
so do not publish two sets of packages within one minute.

ALL THREE PACKAGES ARE PUBLISHED TOGETHER AT THE SAME VERSION. Build them in
one `dotnet build` (or one `dotnet pack`) so that the three share one version
stamp, and push all three.

Each .nupkg contains:
  lib/net10.0/<its own assembly>.dll and .xml
  lib/net10.0/CodeBrix.Swarm.Core.dll and CodeBrix.Swarm.Core.xml
  README.md                  (the repository's, the nuget.org page)
  AGENT-README.txt           (ITS OWN - see below)
  THIRD-PARTY-NOTICES.txt
  LICENSE
  icon-codebrix-128.png

Publishing is Jeremy's, and his alone. Nothing in this repository pushes
anything anywhere.


THE Core PROJECT - the one arrangement worth understanding before changing
===========================================================================
CodeBrix.Swarm.Queen, CodeBrix.Swarm.Hive and CodeBrix.Swarm.Worker MUST NEVER
depend on each other, in any direction and at either level. The contract all
three speak - the message envelope, the hub paths and the client method name,
the roles, the built-in message kinds, the Worker configuration model, the
exit codes and the shared hub-client connection logic - therefore cannot live
in any one of them. It lives in src/CodeBrix.Swarm.Core, which is NOT a
package:

  * Each library references it with PrivateAssets="all", which keeps it out of
    the generated .nuspec.
  * Each library carries an IncludeCoreInPackage target that puts
    CodeBrix.Swarm.Core.dll AND CodeBrix.Swarm.Core.xml into its own
    lib/net10.0/ folder, beside its own assembly.
  * An application that installs two or three of these packages at the same
    version ends up with exactly ONE CodeBrix.Swarm.Core.dll in its output,
    because all the copies are identical. At MIXED versions the SDK keeps the
    higher-versioned copy - which is the real reason the three are published
    together.

A CONSEQUENCE THAT IS EASY TO GET WRONG: because Core is referenced with
PrivateAssets="all", the PackageReference Core itself declares -
Microsoft.AspNetCore.SignalR.Client - does NOT flow into any library's
.nuspec. EACH OF THE THREE PACKABLE PROJECTS DECLARES THAT SAME
PackageReference ITSELF. Remove one of those and the package ships a
CodeBrix.Swarm.Core.dll whose dependencies are missing, and the failure only
shows up in a consuming application at run time. The Queen additionally
declares <FrameworkReference Include="Microsoft.AspNetCore.App" />, because it
builds and runs the web host; the Hive and the Worker must not.

CHECK THE .nuspec INSIDE EVERY .nupkg after any change to a csproj:

    unzip -p src/CodeBrix.Swarm.Queen/bin/Release/CodeBrix.Swarm.Queen.*.nupkg \
      CodeBrix.Swarm.Queen.MitLicenseForever.nuspec

No .nuspec may depend on a CodeBrix.Swarm.Core package; there is no such
package.

THE TEST PROJECTS REFERENCE Core DIRECTLY, for the same reason: PrivateAssets
stops it reaching anything that references a library.

Core's internals are opened in src/CodeBrix.Swarm.Core/InternalsVisibleTo.cs
to all three libraries and to all five test assemblies. Each library has its
own InternalsVisibleTo.cs naming its own .Tests project and the end-to-end
project.

UNLIKE the shared Core project in some other CodeBrix repositories, SOME of
this one's types are PUBLIC on purpose: consumers define message payloads,
register handlers, and read exit codes. Everything that is not part of that
surface is internal.


THE PART A HOST OPERATOR CAN CHANGE
===================================
Three environment variables lower a Hive's limits on one host:

    CODEBRIX_SWARM_MAX_WORKERS
    CODEBRIX_SWARM_MAX_RAM_PERCENT
    CODEBRIX_SWARM_MAX_CPU_PERCENT

THEY CAN ONLY EVER LOWER. A value that would raise what the application asked
for is read, found to be higher, and ignored; so is one that is unset, empty
or unparseable. That direction is deliberate and is tested - do not "fix" it
into a general override. They are read once, when a run starts, through a
delegate, so the tests hand over an environment of their own rather than
changing the process's.

The same one-way rule applies to SwarmHostLimits itself: the defaults ARE the
ceilings (90 percent memory, 90 percent processor), and a value above a
ceiling is REFUSED with a clear exception rather than clamped.


CODING CONVENTIONS
==================
The CodeBrix family conventions apply to every file here:
  * net10.0 only. No multi-targeting.
  * File-scoped namespaces. Never block-scoped.
  * No `?` on reference types, ever, and no null-forgiving `!`. Nullable
    reference types are OFF and stay off. `int?`, `bool?`, `TimeSpan?` and
    friends are fine - those are value types.
  * No `global using`, no `#nullable`, no <ImplicitUsings>, no <NoWarn>, no
    <LangVersion>, no <Nullable>.
  * Usings at the top in one contiguous block, System.* first, alphabetical
    within each group, nothing below the namespace line.
  * No blank first line; one blank line between the usings and the namespace,
    and between the namespace and the first type.
  * No fabricated top-of-file banner comments.
  * XML doc comments on every public member. Internal members are documented
    where the reasoning is worth keeping, and much of this repository's is.
  * Source organised into sub-folders that map to sub-namespaces:
    Messaging/, Configuration/, Connections/, Diagnostics/ in Core;
    Authentication/, Hubs/ in the Queen; HostLoad/, Spawning/, Running/ in the
    Hive; Startup/ in the Worker.
  * Tests: one file per class under test, named <Class>Tests.cs, with
    snake_case method names (Member_snake_case for a member-specific test,
    plain snake_case otherwise); //Arrange //Act //Assert in multi-statement
    bodies; expression-bodied single-statement tests; SilverAssertions fluent
    form; TestContext.Current.CancellationToken threaded into every
    cancellable call. Scenario and helper files may drop the Tests suffix.

THE LIBRARY IS GENERIC AND MUST STAY SO. No consuming application's concepts
belong anywhere in this repository - not in code, comments, tests, samples or
documents. A Worker does "its work"; a Hive starts "Workers"; the payload a
Hive hands a Worker is opaque JSON the swarm never reads.

A WORDING PREFERENCE OF THE OWNER'S, and it is checked: the usual informal
noun for "a copy of a collection taken at one instant" - the photographic one -
is not used anywhere in this repository, in any casing. The method that returns
the Workers in a roster at one instant is called CurrentWorkers instead. Name
things the same way, and grep for it before you finish.


PROVENANCE / VENDORED SOURCES
=============================
No third-party source is incorporated. THIRD-PARTY-NOTICES.txt says so, and is
the file to update if that ever changes.

The DESIGN descends from Jeremy's own RemoteTerminal sample in the
CodeBrix.Terminal repository - RemoteTerminal.Shared / .Server / .Client and
the RemoteTerminal.AuthKey tests. What came across is the shape of the idea:
an AES-GCM access token read from a Bearer header or an access_token query
string, a hub that sends one way, and a client that reconnects. It was adapted
freely rather than ported, and it is NOT a third-party upstream, so no file
carries a "//was previously:" comment and there is nothing to attribute.

Four things were deliberately changed from that sample and should not drift
back:
  * THE SECRET IS NEVER COMPILED IN. The consuming application supplies one
    master secret, and a short one is refused rather than stretched.
  * A KEY PER ROLE, derived with HKDF-SHA256 from that one secret with a
    different information string, with the role also inside the token and also
    used as the AES-GCM associated data. A Hive's token does not decrypt at
    the Worker hub.
  * THE HUB PATHS AND THE CLIENT METHOD NAME ARE SPELLED ONCE, in
    SwarmHubContract. The sample duplicated them as strings on the client
    side, which is how two ends of a protocol drift apart without anything
    failing to compile.
  * A REDIRECTED OUTPUT STREAM IS ALWAYS DRAINED. The sample's process wrapper
    could redirect a stream nobody read, which stops the child process dead.
    Here capture is off by default and, when it is on, both streams are read
    continuously from the instant the process starts.

The Hive's host-load probes declare entry points that each operating system
publishes - /proc files on Linux, kernel32 on Windows, libSystem on macOS.
Those declarations were written from the operating systems' own published
interface descriptions. Nothing is built, bundled or downloaded.


NOTES
=====
* A REJECTED TOKEN COSTS THE WHOLE RETRY WINDOW. A Hive and a Worker cannot
  tell "refused" from "no answer": both are a failed attempt, so a bad token
  costs the full QueenUnreachableWindow - 60 seconds by default - before the
  process gives up with exit code 69. This is accepted for this version and is
  documented in all three AGENT-READMEs. A possible follow-up: fail at once,
  with an exit code of its own, when the coordinator answers "refused" rather
  than not answering at all. That means teasing a 401 apart from a transport
  failure inside the SignalR client, which is why it was not done here.

* THE EXIT CODES ARE A CONTRACT between a Worker and the Hive that started it,
  and they are defined once, in Core's SwarmExitCodes: 0, 69 (coordinator
  unreachable), 70 (the application's work threw), 78 (unusable
  configuration). They follow the long-standing conventional meanings and stay
  clear of 126 and above, which belong to the shell. A Hive's own program uses
  the same codes, through SwarmHiveHost.ExitCodeFor.

* DEVELOPMENT MODE IS --swarm-config AND NOTHING ELSE. The Worker library
  reads exactly one switch from a command line - `--swarm-config <path>` or
  `--swarm-config=<path>` - and leaves every other argument for the consuming
  application. There is no positional fallback and no generic --config or
  --configuration name, because those are names an application is likely to
  want for itself. Widening it again would let a consumer's own switch put a
  Worker into development mode by accident, which is exactly the fault this
  narrowing fixed.

* AN ABSOLUTE ADDRESS IS NOT ENOUGH. On Linux "/swarm/hive" parses perfectly
  well as an absolute FILE address, so every options object that takes the
  coordinator's address requires an http or https scheme:
  SwarmQueenOptions.Validate, SwarmHiveOptions.Validate,
  SwarmConnectionOptions.Validate and WorkerConfiguration.Validate. The last
  of those is what turns an unusable address into exit code 78 instead of an
  exception thrown out of a Worker's own start-up.

* THE OUT-OF-MEMORY PREFERENCE a Hive writes for each Worker on Linux is 500,
  on the kernel's scale of -1000 (never choose this) to 1000 (choose this
  first). Halfway up puts a Worker well ahead of anything ordinary without
  outranking something that has asked to be even more expendable. An
  unprivileged process may only RAISE the figure for its own children, which
  is all this does; it is best effort and a no-op off Linux.

* THE HIVE'S POLL INTERVAL (250 ms) is not in any specification - it is how
  often a Hive looks round while it is doing nothing else, and it also caps
  each step of a backoff wait so that a Worker ending part way through one is
  still noticed when it happens.

* WORKERS ARE NAMED "{HiveId}-{n}", counting every Worker a Hive has ever
  started, with HiveId defaulting to the machine name.

* A WORKER WHOSE MEMORY CANNOT BE READ is left OUT of the running average
  rather than counted as zero. Counting it as zero would let a Hive start
  twice as many as the host can hold.

* THE FIRST SPAWN DECISION of a run is made on memory alone, because the
  processor average is empty until the second reading. The settle interval
  bounds that to one Worker.


FIRST RUN ON WINDOWS
====================
Only the Linux host-load probe can be exercised on the machine this was
written on. The Windows one was written with care and everything that can be
tested without Windows is tested, but the following must be confirmed the
first time a Hive really runs there.

  W1  HostLoadProbes.ForThisHost() returns the Windows probe.
  W2  Marshal.SizeOf<MemoryStatusEx>() is exactly 64. The first field must be
      set to that value before the call, or the call fails.
  W3  GlobalMemoryStatusEx returns true; TotalPhysical matches Task Manager;
      AvailablePhysical is close to Task Manager's "Available".
  W4  UsedRamPercent is within a percent or two of Task Manager's figure.
  W5  GetSystemTimes returns true, and the SECOND reading gives a sensible
      CpuBusyPercent (the first is 0 by design - there is nothing to compare
      it with). IF IT READS ABOUT HALF of Task Manager's figure, the
      kernel/idle relationship has been misread: this code treats the kernel
      total as INCLUDING idle, which is correct for that API.
  W6  Both kernel32.dll entry points resolve.
  W7  A Worker really does start at below-normal priority.
  W8  LinuxOomPreference.TryPrefer returns false and writes nothing.
  W9  The configuration arrives over the pipe, the pipe STAYS OPEN, and the
      Worker sees end-of-file when the Hive closes it. Windows pipe semantics
      differ from POSIX; this is the single most important item on this list.
  W10 Kill(entireProcessTree: true) takes a whole tree down.
  W11 SpawnedProcesses.FindSampleWorkers (the by-name fallback in the
      end-to-end infrastructure) really finds a running sample Worker -
      otherwise the "nothing left running" assertion is vacuous on Windows.
  W12 The end-to-end project's Windows branch appends .exe to the sample
      Worker's path; confirm it resolves.


FIRST RUN ON MACOS
==================
  M1  HostLoadProbes.ForThisHost() returns the macOS probe.
  M2  "libSystem.dylib" resolves, and mach_host_self, host_page_size,
      host_statistics64 and sysctlbyname all bind.
  M3  sysctlbyname("hw.memsize") returns the machine's real memory. The name
      is passed as UTF-8 bytes with the terminating zero, deliberately.
  M4  host_page_size returns 4096 on Intel and 16384 on Apple silicon - the
      SAME unit the vm_statistics64 page counts are in. An available figure
      four times out on Apple silicon means this.
  M5  Marshal.SizeOf<VmStatistics64>() is 152, so the count handed to
      host_statistics64 is 38. Every field is present in the operating
      system's own order; do not tidy any of them away.
  M6  host_statistics64(HOST_VM_INFO64 = 4) and
      host_statistics(HOST_CPU_LOAD_INFO = 3) both return 0. The two flavour
      numbers come from different lists in the headers and are easy to swap.
  M7  AvailableBytes is near total minus Activity Monitor's "Memory Used". Far
      too LOW means file-backed pages are being counted as used; they must not
      be - they are the page cache.
  M8  The SECOND reading gives a sensible CpuBusyPercent. The tick counters
      are 32-bit and wrap, which CpuBusyMath.WrappingDifference handles.
  M9  mach_host_self() is asked for ONCE and cached; the process's Mach port
      count does not grow over a long run.
  M10 A Worker really does start at below-normal priority, and
      LinuxOomPreference.TryPrefer returns false.
  M11 Kill(entireProcessTree: true) takes a whole tree down, and
      SpawnedProcesses' by-name fallback matches a running sample Worker.
  M12 A Worker sees end-of-file on its lifeline when the Hive closes it.


================================================================================
