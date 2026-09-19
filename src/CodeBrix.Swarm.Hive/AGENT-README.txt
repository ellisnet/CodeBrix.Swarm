================================================================================
AGENT-README: CodeBrix.Swarm.Hive
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Swarm.Hive.MitLicenseForever NuGet package
================================================================================

HOW THE THREE ROLES FIT TOGETHER
================================
CodeBrix.Swarm runs a swarm of worker processes across many hosts under one
coordinator. There are three roles, each a library an application is built on,
and nobody uses one of them alone:

  QUEEN   the coordinator, one per swarm. It owns its own web host and opens
          two authenticated, ONE-WAY message hubs - one for Hives, one for
          Workers. It mints the access tokens, sends application-defined
          messages to everything connected to either hub, reports how many of
          each are connected, and says when the work is over. Nothing ever
          calls INTO the Queen.
  HIVE    one per host. It subscribes to the Queen before it does anything
          else, measures the host, starts Worker processes while the host has
          room, hands each one its configuration, watches them, and ends them
          when told.
  WORKER  one process. It reads its configuration at start-up, subscribes to
          the Queen, does the application's work until something ends it, and
          exits with a documented code.

The three libraries never reference each other. The contract they share - the
message envelope, the hub addresses, the roles, the Worker configuration model
and the exit codes - travels INSIDE all three packages, so an application that
installs them at the same version ends up with exactly one copy of it. Install
and publish the three together, always at the same version.

This file documents the HIVE. The Queen's AGENT-README is at the root of the
repository and inside the Queen package; the Worker's is inside its own.


OVERVIEW
========
CodeBrix.Swarm.Hive is the per-host member of a swarm, for .NET 10 or later.
One Hive runs on each host. The library owns the whole of the business of
running Worker processes on that host - gating, pacing, launching, watching,
waiting after failures and terminating - and the consuming application answers
exactly one question, over and over:

    what do I start next?

Everything else is the library's. A Hive's whole program is normally one line:
run the host, and exit on what it returns.

What the library does, in the order it does it:

  1. SUBSCRIBES TO THE COORDINATOR FIRST, and starts nothing at all until it
     has reached it once. A Hive that started Workers before it knew where its
     coordinator was would have no way of ending them.
  2. Over and over: notice which Workers have ended; measure the host; decide
     whether there is room for another; ask the application what to start;
     start ONE; wait a settle interval; measure again.
  3. When something ends the run - the coordinator saying the work is over,
     the coordinator staying out of reach too long, or the application's own
     cancellation token - close every Worker's lifeline, give them a grace
     period to go by themselves, stop whatever is left along with everything
     it started, and return WHY.

WHILE THE COORDINATOR IS OUT OF REACH the Workers already running are left
entirely alone and nothing new is started. The connection retries by its own
rule; only when that rule runs out does the Hive wind down. A brief
interruption therefore costs a swarm nothing.

ONE HIVE PER HOST is the arrangement this is built for. More than one will
work, but each measures the WHOLE host rather than its own share of it, so
between them they can take more of it than either was allowed. Give each its
own HiveId and its own, lower, limits if it has to be done.


INSTALLATION
============
PackageId:  CodeBrix.Swarm.Hive.MitLicenseForever

    dotnet add package CodeBrix.Swarm.Hive.MitLicenseForever

IMPORTANT: the NuGet package id is CodeBrix.Swarm.Hive.MitLicenseForever (NOT
"CodeBrix.Swarm.Hive" - that suffix exists only to make the package license
obvious forever). The primary namespace is CodeBrix.Swarm.Hive.

NuGet dependencies (pulled in automatically, no version pinning needed in the
consuming project):
  - Microsoft.AspNetCore.SignalR.Client    (the hub protocol and its client)

The shared contract assembly, CodeBrix.Swarm.Core, is INSIDE this package -
not a dependency of it. Do not look for a package of that name; there is none.

