public readonly record struct CborOperationResult<T>
{
    public required T Value { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required long MemoryUsed { get; init; }
}