# CodeBrix.Swarm

Three cross-platform .NET libraries for **running a swarm of worker processes across many hosts
under one coordinator**. Each library is one of the three roles, and an application is built on
the one it plays:

* **CodeBrix.Swarm.Queen** - the coordinator. It owns its own web host, so the application that
  coordinates a swarm does not have to be a web application: hand it an address and one master
  secret and it opens two authenticated, one-way message hubs, one for Hives and one for Workers.
  It mints the access tokens - a different key for each of the two roles, so a Hive's token is no
  good at the Worker hub and a Worker's is no good at the Hive hub - sends application-defined
  messages to everything connected to either hub, reports how many of each are connected, raises
  an event as they come and go, and tells them when the work is over.
* **CodeBrix.Swarm.Hive** - one per host. It subscribes to the coordinator before it does anything
  else, measures the host's memory and processor behind an interface with implementations for
  Debian-based Linux, Windows and macOS, and starts Worker processes only while the host still has
  room - one at a time, re-measuring between each, and never past the share of the host it is
  allowed. It hands every Worker its configuration down a private pipe that then stays open as a
  lifeline, notices when one exits, waits longer after one that failed immediately, and ends them
  all in an orderly way when the coordinator says the work is over.
* **CodeBrix.Swarm.Worker** - one process. It reads its configuration at start-up - one line of
  JSON on standard input, or a file named with a switch when it is started by hand - subscribes to
  the coordinator with the token it was given, and runs the work the application supplies until
  that work finishes, the coordinator ends it, the Hive that started it goes away, or the
  coordinator stays out of reach too long. It always runs the application's shutdown step, and
  exits with a documented code that says which of those happened.

The three libraries never reference each other. What they share - the message envelope, the hub
addresses, the role names, the Worker configuration model and the exit codes - travels inside all
three packages, so an application that installs them at the same version ends up with exactly one
copy of it. The three are released together and are to be installed at the same version.

All three are provided as .NET 10 libraries and associated `*.MitLicenseForever` NuGet packages,
and support applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Swarm.Queen.MitLicenseForever
dotnet add package CodeBrix.Swarm.Hive.MitLicenseForever
dotnet add package CodeBrix.Swarm.Worker.MitLicenseForever
```

A swarm is normally three separate programs, so each of them adds the one package for its own
role. Note that the NuGet package IDs and the namespaces are different - there are no packages
named plain `CodeBrix.Swarm.Queen`, `CodeBrix.Swarm.Hive` or `CodeBrix.Swarm.Worker`:

* NuGet package IDs: `CodeBrix.Swarm.Queen.MitLicenseForever`,
  `CodeBrix.Swarm.Hive.MitLicenseForever` and `CodeBrix.Swarm.Worker.MitLicenseForever`
* Assemblies and primary namespaces: `CodeBrix.Swarm.Queen`, `CodeBrix.Swarm.Hive` and
  `CodeBrix.Swarm.Worker` - i.e. `using CodeBrix.Swarm.Queen;`
* The shared contract every role speaks: `CodeBrix.Swarm.Core`, with `CodeBrix.Swarm.Core.Messaging`
  and `CodeBrix.Swarm.Core.Configuration`. It travels inside each of the three packages rather than
  beside them, so there is nothing extra to install.

The `.MitLicenseForever` suffix belongs to the package ID only; it never appears in a namespace, a
using directive or a type name.

XML documentation (IntelliSense) ships alongside every assembly.

Each package pulls in the SignalR client automatically; no version pinning is needed in the
consuming project. The Queen package additionally asks for the ASP.NET Core shared framework,
because it builds and runs the web host that carries the two hubs - a program that uses it needs
the ASP.NET Core runtime installed.

## CodeBrix.Swarm.Queen supports:

* Two authenticated message hubs on one web host the library owns and starts - no web application
  needed, and the two hubs are mapped by name rather than found by looking through an assembly
* One-way traffic: the hubs expose no method a client can call, and nothing is replayed to a Hive
  or a Worker that connects later
* Access tokens minted from one master secret the application supplies, which never leaves the
  coordinator - AES-256-GCM, with a separate key derived per role, so a token for one role is not
  even readable at the other's hub
* Tokens that never expire, or that stop being accepted after a span or at a moment
* Sending any application-defined message type to every connected Hive, or to every connected Worker
* The two messages the swarm defines for itself: "the work is over" to the Hives, and "end
  yourself" to the Workers
* Live counts of connected Hives and Workers, and events as they arrive and leave
* Listening on any free port, with the port that was taken readable afterwards

## CodeBrix.Swarm.Hive supports:

* Subscribing to the coordinator first of all, and starting nothing until it has been reached once
* Asking the application one question - "what do I start next?" - and accepting "not now" as an
  answer that costs nothing
* Memory and processor readings of the host, by direct calls into the operating system's own
  libraries on Debian-based Linux, Windows and macOS - no native build and no extra package
* Safe default limits that are also ceilings, which an application may lower but never raise, plus
  an absolute free-memory floor
* Three environment variables that let whoever operates a host hold a Hive back further still, and
  that can only ever lower what the application asked for
* Starting Workers one at a time with a settle interval between them, admitting each on the
  *projected* memory use after it starts - learnt from the Workers already running
* A growing wait after a Worker that fails immediately, cleared once one survives
* Configuration handed to each Worker down a private pipe that then stays open as a lifeline -
  never on a command line and never in an environment variable, both of which other processes can
  read
* Workers started at below-normal priority, and marked as the kernel's preferred out-of-memory
  victim where the operating system offers that
* Optional collection of what the Workers write, always drained when it is switched on
* Ending every Worker politely and then firmly - lifelines closed, a grace period, then the whole
  process tree stopped

## CodeBrix.Swarm.Worker supports:

* Reading the configuration from the first line of standard input, and watching the rest of that
  pipe as a lifeline
* A development mode for running a Worker by hand with no Hive anywhere, entered only through one
  explicit switch, so an application's own command line is entirely its own
* Subscribing to the coordinator and handing the application its own opaque part of the
  configuration, as text or read back into a type of its own
* Handlers for application-defined message types, registered before the Worker connects so that
  nothing sent in the first moments is missed
* A shutdown step that always runs, and is told why the Worker is stopping
* Documented exit codes that are part of the contract, which a Hive reads to tell an ordinary end
  from a failure

## Sample Code

### The coordinator

```csharp
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Queen;

