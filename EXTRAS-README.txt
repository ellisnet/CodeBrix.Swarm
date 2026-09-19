================================================================================
EXTRAS-README: CodeBrix.Swarm
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

Three projects are packable - src/CodeBrix.Swarm.Queen,
src/CodeBrix.Swarm.Hive and src/CodeBrix.Swarm.Worker - and one more,
src/CodeBrix.Swarm.Core, is not packable but has its assembly packed inside
all three of them. Everything listed below exists only to test or to
demonstrate those, and none of it is included in any package.

For runnable, compilable usage of the libraries, read the samples below and
the test projects: the "WORKING EXAMPLES ON GITHUB" section of each
AGENT-README maps a feature area to the test file that exercises it.


THE SAMPLE TRIO
===============
    samples/SwarmSample.Queen/
    samples/SwarmSample.Hive/
    samples/SwarmSample.Worker/

Three small console programs - one per role - that can be run together as a
real swarm on one machine, or across several. Each is the smallest honest
program of its kind: everything that is the library's job is the library's,
and what is left is what a consuming application really has to write.

RUNNING THEM TOGETHER

  1. Start the coordinator. With no arguments it takes any free port on the
     loopback interface; --url=http://0.0.0.0:5000 makes it reachable from
     other machines.

         dotnet run --project samples/SwarmSample.Queen

     It invents a fresh master secret for the run, prints the two hub
     addresses, and prints ONE LINE OF JSON holding everything a host needs:
     the address, a Hive token, a Worker token, and the Worker program to
     start. Fill in the path of the Worker program before using it.

  2. Start a Hive, giving it that line on its standard input:

         echo '<the line the coordinator printed>' \
           | dotnet run --project samples/SwarmSample.Hive

     The Hive connects, measures the host, and starts Workers up to the cap in
     the settings, one at a time.

  3. Type commands at the coordinator, one per line:
         note-hives     send an application-defined message to every Hive
         note-workers   send one to every Worker
         terminate      the work is over - every Hive ends its Workers,
                        finishes, and nothing starts again
         counts         how many Hives and Workers are connected
         quit           stop the coordinator

WHY THE SETTINGS ARRIVE ON STANDARD INPUT, AND NOT AS ARGUMENTS: they carry
two access tokens, and both a command line and an environment variable are
readable by anything else running on the host. It is the same reason a Hive
hands a Worker its configuration down a pipe rather than on a command line.
A real application would take the tokens from wherever it keeps its secrets;
what matters is that they are not left lying about. The sample Hive will also
read the same JSON from a file with --settings=<path>, for convenience while
experimenting.

WHAT EACH ONE SHOWS

  SwarmSample.Queen   the library owning the web host, so a console program
                      that is not a web application can be a coordinator;
                      minting BOTH tokens a host needs and handing them over
                      out of band; sending an application-defined message
                      type; the built-in "the work is over"; live counts and
                      the connect and disconnect events.

  SwarmSample.Hive    the one question an application answers - what do I
                      start next - and "not now" as an ordinary answer once
                      enough Workers have been handed out; registering a
                      handler before the Hive connects; being told about every
                      Worker that ends; lowering MaxWorkers; ending tidily on
                      Ctrl-C rather than letting the runtime end the process
                      and leave its Workers to notice for themselves.

  SwarmSample.Worker  a Worker whose whole program is "describe the work, hand
                      it over, exit on what comes back". The work is
                      deliberately dull - count, wait, say so - because the
                      point is the SHAPE of a Worker, not what one does; a
                      real Worker's work goes exactly where the counting is.
                      Its configuration is a little JSON object of its own
                      (label, steps, stepMilliseconds, failAtStep, report)
                      that the swarm carries from the Hive without reading.
                      It is silent unless the configuration asks it to report,
                      so a swarm of them says nothing at all by default. It
                      also demonstrates an application's own command-line
                      switch (--queen-window-seconds=N), which the library
                      leaves entirely alone.

RUNNING ONE WORKER BY HAND, with no Hive and no Hive-shaped scaffolding: put
the same configuration JSON in a file and name it with the library's one
switch.

    dotnet run --project samples/SwarmSample.Worker \
      -- --swarm-config /tmp/w.json

That is the only thing the Worker library reads from a command line, and it is
how a Worker is debugged.


THE END-TO-END TEST PROJECT
===========================
    tests/CodeBrix.Swarm.EndToEnd.Tests/

The scenarios that start a REAL coordinator on a free loopback port, run a
REAL Hive, and start the REAL sample Worker executable as child processes -
tokens refused across roles, nothing started before the coordinator has been
reached, a cap honoured, configurations arriving intact, messages reaching a
whole swarm, Workers finishing and failing and being replaced, both kinds of
termination, the coordinator going away, the lifeline closing, and a Worker
started by hand with no Hive at all. Every scenario ends by asserting that no
spawned process is left behind.

IT IS ON BY DEFAULT and needs nothing but the SDK and localhost. It is not
opt-in and should not become so.

ONE SUBSTITUTION, AND ONE ONLY: the host-load probe, which reports a quiet
host. Without it a busy machine could fail the run for reasons that have
nothing to do with the code. The real Linux probe has tests of its own in
tests/CodeBrix.Swarm.Hive.Tests.

The suite references samples/SwarmSample.Worker so that it is always built
first, and starts the executable where it was built. See MAINTAINER-README.txt
(TESTING) for how it finds it, and for how to run it.


THE OTHER TEST PROJECTS
=======================
    tests/CodeBrix.Swarm.Core.Tests/
    tests/CodeBrix.Swarm.Queen.Tests/
    tests/CodeBrix.Swarm.Hive.Tests/
    tests/CodeBrix.Swarm.Worker.Tests/

One per library. xUnit v3; run them as described in MAINTAINER-README.txt
(TESTING). CodeBrix.Swarm.Queen.Tests also holds an in-process check that a
real coordinator and real connections meet: a token accepted, a message
delivered, and each role's token refused at the other role's hub.

None of these projects is packable, and none of them ships anywhere.


================================================================================
