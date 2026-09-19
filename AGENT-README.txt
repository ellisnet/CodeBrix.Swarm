================================================================================
AGENT-README: CodeBrix.Swarm.Queen
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Swarm.Queen.MitLicenseForever NuGet package
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

This file documents the QUEEN. The Hive's and the Worker's own AGENT-README
files are inside their own packages.


OVERVIEW
========
CodeBrix.Swarm.Queen is the coordinator of a swarm, for .NET 10 or later. It
starts and owns an ASP.NET Core web host with two SignalR hubs on it, so the
application that coordinates a swarm does NOT have to be a web application - a
desktop program, a service or a console tool hands over an address and one
master secret and gets back an object to send with, to read counts from, and
to stop.

Three things about it are deliberate and shape everything else:

  ONE-WAY TRAFFIC. The hubs expose no method a client can call. A Hive or a
  Worker receives; it never asks. Anything a Hive needs to be told - including
  the token it passes on to its Workers - reaches it some other way, before it
  starts.

  NOTHING IS RETAINED. A message reaches whatever is connected at the instant
  it is sent. Nothing is queued, and nothing is replayed to a Hive or a Worker
  that connects afterwards. An application that needs a late arrival to learn
  something must put it in that process's start-up configuration instead.

  THE SECRET NEVER LEAVES THE COORDINATOR. The application supplies one master
  secret; the library derives a DIFFERENT key per role from it and mints
  opaque tokens. A Hive or a Worker holds a token and can learn nothing from
  it. A token minted for one role is not merely rejected at the other role's
  hub - it cannot even be decrypted there.


INSTALLATION
============
PackageId:  CodeBrix.Swarm.Queen.MitLicenseForever

    dotnet add package CodeBrix.Swarm.Queen.MitLicenseForever

IMPORTANT: the NuGet package id is CodeBrix.Swarm.Queen.MitLicenseForever (NOT
"CodeBrix.Swarm.Queen" - that suffix exists only to make the package license
obvious forever). The primary namespace is CodeBrix.Swarm.Queen.

NuGet dependencies (pulled in automatically, no version pinning needed in the
consuming project):
  - Microsoft.AspNetCore.SignalR.Client    (the hub protocol and its client)

FRAMEWORK REFERENCE: this package declares the ASP.NET Core shared framework,
because it builds and runs the web host that carries the two hubs. A program
that uses it needs the ASP.NET Core runtime installed. The Hive and Worker
packages do NOT need it.

The shared contract assembly, CodeBrix.Swarm.Core, is INSIDE this package -
not a dependency of it. Do not look for a package of that name; there is none.

License: MIT (SPDX: MIT)

Requirements: .NET 10 or later, with the ASP.NET Core runtime. Debian-based
Linux, Windows or macOS.

TRANSPORT AND SECRECY - READ THIS BEFORE DEPLOYING
    The intended arrangement is plain HTTP on a trusted local network, with
    the token as the whole of the authentication. OVER PLAIN HTTP THE TOKEN
    TRAVELS IN THE CLEAR: anything that can watch the network between a host
    and the coordinator can read a token and then use it. Treat a swarm on
    plain HTTP as something that belongs on a network you control.
    An https address is not blocked. Supply one in SwarmQueenOptions.Url,
    together with whatever certificate configuration the ASP.NET Core host
    needs, and it is used exactly as given.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Swarm.Queen;            // SwarmQueen, SwarmQueenOptions,
                                           //   SwarmConnectionEventArgs
    using CodeBrix.Swarm.Queen.Authentication;
                                           // SwarmTokenAuthority, SwarmToken,
                                           //   SwarmTokenException
    using CodeBrix.Swarm.Core;             // SwarmRole, SwarmHubContract,
                                           //   SwarmExitCodes,
                                           //   SwarmConfigurationException
    using CodeBrix.Swarm.Core.Messaging;   // SwarmMessage, SwarmMessageKinds,
                                           //   SwarmMessageHandler,
                                           //   SwarmMessageDispatcher
    using CodeBrix.Swarm.Core.Configuration;
                                           // WorkerConfiguration,
                                           //   WorkerSwarmSettings

Everything else in CodeBrix.Swarm.Core is internal to the three libraries.


================================================================================

CORE API REFERENCE
==================