await using var queen = await SwarmQueen.StartAsync(
    new SwarmQueenOptions
    {
        Url = "http://0.0.0.0:5000",
        MasterSecret = secretFromTheApplicationsOwnStore
    },
    CancellationToken.None);

//Minted here, because only the coordinator ever holds the secret, and handed to each host out of
//band. A Hive needs both its own token and the one to pass on to its Workers.
var hiveToken = queen.CreateHiveToken();
var workerToken = queen.CreateWorkerToken();

queen.Connected += (_, connection) =>
    Console.WriteLine($"{connection.Role} arrived: {queen.HiveCount} Hive(s), {queen.WorkerCount} Worker(s)");

await queen.SendToWorkersAsync(
    SwarmMessage.Create("app.rate-limit", new { PerSecond = 20 }),
    CancellationToken.None);

//The work is over: every Hive ends its Workers and finishes, and nothing starts another.
await queen.SendTerminateAllWorkersToHivesAsync(CancellationToken.None);
```

### One host

```csharp
using CodeBrix.Swarm.Hive;

var outcome = await SwarmHiveHost.RunAsync(new SwarmHiveOptions
{
    QueenUrl = "http://192.168.1.10:5000",
    HiveToken = hiveToken,
    WorkerToken = workerToken,

    //The one question an application answers. "Not now" is never a failure.
    NextWorkerAsync = (context, cancellationToken) => Task.FromResult(
        queue.TryDequeue(out var item)
            ? WorkerLaunch.StartWithWork("/opt/example/ExampleWorker", [], item)
            : WorkerLaunch.NotNow),

    //Optional: leave more of the host alone than the defaults do. Asking for MORE is refused.
    Limits = new SwarmHostLimits { MaxRamPercent = 70, MaxWorkers = 8 }
});

return SwarmHiveHost.ExitCodeFor(outcome);
```

### One Worker

```csharp
using CodeBrix.Swarm.Worker;

return await SwarmWorkerHost.RunAsync(
    new SwarmWorkerOptions
    {
        //Runs before the Worker connects, so nothing sent in the first moments is missed.
        ConfigureAsync = (context, _) =>
        {
            context.RegisterHandler("app.rate-limit", (message, _) =>
            {
                limit = message.GetPayload<RateLimit>();
                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        },

        //The work. Returning normally means finished; throwing means failed; the token is cancelled
        //when the coordinator says to end, when the Hive goes, or when the coordinator has been out
        //of reach too long.
        WorkAsync = async (context, cancellationToken) =>
            await ProcessAsync(context.GetWork<WorkItem>(), cancellationToken),

        //Always runs, whatever ended the work. The context says why.
        ShutdownAsync = (context, _) => PutThingsDownAsync(context.Outcome)
    },
    args);
```

## Documentation

Each NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written
for AI coding agents - point your agent at the file inside the package it is writing code against.
The three files map to the three roles, and each of them opens with the same short account of how
the roles fit together, because nobody uses one of them alone.

Additional sample code and usage examples are available in the test projects and in the sample
console trio - a coordinator, a host member and a Worker that can be run together:
https://github.com/ellisnet/CodeBrix.Swarm/tree/main/tests
https://github.com/ellisnet/CodeBrix.Swarm/tree/main/samples

## License

CodeBrix.Swarm is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Swarm/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Swarm/blob/main/THIRD-PARTY-NOTICES.txt).
