using NUnit.Framework;

// Fixtures run in parallel; tests inside a fixture stay sequential.
// Safe because: TestDataRegistry is keyed per test id, TokenProvider is
// thread-safe, and every test creates its own uniquely-named data.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