This package does NOT need the ASP.NET Core shared framework. Only the Queen
does.

License: MIT (SPDX: MIT)

Requirements: .NET 10 or later. Debian-based Linux, Windows or macOS - a Hive
measures the host it runs on, and there is an implementation for each of those
three. On anything else RunAsync throws PlatformNotSupportedException. No
native library is built, downloaded or shipped: the measurements are direct
calls into each operating system's own libraries.

TRANSPORT AND SECRECY: a swarm's intended arrangement is plain HTTP on a
trusted local network, with the access token as the whole of the
authentication. OVER PLAIN HTTP THE TOKEN TRAVELS IN THE CLEAR - both this
Hive's own and the Worker token it passes on. An https address is used as
given when the coordinator serves one.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Swarm.Hive;             // SwarmHiveHost, SwarmHiveOptions,
                                           //   SwarmHiveContext,
                                           //   SwarmHiveOutcome,
                                           //   SwarmHostLimits, WorkerLaunch,
                                           //   WorkerExit, WorkerOutputLine
    using CodeBrix.Swarm.Core;             // SwarmExitCodes, SwarmRole,
                                           //   SwarmConfigurationException
    using CodeBrix.Swarm.Core.Messaging;   // SwarmMessage, SwarmMessageKinds,
                                           //   SwarmMessageHandler

The host-load probes, the process machinery and the admission arithmetic are
internal. They are implementation, and substituting them is a matter for this
repository's own tests.


================================================================================

CORE API REFERENCE
==================

SwarmHiveHost  (static)
-----------------------
    static Task<SwarmHiveOutcome> RunAsync(SwarmHiveOptions options)
    static Task<SwarmHiveOutcome> RunAsync(SwarmHiveOptions options,
                                           CancellationToken cancellationToken)
        Runs the Hive until something ends it, and returns why. RETURNING IS
        how the library tells the application it has FINISHED; the application
        then exits.
        The cancellation token winds the Hive down from outside, ending its
        Workers in the same orderly way the coordinator would.
        Throws ArgumentNullException (options null),
        SwarmConfigurationException (something in the options is missing or
        out of range), PlatformNotSupportedException (an operating system a
        Hive cannot measure).

    static int ExitCodeFor(SwarmHiveOutcome outcome)
        The code a Hive's own process should exit with for that ending. These
        are the same codes a Worker uses:
            ToldToTerminate   -> 0
            Cancelled         -> 0
            QueenUnreachable  -> 69
            Failed            -> 70


SwarmHiveOutcome  (enum) - why the Hive finished
------------------------------------------------
    ToldToTerminate = 1   The coordinator said the work is over. Every Worker
                          was ended in an orderly way, and NOTHING starts
                          another: there is no message that undoes this.
    QueenUnreachable = 2  The coordinator stayed out of reach for the whole
                          retry window, at start-up or after a drop. The
                          Workers that were running were ended first.
    Cancelled = 3         Something outside the swarm ended the run.
    Failed = 4            The application's own configure step, or the step
                          that says what to start next, threw.


SwarmHiveOptions  (sealed)
--------------------------
REQUIRED

    string QueenUrl         The coordinator's base address, such as
                            http://192.168.1.10:5000. Must be an absolute http
                            or https address. The Hive appends the hub path
                            itself, and passes the same base address on to
                            every Worker.
    string HiveToken        THIS Hive's own token, good only at the Hive hub.
    string WorkerToken      The token every Worker this Hive starts is given,
                            good only at the Worker hub. The Hive passes it on
                            unchanged and never looks inside it.
    Func<SwarmHiveContext, CancellationToken, Task<WorkerLaunch>>
                            NextWorkerAsync
                            Asked whenever the Hive has room. Answer with
                            WorkerLaunch.Start(...) or WorkerLaunch.NotNow.