SwarmQueen  (sealed, IAsyncDisposable)
--------------------------------------
The running coordinator. Create it with the static factory; there is no
constructor.

    static Task<SwarmQueen> StartAsync(SwarmQueenOptions options,
                                       CancellationToken cancellationToken)
        Validates the options, starts the web host, maps the two hubs by name,
        waits until it is listening, and returns the coordinator.
        Throws ArgumentNullException (options null),
        SwarmConfigurationException (address missing or not an absolute http
        or https address), SwarmTokenException (master secret missing or
        shorter than 32 characters).

    string BaseUrl          Where it is REALLY listening. When the options
                            asked for port zero, this is where the port that
                            was actually taken can be read.
    string HiveHubUrl       BaseUrl + "/swarm/hive"
    string WorkerHubUrl     BaseUrl + "/swarm/worker"
    int    HiveCount        How many Hives are connected right now.
    int    WorkerCount      How many Workers are connected right now.

    event EventHandler<SwarmConnectionEventArgs> Connected
    event EventHandler<SwarmConnectionEventArgs> Disconnected
        Raised as a Hive or a Worker arrives at or leaves its hub. The event
        argument carries Role, ConnectionId and both counts as they were at
        that moment.

    string CreateHiveToken()
    string CreateHiveToken(TimeSpan lifetime)
    string CreateWorkerToken()
    string CreateWorkerToken(TimeSpan lifetime)
        Mint an access token for one role. The no-argument forms never expire.
        A lifetime of zero or less throws ArgumentOutOfRangeException.

    Task SendToHivesAsync(SwarmMessage message, CancellationToken ct)
    Task SendToWorkersAsync(SwarmMessage message, CancellationToken ct)
        Send an APPLICATION-DEFINED message to everything connected to that
        hub. Throws ArgumentNullException (message null), ArgumentException
        (the kind is missing, or starts with the reserved prefix "swarm."),
        ObjectDisposedException (the coordinator has been stopped).

    Task SendTerminateAllWorkersToHivesAsync(CancellationToken ct)
        THE WORK IS OVER. Every connected Hive ends its Workers in an orderly
        way, stops starting new ones FOR GOOD, and finishes. There is no
        message that undoes this.

    Task SendTerminateToWorkersAsync(CancellationToken ct)
        Every connected Worker stops its work, runs its application's shutdown
        step and exits with code 0. A Hive whose Workers end this way is free
        to start more.

    Task StopAsync(CancellationToken ct)
        Stops listening and closes every connection. Sending afterwards throws
        ObjectDisposedException. Calling it twice is harmless. It is NOT a way
        to end the work: Hives keep going until their own retry window runs
        out, and may start Workers in the meantime - see "STOPPING THE
        COORDINATOR INSTEAD OF ENDING THE WORK" under COMMON PITFALLS.

    ValueTask DisposeAsync()
        Stops if it has not been stopped, then disposes the host. Never throws
        on the way out.


SwarmQueenOptions  (sealed)
---------------------------
    const string AnyFreeLoopbackPortUrl = "http://127.0.0.1:0"
        Asks the operating system for any free port on the loopback interface.
        Read SwarmQueen.BaseUrl afterwards for the port that was taken. This
        is what a test should use; a fixed port makes two runs collide.

    string Url              Default: AnyFreeLoopbackPortUrl. Must be an
                            absolute http or https address. A port of zero
                            means any free port. Use "http://0.0.0.0:5000" to
                            listen on every interface.
    string MasterSecret     Required. At least 32 characters. Every token in
                            the swarm is derived from it. It is never compiled
                            into the library and never leaves the coordinator.
    ILoggerFactory LoggerFactory
                            Where the library's own web host sends its
                            logging, or null - the default - for none at all.
                            The host's built-in providers are cleared, so an
                            application that wants to see anything supplies
                            this.
    void Validate()         Throws SwarmConfigurationException or
                            SwarmTokenException. StartAsync calls it.


SwarmConnectionEventArgs  (sealed)
----------------------------------
    SwarmRole Role          Hive or Worker.
    string ConnectionId     The hub's own identifier for that connection. It
                            is NOT the Hive's or Worker's name - the swarm
                            carries no name on the wire.
    int HiveCount           The counts as they were when this happened.
    int WorkerCount


