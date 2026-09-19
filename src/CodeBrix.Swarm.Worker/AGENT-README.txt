================================================================================
AGENT-README: CodeBrix.Swarm.Worker
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Swarm.Worker.MitLicenseForever NuGet package
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

This file documents the WORKER. The Queen's AGENT-README is at the root of the
repository and inside the Queen package; the Hive's is inside its own.


OVERVIEW
========
CodeBrix.Swarm.Worker is one process of a swarm, for .NET 10 or later. The
library runs the whole of a Worker's life around the application's work:

  1. Reads the configuration - THE FIRST LINE OF STANDARD INPUT, written there
     by the Hive that started this process. It never comes from the command
     line or from an environment variable, because both of those are readable
     by other processes on the host and the access token is in it.
  2. Runs the application's configure step, so handlers for
     application-defined messages are registered BEFORE anything can arrive.
  3. Subscribes to the coordinator's Worker hub with the token it was given,
     retrying for its window if the coordinator is not there yet.
  4. Runs the application's work, until the work finishes by itself, the
     coordinator says to end, the Hive that started it goes away, the
     coordinator stays out of reach too long, or something cancels the run.
  5. Runs the application's shutdown step - ALWAYS, whatever ended the work,
     and with an uncancelled token.
  6. Returns the exit code that says which of those happened.

A Worker's whole program is normally one line: return what RunAsync returns.

THE LIFELINE. After writing the configuration, a Hive LEAVES THE PIPE OPEN.
While it is open, the Hive is there. The end of it means the Hive has gone, or
is asking this Worker to exit, and either way the Worker winds itself down and
exits with code 0. The library watches the pipe; an application never touches
standard input itself.

DEVELOPMENT MODE. A Worker can also be started by hand, with no Hive anywhere,
by putting the same JSON in a file and naming it with ONE explicit switch:

    ExampleWorker --swarm-config /tmp/worker.json
    ExampleWorker --swarm-config=/tmp/worker.json

That is the ONLY thing the library reads from a command line. There is no
positional argument that means a configuration file, and no generic --config
or --configuration name. EVERY OTHER ARGUMENT IS LEFT ENTIRELY ALONE: an
application defines whatever switches and positional arguments it likes and
parses them itself, and none of them can put a Worker into development mode by
accident. In development mode there is no lifeline, so nothing ends the Worker
when no Hive is there; SwarmWorkerContext.IsDevelopmentMode says so.


INSTALLATION
============
PackageId:  CodeBrix.Swarm.Worker.MitLicenseForever

    dotnet add package CodeBrix.Swarm.Worker.MitLicenseForever

IMPORTANT: the NuGet package id is CodeBrix.Swarm.Worker.MitLicenseForever
(NOT "CodeBrix.Swarm.Worker" - that suffix exists only to make the package
license obvious forever). The primary namespace is CodeBrix.Swarm.Worker.

NuGet dependencies (pulled in automatically, no version pinning needed in the
consuming project):
  - Microsoft.AspNetCore.SignalR.Client    (the hub protocol and its client)

The shared contract assembly, CodeBrix.Swarm.Core, is INSIDE this package -
not a dependency of it. Do not look for a package of that name; there is none.

This package does NOT need the ASP.NET Core shared framework. Only the Queen
does.

License: MIT (SPDX: MIT)

Requirements: .NET 10 or later. Debian-based Linux, Windows or macOS.

TRANSPORT AND SECRECY: a swarm's intended arrangement is plain HTTP on a
trusted local network, with the access token as the whole of the
authentication. OVER PLAIN HTTP THE TOKEN TRAVELS IN THE CLEAR. An https
address is used as given when the coordinator serves one.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Swarm.Worker;           // SwarmWorkerHost,
                                           //   SwarmWorkerOptions,
                                           //   SwarmWorkerContext,
                                           //   SwarmWorkerOutcome
    using CodeBrix.Swarm.Core;             // SwarmExitCodes, SwarmRole,
                                           //   SwarmConfigurationException
    using CodeBrix.Swarm.Core.Messaging;   // SwarmMessage, SwarmMessageKinds,
                                           //   SwarmMessageHandler
    using CodeBrix.Swarm.Core.Configuration;
                                           // WorkerConfiguration,
                                           //   WorkerSwarmSettings - needed
                                           //   only to WRITE a configuration
                                           //   file for development mode