WHY THERE ARE TWO TOKENS: traffic in a swarm is one-way, so a Hive can never
ask the coordinator for anything - including a token for its Workers. The
application that owns the coordinator mints BOTH (CreateHiveToken and
CreateWorkerToken) and gets them to the host out of band, before the Hive
starts. Put them somewhere a command line and an environment variable are not:
both of those are readable by anything else running on the host.

OPTIONAL - naming, and the application's own steps

    string HiveId           What this Hive is called; the host's own machine
                            name when it is not set. Every Worker is named
                            "{HiveId}-{n}", so two Hives sharing a name would
                            give their Workers the same names.
    Func<SwarmHiveContext, CancellationToken, Task> ConfigureAsync
                            Run BEFORE the Hive connects. Register handlers
                            for application-defined messages here, so nothing
                            sent in the first moments is missed.
    Func<SwarmHiveContext, CancellationToken, Task> ShutdownAsync
                            Run once the Hive has finished and every Worker
                            has been ended. The context's Outcome says why. It
                            is given an UNCANCELLED token: this is the last
                            chance to put things down tidily.

OPTIONAL - how much of the host to take

    SwarmHostLimits Limits  Defaults to a new SwarmHostLimits. See below.

OPTIONAL - every interval, with its default

    TimeSpan QueenUnreachableWindow  60 s   how long to keep trying to reach
                                            the coordinator before giving up,
                                            at start-up and after a drop
    TimeSpan FirstRetryDelay        250 ms  the wait after the first failed
                                            attempt; each one after it is
                                            twice the last
    TimeSpan MaximumRetryDelay        3 s   the longest wait between attempts
    TimeSpan SettleInterval           5 s   after starting a Worker, and after
                                            finding no room, before measuring
                                            the host again
    TimeSpan NotNowInterval           5 s   before asking the application
                                            again after "not now"
    TimeSpan PollInterval           250 ms  how often the Hive looks round
                                            while it is doing nothing else
    TimeSpan ImmediateFailureWindow  10 s   how soon after starting a failure
                                            counts as an immediate one
    TimeSpan FirstBackoff             5 s   the wait after the first immediate
                                            failure; each one doubles it
    TimeSpan MaximumBackoff           5 min the longest that wait ever gets
    TimeSpan BackoffResetAfter        1 min how long a Worker must survive to
                                            clear the waits
    TimeSpan TerminationGracePeriod  10 s   how long Workers have to end by
                                            themselves after their lifelines
                                            are closed, before what is left is
                                            stopped

OPTIONAL - being told things

    bool CaptureWorkerOutput        false   collect what the Workers write
    Action<WorkerOutputLine> WorkerOutput   where each collected line goes
    Action<WorkerExit> WorkerExited         told about every Worker that ends
    Action<string> Report                   a line about anything the Hive
                                            dealt with by itself. Nothing is
                                            written anywhere when it is unset.

    void Validate()         Throws SwarmConfigurationException. RunAsync calls
                            it.


SwarmHostLimits  (sealed) - THE DEFAULTS ARE ALSO CEILINGS
----------------------------------------------------------
    double MaxRamPercent        default and CEILING 90
    double MaxCpuPercent        default and CEILING 90, averaged over the
                                window below
    long   FreeRamFloorBytes    default 1 GiB - never start a Worker that
                                would leave the host with less free than this,
                                whatever the percentage says
    TimeSpan CpuAveragingWindow default 30 s
    int?   MaxWorkers           default null - no limit beyond what the host
                                allows

    const double RamPercentCeiling = 90
    const double CpuPercentCeiling = 90
    const long   DefaultFreeRamFloorBytes = 1 GiB
    static readonly TimeSpan DefaultCpuAveragingWindow = 30 s
    const string MaxWorkersVariable    = "CODEBRIX_SWARM_MAX_WORKERS"
    const string MaxRamPercentVariable = "CODEBRIX_SWARM_MAX_RAM_PERCENT"
    const string MaxCpuPercentVariable = "CODEBRIX_SWARM_MAX_CPU_PERCENT"

    void Validate()
    SwarmHostLimits Copy()