SwarmTokenAuthority  (sealed) - namespace CodeBrix.Swarm.Queen.Authentication
-----------------------------------------------------------------------------
SwarmQueen holds one of these and exposes what most applications need through
CreateHiveToken / CreateWorkerToken. Use the type directly when tokens are
minted somewhere other than the running coordinator - a provisioning tool, for
instance - or when a token has to be inspected.

    const int MinimumMasterSecretLength = 32

    SwarmTokenAuthority(string masterSecret)
        Throws SwarmTokenException when the secret is missing, is whitespace,
        or is shorter than 32 characters. A short secret is REFUSED, never
        stretched.

    string CreateToken(SwarmRole role)
    string CreateToken(SwarmRole role, TimeSpan lifetime)
    string CreateToken(SwarmRole role, DateTime expiresUtc)

    SwarmToken ReadToken(SwarmRole expectedRole, string token)
        Throws SwarmTokenException when the token is missing, undecodable,
        minted for the other role, altered, or expired.
    bool TryReadToken(SwarmRole expectedRole, string token, out SwarmToken r)

HOW A TOKEN IS BUILT, and why the role cannot be forged: the encoded form is
AES-256-GCM - nonce, then authentication tag, then ciphertext - in a URL-safe
encoding, so it survives being put on a query string with no escaping. The key
is derived from the master secret with HKDF-SHA256 and a DIFFERENT information
string per role, and the role's name is fed in as associated data as well, so
a Hive's token does not decrypt at all at the Worker hub. The role is inside
the encrypted payload too and is checked after decryption, so the refusal does
not rest on the key derivation alone. A five-minute allowance is made for a
clock-skewed issue time; an expiry is not given that allowance.

SwarmToken  (sealed): Guid TokenId, SwarmRole Role, DateTime IssuedUtc,
DateTime? ExpiresUtc (null when it never expires).

SwarmTokenException  (sealed, Exception): a token or a master secret that
cannot be used.


THE SHARED CONTRACT (namespace CodeBrix.Swarm.Core)
---------------------------------------------------
    enum SwarmRole              Hive = 1, Worker = 2

    static class SwarmHubContract
        const string HiveHubPath = "/swarm/hive"
        const string WorkerHubPath = "/swarm/worker"
        const string ReceiveMessageMethodName = "ReceiveSwarmMessage"
        static string HubPathFor(SwarmRole role)
        static string HubUrlFor(string baseUrl, SwarmRole role)

    static class SwarmExitCodes     THE EXIT-CODE CONTRACT
        const int Success             = 0   ended normally: the work finished,
                                            it was told to end, or its Hive
                                            closed the lifeline
        const int QueenUnreachable    = 69  the coordinator stayed out of
                                            reach for the whole retry window
        const int WorkFailed          = 70  the application's own work threw
        const int ConfigurationInvalid = 78 no usable configuration
        static bool IsSuccess(int exitCode)
        These are what a Worker process exits with, and what a Hive's own
        program should exit with for the same situations. A Hive reads them to
        tell an ordinary ending from a failure.

    sealed class SwarmConfigurationException : Exception
        Something a swarm was handed to start from cannot be used.


MESSAGES (namespace CodeBrix.Swarm.Core.Messaging)
--------------------------------------------------
    sealed class SwarmMessage
        Guid   MessageId       a new one per message
        DateTime SentUtc       the coordinator's reading
        string Kind            what a receiver registers a handler for; exact,
                               case-sensitive comparison
        string Payload         the body as JSON text, or null
        bool   HasPayload
        static SwarmMessage Create(string kind)
        static SwarmMessage Create<TPayload>(string kind, TPayload payload)
        TPayload GetPayload<TPayload>()

    static class SwarmMessageKinds
        const string ReservedPrefix        = "swarm."
        const string TerminateAllWorkers   = "swarm.terminate-all-workers"
        const string TerminateWorker       = "swarm.terminate-worker"
        static bool IsBuiltIn(string kind)
        static string TerminateKindFor(SwarmRole role)

    delegate Task SwarmMessageHandler(SwarmMessage message,
                                      CancellationToken cancellationToken)

    sealed class SwarmMessageDispatcher
        Register / Unregister / IsRegistered / DispatchAsync. A Hive and a
        Worker each own one; a coordinator rarely touches it directly.

APPLICATION KINDS: name them anything that does NOT begin with "swarm." - a
prefix of the application's own, such as "app.", keeps them apart from the
swarm's two for ever. SendToHivesAsync and SendToWorkersAsync REFUSE a
reserved kind, so an application cannot forge a built-in message; the two
built-ins have methods of their own.


