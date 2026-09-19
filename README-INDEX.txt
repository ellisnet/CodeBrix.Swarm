================================================================================
README-INDEX: CodeBrix.Swarm
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below
and read its AGENT-README file in full. Read MAINTAINER-README.txt only if you
are changing this repository itself.

This repository produces three packages - one per role in a swarm of worker
processes spread across many hosts. The Queen is the coordinator; a Hive runs
on each host and starts the Worker processes there; a Worker is one process
doing the work. The three libraries never reference each other: what they share
travels inside all three packages. They are released together and are to be
installed at the same version. Each AGENT-README opens with that same account,
because nobody uses one of them alone.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.Swarm.Queen.MitLicenseForever - the coordinator of a swarm. It
      owns its own web host, so a desktop or console application can be the
      coordinator: two authenticated, one-way message hubs, one for Hives and
      one for Workers, with a separate access-token key derived for each role.
      It mints the tokens, sends application-defined messages to everything
      connected to either hub, reports and raises events on how many of each
      are connected, and tells the swarm when the work is over.
  src/CodeBrix.Swarm.Hive/AGENT-README.txt
      CodeBrix.Swarm.Hive.MitLicenseForever - the per-host member of a swarm.
      It subscribes to the coordinator before it does anything else, measures
      the host's memory and processor on Debian-based Linux, Windows and macOS
      without any native library, and starts Worker processes only while the
      host still has room - one at a time, on projected rather than current
      memory use, never past limits whose defaults are also ceilings. It hands
      each Worker its configuration down a private pipe that stays open as a
      lifeline, waits longer after one that failed immediately, and ends them
      all in an orderly way when the work is over. The consuming application
      answers one question: what do I start next?
  src/CodeBrix.Swarm.Worker/AGENT-README.txt
      CodeBrix.Swarm.Worker.MitLicenseForever - one process of a swarm. It
      reads its configuration as one line of JSON on standard input, watches
      the rest of that pipe as its lifeline, subscribes to the coordinator,
      and runs the work the application supplies until that work finishes, the
      coordinator ends it, its Hive goes away, or the coordinator stays out of
      reach too long. It always runs the application's shutdown step and exits
      with a documented code. One explicit switch runs a Worker by hand with
      no Hive anywhere; every other argument belongs to the application.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers, including how the shared project travels inside all three
      packages and what to check the first time a Hive is run on Windows or
      macOS.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository,
      including the console trio that can be run together as a real swarm and
      the end-to-end test project that starts a real coordinator and real
      Worker processes.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  README-INDEX.txt
      This file.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences.

================================================================================