The configuration reader, the lifeline and the connection are internal.


================================================================================

CORE API REFERENCE
==================

SwarmWorkerHost  (static)
-------------------------
    static Task<int> RunAsync(SwarmWorkerOptions options, string[] args)
    static Task<int> RunAsync(SwarmWorkerOptions options, string[] args,
                              CancellationToken cancellationToken)
        Runs the Worker from start to finish and returns one of the codes on
        SwarmExitCodes. Pass the process's own arguments straight through -
        the library looks for --swarm-config and nothing else.
        Throws ArgumentNullException (options null) and
        SwarmConfigurationException (the options themselves are unusable - no
        WorkAsync, a negative window, a longest delay shorter than the first).
        A configuration the Worker cannot read or use is NOT thrown: it
        becomes exit code 78.


SwarmWorkerOptions  (sealed)
----------------------------
REQUIRED

    Func<SwarmWorkerContext, CancellationToken, Task> WorkAsync
        The work this Worker exists to do. Returning normally means the work
        is FINISHED. Throwing means it FAILED - exit code 70. The token is
        cancelled when the Worker is told to end, when its Hive goes away,
        when the coordinator has been out of reach too long, or when the
        application's own token is cancelled.

OPTIONAL

    Func<SwarmWorkerContext, CancellationToken, Task> ConfigureAsync
        Run after the configuration has been read and BEFORE the Worker
        connects. Register message handlers here, so that nothing sent in the
        first moments is missed. Throwing makes the run WorkFailed.
    Func<SwarmWorkerContext, CancellationToken, Task> ShutdownAsync
        Run once the work has ended, whatever ended it, and before the process
        exits. context.Outcome says why. It is given an UNCANCELLED token,
        because this is the last chance to put things down tidily. Throwing
        makes the run WorkFailed.
    Action<string> Report
        A line about anything the Worker dealt with by itself - a failed
        connection attempt, a handler that threw, the reason a configuration
        was refused. Nothing is written anywhere when it is unset, which is
        the default: a swarm of Workers says nothing unless asked to.

    TimeSpan QueenUnreachableWindow   60 s    how long to keep trying to reach
                                              the coordinator before giving
                                              up, at start-up and after a drop
    TimeSpan FirstRetryDelay         250 ms   the wait after the first failed
                                              attempt; each one after it is
                                              twice the last
    TimeSpan MaximumRetryDelay         3 s    the longest wait between
                                              attempts

    void Validate()   Throws SwarmConfigurationException. RunAsync calls it.

WHILE IT IS RETRYING AFTER A DROP, THE WORK CARRIES ON. Losing the connection
does not stop the work; only the window running out does.


SwarmWorkerContext  (sealed) - what the application's steps are handed
----------------------------------------------------------------------
    string WorkerId             What this Worker is called. The Hive gave
                                every Worker it started a different one.
    string Work                 The application's own part of the
                                configuration, as the JSON text it was written
                                as. The swarm carried it here WITHOUT READING
                                IT. Null when the application sent nothing.
    bool   IsDevelopmentMode    True when this Worker was started by hand from
                                a configuration file rather than by a Hive.
                                There is no lifeline in that case.
    SwarmWorkerOutcome Outcome  Why the Worker is stopping. Meaningless until
                                the work has ended; the shutdown step is where
                                it is worth reading.

    TWork GetWork<TWork>()
        Reads Work into a type of the application's own. Returns default when
        there was no application part. Throws JsonException when the text is
        not valid JSON for that type. Property names are matched
        case-insensitively; the Hive's side writes them camel-cased.

    void RegisterHandler(string kind, SwarmMessageHandler handler)
        Throws ArgumentException when the kind is missing or is one the swarm
        reserves (anything starting "swarm."), ArgumentNullException when the
        handler is null. Register from ConfigureAsync.