THE WORKER CONFIGURATION (namespace CodeBrix.Swarm.Core.Configuration)
----------------------------------------------------------------------
The coordinator does not write these - a Hive does - but the type is public
because an application may need to construct one, for instance to run a single
Worker by hand against a running coordinator.

    sealed class WorkerConfiguration
        WorkerSwarmSettings Swarm    the swarm's part
        string Work                  the application's part, as raw JSON text,
                                     carried through unread
        string ToJsonLine()
        static WorkerConfiguration Parse(string json)
        void Validate()

    sealed class WorkerSwarmSettings
        string QueenUrl     absolute http or https
        string Token        a WORKER token
        string WorkerId     what this Worker is called


================================================================================

COMPLETE EXAMPLES
=================

--- A coordinator, from nothing to sending ---------------------------------

    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Core.Messaging;
    using CodeBrix.Swarm.Queen;

    await using var queen = await SwarmQueen.StartAsync(
        new SwarmQueenOptions
        {
            Url = "http://0.0.0.0:5000",
            MasterSecret = secretFromTheApplicationsOwnStore   // >= 32 chars
        },
        CancellationToken.None);

    Console.WriteLine($"listening on {queen.BaseUrl}");

    queen.Connected += (_, c) => Console.WriteLine(
        $"{c.Role} arrived: {c.HiveCount} Hive(s), {c.WorkerCount} Worker(s)");
    queen.Disconnected += (_, c) => Console.WriteLine(
        $"{c.Role} left: {c.HiveCount} Hive(s), {c.WorkerCount} Worker(s)");

    await queen.SendToWorkersAsync(
        SwarmMessage.Create("app.rate-limit", new RateLimit { PerSecond = 20 }),
        CancellationToken.None);


--- Handing a host its two tokens -------------------------------------------

A Hive needs BOTH its own token and the Worker token it passes on, because
traffic is one-way and it can never ask for one. Mint both here and get them
to the host out of band - a deployment step, a secret store, a line of JSON on
the Hive program's standard input. Do not put them on a command line or in an
environment variable: anything else running on the host can read those.

    var forThatHost = new
    {
        queenUrl    = queen.BaseUrl,
        hiveToken   = queen.CreateHiveToken(),
        workerToken = queen.CreateWorkerToken()
    };


--- Tokens that expire ------------------------------------------------------

    var token = queen.CreateHiveToken(TimeSpan.FromHours(8));

An expiry is checked when the connection is made, not while it is open: a Hive
whose token expires mid-run keeps the connection it has, and is refused the
next time it has to reconnect. See COMMON PITFALLS - a refusal costs the whole
retry window.


--- Ending the work ---------------------------------------------------------

    // Stop the Workers but leave the Hives free to start more:
    await queen.SendTerminateToWorkersAsync(CancellationToken.None);

    // THE SHOW IS OVER - every Hive ends its Workers and finishes for good:
    await queen.SendTerminateAllWorkersToHivesAsync(CancellationToken.None);

    await queen.StopAsync(CancellationToken.None);

Send the Hive message and then give the Hives a moment before stopping the
coordinator: a Hive that loses its connection first winds down anyway, but by
the slower route of its retry window running out.


MINIMUM VIABLE PROJECT TEMPLATE
===============================

ExampleQueen.csproj

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>ExampleQueen</RootNamespace>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Swarm.Queen.MitLicenseForever" />
      </ItemGroup>
    </Project>

Program.cs

    using System;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Swarm.Queen;

    namespace ExampleQueen;

    internal static class Program
    {
        private static async Task<int> Main()
        {
            using var stopping = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stopping.Cancel();
            };

            await using var queen = await SwarmQueen.StartAsync(
                new SwarmQueenOptions
                {
                    Url = "http://0.0.0.0:5000",
                    //A real application keeps ONE secret and gives every
                    //coordinator it starts the same one.
                    MasterSecret = Convert.ToBase64String(
                        RandomNumberGenerator.GetBytes(32))
                },
                stopping.Token);

            Console.WriteLine("hive hub:   " + queen.HiveHubUrl);
            Console.WriteLine("worker hub: " + queen.WorkerHubUrl);
            Console.WriteLine("hive token:   " + queen.CreateHiveToken());
            Console.WriteLine("worker token: " + queen.CreateWorkerToken());

            try
            {
                await Task.Delay(Timeout.Infinite, stopping.Token);
            }
            catch (OperationCanceledException)
            {
                //Asked to stop.
            }

            await queen.SendTerminateAllWorkersToHivesAsync(
                CancellationToken.None);
            await queen.StopAsync(CancellationToken.None);
            return 0;
        }
    }


PERFORMANCE TIPS
================
* ONE COORDINATOR PER SWARM. It is a message fan-out and a pair of counters;
  it does no work of its own and does not need to be scaled. Two coordinators
  are two swarms.