AN APPLICATION MAY ASK FOR LESS OF THE HOST, NEVER MORE. A percentage above
the ceiling is REFUSED with a clear SwarmConfigurationException, not quietly
clamped. MaxWorkers must be at least one when it is set.

THE LIMITS ONLY EVER STOP NEW WORKERS FROM STARTING. A Hive never ends a
Worker that is already running because the host got busy.

THE THREE ENVIRONMENT VARIABLES lower these further on one host, which is how
somebody who operates a machine holds back one that is shared with something
else:

    CODEBRIX_SWARM_MAX_WORKERS       an integer, at least 1
    CODEBRIX_SWARM_MAX_RAM_PERCENT   a number above 0 and up to 90
    CODEBRIX_SWARM_MAX_CPU_PERCENT   a number above 0 and up to 90

THEY CAN ONLY LOWER. A value that would RAISE what the application asked for
is read, found to be higher, and ignored - and so is one that is not set, is
empty, or cannot be read as a number. Nothing in the environment can talk a
Hive into taking more of a host than the application considered safe. They are
read once, when RunAsync starts.


WorkerLaunch  (sealed) - the answer to "what do I start next?"
--------------------------------------------------------------
    static WorkerLaunch NotNow
        Nothing to start at the moment. THIS IS NEVER A FAILURE and never
        counts against anything: the Hive waits NotNowInterval and asks again,
        for as long as it runs. An application with nothing to hand out yet
        says so as often as it likes.

    static WorkerLaunch Start(string executable)
    static WorkerLaunch Start(string executable, IEnumerable<string> arguments)
    static WorkerLaunch Start(string executable, IEnumerable<string> arguments,
                              string workJson)
    static WorkerLaunch StartWithWork<TWork>(string executable,
                                             IEnumerable<string> arguments,
                                             TWork work)
        Throws ArgumentException when the executable is null, empty or
        whitespace. A null element in the arguments is dropped.

    bool   IsNotNow
    string Executable
    IReadOnlyList<string> Arguments     never null; each argument its own
                                        element, handed to the operating
                                        system separately, so nothing has to
                                        be quoted
    string Work                         the application's part of the Worker's
                                        configuration, as JSON text
    string WorkingDirectory { get; set; }
                                        where to start the program, or null
                                        for the Hive's own folder

THE CONFIGURATION IS NOT AMONG THE ARGUMENTS, and must not be. It carries an
access token, and a command line is readable by anything else on the host. The
library writes it to the Worker's standard input instead.


SwarmHiveContext  (sealed) - what the application's steps are handed
--------------------------------------------------------------------
    string HiveId
    int    RunningWorkerCount      how many are running at this moment
    int    StartedWorkerCount      how many have been started altogether
    bool   IsConnected             true while the connection is open
    SwarmHiveOutcome Outcome       meaningless until the run has ended; the
                                   shutdown step is where it is worth reading
    void RegisterHandler(string kind, SwarmMessageHandler handler)
        Throws ArgumentException when the kind is missing or is one the swarm
        reserves (anything starting "swarm."), ArgumentNullException when the
        handler is null. Register from ConfigureAsync.


WorkerExit  (sealed) - a Worker has ended
-----------------------------------------
    string   WorkerId
    int      ProcessId          the operating system's number, for matching up
                                records afterwards; the process is already
                                gone
    int      ExitCode
    TimeSpan Ran
    bool     IsFailure          the code is anything but zero
    bool     WasImmediateFailure  it failed within ImmediateFailureWindow of
                                starting - the case the Hive waits after

The Hive has already done everything it does about the ending by the time the
application is told: the Worker has been let go of, and the wait before the
next start has already been set.


WorkerOutputLine  (sealed)
--------------------------
    string WorkerId, int ProcessId, bool IsError, string Text

