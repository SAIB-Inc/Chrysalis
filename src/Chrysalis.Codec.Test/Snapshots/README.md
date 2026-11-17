# Generator snapshots

The `GeneratorIsolationTests` snapshot cases verify both metadata and serializer
output with Verify.SourceGenerators. Generated output must also compile without
errors. Record-struct and class-record samples pin both emission paths. The class
union includes a validated list reader and colliding `CborIndex` cases to pin its
secondary probe; the scalar record case pins validator metadata and writer emission.

When a generator change intentionally changes output, run the snapshot tests and
review every `.received.cs` against its `.verified.cs`. Replace only accepted files,
add new files reported as `New`, and remove orphaned files reported as `Delete`.
Include those diffs in the generator change's review, delete received leftovers,
and rerun the tests. Do not enable automatic acceptance in the tests or CI.

The accepted files contain no machine paths or timestamps. Verify orders each
generator's output by hint name. The source namespace and compilation name are
fixed; snapshots are shared across operating systems, not suffixed per runtime.
Diff applications are disabled: failures stay headless and print the received and
verified contents, not a diff.
