using System.Text.Json.Serialization;

namespace Dispatch.Core.Resilience;

/// <summary>What an operation is doing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JournalOp>))]
public enum JournalOp
{
    /// <summary>A file placed into the game folder.</summary>
    Place,

    /// <summary>A config value written in place.</summary>
    Configure,

    /// <summary>A texture installed via OpenIV.</summary>
    Texture,

    /// <summary>A directory created.</summary>
    Mkdir,
}

/// <summary>Where an operation is in its lifecycle.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JournalState>))]
public enum JournalState
{
    /// <summary>Written before the operation runs.</summary>
    Pending,

    /// <summary>Written after it succeeded.</summary>
    Complete,

    /// <summary>Written if it failed.</summary>
    Failed,
}

/// <summary>
/// One line in a run journal: an operation, written before it executes and
/// updated after.
/// </summary>
/// <remarks>
/// Every field the spec's journal example carries is here. The sequence number
/// makes gaps detectable; the backup path is what a rollback restores; the hash
/// is what a resume verifies against in case something changed underneath.
/// </remarks>
public sealed record JournalEntry
{
    /// <summary>Monotonic sequence number within the run.</summary>
    public required int Seq { get; init; }

    /// <summary>What the operation is.</summary>
    public JournalOp Op { get; init; }

    /// <summary>The mod this operation belongs to.</summary>
    public string? Mod { get; init; }

    /// <summary>Source path, in staging.</summary>
    public string? Src { get; init; }

    /// <summary>Destination path, in the game folder.</summary>
    public string? Dst { get; init; }

    /// <summary>Where the overwritten file was backed up, if anything was there.</summary>
    public string? Backup { get; init; }

    /// <summary>For a config write: the section and key.</summary>
    public string? Key { get; init; }

    /// <summary>For a config write: the value before.</summary>
    public string? OldValue { get; init; }

    /// <summary>For a config write: the value after.</summary>
    public string? NewValue { get; init; }

    /// <summary>Lifecycle state.</summary>
    public JournalState State { get; init; }

    /// <summary>Hash of the placed file, recorded on completion.</summary>
    public string? Hash { get; init; }
}