Collecting is OFF by default, and a Worker's output then goes wherever the
Hive's own output goes - which costs nothing and cannot go wrong. When
CaptureWorkerOutput is on, the Hive reads BOTH streams continuously for as
long as each Worker lives, whether or not WorkerOutput is set: a redirected
stream that nobody reads fills up, and a Worker whose output stream is full
stops running until somebody empties it.


WHAT THE LIBRARY DOES FOR EVERY WORKER IT STARTS
================================================
* Writes the configuration - the swarm's part and the application's, as ONE
  LINE OF JSON - to the Worker's standard input, and then LEAVES THE PIPE
  OPEN. While it is open, the Hive is there. Closing it is how a Hive asks a
  Worker to wind itself down, and a Worker whose Hive vanishes sees the end of
  it and exits by itself.
* Names it "{HiveId}-{n}", counting every Worker this Hive has ever started.
* Starts it at BELOW-NORMAL priority, so the Hive and the host stay
  responsive.
* On Linux, marks it as the kernel's preferred choice if the host ever runs
  out of memory altogether, so that a Worker is lost rather than the Hive or
  whatever the host is really for. Best effort; a no-op elsewhere, and on a
  host that does not allow it.
* Reaps it when it ends, reports it, and tells the application.

HOW THE ROOM IS WORKED OUT, in this order:
    1. the Worker limit, when there is one;
    2. a host that cannot be measured at all -> start nothing;
    3. PROJECTED free memory - what is available now, less what a Worker of
       this Hive's has turned out to cost - against the free-memory floor;
    4. the same projected figure against the memory percentage;
    5. the AVERAGED processor figure against the processor percentage.
The first Worker is admitted on the host's own figures, because there is
nothing yet to learn a footprint from; the settle interval bounds that to one
Worker. A Worker whose memory cannot be read is left OUT of the average rather
than counted as nothing. The very first decision is made on memory alone,
because the processor average is empty until the second reading.


================================================================================

COMPLETE EXAMPLES
=================

--- A whole Hive program ----------------------------------------------------

    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Hive;

    var outcome = await SwarmHiveHost.RunAsync(
        new SwarmHiveOptions
        {
            QueenUrl    = settings.QueenUrl,
            HiveToken   = settings.HiveToken,
            WorkerToken = settings.WorkerToken,

            NextWorkerAsync = (context, cancellationToken) => Task.FromResult(
                queue.TryDequeue(out var item)
                    ? WorkerLaunch.StartWithWork(
                        "/opt/example/ExampleWorker", [], item)
                    : WorkerLaunch.NotNow)
        },
        stopping.Token);

    return SwarmHiveHost.ExitCodeFor(outcome);


--- Leaving more of the host alone ------------------------------------------

    Limits = new SwarmHostLimits
    {
        MaxRamPercent = 70,          // 90 is the ceiling; less is allowed
        MaxCpuPercent = 60,
        FreeRamFloorBytes = 4L * 1024 * 1024 * 1024,
        MaxWorkers = 8
    }

    // MaxRamPercent = 95 would throw SwarmConfigurationException from
    // RunAsync. The default IS the ceiling.


