using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

static class Extensions
{
    public static string Remove(this string t, string word) => t.Replace(word, string.Empty);
    public static byte[] ToHash(this string t)
    {
        using (SHA256 shA256 = SHA256.Create())
        {
            byte[] hash = shA256.ComputeHash(Encoding.Default.GetBytes(t));
            ulong uint64_1 = BitConverter.ToUInt64(hash, 0);
            ulong uint64_2 = BitConverter.ToUInt64(hash, 8);
            ulong uint64_3 = BitConverter.ToUInt64(hash, 16);
            ulong uint64_4 = BitConverter.ToUInt64(hash, 24);
            ulong num1 = uint64_1 ^ uint64_3;
            ulong num2 = uint64_2 ^ uint64_4;
            Memory<byte> memory = new Memory<byte>(new byte[16]);
            BitConverter.TryWriteBytes(memory.Span, num1);
            BitConverter.TryWriteBytes(memory.Slice(8, 8).Span, num2);
            return memory.ToArray();
        }
    }

    public static Guid ToGuid(this string t) => new Guid(t.ToHash());

    public static byte[] NameHash(this Type t1)
    {
        return _hashCache.GetOrAdd(t1, (Func<Type, byte[]>) (t2 => t2.FullName.ToHash()));
    }
    private static ConcurrentDictionary<Type, byte[]> _hashCache = new ConcurrentDictionary<Type, byte[]>();

    public static Guid NameId(this Type t) => new Guid(t.NameHash());
}