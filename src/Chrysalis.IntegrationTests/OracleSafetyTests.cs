using System.Text.Json;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;
using Xunit.Sdk;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline regressions for non-empty reference data and bounded independent snapshot ordering.</summary>
public sealed class OracleSafetyTests
{
    /// <summary>An empty SDK result and empty CLI result cannot bless the address-query decoder.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void EmptyAddressReferenceCannotPassByMatchingAnEmptyModel()
    {
        using JsonDocument reference = JsonDocument.Parse("{}");
        _ = Assert.ThrowsAny<XunitException>(() => ProtocolOracles.AssertUtxos(reference.RootElement, new UtxoByAddressResponse([])));
    }

    /// <summary>Unstable oracle readings exhaust the bounded retries without ever touching the subject.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task UnstableOracleNeverCallsTheSubject()
    {
        int reads = 0;
        int subjectCalls = 0;
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            _ = await StableOracleSnapshot.CompareAsync(ct => Task.FromResult(++reads), (a, b) => a == b,
                (snapshot, ct) => { subjectCalls++; return Task.FromResult(snapshot); }, CancellationToken.None);
        });
        Assert.Equal(6, reads);
        Assert.Equal(0, subjectCalls);
    }

    /// <summary>Movement before or after the subject retries and only a stable bracket is accepted.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SnapshotMovementRetriesBeforeAndAfterTheSubject()
    {
        Queue<int> readings = new([1, 2, 3, 3, 4, 5, 5, 5]);
        List<int> subjectSnapshots = [];
        (int expected, string actual) = await StableOracleSnapshot.CompareAsync(ct => Task.FromResult(readings.Dequeue()), (a, b) => a == b,
            (snapshot, ct) => { subjectSnapshots.Add(snapshot); return Task.FromResult("sample-" + snapshot); }, CancellationToken.None);
        Assert.Equal(5, expected);
        Assert.Equal("sample-5", actual);
        Assert.Equal([3, 5], subjectSnapshots);
        Assert.Empty(readings);
    }

    /// <summary>A subject failure is preserved, but only after two equivalent independent oracle readings.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SubjectFailureIsVisibleOnlyAfterTheOraclePrecondition()
    {
        int reads = 0;
        InvalidDataException failure = new("synthetic subject failure");
        InvalidDataException actual = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await StableOracleSnapshot.CompareAsync(ct => { reads++; return Task.FromResult(7); }, (a, b) => a == b,
                (snapshot, ct) => Task.FromException<int>(failure), CancellationToken.None);
        });
        Assert.Same(failure, actual);
        Assert.Equal(2, reads);
    }
}