--- Listening for the application's own messages ----------------------------

    ConfigureAsync = (context, _) =>
    {
        context.RegisterHandler("app.pause", (message, _) =>
        {
            paused = message.GetPayload<PauseRequest>().Paused;
            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    },

    NextWorkerAsync = (context, _) => Task.FromResult(
        paused ? WorkerLaunch.NotNow : NextFromTheQueue()),

Pausing is exactly this: answer "not now" until it is over. There is no pause
in the library, because "not now" already is one.


--- Watching what happens ---------------------------------------------------

    Report = line => logger.LogInformation("{Line}", line),

    WorkerExited = exit =>
    {
        if (exit.IsFailure)
        {
            queue.PutBack(exit.WorkerId);
        }

        logger.LogInformation(
            "{Worker} ended with {Code} after {Seconds:F1}s (immediate: {Imm})",
            exit.WorkerId, exit.ExitCode, exit.Ran.TotalSeconds,
            exit.WasImmediateFailure);
    },

    CaptureWorkerOutput = true,
    WorkerOutput = line => logger.LogInformation(
        "{Worker}{Stream}: {Text}",
        line.WorkerId, line.IsError ? " (err)" : "", line.Text),


--- Ending tidily on Ctrl-C -------------------------------------------------

    using var stopping = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        //Ending the Hive tidily is better than letting the runtime end the
        //process: its Workers would otherwise be left to notice for
        //themselves.
        e.Cancel = true;
        stopping.Cancel();
    };


MINIMUM VIABLE PROJECT TEMPLATE
===============================

ExampleHive.csproj

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>ExampleHive</RootNamespace>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Swarm.Hive.MitLicenseForever" />
      </ItemGroup>
    </Project>

Program.cs

    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Hive;

    namespace ExampleHive;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            using var stopping = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stopping.Cancel();
            };

            //The two tokens and the address reach this host from outside the
            //swarm. Reading them from standard input keeps them off the
            //command line and out of the environment.
            var settings = await ReadSettingsAsync(stopping.Token);

            var handedOut = 0;

            var outcome = await SwarmHiveHost.RunAsync(
                new SwarmHiveOptions
                {
                    QueenUrl    = settings.QueenUrl,
                    HiveToken   = settings.HiveToken,
                    WorkerToken = settings.WorkerToken,
                    Report      = Console.Out.WriteLine,

                    NextWorkerAsync = (_, _) =>
                    {
                        if (handedOut >= settings.TotalWorkers)
                        {
                            return Task.FromResult(WorkerLaunch.NotNow);
                        }

                        handedOut++;

                        return Task.FromResult(
                            WorkerLaunch.StartWithWork(
                                settings.WorkerExecutable,
                                [],
                                new { batch = handedOut }));
                    }
                },
                stopping.Token);

            Console.WriteLine("finished: " + outcome);
            return SwarmHiveHost.ExitCodeFor(outcome);
        }
    }


PERFORMANCE TIPS
================
* NextWorkerAsync IS ON THE HIVE'S ONLY THREAD OF DECISION. Nothing else
  happens while it runs - no Worker is reaped, no reading is taken. Make it
  quick. If choosing the next piece of work means a network call, do that
  work in the background and have NextWorkerAsync take from a ready queue or
  answer NotNow.

* "Not now" is cheap and is the right answer whenever anything is uncertain.
  It costs one NotNowInterval and nothing else.

* SettleInterval is the single most useful number to tune. Shorten it and a
  Hive fills a host faster but on less settled readings; lengthen it and it
  fills more slowly and more accurately. Five seconds suits a Worker that
  reaches its working size within a few seconds.

* PollInterval bounds how quickly a Hive notices a Worker that has ended, the
  connection coming back, and the end of a grace period. Shorten it for a
  livelier Hive at the cost of a little more waking up.

* MaxWorkers is worth setting even when memory is the real constraint: it
  makes a run predictable and stops a Hive with very small Workers from
  putting thousands of processes on a host.

* Leave CaptureWorkerOutput off unless the output is wanted. With it off the
  Workers' streams are not redirected at all.

* A Worker's memory is read from the operating system once per pass. Nothing
  here is expensive; the intervals exist to keep the readings meaningful, not
  to save work.


COMMON PITFALLS TO AVOID
========================
* GIVING A HIVE ONLY ONE TOKEN. Both HiveToken and WorkerToken are required.
  Getting them the wrong way round gives a Hive that connects while its
  Workers never do - or one that never connects at all.
  FIX: CreateHiveToken for HiveToken, CreateWorkerToken for WorkerToken.