SwarmWorkerOutcome  (enum) - why the Worker stopped
---------------------------------------------------
    WorkFinished = 1         The work ran to its end by itself.     -> exit 0
    ToldToTerminate = 2      The coordinator sent "end yourself".   -> exit 0
    LifelineClosed = 3       The Hive has gone, or is asking this
                             Worker to exit.                        -> exit 0
    QueenUnreachable = 4     Out of reach for the whole window.     -> exit 69
    WorkFailed = 5           The application's work threw.          -> exit 70
    ConfigurationInvalid = 6 No usable configuration.               -> exit 78
    Cancelled = 7            The application's own token.           -> exit 0

THE FIRST REASON RECORDED IS THE ONE REPORTED. Several things can decide at
once - a terminate message arriving just as the lifeline closes - and the
first one wins.


THE EXIT CODES (CodeBrix.Swarm.Core.SwarmExitCodes)
---------------------------------------------------
    const int Success             = 0    ended normally: the work finished, it
                                         was told to end, its Hive closed the
                                         lifeline, or it was cancelled
    const int QueenUnreachable    = 69   the coordinator stayed out of reach
                                         for the whole retry window
    const int WorkFailed          = 70   the application's own work threw
    const int ConfigurationInvalid = 78  no usable configuration: nothing on
                                         standard input, text that was not
                                         JSON, a missing field, an address
                                         that is not http or https, or a file
                                         that could not be read
    static bool IsSuccess(int exitCode)

THEY ARE PART OF THE CONTRACT. The Hive that started this Worker reads them:
zero is never a failure, and a non-zero code within the Hive's immediate-
failure window is what makes it wait before starting another Worker. RETURN
WHAT RunAsync RETURNS - an application that exits with a code of its own tells
its Hive something untrue.


THE CONFIGURATION (CodeBrix.Swarm.Core.Configuration)
------------------------------------------------------
An application does not normally construct one of these - a Hive writes it -
but the types are public because a configuration file for development mode has
to be written somehow.

    sealed class WorkerConfiguration
        WorkerSwarmSettings Swarm    the swarm's part
        string Work                  the application's part, as raw JSON text
        string ToJsonLine()          one line, no line break at the end
        static WorkerConfiguration Parse(string json)
        void Validate()

    sealed class WorkerSwarmSettings
        string QueenUrl     the coordinator's base address; absolute http or
                            https
        string Token        a WORKER token, minted by the coordinator
        string WorkerId     what this Worker is called

On the wire it is one line of JSON:

    {"swarm":{"queenUrl":"http://10.0.0.5:5000","token":"...",
     "workerId":"host-1-3"},"work":{"batch":7}}

The "work" member is ANY JSON value at all - an object, an array, a number -
and it is copied through byte for byte. The swarm never parses it.


================================================================================

COMPLETE EXAMPLES
=================

--- A whole Worker program --------------------------------------------------

    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Worker;

    return await SwarmWorkerHost.RunAsync(
        new SwarmWorkerOptions
        {
            WorkAsync = async (context, cancellationToken) =>
            {
                var item = context.GetWork<WorkItem>();
                await ProcessAsync(item, cancellationToken);
            }
        },
        args);

Returning normally = finished. Throwing = failed. Nothing else to write.


--- Work that runs until it is ended ----------------------------------------

    WorkAsync = async (context, cancellationToken) =>
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var item = await FetchNextAsync(cancellationToken);

            if (item == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            await ProcessAsync(item, cancellationToken);
        }
    },

