using System.Runtime.CompilerServices;

// The contract this project holds is partly public, because consumers define message payloads and
// register handlers. What is NOT public - the hub-client connection logic, the clock and the JSON
// helpers - is opened here, and only to assemblies of this repository.

// The three libraries. All of them reference this project and pack this assembly inside their own
// package.
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Queen")]
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Hive")]
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Worker")]

// This project's own test suite.
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Core.Tests")]

// The three libraries' test suites drive the shared connection logic through the same substituted
// hub connection and clock that this project's own tests use, so they need the internal types too.
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Queen.Tests")]
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Hive.Tests")]
[assembly: InternalsVisibleTo("CodeBrix.Swarm.Worker.Tests")]

// The end-to-end suite runs a real Queen, a real Hive and real Workers together.
[assembly: InternalsVisibleTo("CodeBrix.Swarm.EndToEnd.Tests")]