* Sending is a fan-out to every connection on that hub. Prefer ONE message
  that every receiver filters over a message per receiver - the coordinator
  has no way of addressing one Hive or one Worker anyway.

* Payloads are serialized once per send, not once per connection. A large
  payload still crosses the network once per connection, so send an identifier
  and let the Workers fetch the bulk of it if it is big.

* HiveCount and WorkerCount are plain reads of counters kept by the hubs. Poll
  them as often as you like; prefer the Connected and Disconnected events if
  you want to react rather than watch.

* Mint tokens when a host is provisioned, not per message. Minting derives no
  keys - the two role keys are derived once, when the coordinator starts - but
  it does encrypt, and there is no reason to do it more often than necessary.

* Leave LoggerFactory null in production unless the host's own logging is
  wanted. With it null the web host has no logging providers at all.


COMMON PITFALLS TO AVOID
========================
* EXPECTING A LATE JOINER TO BE CAUGHT UP. Nothing is retained. A Hive that
  connects a second after a message was sent never sees it. Put anything a
  new arrival must know into its start-up configuration instead.
  FIX: treat the hubs as live notification, never as a queue.

* EXPECTING TO CALL INTO THE COORDINATOR. The hubs have no client-callable
  methods, by design. A Hive cannot ask for a token, report progress, or
  request work.
  FIX: give a Hive everything it needs before it starts, and carry results
  back over whatever the application already uses for data.

* HANDING A HIVE ONLY ONE TOKEN. A Hive's options carry TWO: its own, for the
  Hive hub, and the Worker token it puts into every Worker's configuration.
  Mint both with CreateHiveToken and CreateWorkerToken and send both.
  FIX: if a swarm starts and the Hive connects but its Workers never do, the
  Worker token is the first thing to check.

* USING A HIVE TOKEN FOR A WORKER, or the other way round. It cannot work -
  the keys are different - and the failure looks like a connection problem,
  not like a refusal.

* A REFUSED TOKEN COSTS THE WHOLE RETRY WINDOW. A Hive or a Worker cannot tell
  "the coordinator said no" from "the coordinator did not answer": both are a
  failed attempt, and it keeps retrying for its full window - 60 seconds by
  default - before it gives up and exits with code 69. A wrong or expired
  token therefore shows up as a minute of silence and then a process that ends
  with "the coordinator was unreachable".
  FIX: when a host goes quiet for exactly the retry window, suspect the token
  before suspecting the network.

* A MASTER SECRET SHORTER THAN 32 CHARACTERS. It is refused, not padded.
  FIX: 32 random bytes, base64-encoded, is a good secret.

* A NEW SECRET ON EVERY RESTART. Every token in the swarm is derived from it,
  so restarting the coordinator with a fresh secret invalidates every token
  already handed out.
  FIX: keep the secret where the application keeps its other secrets.

* PLAIN HTTP ON AN UNTRUSTED NETWORK. The token is the whole of the
  authentication and it is in the clear.
  FIX: keep the swarm on a network you control, or supply an https address
  and the certificate configuration the ASP.NET Core host needs.

* A FIXED PORT IN A TEST. Two runs collide.
  FIX: SwarmQueenOptions.AnyFreeLoopbackPortUrl, then read BaseUrl.

* SENDING A KIND THAT STARTS WITH "swarm.". It is refused with an
  ArgumentException, on purpose.
  FIX: use SendTerminateAllWorkersToHivesAsync or
  SendTerminateToWorkersAsync for the two built-ins, and a prefix of your own
  for everything else.

* SENDING AFTER StopAsync. It throws ObjectDisposedException.

* STOPPING THE COORDINATOR INSTEAD OF ENDING THE WORK. StopAsync on its own
  tells nobody the work is over. Every Hive and Worker treats the lost
  connection as an interruption that may pass, and keeps retrying for its
  own unreachable window (60 seconds by default) before it gives up, ends
  its Workers and exits with code 69. During that window, expect the
  following. All of it is normal, and all of it ends when the Hive's window
  runs out:
    - Workers that were already running carry on with their work until
      their own window runs out, or the Hive ends them as it gives up.
    - A Worker that runs out of its window exits with code 69, which frees
      a slot, and the HIVE MAY START A REPLACEMENT. It does not start one
      while it knows it is disconnected. But when the coordinator shuts
      down, the connections close slightly before the listener stops, so
      the Hive's first reconnect can briefly succeed and it can start
      Workers then. So the number of Worker processes started after
      StopAsync is not fixed; it depends on timing and on how busy the host
      is.
    - A replacement Worker connects BEFORE it runs any work. If the
      coordinator is gone, it retries for its window and exits with code 69
      without running the application's work at all.
    - If a replacement's first connection does land while the coordinator
      is still shutting down, it DOES start its work. Once the connection
      drops, it can keep working for up to its window - about a minute by
      default - for a coordinator that is already gone.
  FIX: to end a swarm deliberately, send SendTerminateAllWorkersToHivesAsync
  first. Each Hive then ends its Workers in an orderly way and stops
  starting new ones for good. Only after that, StopAsync. Make the work
  safe to be cut off, or to finish after the swarm is over, because a
  coordinator that crashes or loses the network gets the behaviour above.