An OperationCanceledException thrown by an awaited call because THAT token was
cancelled is NOT treated as a failure: the Worker is being ended, and stopping
in answer to that is a normal end.


--- Listening for the application's own messages ----------------------------

    ConfigureAsync = (context, _) =>
    {
        context.RegisterHandler("app.rate-limit", (message, _) =>
        {
            limit = message.GetPayload<RateLimit>();
            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    },

Register in the CONFIGURE step, not in the work: the configure step runs
before the Worker connects, so nothing sent in the first moments is missed.
A kind nothing is registered for is ignored, quietly and on purpose - the
coordinator sends to every Worker, and most of what it sends is not for all
of them.


--- A shutdown step that is told why ----------------------------------------

    ShutdownAsync = async (context, _) =>
    {
        switch (context.Outcome)
        {
            case SwarmWorkerOutcome.WorkFinished:
                await MarkDoneAsync();
                break;

            case SwarmWorkerOutcome.ToldToTerminate:
            case SwarmWorkerOutcome.LifelineClosed:
                await PutTheWorkBackAsync();   // somebody else will finish it
                break;

            default:
                await RecordAsync(context.Outcome);
                break;
        }
    },


--- An application's own command line ---------------------------------------

    private static async Task<int> Main(string[] args)
    {
        //Entirely the application's own. The library reads --swarm-config and
        //NOTHING else from this array, so these cannot collide with it.
        var tenant  = ValueAfter(args, "--tenant");
        var verbose = args.Contains("--verbose");
        var inputs  = args.Where(a => !a.StartsWith('-')).ToArray();

        return await SwarmWorkerHost.RunAsync(
            new SwarmWorkerOptions { WorkAsync = ... },
            args);
    }


--- Running one Worker by hand, with no Hive --------------------------------

Write the same JSON to a file and name it with --swarm-config:

    using CodeBrix.Swarm.Core.Configuration;

    var configuration = new WorkerConfiguration
    {
        Swarm = new WorkerSwarmSettings
        {
            QueenUrl = "http://127.0.0.1:5000",
            Token    = workerTokenFromTheCoordinator,
            WorkerId = "by-hand"
        },
        Work = "{\"batch\":7}"
    };

    File.WriteAllText("/tmp/worker.json", configuration.ToJsonLine());

    //  ExampleWorker --swarm-config /tmp/worker.json

There is no lifeline in development mode, so this Worker runs until its work
finishes, the coordinator ends it, or the coordinator goes out of reach.


--- Shortening the window in a test ------------------------------------------

    QueenUnreachableWindow = TimeSpan.FromSeconds(2),
    FirstRetryDelay        = TimeSpan.FromMilliseconds(100),
    MaximumRetryDelay      = TimeSpan.FromMilliseconds(250),

A Worker pointed at an address nothing is listening on then exits with 69 in
about two seconds instead of about a minute.


MINIMUM VIABLE PROJECT TEMPLATE
===============================

ExampleWorker.csproj

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>ExampleWorker</RootNamespace>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Swarm.Worker.MitLicenseForever" />
      </ItemGroup>
    </Project>

Program.cs

    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Worker;

    namespace ExampleWorker;

    internal sealed class Assignment
    {
        public string Label { get; set; }
        public int Items { get; set; }
    }

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var options = new SwarmWorkerOptions
            {
                Report = line => Console.Error.WriteLine(line),

                WorkAsync = async (context, cancellationToken) =>
                {
                    var assignment =
                        context.GetWork<Assignment>() ?? new Assignment();

                    for (var i = 0; i < assignment.Items; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await DoOneAsync(
                            assignment.Label, i, cancellationToken);
                    }
                },

                ShutdownAsync = (context, _) =>
                {
                    Console.Error.WriteLine(
                        "ending because " + context.Outcome);
                    return Task.CompletedTask;
                }
            };

            //The library reads --swarm-config from args and leaves the rest
            //for this program.
            return await SwarmWorkerHost.RunAsync(options, args);
        }
    }