* A REFUSED TOKEN COSTS THE WHOLE RETRY WINDOW. A Hive cannot tell "the
  coordinator said no" from "the coordinator did not answer": both are a
  failed attempt, and it keeps retrying for QueenUnreachableWindow - 60
  seconds by default - before it gives up, ends its Workers and returns
  QueenUnreachable (exit code 69). A wrong, expired or wrong-role token
  therefore shows up as a minute of nothing and then a Hive that says the
  coordinator was unreachable.
  FIX: when a Hive dies after exactly the retry window, suspect the token
  before the network. Report is the place to see the attempts going by.

* PUTTING THE CONFIGURATION ON THE WORKER'S COMMAND LINE. It carries a token,
  and a command line is readable by anything else on the host. The library
  will not do it, and neither should an application: use the workJson
  parameter of Start / StartWithWork.

* EXPECTING "TERMINATE ALL WORKERS" TO BE UNDONE. It means the show is over:
  the Hive ends its Workers, finishes, and NOTHING starts spawning again.
  There is no resume message and there is not going to be one.
  FIX: to pause, answer WorkerLaunch.NotNow.

* ASKING FOR MORE OF THE HOST THAN THE CEILING. MaxRamPercent = 95 throws.
  The default IS the ceiling.
  FIX: the limits exist to be lowered.

* EXPECTING THE LIMITS TO END RUNNING WORKERS. They never do. They only stop
  new ones from starting.

* EXPECTING AN ENVIRONMENT VARIABLE TO RAISE A LIMIT. It cannot, by design. A
  variable that asks for more than the application allowed is ignored
  silently - as is one that is misspelt or unparseable.
  FIX: raise the value in the application's own options.

* A Worker THAT CANNOT BE STARTED AT ALL - a program that is not there, a
  path that is wrong - engages the wait exactly as an immediate failure does,
  because the next attempt will fail the same way.
  FIX: check Report at the very start of a run; the message names the
  executable.

* EXIT CODE ZERO IS NEVER A FAILURE. A Worker that finishes its work exits
  with zero and is replaced immediately, with no wait. Only a non-zero code
  within ImmediateFailureWindow lengthens the wait.

* A SLOW OR THROWING NextWorkerAsync. Throwing ends the whole run as Failed.
  FIX: catch inside it and answer NotNow if the failure is temporary.

* TWO HIVES ON ONE HOST. Each measures the whole host, so together they can
  take more of it than either was allowed.
  FIX: one per host - or give each its own HiveId and halve their limits.

* RUNNING A HIVE ON AN OPERATING SYSTEM IT CANNOT MEASURE. RunAsync throws
  PlatformNotSupportedException rather than guessing.

* NOT REDIRECTING, AND NOT DRAINING. If an application starts Worker
  processes of its own outside the Hive, remember that a redirected stream
  nobody reads fills up and stops the child. The Hive always drains the ones
  it redirects.


WHAT THIS PACKAGE DOES NOT DO
=============================
* It does not coordinate a swarm. It receives; it never calls the coordinator.
* It does not decide WHAT the work is. It asks the application, every time.
* It does not read the application's part of a Worker's configuration. That
  JSON is carried through byte for byte.
* It does not queue, retry or hand work back out. A Worker that failed is
  reported; putting the work back is the application's business.
* It does not end a running Worker because the host got busy.
* It does not limit a Worker's memory or processor use - it only decides
  whether to start one. Use the operating system's own facilities for that.
* It does not discover the coordinator. The address arrives in the options.
* It does not mint tokens, and cannot ask for one.
* It does not persist anything across a restart.
* It does not manage more than one host. One Hive, one host.
* It does not build, download or ship a native library.


WORKING EXAMPLES ON GITHUB
==========================
The admission decision - the Worker limit, an unmeasurable host, the projected
memory arithmetic, the free-memory floor, the averaged processor figure:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/Spawning/SpawnAdmissionTests.cs