* FORGETTING THE ASP.NET CORE RUNTIME. This package needs the shared
  framework; a machine with only the base .NET runtime fails to start the
  program.


WHAT THIS PACKAGE DOES NOT DO
=============================
* It does not queue, retain or replay messages.
* It does not let a Hive or a Worker call in, report progress, or request
  anything. Traffic is one-way.
* It does not address one Hive or one Worker. A message goes to every
  connection on a hub.
* It does not start, watch or end any process. That is the Hive's work.
* It does not distribute the work itself, and never reads an application's
  payload.
* It does not hand tokens out. It mints them; getting them to a host is the
  application's job, and deliberately so.
* It does not persist anything. A restarted coordinator knows nothing about
  what was connected before.
* It does not discover Hives, and there is no registry of hosts.
* It does not provide encryption of its own beyond the token. Over plain HTTP
  the traffic, and the token with it, is in the clear.
* It does not decide when the work is over - the application does, and says so
  with SendTerminateAllWorkersToHivesAsync.


WORKING EXAMPLES ON GITHUB
==========================
Token minting, role separation, expiry and tampering:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Queen.Tests/Authentication/SwarmTokenAuthorityTests.cs

A real coordinator on a real port, with real connections - a Hive token
refused at the Worker hub and a Worker token refused at the Hive hub, counts,
connect and disconnect notifications, reserved kinds refused:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Queen.Tests/QueenWorkerConnectionSmoke.cs

Options validation:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.Queen.Tests/SwarmQueenOptionsTests.cs

Messages reaching a whole swarm, end to end:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/tests/CodeBrix.Swarm.EndToEnd.Tests/MessageScenarios.cs

A small, complete coordinator program:
https://github.com/ellisnet/CodeBrix.Swarm/blob/main/samples/SwarmSample.Queen/Program.cs


QUICK REFERENCE CARD
====================

--- Starting and stopping ---
Start:              await SwarmQueen.StartAsync(options, ct)
Any free port:      Url = SwarmQueenOptions.AnyFreeLoopbackPortUrl
Real address:       queen.BaseUrl
Hub addresses:      queen.HiveHubUrl / queen.WorkerHubUrl
Stop:               await queen.StopAsync(ct)
Dispose:            await using var queen = ...

--- Tokens ---
For a Hive:         queen.CreateHiveToken()
For a Worker:       queen.CreateWorkerToken()
Time-limited:       queen.CreateHiveToken(TimeSpan.FromHours(8))
Inspect:            new SwarmTokenAuthority(secret).ReadToken(role, token)
Secret minimum:     SwarmTokenAuthority.MinimumMasterSecretLength  // 32

--- Sending ---
To all Hives:       await queen.SendToHivesAsync(message, ct)
To all Workers:     await queen.SendToWorkersAsync(message, ct)
Build a message:    SwarmMessage.Create("app.thing", payload)
Read one:           message.GetPayload<TPayload>()
Work is over:       await queen.SendTerminateAllWorkersToHivesAsync(ct)
End the Workers:    await queen.SendTerminateToWorkersAsync(ct)

--- Watching ---
Counts:             queen.HiveCount / queen.WorkerCount
Arrivals:           queen.Connected += (s, e) => { e.Role; e.HiveCount; }
Departures:         queen.Disconnected += (s, e) => { ... }

--- Exit codes a swarm uses ---
0   SwarmExitCodes.Success               ended normally
69  SwarmExitCodes.QueenUnreachable      out of reach for the whole window
70  SwarmExitCodes.WorkFailed            the application's work threw
78  SwarmExitCodes.ConfigurationInvalid  no usable configuration

Target: .NET 10 or later, with the ASP.NET Core runtime
License: MIT


================================================================================
END OF AGENT-README