PERFORMANCE TIPS
================
* ONE WORKER IS ONE PROCESS. Scale by letting the Hive start more of them, not
  by making one Worker do several things at once - the Hive's admission
  arithmetic learns the footprint of a Worker and stops when the host is full,
  and it can only do that if a Worker is one unit of work.

* THREAD THE CANCELLATION TOKEN THROUGH EVERYTHING the work awaits. It is how
  a Worker stops promptly when the coordinator, or its Hive, ends it. A Worker
  that ignores it is stopped the hard way after its Hive's grace period - ten
  seconds by default - and loses whatever it was in the middle of.

* Keep the configure step short. The Worker is not connected yet while it
  runs, and the coordinator's retry window has not started.

* A handler is awaited before the next message OF THE SAME KIND is delivered,
  so a slow handler holds up that kind. Do the slow part elsewhere and have
  the handler only record what arrived.

* Report is off unless it is set, and a Worker that writes nothing costs
  nothing. When a Hive is collecting output, every line a Worker writes
  crosses a pipe; a chatty swarm makes work for its Hive.

* GetWork<TWork>() parses each time it is called. Call it once and keep the
  result.


COMMON PITFALLS TO AVOID
========================
* READING STANDARD INPUT YOURSELF. The library takes the first line as the
  configuration and then watches the rest as THE LIFELINE. An application that
  reads it as well steals the end-of-pipe that tells this Worker its Hive has
  gone.
  FIX: never touch Console.In in a Worker. Everything the application needs is
  in context.Work.

* EXITING WITH A CODE OF YOUR OWN. The codes are the contract a Hive reads.
  Exiting with 1 because something went wrong makes the Hive believe the
  Worker failed in a way it did not, and can engage its wait for nothing.
  FIX: return what RunAsync returns. Let the work THROW to report a failure,
  which becomes 70.

* A REFUSED TOKEN COSTS THE WHOLE RETRY WINDOW. A Worker cannot tell "the
  coordinator said no" from "the coordinator did not answer": both are a
  failed attempt, and it keeps retrying for QueenUnreachableWindow - 60
  seconds by default - before it gives up and exits with 69. A wrong, expired
  or wrong-role token therefore looks like a minute of silence and then a
  Worker that says the coordinator was unreachable. Its Hive sees a non-zero
  exit and, because a minute is longer than the immediate-failure window, does
  NOT wait before starting another - which will do exactly the same.
  FIX: when every Worker on a host dies after exactly the window, suspect the
  Worker token the Hive was given. Report shows the attempts going by.

* PUTTING A CONFIGURATION FILE'S PATH ON THE COMMAND LINE AS A BARE ARGUMENT.
  That does NOT enter development mode any more. Only --swarm-config does.
  FIX: ExampleWorker --swarm-config /tmp/worker.json

* EXPECTING --config OR --configuration TO WORK. They do not, deliberately:
  they are names an application is likely to want for itself.

* SWALLOWING OperationCanceledException IN THE WORK and carrying on. The token
  is cancelled because something is ending this Worker.
  FIX: let it propagate, or return.

* DOING CLEANUP IN THE WORK'S finally BLOCK ONLY. The shutdown step is the
  place, and it is given an uncancelled token; a finally block in the work
  runs with a token that is already cancelled.

* EXPECTING TO HEAR A MESSAGE SENT BEFORE THIS WORKER CONNECTED. Nothing is
  retained by the coordinator and nothing is replayed.
  FIX: put anything a new Worker must know into the work part of its
  configuration.

* REGISTERING A HANDLER FOR A KIND STARTING "swarm.". It throws
  ArgumentException: the library already handles those two.

* EXPECTING A SECOND LINE ON THE PIPE. The Hive writes the configuration and
  then nothing. The pipe is a lifeline, not a channel.

* EXPECTING THE COORDINATOR TO BE ASKED ANYTHING. Traffic is one-way. A Worker
  cannot report progress or request work over the hub; use whatever the
  application already uses for data.

