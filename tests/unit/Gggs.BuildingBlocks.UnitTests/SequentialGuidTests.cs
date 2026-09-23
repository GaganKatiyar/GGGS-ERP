using System.Data;
using Gggs.BuildingBlocks.Domain.Primitives;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

/// <summary>
/// The key strategy only pays off if the GUIDs really do sort in creation order the way SQL Server
/// sorts them. If this breaks, every clustered index in the platform starts fragmenting, so the
/// ordering is asserted against SQL Server's own comparison rules rather than .NET's.
/// </summary>
public sealed class SequentialGuidTests
{
    [Fact]
    public void Create_ProducesGuidsThatSqlServerSortsInCreationOrder()
    {
        DateTimeOffset start = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        List<Guid> generated = [.. Enumerable.Range(0, 200)
            .Select(offset => SequentialGuid.Create(start.AddMilliseconds(offset * 5)))];

        List<Guid> sortedAsSqlServerWould = [.. generated.OrderBy(id => id, SqlServerGuidComparer.Instance)];

        sortedAsSqlServerWould.ShouldBe(generated);
    }

    [Fact]
    public void Create_IsUniqueAcrossRapidCalls()
    {
        HashSet<Guid> generated = [.. Enumerable.Range(0, 10_000).Select(_ => SequentialGuid.Create())];

        // Same millisecond for most of them: uniqueness has to come from the random bytes.
        generated.Count.ShouldBe(10_000);
    }

    [Fact]
    public void Create_EncodesTheTimestampInTheBytesSqlServerSortsFirst()
    {
        DateTimeOffset timestamp = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        byte[] bytes = SequentialGuid.Create(timestamp).ToByteArray();
        long milliseconds = timestamp.ToUnixTimeMilliseconds();

        bytes[10].ShouldBe((byte)(milliseconds >> 40));
        bytes[15].ShouldBe((byte)milliseconds);
    }

    /// <summary>
    /// SQL Server compares uniqueidentifier by the last six bytes first, then 8-9, 6-7, 4-5 and
    /// finally 0-3. <see cref="Guid.CompareTo(Guid)"/> does something different, which is exactly
    /// why sequential GUIDs have to be built for the database rather than for .NET.
    /// </summary>
    private sealed class SqlServerGuidComparer : IComparer<Guid>
    {
        private static readonly int[] ByteOrder = [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];

        public static SqlServerGuidComparer Instance { get; } = new();

        public int Compare(Guid x, Guid y)
        {
            byte[] left = x.ToByteArray();
            byte[] right = y.ToByteArray();

            foreach (int index in ByteOrder)
            {
                int comparison = left[index].CompareTo(right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }
    }
}