The wait after immediate failures, and what clears it:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/Spawning/CrashBackoffTests.cs

The limits, the ceilings, and the three environment variables that can only
lower them:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/SwarmHostLimitsTests.cs
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/Spawning/HostLimitsFromEnvironmentTests.cs

The whole run - connecting first, pacing, "not now", reaping, terminating:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/SwarmHiveHostTests.cs

Options and their validation:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Hive.Tests/SwarmHiveOptionsTests.cs

A real Hive on a real coordinator, starting real Worker processes:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.EndToEnd.Tests/SpawningScenarios.cs
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.EndToEnd.Tests/TerminationScenarios.cs

A small, complete Hive program:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/samples/SwarmSample.Hive/Program.cs


QUICK REFERENCE CARD
====================

--- The whole program ---
Run:                var outcome = await SwarmHiveHost.RunAsync(options, ct)
Exit:               return SwarmHiveHost.ExitCodeFor(outcome)

--- Required options ---
Address:            QueenUrl = "http://192.168.1.10:5000"   // http or https
Own token:          HiveToken = ...        // queen.CreateHiveToken()
Pass-on token:      WorkerToken = ...      // queen.CreateWorkerToken()
What to start:      NextWorkerAsync = (ctx, ct) => Task.FromResult(launch)

--- Answering "what next?" ---
Nothing yet:        WorkerLaunch.NotNow
A program:          WorkerLaunch.Start("/path/to/worker")
With arguments:     WorkerLaunch.Start(exe, ["--tenant", "acme"])
With work:          WorkerLaunch.StartWithWork(exe, [], myWorkObject)
Raw JSON work:      WorkerLaunch.Start(exe, [], "{\"batch\":7}")
Folder:             launch.WorkingDirectory = "/var/lib/example"

--- Limits (defaults ARE ceilings) ---
Memory:             Limits.MaxRamPercent = 70       // <= 90
Processor:          Limits.MaxCpuPercent = 60       // <= 90
Free floor:         Limits.FreeRamFloorBytes = 4L * 1024 * 1024 * 1024
Averaging:          Limits.CpuAveragingWindow = TimeSpan.FromSeconds(30)
Cap:                Limits.MaxWorkers = 8           // null = no cap
Lower on one host:  CODEBRIX_SWARM_MAX_WORKERS / _MAX_RAM_PERCENT /
                    _MAX_CPU_PERCENT     (they can ONLY lower)

--- Pacing ---
Between starts:     SettleInterval = TimeSpan.FromSeconds(5)
After "not now":    NotNowInterval = TimeSpan.FromSeconds(5)
Looking round:      PollInterval = TimeSpan.FromMilliseconds(250)
Coordinator window: QueenUnreachableWindow = TimeSpan.FromSeconds(60)
Immediate failure:  ImmediateFailureWindow = TimeSpan.FromSeconds(10)
Wait after one:     FirstBackoff = 5 s, doubling to MaximumBackoff = 5 min
Clears after:       BackoffResetAfter = TimeSpan.FromMinutes(1)
Grace on ending:    TerminationGracePeriod = TimeSpan.FromSeconds(10)

--- Being told ---
Anything at all:    Report = Console.Out.WriteLine
A Worker ended:     WorkerExited = exit => { exit.ExitCode; exit.IsFailure; }
Worker output:      CaptureWorkerOutput = true; WorkerOutput = line => ...
Messages:           ConfigureAsync = (ctx, _) => {
                        ctx.RegisterHandler("app.kind", handler);
                        return Task.CompletedTask; }

--- Why it finished ---
ToldToTerminate     the work is over; nothing starts again          -> exit 0
QueenUnreachable    out of reach for the whole window               -> exit 69
Cancelled           the application's own token                     -> exit 0
Failed              the application's own step threw                -> exit 70

Target: .NET 10 or later, on Debian-based Linux, Windows or macOS
License: MIT


================================================================================
END OF AGENT-README