* A LONG-RUNNING ShutdownAsync. It runs while the Hive's grace period is
  counting down - ten seconds by default. A shutdown step that takes longer
  than that gets the Worker stopped the hard way.


WHAT THIS PACKAGE DOES NOT DO
=============================
* It does not start itself, restart itself, or start anything else. A Hive
  does that.
* It does not call into the coordinator. Traffic is one-way.
* It does not parse the application's command line beyond --swarm-config.
* It does not read the application's part of the configuration. That JSON is
  handed over exactly as it arrived.
* It does not retry the application's work, and does not retry a Worker that
  failed - the Hive decides what happens next.
* It does not limit its own memory or processor use.
* It does not write to the console, or log anywhere, unless Report is set.
* It does not persist anything between runs.
* It does not queue messages, and nothing sent before it connected reaches it.
* It does not choose its own name or its own token; both arrive in the
  configuration.


WORKING EXAMPLES ON GITHUB
==========================
Finding the configuration - the pipe, the --swarm-config switch in both its
forms, and an application's own arguments being left alone:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Worker.Tests/Startup/WorkerConfigurationReaderTests.cs

The whole life of a Worker - connecting, the work, terminate, the lifeline,
the shutdown step, every exit code:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Worker.Tests/SwarmWorkerHostTests.cs

The lifeline by itself:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Worker.Tests/Startup/WorkerLifelineTests.cs

Options and their validation:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Worker.Tests/SwarmWorkerOptionsTests.cs

Real Worker processes started by a real Hive against a real coordinator, and a
Worker started by hand with no Hive at all:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.EndToEnd.Tests/WorkerEndingScenarios.cs
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.EndToEnd.Tests/DevelopmentModeScenarios.cs

A small, complete Worker program:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/samples/SwarmSample.Worker/Program.cs


QUICK REFERENCE CARD
====================

--- The whole program ---
Run:                return await SwarmWorkerHost.RunAsync(options, args)
With a token:       await SwarmWorkerHost.RunAsync(options, args, ct)

--- Options ---
The work:           WorkAsync = (ctx, ct) => DoItAsync(ctx, ct)
Before connecting:  ConfigureAsync = (ctx, ct) => { ... }
On the way out:     ShutdownAsync = (ctx, _) => { ctx.Outcome; ... }
Diagnostics:        Report = Console.Error.WriteLine
Window:             QueenUnreachableWindow = TimeSpan.FromSeconds(60)
Retries:            FirstRetryDelay = 250 ms, MaximumRetryDelay = 3 s

--- The context ---
Name:               context.WorkerId
Work as JSON text:  context.Work
Work as a type:     context.GetWork<MyWork>()
Started by hand:    context.IsDevelopmentMode
Why it is ending:   context.Outcome        // read it in ShutdownAsync
Messages:           context.RegisterHandler("app.kind", (msg, ct) => ...)
Read a payload:     message.GetPayload<MyPayload>()

--- Development mode (the ONLY switch the library reads) ---
                    ExampleWorker --swarm-config /tmp/worker.json
                    ExampleWorker --swarm-config=/tmp/worker.json
Write the file:     File.WriteAllText(path, configuration.ToJsonLine())
Everything else on the command line belongs to the application.

--- Exit codes ---
0   SwarmExitCodes.Success               finished, told to end, lifeline
                                         closed, or cancelled
69  SwarmExitCodes.QueenUnreachable      out of reach for the whole window
70  SwarmExitCodes.WorkFailed            the work threw
78  SwarmExitCodes.ConfigurationInvalid  no usable configuration

--- Why it stopped (SwarmWorkerOutcome) ---
WorkFinished / ToldToTerminate / LifelineClosed / QueenUnreachable /
WorkFailed / ConfigurationInvalid / Cancelled

Target: .NET 10 or later
License: MIT


================================================================================
END OF AGENT-README
